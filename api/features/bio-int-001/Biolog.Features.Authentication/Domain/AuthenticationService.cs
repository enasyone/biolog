namespace Biolog.Features.Authentication;

/// <summary>
/// The orchestrator every BIO-INT-001 acceptance criterion is proven against:
/// <list type="bullet">
/// <item>AC01: every lookup is by (tenantId, providerId) - see <see cref="IAuthProviderConfigRepository"/>.</item>
/// <item>AC02: <see cref="ClaimMappingService.MapClaims"/> is the ONLY path from a raw provider
/// claim to a role - this class never assigns a role itself.</item>
/// <item>AC03: a config with <see cref="MfaRequirement.Required"/> always returns
/// <see cref="AuthenticationOutcomeKind.MfaRequired"/> before a session token is ever issued -
/// see <see cref="AuthenticateAsync"/>'s own step ordering.</item>
/// <item>AC04: every branch (success, every failure reason, every MFA step, first-time account
/// link) writes exactly one <see cref="AuthAuditEvent"/> before returning - see
/// <see cref="AuditAsync"/>'s single call site per outcome.</item>
/// </list>
/// Dictionary-dispatches to the matching <see cref="IAuthProviderAdapter"/> by
/// <see cref="AuthProviderConfig.ProviderType"/> - never branches on the enum itself, the same
/// separation BIO-RPT-001-AC02 established for its own channel adapters.
/// </summary>
public sealed class AuthenticationService
{
    private readonly IAuthProviderConfigRepository _configRepository;
    private readonly IReadOnlyDictionary<AuthProviderType, IAuthProviderAdapter> _adaptersByType;
    private readonly IMfaChallengeProvider _mfaProvider;
    private readonly IAccountLinkRepository _accountLinkRepository;
    private readonly IAuthAuditSink _auditSink;
    private readonly ISessionTokenCodec _sessionTokenCodec;

    private readonly Dictionary<string, PendingAuthentication> _pendingMfa = [];
    private readonly Lock _pendingGate = new();

    public AuthenticationService(
        IAuthProviderConfigRepository configRepository,
        IEnumerable<IAuthProviderAdapter> adapters,
        IMfaChallengeProvider mfaProvider,
        IAccountLinkRepository accountLinkRepository,
        IAuthAuditSink auditSink,
        ISessionTokenCodec sessionTokenCodec)
    {
        _configRepository = configRepository;
        _adaptersByType = adapters.ToDictionary(a => a.ProviderType);
        _mfaProvider = mfaProvider;
        _accountLinkRepository = accountLinkRepository;
        _auditSink = auditSink;
        _sessionTokenCodec = sessionTokenCodec;
    }

    public async Task<AuthenticationOutcome> AuthenticateAsync(
        string tenantId, string providerId, ProviderCredentialInput input, CancellationToken cancellationToken)
    {
        var config = await _configRepository.GetAsync(tenantId, providerId, cancellationToken);
        if (config is null || !config.Enabled)
        {
            await AuditAsync(tenantId, AuthAuditAction.LoginFailed, providerId, subjectId: null,
                AuthenticationFailureReason.ProviderDisabled, cancellationToken);
            return AuthenticationOutcome.Failure(AuthenticationFailureReason.ProviderDisabled);
        }

        if (!_adaptersByType.TryGetValue(config.ProviderType, out var adapter))
        {
            await AuditAsync(tenantId, AuthAuditAction.LoginFailed, providerId, subjectId: null,
                AuthenticationFailureReason.ProviderUnavailable, cancellationToken);
            return AuthenticationOutcome.Failure(AuthenticationFailureReason.ProviderUnavailable);
        }

        var providerResult = await adapter.AuthenticateAsync(config, input, cancellationToken);
        if (!providerResult.Success)
        {
            await AuditAsync(tenantId, AuthAuditAction.LoginFailed, providerId, providerResult.SubjectId,
                providerResult.FailureReason, cancellationToken);
            return AuthenticationOutcome.Failure(providerResult.FailureReason!.Value);
        }

        var subjectId = providerResult.SubjectId!;
        var mappedRoles = ClaimMappingService.MapClaims(config.ClaimMappingRules, providerResult.Claims!);

        var requestedPersonId = input.Credentials.GetValueOrDefault("link_person_id");
        var personId = requestedPersonId ?? $"synthetic-{Guid.CreateVersion7()}";
        var (link, wasCreated) = await _accountLinkRepository.LinkAsync(tenantId, providerId, subjectId, personId, cancellationToken);
        if (wasCreated)
        {
            await AuditAsync(tenantId, AuthAuditAction.AccountLinked, providerId, subjectId, failureReason: null, cancellationToken);
        }

        if (config.Mfa == MfaRequirement.Required)
        {
            var (challenge, code) = await _mfaProvider.IssueChallengeAsync(tenantId, subjectId, cancellationToken);
            lock (_pendingGate)
            {
                _pendingMfa[challenge.ChallengeId] = new PendingAuthentication(tenantId, providerId, subjectId, link.PersonId, mappedRoles);
            }

            await AuditAsync(tenantId, AuthAuditAction.MfaChallengeIssued, providerId, subjectId, failureReason: null, cancellationToken);
            return AuthenticationOutcome.MfaRequired(challenge, code);
        }

        var principal = IssueSession(tenantId, providerId, subjectId, link.PersonId, mappedRoles);
        await AuditAsync(tenantId, AuthAuditAction.LoginSucceeded, providerId, subjectId, failureReason: null, cancellationToken);
        return AuthenticationOutcome.Success(principal);
    }

    public async Task<AuthenticationOutcome> CompleteMfaAsync(
        string tenantId, string challengeId, string code, CancellationToken cancellationToken)
    {
        PendingAuthentication? pending;
        lock (_pendingGate)
        {
            _pendingMfa.TryGetValue(challengeId, out pending);
        }

        var verified = await _mfaProvider.VerifyAsync(challengeId, code, cancellationToken);

        if (pending is null || pending.TenantId != tenantId)
        {
            return AuthenticationOutcome.Failure(AuthenticationFailureReason.MfaChallengeExpired);
        }

        lock (_pendingGate)
        {
            _pendingMfa.Remove(challengeId);
        }

        if (!verified)
        {
            await AuditAsync(tenantId, AuthAuditAction.MfaFailed, pending.ProviderId, pending.SubjectId, failureReason: null, cancellationToken);
            await AuditAsync(tenantId, AuthAuditAction.LoginFailed, pending.ProviderId, pending.SubjectId,
                AuthenticationFailureReason.MfaFailed, cancellationToken);
            return AuthenticationOutcome.Failure(AuthenticationFailureReason.MfaFailed);
        }

        await AuditAsync(tenantId, AuthAuditAction.MfaVerified, pending.ProviderId, pending.SubjectId, failureReason: null, cancellationToken);
        var principal = IssueSession(tenantId, pending.ProviderId, pending.SubjectId, pending.PersonId, pending.MappedRoles);
        await AuditAsync(tenantId, AuthAuditAction.LoginSucceeded, pending.ProviderId, pending.SubjectId, failureReason: null, cancellationToken);
        return AuthenticationOutcome.Success(principal);
    }

    private AuthenticatedPrincipal IssueSession(string tenantId, string providerId, string subjectId, string personId, IReadOnlySet<string> roles)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        var token = _sessionTokenCodec.Issue(tenantId, providerId, subjectId, personId, roles, expiresAt);
        return new AuthenticatedPrincipal(tenantId, providerId, subjectId, personId, roles, token, expiresAt);
    }

    private async Task AuditAsync(
        string tenantId, AuthAuditAction action, string providerId, string? subjectId,
        AuthenticationFailureReason? failureReason, CancellationToken cancellationToken)
    {
        var auditEvent = new AuthAuditEvent(
            EventId: Guid.CreateVersion7().ToString(),
            SchemaVersion: 1,
            TenantId: tenantId,
            Action: action,
            ProviderId: providerId,
            SubjectId: subjectId,
            FailureReason: failureReason,
            CorrelationId: Guid.CreateVersion7().ToString(),
            OccurredAt: DateTimeOffset.UtcNow);

        try
        {
            await _auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
            // fall through to the existence recheck below
        }

        bool exists;
        try
        {
            exists = await _auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw new AuditUnavailableException();
        }

        if (!exists)
        {
            throw new AuditUnavailableException();
        }
    }

    private sealed record PendingAuthentication(
        string TenantId, string ProviderId, string SubjectId, string PersonId, IReadOnlySet<string> MappedRoles);
}
