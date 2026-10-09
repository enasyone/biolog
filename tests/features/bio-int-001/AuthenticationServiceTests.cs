using Biolog.Features.Authentication;
using Xunit;

namespace Biolog.Features.Authentication.Tests;

/// <summary>
/// The end-to-end orchestrator tests: AC02 (claim mapping), AC03 (MFA hook), AC04 (audit),
/// and the account-link half of AC04, all exercised through the real
/// <see cref="AuthenticationService"/> rather than any one component in isolation.
/// </summary>
public sealed class AuthenticationServiceTests
{
    private sealed record Harness(
        AuthenticationService Service,
        IAuthProviderConfigRepository ConfigRepo,
        ICredentialStore CredentialStore,
        IAuthAuditSink AuditSink,
        IAccountLinkRepository AccountLinkRepo);

    private static Harness Build()
    {
        var configRepo = new InMemoryAuthProviderConfigRepository();
        var credentialStore = new InMemoryCredentialStore();
        var auditSink = new InMemoryAuthAuditSink();
        var accountLinkRepo = new InMemoryAccountLinkRepository();
        var mfaProvider = new InMemoryMfaChallengeProvider();
        var sessionCodec = new HmacSessionTokenCodec("test-session-token-secret-32chars-min");

        IAuthProviderAdapter[] adapters =
        [
            new PasswordProviderAdapter(credentialStore),
            new EntraIdProviderAdapter(),
            new Saml2ProviderAdapter(),
            new OidcProviderAdapter(),
            new GoogleProviderAdapter(),
            new LdapProviderAdapter(),
        ];

        var service = new AuthenticationService(configRepo, adapters, mfaProvider, accountLinkRepo, auditSink, sessionCodec);
        return new Harness(service, configRepo, credentialStore, auditSink, accountLinkRepo);
    }

    private static async Task<AuthProviderConfig> RegisterPasswordProviderAsync(
        Harness h, string tenantId, MfaRequirement mfa, IReadOnlyList<ClaimMappingRule> rules, string username, string password, IReadOnlyList<ProviderClaim> claims)
    {
        var config = await h.ConfigRepo.CreateAsync(tenantId, new AuthProviderRegistrationRequest(AuthProviderType.Password, "Test password provider", mfa, rules), CancellationToken.None);
        await h.CredentialStore.RegisterAsync(tenantId, config.ProviderId, username, password, claims, CancellationToken.None);
        return config;
    }

    [Fact]
    public async Task Password_login_without_mfa_succeeds_and_returns_ac02_mapped_roles_only()
    {
        var h = Build();
        var rules = new List<ClaimMappingRule> { new("group", "registrars", "RegistrarHr") };
        var config = await RegisterPasswordProviderAsync(
            h, "ten_a", MfaRequirement.None, rules, "alice", "correct-horse-battery-staple",
            [new ProviderClaim("group", "registrars"), new ProviderClaim("group", "unmapped-group")]);

        var outcome = await h.Service.AuthenticateAsync(
            "ten_a", config.ProviderId,
            new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "correct-horse-battery-staple" }, []),
            CancellationToken.None);

        Assert.Equal(AuthenticationOutcomeKind.Success, outcome.Kind);
        Assert.Equal(["RegistrarHr"], outcome.Principal!.Roles); // the unmapped-group claim contributed nothing
        Assert.NotEmpty(outcome.Principal.SessionToken);
    }

    [Fact]
    public async Task Wrong_password_fails_closed_and_is_audited()
    {
        var h = Build();
        var config = await RegisterPasswordProviderAsync(h, "ten_a", MfaRequirement.None, [], "alice", "correct-password", []);

        var outcome = await h.Service.AuthenticateAsync(
            "ten_a", config.ProviderId,
            new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "wrong-password" }, []),
            CancellationToken.None);

        Assert.Equal(AuthenticationOutcomeKind.Failure, outcome.Kind);
        Assert.Equal(AuthenticationFailureReason.InvalidCredentials, outcome.FailureReason);

        var events = await h.AuditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Contains(events, e => e.Action == AuthAuditAction.LoginFailed && e.FailureReason == AuthenticationFailureReason.InvalidCredentials);
    }

    [Fact]
    public async Task A_disabled_provider_fails_closed_the_same_way_a_nonexistent_one_does()
    {
        var h = Build();
        var config = await RegisterPasswordProviderAsync(h, "ten_a", MfaRequirement.None, [], "alice", "pw", []);
        await h.ConfigRepo.UpdateStatusAsync("ten_a", config.ProviderId, new AuthProviderStatusChangeRequest(false, 1), CancellationToken.None);

        var disabledOutcome = await h.Service.AuthenticateAsync(
            "ten_a", config.ProviderId, new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "pw" }, []), CancellationToken.None);
        var missingOutcome = await h.Service.AuthenticateAsync(
            "ten_a", "not-a-real-provider-id", new ProviderCredentialInput(new Dictionary<string, string>(), []), CancellationToken.None);

        Assert.Equal(AuthenticationFailureReason.ProviderDisabled, disabledOutcome.FailureReason);
        Assert.Equal(AuthenticationFailureReason.ProviderDisabled, missingOutcome.FailureReason); // no existence oracle
    }

    [Fact]
    public async Task Mfa_required_config_never_issues_a_session_before_the_challenge_is_verified()
    {
        var h = Build();
        var rules = new List<ClaimMappingRule> { new("group", "registrars", "RegistrarHr") };
        var config = await RegisterPasswordProviderAsync(
            h, "ten_a", MfaRequirement.Required, rules, "alice", "pw", [new ProviderClaim("group", "registrars")]);

        var first = await h.Service.AuthenticateAsync(
            "ten_a", config.ProviderId, new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "pw" }, []), CancellationToken.None);

        Assert.Equal(AuthenticationOutcomeKind.MfaRequired, first.Kind);
        Assert.Null(first.Principal);

        var completed = await h.Service.CompleteMfaAsync("ten_a", first.Challenge!.ChallengeId, first.DemoOnlyMfaCode!, CancellationToken.None);

        Assert.Equal(AuthenticationOutcomeKind.Success, completed.Kind);
        Assert.Equal(["RegistrarHr"], completed.Principal!.Roles);
    }

    [Fact]
    public async Task Mfa_wrong_code_fails_and_records_both_mfa_failed_and_login_failed()
    {
        var h = Build();
        var config = await RegisterPasswordProviderAsync(h, "ten_a", MfaRequirement.Required, [], "alice", "pw", []);

        var first = await h.Service.AuthenticateAsync(
            "ten_a", config.ProviderId, new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "pw" }, []), CancellationToken.None);

        var completed = await h.Service.CompleteMfaAsync("ten_a", first.Challenge!.ChallengeId, "000000", CancellationToken.None);

        Assert.Equal(AuthenticationOutcomeKind.Failure, completed.Kind);
        Assert.Equal(AuthenticationFailureReason.MfaFailed, completed.FailureReason);

        var events = await h.AuditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Contains(events, e => e.Action == AuthAuditAction.MfaFailed);
        Assert.Contains(events, e => e.Action == AuthAuditAction.LoginFailed && e.FailureReason == AuthenticationFailureReason.MfaFailed);
    }

    /// <summary>BIO-INT-001-F01 (Zainab, High): the demo-only code must be absent from every
    /// outcome kind except MfaRequired - in particular, never present on Success (where a real
    /// caller receives a session token and roles, and the code field must not ride along) or
    /// Failure.</summary>
    [Fact]
    public async Task DemoOnlyMfaCode_is_only_ever_populated_on_the_MfaRequired_outcome()
    {
        var h = Build();
        var noMfaConfig = await RegisterPasswordProviderAsync(h, "ten_a", MfaRequirement.None, [], "alice", "pw", []);
        var successOutcome = await h.Service.AuthenticateAsync(
            "ten_a", noMfaConfig.ProviderId, new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "pw" }, []), CancellationToken.None);
        var failureOutcome = await h.Service.AuthenticateAsync(
            "ten_a", noMfaConfig.ProviderId, new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "wrong" }, []), CancellationToken.None);

        Assert.Null(successOutcome.DemoOnlyMfaCode);
        Assert.Null(failureOutcome.DemoOnlyMfaCode);
    }

    [Fact]
    public async Task A_verified_mfa_challenge_cannot_be_completed_a_second_time()
    {
        var h = Build();
        var config = await RegisterPasswordProviderAsync(h, "ten_a", MfaRequirement.Required, [], "alice", "pw", []);
        var first = await h.Service.AuthenticateAsync(
            "ten_a", config.ProviderId, new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "pw" }, []), CancellationToken.None);
        var code = first.DemoOnlyMfaCode!;

        var firstCompletion = await h.Service.CompleteMfaAsync("ten_a", first.Challenge!.ChallengeId, code, CancellationToken.None);
        var secondCompletion = await h.Service.CompleteMfaAsync("ten_a", first.Challenge.ChallengeId, code, CancellationToken.None);

        Assert.Equal(AuthenticationOutcomeKind.Success, firstCompletion.Kind);
        Assert.Equal(AuthenticationOutcomeKind.Failure, secondCompletion.Kind);
    }

    [Fact]
    public async Task First_successful_login_creates_exactly_one_account_link_and_a_second_login_reuses_it()
    {
        var h = Build();
        var config = await RegisterPasswordProviderAsync(h, "ten_a", MfaRequirement.None, [], "alice", "pw", []);
        var input = new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "pw" }, []);

        var firstOutcome = await h.Service.AuthenticateAsync("ten_a", config.ProviderId, input, CancellationToken.None);
        var secondOutcome = await h.Service.AuthenticateAsync("ten_a", config.ProviderId, input, CancellationToken.None);

        Assert.Equal(firstOutcome.Principal!.PersonId, secondOutcome.Principal!.PersonId);

        var events = await h.AuditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Single(events, e => e.Action == AuthAuditAction.AccountLinked);
    }

    [Fact]
    public async Task An_explicit_link_person_id_is_honored_instead_of_a_synthetic_one()
    {
        var h = Build();
        var config = await RegisterPasswordProviderAsync(h, "ten_a", MfaRequirement.None, [], "alice", "pw", []);
        var input = new ProviderCredentialInput(
            new Dictionary<string, string> { ["username"] = "alice", ["password"] = "pw", ["link_person_id"] = "per_explicit_1" }, []);

        var outcome = await h.Service.AuthenticateAsync("ten_a", config.ProviderId, input, CancellationToken.None);

        Assert.Equal("per_explicit_1", outcome.Principal!.PersonId);
    }

    [Fact]
    public async Task Federated_provider_dispatch_reaches_the_correct_adapter_for_each_provider_type()
    {
        var h = Build();
        var rules = new List<ClaimMappingRule> { new("group", "registrars", "RegistrarHr") };
        var config = await h.ConfigRepo.CreateAsync(
            "ten_a", new AuthProviderRegistrationRequest(AuthProviderType.EntraId, "Entra", MfaRequirement.None, rules), CancellationToken.None);

        var outcome = await h.Service.AuthenticateAsync(
            "ten_a", config.ProviderId,
            new ProviderCredentialInput(new Dictionary<string, string> { ["subject"] = "entra-subject-1" }, [new ProviderClaim("group", "registrars")]),
            CancellationToken.None);

        Assert.Equal(AuthenticationOutcomeKind.Success, outcome.Kind);
        Assert.Equal("entra-subject-1", outcome.Principal!.SubjectId);
        Assert.Equal(["RegistrarHr"], outcome.Principal.Roles);
    }

    [Fact]
    public async Task Every_login_attempt_writes_exactly_one_audit_event_whether_it_succeeds_or_fails()
    {
        var h = Build();
        var config = await RegisterPasswordProviderAsync(h, "ten_a", MfaRequirement.None, [], "alice", "pw", []);

        await h.Service.AuthenticateAsync("ten_a", config.ProviderId, new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "pw" }, []), CancellationToken.None);
        await h.Service.AuthenticateAsync("ten_a", config.ProviderId, new ProviderCredentialInput(new Dictionary<string, string> { ["username"] = "alice", ["password"] = "wrong" }, []), CancellationToken.None);

        var events = await h.AuditSink.ListForTenantAsync("ten_a", CancellationToken.None);
        Assert.Single(events, e => e.Action == AuthAuditAction.LoginSucceeded);
        Assert.Single(events, e => e.Action == AuthAuditAction.LoginFailed);
    }
}
