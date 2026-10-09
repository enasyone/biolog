using System.Security.Cryptography.X509Certificates;
using Biolog.Features.EvidenceComparison.Audit;
using Biolog.Features.EvidenceComparison.Model;
using Biolog.Features.EvidenceComparison.Tests.Fixtures;
using Biolog.Features.NfcReader.Model;
using Xunit;

namespace Biolog.Features.EvidenceComparison.Tests;

/// <summary>Developer tests for BIO-DOC-008-AC03 ("conflicts route to review") and AC05
/// ("comparison result is versioned/audited").</summary>
public sealed class CrossComparisonResultTests
{
    private const string SampleActorId = "actor-42";
    private const string ValidTimestamp = "2026-09-23T12:00:00.000Z";
    private const string CorrelationId = "corr-1";

    private static SourceFieldValue Value(EvidenceSourceKind source, string normalized) =>
        SourceFieldValue.Create(source, normalized, normalized, 0.9);

    private static NfcAccessKeys SampleKeys() => ChipAccessKeyDerivation.DeriveKeys(
        NfcAccessKeySeedMaterial.CardAccessNumber.Create("123456"));

    private static CrossComparisonResult ComputeResult(
        IReadOnlyList<FieldComparison> fieldComparisons,
        NfcCorroboration nfcCorroboration,
        IEvidenceComparisonAuditSink? sink = null,
        int versionNumber = 1,
        string computedByActorId = SampleActorId,
        string computedAt = ValidTimestamp,
        string correlationId = CorrelationId,
        string documentSessionId = "session-1",
        IVersionStore? versionStore = null) =>
        CrossComparisonResult.Compute(
            documentSessionId, versionNumber, computedByActorId, computedAt, correlationId,
            fieldComparisons, nfcCorroboration, sink ?? new InMemoryEvidenceComparisonAuditSink(),
            versionStore ?? new InMemoryVersionStore());

    [Fact]
    public void AC05_carries_version_and_audit_attribution_exactly_as_supplied()
    {
        // Version 3 is only legitimate as the third comparison of this session - seed the store
        // with its two predecessors rather than asserting an arbitrary, out-of-sequence number.
        var store = new InMemoryVersionStore();
        ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store);
        ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 2, versionStore: store);
        var result = ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 3, versionStore: store);

        Assert.Equal("session-1", result.DocumentSessionId);
        Assert.Equal(3, result.VersionNumber);
        Assert.Equal("actor-42", result.ComputedByActorId);
        Assert.Equal(ValidTimestamp, result.ComputedAt);
    }

    [Fact]
    public void AC05_a_durable_audit_event_is_written_before_the_result_is_returned()
    {
        var sink = new InMemoryEvidenceComparisonAuditSink();

        var result = ComputeResult([], NfcCorroboration.NotAvailable, sink);

        var written = Assert.Single(sink.Events);
        Assert.Equal(result.AuditEventId, written.EventId);
        Assert.Equal("evidence_comparison.computed", written.Action);
        Assert.Equal("document_session", written.Target.Type);
        Assert.Equal("session-1", written.Target.Id);
        Assert.Equal(ActorType.Unknown, written.Actor.Type);
        Assert.Equal("actor-42", written.Actor.Id);
    }

    [Fact]
    public void AC05_a_version_number_below_one_is_rejected_never_silently_accepted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 0));
    }

    [Fact]
    public void AC05_QA03_sequential_versions_for_the_same_session_succeed_in_order()
    {
        var store = new InMemoryVersionStore();

        var first = ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store);
        var second = ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 2, versionStore: store);

        Assert.Equal(1, first.VersionNumber);
        Assert.Equal(2, second.VersionNumber);
    }

    [Fact]
    public void AC05_QA03_reusing_a_version_number_for_the_same_session_is_rejected()
    {
        // Chioma's exact round-4 scenario: a caller reuses version 1 for two different results.
        var store = new InMemoryVersionStore();
        ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store);

        Assert.Throws<StaleComparisonVersionException>(
            () => ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store));
    }

    [Fact]
    public void AC05_QA03_skipping_ahead_to_an_arbitrary_version_for_the_same_session_is_rejected()
    {
        // Chioma's exact round-4 scenario: a caller labels the first result version 999.
        var store = new InMemoryVersionStore();

        Assert.Throws<StaleComparisonVersionException>(
            () => ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 999, versionStore: store));
    }

    [Fact]
    public void AC05_QA03_moving_backwards_for_the_same_session_is_rejected()
    {
        var store = new InMemoryVersionStore();
        ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store);
        ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 2, versionStore: store);

        Assert.Throws<StaleComparisonVersionException>(
            () => ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store));
    }

    [Fact]
    public void AC05_QA03_a_stale_version_rejection_does_not_advance_the_tracked_sequence()
    {
        // A rejected version claim must not consume or otherwise disturb the session's sequence -
        // the next legitimate caller should still be able to submit version 2.
        var store = new InMemoryVersionStore();
        ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store);
        Assert.Throws<StaleComparisonVersionException>(
            () => ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store));

        var second = ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 2, versionStore: store);

        Assert.Equal(2, second.VersionNumber);
    }

    [Fact]
    public void AC05_QA03_different_document_sessions_have_independent_version_sequences()
    {
        var store = new InMemoryVersionStore();

        var sessionA = ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store, documentSessionId: "session-a");
        var sessionB = ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store, documentSessionId: "session-b");

        Assert.Equal(1, sessionA.VersionNumber);
        Assert.Equal(1, sessionB.VersionNumber);
    }

    [Fact]
    public void AC05_QA03_an_unrelated_validation_failure_does_not_burn_a_version_number()
    {
        // A rejected call due to e.g. a blank actor id must not consume the version claim - the
        // caller should still be able to retry the SAME version number once the real problem is fixed.
        var store = new InMemoryVersionStore();
        Assert.Throws<ArgumentException>(
            () => ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, computedByActorId: "  ", versionStore: store));

        var retried = ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store);

        Assert.Equal(1, retried.VersionNumber);
    }

    [Fact]
    public void AC05_QA04_a_failed_audit_write_does_not_permanently_burn_the_reserved_version()
    {
        // Chioma's exact round-5 required regression: audit fails for version 1, then a retry of
        // version 1 with a healthy sink succeeds and is returned as version 1 - the reservation
        // made before the failed audit write must have been released, not committed.
        var store = new InMemoryVersionStore();
        Assert.Throws<EvidenceComparisonAuditUnavailableException>(
            () => ComputeResult([], NfcCorroboration.NotAvailable, new AlwaysFailingAuditSink(), versionNumber: 1, versionStore: store));

        var retried = ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store);

        Assert.Equal(1, retried.VersionNumber);
    }

    [Fact]
    public void AC05_QA04_a_reservation_still_outstanding_for_a_session_blocks_a_second_reservation()
    {
        // While version 1's audit write is "in flight" (here: about to fail), no other version can
        // be reserved for the SAME session - this is what makes the reserve/commit/release protocol
        // atomic end to end, not just at the individual reserve step.
        var store = new InMemoryVersionStore();
        store.AllocateVersion("session-1", 1); // simulates an outstanding, never-resolved reservation

        Assert.Throws<StaleComparisonVersionException>(
            () => ComputeResult([], NfcCorroboration.NotAvailable, versionNumber: 1, versionStore: store));
    }

    [Fact]
    public void AC05_a_blank_actor_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ComputeResult([], NfcCorroboration.NotAvailable, computedByActorId: "  "));
    }

    [Fact]
    public void AC05_a_malformed_timestamp_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ComputeResult([], NfcCorroboration.NotAvailable, computedAt: "not-a-real-timestamp"));
    }

    [Fact]
    public void AC05_QA02_a_bare_date_with_no_time_or_offset_is_rejected_not_silently_accepted()
    {
        // Seyi's exact round-2 reproduction: DateTimeOffset.TryParse alone accepts "2026-09-23"
        // by defaulting the missing time/offset - that is not an RFC 3339 date-time.
        Assert.Throws<ArgumentException>(() => ComputeResult([], NfcCorroboration.NotAvailable, computedAt: "2026-09-23"));
    }

    [Fact]
    public void AC05_QA02_a_timestamp_missing_an_explicit_offset_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ComputeResult([], NfcCorroboration.NotAvailable, computedAt: "2026-09-23T12:00:00"));
    }

    [Fact]
    public void AC05_QA02_a_full_RFC3339_timestamp_with_a_numeric_offset_is_accepted()
    {
        var result = ComputeResult([], NfcCorroboration.NotAvailable, computedAt: "2026-09-23T12:00:00+01:00");

        Assert.Equal("2026-09-23T12:00:00+01:00", result.ComputedAt);
    }

    [Fact]
    public void AC05_a_blank_correlation_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => ComputeResult([], NfcCorroboration.NotAvailable, correlationId: ""));
    }

    private sealed class AlwaysFailingAuditSink : IEvidenceComparisonAuditSink
    {
        public Task WriteAsync(EvidenceComparisonAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false); // genuinely never persisted
    }

    [Fact]
    public void AC05_a_genuinely_unavailable_audit_sink_never_returns_a_successful_comparison()
    {
        Assert.Throws<EvidenceComparisonAuditUnavailableException>(
            () => ComputeResult([], NfcCorroboration.NotAvailable, new AlwaysFailingAuditSink()));
    }

    private sealed class ThrowsButActuallyWroteAuditSink : IEvidenceComparisonAuditSink
    {
        private readonly List<EvidenceComparisonAuditEvent> _events = [];

        public Task WriteAsync(EvidenceComparisonAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            _events.Add(auditEvent); // durably "landed" despite what happens next
            throw new InvalidOperationException("acknowledgement lost, but the write itself committed");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_events.Any(e => e.EventId == eventId));
    }

    [Fact]
    public void AC05_an_ambiguous_write_confirmed_durable_by_ExistsAsync_still_returns_a_successful_comparison()
    {
        // Mirrors ITenantAuditSink's own ambiguous-write resolution (BIO-TEN-001-QA-01 round 4):
        // a WriteAsync that throws is not, on its own, proof nothing was persisted.
        var result = ComputeResult([], NfcCorroboration.NotAvailable, new ThrowsButActuallyWroteAuditSink());

        Assert.NotNull(result.AuditEventId);
    }

    [Fact]
    public void AC03_a_mismatched_field_is_named_in_FieldsRequiringReview_and_forces_review()
    {
        var mismatch = ComparisonEngine.Compare("surname", new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "OKAFOR"),
            [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "OKAFOR-JONES"),
        });

        var result = ComputeResult([mismatch], NfcCorroboration.NotAvailable);

        Assert.Equal(["surname"], result.FieldsRequiringReview);
        Assert.True(result.RequiresManualReview);
    }

    [Fact]
    public void AC03_agreeing_fields_never_require_review_on_their_own()
    {
        var match = ComparisonEngine.Compare("surname", new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "OKAFOR"),
            [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "OKAFOR"),
        });

        var result = ComputeResult([match], NfcCorroboration.NotAvailable);

        Assert.Empty(result.FieldsRequiringReview);
        Assert.False(result.RequiresManualReview);
    }

    [Fact]
    public void AC01_AC03_a_failed_passive_authentication_forces_review_even_when_every_field_agrees()
    {
        var match = ComparisonEngine.Compare("surname", new Dictionary<EvidenceSourceKind, SourceFieldValue>
        {
            [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "OKAFOR"),
            [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "OKAFOR"),
        });
        // Drive a REAL failed passive-authentication result through the actual BIO-DOC-007
        // engine (a deliberately wrong SOD-declared hash) - PassiveAuthenticationResult is
        // locked down (internal init), so this cannot be fabricated via `with` from this
        // assembly, matching every other locked-down result type in this epic.
        var dg1Bytes = new byte[] { 1, 2, 3 };
        var transport = new FakeChipDataGroupTransport(true, new Dictionary<ChipDataGroupId, byte[]> { [ChipDataGroupId.Dg1] = dg1Bytes });
        var (csca, dsc) = SyntheticCertificates.BuildTrustedChain();
        var chipOutcome = ChipReadSession.Read(
            transport, SampleKeys(), [ChipDataGroupId.Dg1], "session-1",
            passiveAuthenticationInputs: new PassiveAuthenticationInputs(
                new Dictionary<ChipDataGroupId, string> { [ChipDataGroupId.Dg1] = "not-the-real-hash" },
                dsc,
                new X509Certificate2Collection(csca)));
        Assert.False(chipOutcome.PassiveAuthentication.Passed);
        var nfc = NfcCorroboration.From(chipOutcome);

        var result = ComputeResult([match], nfc);

        Assert.Empty(result.FieldsRequiringReview); // no field-level mismatch
        Assert.True(result.RequiresManualReview); // but NFC authenticity failure still forces review
    }

    [Fact]
    public void AC01_NfcCorroboration_From_a_real_chip_outcome_reflects_communication_success()
    {
        var transport = new FakeChipDataGroupTransport(true, new Dictionary<ChipDataGroupId, byte[]> { [ChipDataGroupId.Dg1] = [1, 2, 3] });
        var outcome = ChipReadSession.Read(transport, SampleKeys(), [ChipDataGroupId.Dg1], "session-1");

        var corroboration = NfcCorroboration.From(outcome);

        Assert.True(corroboration.Available);
        Assert.True(corroboration.CommunicationSucceeded);
        Assert.Null(corroboration.PassiveAuthenticationPassed); // never attempted in this scenario
    }

    [Fact]
    public void AC01_NfcCorroboration_reflects_a_failed_chip_communication()
    {
        var transport = new FakeChipDataGroupTransport(false, new Dictionary<ChipDataGroupId, byte[]>());
        var outcome = ChipReadSession.Read(transport, SampleKeys(), [ChipDataGroupId.Dg1], "session-1");

        var corroboration = NfcCorroboration.From(outcome);

        Assert.True(corroboration.Available);
        Assert.False(corroboration.CommunicationSucceeded);
    }
}
