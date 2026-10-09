using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FingerprintEnrolment.Audit;
using Biolog.Features.FingerprintEnrolment.Domain;
using Biolog.Features.FingerprintEnrolment.Quality;

namespace Biolog.Features.FingerprintEnrolment.Service;

/// <summary>
/// BIO-BIO-002: orchestrates a guided tenprint/single-finger enrolment session. Implements the
/// same audit-atomicity pattern established across BIO-TEN-001/BIO-TEN-002/BIO-DEV-001/BIO-IDN-001
/// (ADR-0003 Decision 7): every mutating method holds an in-process, per-session-id lock
/// (<see cref="FingerprintEnrolmentLockRegistry"/>) across the whole read-validate-audit-commit
/// sequence, audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="IFingerprintEnrolmentAuditSink.ExistsAsync"/> rather than assuming failure, and
/// passes <see cref="CancellationToken.None"/> to the repository commit once the audit write is
/// confirmed - a caller cancellation arriving after that point must not abort a write whose audit
/// event already durably claims it happened.
///
/// <b>AC04 - "approved template derivation".</b> Extraction (<see cref="IFingerprintProvider.ExtractTemplateAsync"/>)
/// and quality scoring both complete successfully before a captured finger is ever committed - a
/// failed extraction throws before <see cref="Domain.FingerprintEnrolmentSession.CaptureFinger"/>
/// is even called, so no template is derived and no raw image is retained either. A raw image is
/// attached to the committed <see cref="FingerRecord"/> ONLY when BOTH the session itself was
/// started with <see cref="Domain.FingerprintEnrolmentSession.RetentionAuthorised"/> true AND
/// <see cref="IRawImageRetentionAuthority"/> - the AUTHORITATIVE, tenant-scoped, server-side
/// policy BIO-BIO-009 owns - agrees, at the moment of capture (BIO-BIO-009-AC03, BIO-BIO-009-F01).
/// The session flag alone is never sufficient: it is a caller-asserted intent, not an authority,
/// and this authority fails closed (never retains) on any failure to reach it - see
/// <see cref="TryIsRawImageRetentionAuthorisedAsync"/>.
///
/// <b>Tenant context.</b> Like BIO-BIO-001's <c>BiometricService</c>, this service takes a
/// caller-resolved tenant/person id rather than resolving one itself - this project defines no
/// tenant-context seam of its own; a later story that adds an HTTP surface resolves the real
/// tenant context and passes it in.
///
/// <b>Internal, like BIO-IDN-001's <c>PersonService</c>.</b> This service depends on the
/// internal <see cref="IFingerprintEnrolmentRepository"/>, so it is internal too - reachable only
/// from this assembly, <c>Biolog.Api</c> (DI registration/wiring, via the
/// <c>InternalsVisibleTo</c> grant in this project's <c>.csproj</c>) and this feature's own test
/// project.
/// </summary>
internal sealed class FingerprintEnrolmentService(
    IFingerprintEnrolmentRepository repository,
    IFingerprintProvider fingerprintProvider,
    IFingerQualityScorer qualityScorer,
    IFingerprintEnrolmentAuditSink auditSink,
    FingerprintEnrolmentLockRegistry lockRegistry,
    IRawImageRetentionAuthority rawImageRetentionAuthority,
    Func<string>? newId = null,
    Func<string>? nowUtc = null)
{
    private readonly Func<string> _newId = newId ?? (() => Guid.NewGuid().ToString());
    private readonly Func<string> _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));

    public async Task<FingerprintEnrolmentSession> StartSessionAsync(
        string tenantId, string personId, EnrolmentMode mode, IReadOnlyList<FingerPosition> requestedPositions,
        bool retentionAuthorised, string actorId, CancellationToken cancellationToken)
    {
        var now = _nowUtc();
        var session = FingerprintEnrolmentSession.Start(_newId(), tenantId, personId, mode, requestedPositions, retentionAuthorised, now);

        using var _ = await lockRegistry.AcquireAsync(session.Id, cancellationToken);

        await AuditAsync(tenantId, session.Id, personId, "fingerprint_enrolment.session_started", fingerPosition: null, reason: null, actorId, now, cancellationToken);

        return await repository.CreateAsync(session, CancellationToken.None);
    }

    public Task<FingerprintEnrolmentSession?> GetSessionAsync(string tenantId, string sessionId, CancellationToken cancellationToken) =>
        repository.GetAsync(tenantId, sessionId, cancellationToken);

    /// <summary>AC02/AC03/AC04: captures (or recaptures) one finger. Extracts a template through
    /// BIO-BIO-001's provider-neutral contract, scores quality, and attaches the raw sample only
    /// when the session authorised retention (AC04).</summary>
    public async Task<FingerprintEnrolmentSession> CaptureFingerAsync(
        string tenantId, string sessionId, FingerPosition position, BiometricSample sample, int expectedVersion, string actorId,
        CancellationToken cancellationToken)
    {
        using var _ = await lockRegistry.AcquireAsync(sessionId, cancellationToken);
        var current = await repository.GetAsync(tenantId, sessionId, cancellationToken) ?? throw FingerprintEnrolmentException.SessionNotFound(sessionId);

        var now = _nowUtc();
        var features = await fingerprintProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false);
        var quality = await qualityScorer.ScoreAsync(sample, cancellationToken).ConfigureAwait(false);

        var template = BiometricTemplate.Create(
            _newId(), tenantId, current.PersonId, BiometricModality.Fingerprint, fingerprintProvider.ProviderId, features.ProviderVersion,
            features.Payload, now);

        var rawImageRetentionAuthorised = current.RetentionAuthorised
            && await TryIsRawImageRetentionAuthorisedAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var capturedRecord = new FingerRecord
        {
            Position = position,
            Status = FingerCaptureStatus.Captured,
            QualityScore = quality,
            Template = template,
            RetainedRawImage = rawImageRetentionAuthorised ? sample.Payload : null,
        };

        var updated = current.CaptureFinger(position, capturedRecord, expectedVersion, now);

        await AuditAsync(tenantId, sessionId, current.PersonId, "fingerprint_enrolment.finger_captured", position.ToString(), reason: null, actorId, now, cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>AC03: the missing/amputated finger exception path.</summary>
    public async Task<FingerprintEnrolmentSession> MarkMissingOrAmputatedAsync(
        string tenantId, string sessionId, FingerPosition position, string reason, int expectedVersion, string actorId,
        CancellationToken cancellationToken)
    {
        using var _ = await lockRegistry.AcquireAsync(sessionId, cancellationToken);
        var current = await repository.GetAsync(tenantId, sessionId, cancellationToken) ?? throw FingerprintEnrolmentException.SessionNotFound(sessionId);

        var now = _nowUtc();
        var updated = current.MarkMissingOrAmputated(position, reason, expectedVersion, now);

        if (ReferenceEquals(updated, current))
        {
            return current; // idempotent no-op: nothing changed, nothing to audit or commit
        }

        await AuditAsync(tenantId, sessionId, current.PersonId, "fingerprint_enrolment.finger_missing_amputated", position.ToString(), reason, actorId, now, cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<FingerprintEnrolmentSession> CompleteSessionAsync(
        string tenantId, string sessionId, int expectedVersion, string actorId, CancellationToken cancellationToken)
    {
        using var _ = await lockRegistry.AcquireAsync(sessionId, cancellationToken);
        var current = await repository.GetAsync(tenantId, sessionId, cancellationToken) ?? throw FingerprintEnrolmentException.SessionNotFound(sessionId);

        var now = _nowUtc();
        var updated = current.Complete(expectedVersion, now);

        await AuditAsync(tenantId, sessionId, current.PersonId, "fingerprint_enrolment.session_completed", fingerPosition: null, reason: null, actorId, now, cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>BIO-BIO-009-AC02/AC04: removes a session outright, reusing this method's own
    /// shared lock/audit infrastructure exactly like every other mutating method here - a
    /// concurrent capture-vs-delete race on the same session is genuinely serialized by
    /// <see cref="FingerprintEnrolmentLockRegistry"/>, the same instance every other method
    /// already acquires. Returns <c>null</c> for a session that does not exist (never an
    /// exception, never audited - nothing happened); otherwise the deleted session's own
    /// <see cref="FingerprintEnrolmentSession.PersonId"/> - resolved from the record itself, so a
    /// caller can never substitute a different identity for cache-eviction purposes
    /// (BIO-BIO-009-AC04, ZA-BIO009-01).</summary>
    public async Task<string?> DeleteSessionAsync(
        string tenantId, string sessionId, string reason, string actorId, CancellationToken cancellationToken)
    {
        using var _ = await lockRegistry.AcquireAsync(sessionId, cancellationToken);
        var current = await repository.GetAsync(tenantId, sessionId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var now = _nowUtc();
        await AuditAsync(tenantId, sessionId, current.PersonId, "fingerprint_enrolment.session_deleted", fingerPosition: null, reason, actorId, now, cancellationToken);

        var deleted = await repository.DeleteAsync(tenantId, sessionId, CancellationToken.None);
        return deleted ? current.PersonId : null;
    }

    public async Task<FingerprintEnrolmentSession> CancelSessionAsync(
        string tenantId, string sessionId, int expectedVersion, string actorId, CancellationToken cancellationToken)
    {
        using var _ = await lockRegistry.AcquireAsync(sessionId, cancellationToken);
        var current = await repository.GetAsync(tenantId, sessionId, cancellationToken) ?? throw FingerprintEnrolmentException.SessionNotFound(sessionId);

        var now = _nowUtc();
        var updated = current.Cancel(expectedVersion, now);

        await AuditAsync(tenantId, sessionId, current.PersonId, "fingerprint_enrolment.session_cancelled", fingerPosition: null, reason: null, actorId, now, cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>BIO-BIO-009-AC03/F01: fails closed - any exception from the authoritative policy
    /// (unreachable, unconfigured, whatever) is treated as "not authorised", never as "assume
    /// authorised and keep the raw image". A caller/session asserting retention is never, on its
    /// own, enough to retain a raw image - see this class's own header.</summary>
    private async Task<bool> TryIsRawImageRetentionAuthorisedAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            return await rawImageRetentionAuthority.IsAuthorisedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>ADR-0003 Decision 7, element 4: resolves an ambiguous <see cref="IFingerprintEnrolmentAuditSink.WriteAsync"/>
    /// throw via <see cref="IFingerprintEnrolmentAuditSink.ExistsAsync"/> before treating it as a
    /// real failure, rather than assuming the write never happened.</summary>
    private async Task AuditAsync(
        string tenantId, string sessionId, string personId, string action, string? fingerPosition, string? reason, string actorId,
        string nowUtc, CancellationToken cancellationToken)
    {
        var eventId = _newId();
        var auditEvent = new FingerprintEnrolmentAuditEvent
        {
            EventId = eventId,
            TenantId = tenantId,
            SessionId = sessionId,
            PersonId = personId,
            Action = action,
            FingerPosition = fingerPosition,
            Reason = reason,
            ActorId = actorId,
            OccurredAtUtc = nowUtc,
        };

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return; // confirmed: the write completed without throwing
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(eventId, cancellationToken);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("audit_unavailable: audit sink write failed and verification could not confirm durability.");
        }

        if (!exists)
        {
            throw new InvalidOperationException("audit_unavailable: audit sink write failed and the event was not found on verification.");
        }
    }
}
