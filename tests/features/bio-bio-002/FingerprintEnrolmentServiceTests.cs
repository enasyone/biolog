using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FingerprintEnrolment.Audit;
using Biolog.Features.FingerprintEnrolment.Domain;
using Biolog.Features.FingerprintEnrolment.Quality;
using Biolog.Features.FingerprintEnrolment.Service;
using Xunit;

namespace Biolog.Features.FingerprintEnrolment.Tests;

/// <summary>End-to-end tests for <see cref="FingerprintEnrolmentService"/>: AC02 (identity +
/// quality recorded), AC04 (raw image retention policy), AC05 (audit).</summary>
public sealed class FingerprintEnrolmentServiceTests
{
    private const string TenantA = "ten_a";
    private const string TenantB = "ten_b";
    private const string PersonA = "per_a";

    /// <summary>BIO-BIO-009-AC03/F01: a test double for the authoritative, tenant-scoped raw-image
    /// retention policy. Defaults to always-authorised so every pre-existing test here keeps
    /// exercising this service's OWN AC04 (the session-level consent flag) unchanged; the new
    /// fail-closed tests below pass a stricter or throwing instance explicitly.</summary>
    private sealed class FakeRawImageRetentionAuthority(bool authorised = true, bool throws = false) : IRawImageRetentionAuthority
    {
        public Task<bool> IsAuthorisedAsync(string tenantId, CancellationToken cancellationToken) =>
            throws ? throw new InvalidOperationException("simulated policy outage") : Task.FromResult(authorised);
    }

    private static FingerprintEnrolmentService NewService(
        out InMemoryFingerprintEnrolmentAuditSink auditSink, out InMemoryFingerprintEnrolmentRepository repository,
        IRawImageRetentionAuthority? rawImageRetentionAuthority = null)
    {
        repository = new InMemoryFingerprintEnrolmentRepository();
        auditSink = new InMemoryFingerprintEnrolmentAuditSink();
        return new FingerprintEnrolmentService(
            repository, new SyntheticFingerprintProvider(), new SyntheticFingerQualityScorer(), auditSink,
            new FingerprintEnrolmentLockRegistry(), rawImageRetentionAuthority ?? new FakeRawImageRetentionAuthority());
    }

    private static BiometricSample Sample(byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = "2026-01-01T00:00:00Z",
    };

    [Fact]
    public async Task AC01_StartSessionAsync_creates_a_session_in_the_repository()
    {
        var service = NewService(out _, out var repository);

        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);

        var stored = await repository.GetAsync(TenantA, session.Id, CancellationToken.None);
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task AC02_CaptureFingerAsync_records_finger_identity_and_a_quality_score()
    {
        var service = NewService(out _, out _);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);

        var updated = await service.CaptureFingerAsync(
            TenantA, session.Id, FingerPosition.RightIndex, Sample([10, 20, 30]), session.Version, "operator-1", CancellationToken.None);

        var record = updated.Fingers[FingerPosition.RightIndex];
        Assert.Equal(FingerPosition.RightIndex, record.Position);
        Assert.NotNull(record.QualityScore);
        Assert.InRange(record.QualityScore!.Value, 0, 100);
        Assert.NotNull(record.Template);
        Assert.Equal(BiometricModality.Fingerprint, record.Template!.Modality);
    }

    // AC04: raw fingerprint images are discarded by default.
    [Fact]
    public async Task AC04_raw_image_is_discarded_by_default_after_template_derivation()
    {
        var service = NewService(out _, out _);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);

        var updated = await service.CaptureFingerAsync(
            TenantA, session.Id, FingerPosition.RightIndex, Sample([10, 20, 30]), session.Version, "operator-1", CancellationToken.None);

        Assert.Null(updated.Fingers[FingerPosition.RightIndex].RetainedRawImage);
    }

    [Fact]
    public async Task AC04_raw_image_is_retained_only_when_the_session_authorised_retention()
    {
        var service = NewService(out _, out _);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: true, "operator-1", CancellationToken.None);

        var updated = await service.CaptureFingerAsync(
            TenantA, session.Id, FingerPosition.RightIndex, Sample([10, 20, 30]), session.Version, "operator-1", CancellationToken.None);

        Assert.Equal(new byte[] { 10, 20, 30 }, updated.Fingers[FingerPosition.RightIndex].RetainedRawImage);
    }

    // AC05: enrolment is audited.
    [Fact]
    public async Task AC05_every_mutation_writes_an_audit_event()
    {
        var service = NewService(out var auditSink, out _);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);
        var captured = await service.CaptureFingerAsync(
            TenantA, session.Id, FingerPosition.RightIndex, Sample([1, 2, 3]), session.Version, "operator-1", CancellationToken.None);
        await service.CompleteSessionAsync(TenantA, session.Id, captured.Version, "operator-1", CancellationToken.None);

        var actions = auditSink.AllEvents.Select(e => e.Action).ToArray();
        Assert.Contains("fingerprint_enrolment.session_started", actions);
        Assert.Contains("fingerprint_enrolment.finger_captured", actions);
        Assert.Contains("fingerprint_enrolment.session_completed", actions);
    }

    [Fact]
    public async Task AC05_audit_events_never_carry_the_raw_image_payload()
    {
        var service = NewService(out var auditSink, out _);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: true, "operator-1", CancellationToken.None);
        await service.CaptureFingerAsync(TenantA, session.Id, FingerPosition.RightIndex, Sample([1, 2, 3]), session.Version, "operator-1", CancellationToken.None);

        // FingerprintEnrolmentAuditEvent has no byte[]/payload-shaped member at all - a
        // structural guarantee, not just "happened not to set one this time".
        var eventType = typeof(Audit.FingerprintEnrolmentAuditEvent);
        Assert.DoesNotContain(eventType.GetProperties(), p => p.PropertyType == typeof(byte[]));
    }

    [Fact]
    public async Task ADR0002_S4_3_a_session_started_in_one_tenant_is_not_visible_from_another_tenant()
    {
        var service = NewService(out _, out _);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);

        var fromOtherTenant = await service.GetSessionAsync(TenantB, session.Id, CancellationToken.None);

        Assert.Null(fromOtherTenant);
    }

    [Fact]
    public async Task CompleteSessionAsync_throws_when_positions_remain_unresolved()
    {
        var service = NewService(out _, out _);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);

        await Assert.ThrowsAsync<FingerprintEnrolmentException>(
            () => service.CompleteSessionAsync(TenantA, session.Id, session.Version, "operator-1", CancellationToken.None));
    }

    [Fact]
    public async Task MarkMissingOrAmputatedAsync_resolves_a_finger_without_a_capture()
    {
        var service = NewService(out _, out _);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);

        var updated = await service.MarkMissingOrAmputatedAsync(
            TenantA, session.Id, FingerPosition.RightIndex, "amputated", session.Version, "operator-1", CancellationToken.None);
        var completed = await service.CompleteSessionAsync(TenantA, session.Id, updated.Version, "operator-1", CancellationToken.None);

        Assert.Equal(EnrolmentSessionStatus.Completed, completed.Status);
    }

    /// <summary>BIO-BIO-009-AC02/AC04: approved, additive extension (Kemi, 2026-10-06) - a
    /// session can now be deleted outright, reusing this service's own existing lock/audit
    /// infrastructure exactly like every other mutating method here.</summary>
    [Fact]
    public async Task DeleteSessionAsync_removes_the_session_and_audits_the_deletion()
    {
        var service = NewService(out var auditSink, out var repository);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);

        var deletedPersonId = await service.DeleteSessionAsync(TenantA, session.Id, "subject exited the tenant", "operator-1", CancellationToken.None);

        Assert.Equal(PersonA, deletedPersonId);
        Assert.Null(await repository.GetAsync(TenantA, session.Id, CancellationToken.None));
        Assert.Contains(auditSink.AllEvents, e => e.Action == "fingerprint_enrolment.session_deleted" && e.SessionId == session.Id);
    }

    [Fact]
    public async Task DeleteSessionAsync_returns_null_for_a_session_that_does_not_exist()
    {
        var service = NewService(out var auditSink, out _);

        var deletedPersonId = await service.DeleteSessionAsync(TenantA, "no-such-session", "reason", "operator-1", CancellationToken.None);

        Assert.Null(deletedPersonId);
        Assert.DoesNotContain(auditSink.AllEvents, e => e.Action == "fingerprint_enrolment.session_deleted");
    }

    [Fact]
    public async Task DeleteSessionAsync_cannot_delete_another_tenants_session()
    {
        var service = NewService(out _, out var repository);
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: false, "operator-1", CancellationToken.None);

        var deletedPersonId = await service.DeleteSessionAsync(TenantB, session.Id, "reason", "operator-1", CancellationToken.None);

        Assert.Null(deletedPersonId);
        Assert.NotNull(await repository.GetAsync(TenantA, session.Id, CancellationToken.None));
    }

    /// <summary>BIO-BIO-009-F01/ZB-BIO009-03: the session's own caller-asserted
    /// <c>RetentionAuthorised</c> flag alone must never retain a raw image - the AUTHORITATIVE
    /// tenant policy must also agree, at capture time.</summary>
    [Fact]
    public async Task AC04_raw_image_is_discarded_when_the_tenant_authority_does_not_permit_it_even_if_the_session_requested_retention()
    {
        var service = NewService(out _, out _, new FakeRawImageRetentionAuthority(authorised: false));
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: true, "operator-1", CancellationToken.None);

        var updated = await service.CaptureFingerAsync(
            TenantA, session.Id, FingerPosition.RightIndex, Sample([10, 20, 30]), session.Version, "operator-1", CancellationToken.None);

        Assert.Null(updated.Fingers[FingerPosition.RightIndex].RetainedRawImage);
    }

    /// <summary>BIO-BIO-009-F01: an unreachable/throwing authority fails CLOSED - never falls
    /// back to trusting the caller's own session flag.</summary>
    [Fact]
    public async Task AC04_raw_image_is_discarded_when_the_tenant_authority_is_unavailable()
    {
        var service = NewService(out _, out _, new FakeRawImageRetentionAuthority(throws: true));
        var session = await service.StartSessionAsync(
            TenantA, PersonA, EnrolmentMode.SingleFinger, [FingerPosition.RightIndex], retentionAuthorised: true, "operator-1", CancellationToken.None);

        var updated = await service.CaptureFingerAsync(
            TenantA, session.Id, FingerPosition.RightIndex, Sample([10, 20, 30]), session.Version, "operator-1", CancellationToken.None);

        Assert.Null(updated.Fingers[FingerPosition.RightIndex].RetainedRawImage);
    }
}
