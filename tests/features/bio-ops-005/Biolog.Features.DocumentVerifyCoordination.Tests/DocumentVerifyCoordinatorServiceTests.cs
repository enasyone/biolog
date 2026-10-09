using Biolog.Features.DocumentVerify.Model;
using Biolog.Features.DocumentVerify.Storage;
using Biolog.Features.DocumentVerifyCoordination.Api;
using Xunit;

namespace Biolog.Features.DocumentVerifyCoordination.Tests;

/// <summary>
/// Kemi's BIO-OPS-005 ruling, point 8: "an end-to-end synthetic test (success, mismatch,
/// unavailable NFC, manual review, accepted/rejected PERSON proposal, replay, concurrent review -
/// Seyi's own list)". Exercises the coordinator's PUBLIC API against the real
/// <see cref="InMemoryDocumentSessionStore"/> and real BIO-DOC-002/003/004/008 engines - only
/// the two cross-story gateways (which live as adapters in <c>Biolog.Api.Modules</c>, not
/// reachable from this feature-level test project) are faked, and even the fake derives its own
/// reported state honestly from the real <see cref="Biolog.Features.EvidenceComparison.Model.CrossComparisonResult"/>
/// the coordinator computed (see <see cref="FakeAuthenticityAssessmentGateway"/>'s own remarks).
/// </summary>
public class DocumentVerifyCoordinatorServiceTests
{
    private const string TenantId = "ten_00000000-0000-7000-8000-000000000001";
    private const string ActorId = "per_00000000-0000-7000-8000-000000000002";
    private const string CorrelationId = "evt_00000000-0000-7000-8000-000000000003";

    private static DocumentVerifyCoordinatorService NewService(
        out IDocumentSessionStore sessionStore,
        out FakeAuthenticityAssessmentGateway authenticityGateway,
        out FakeDocumentIdentificationGateway identificationGateway)
    {
        sessionStore = new InMemoryDocumentSessionStore();
        authenticityGateway = new FakeAuthenticityAssessmentGateway();
        identificationGateway = new FakeDocumentIdentificationGateway();
        return new DocumentVerifyCoordinatorService(
            new FakeCurrentTenantProvider(TenantId),
            sessionStore,
            authenticityGateway,
            identificationGateway,
            new DocumentComparisonVersioningState(),
            new DocumentVerifyLockRegistry());
    }

    [Fact]
    public async Task TenantContextMissing_is_thrown_when_no_tenant_is_resolved()
    {
        var service = new DocumentVerifyCoordinatorService(
            new FakeCurrentTenantProvider(null), new InMemoryDocumentSessionStore(),
            new FakeAuthenticityAssessmentGateway(), new FakeDocumentIdentificationGateway(),
            new DocumentComparisonVersioningState(), new DocumentVerifyLockRegistry());

        var error = await Assert.ThrowsAsync<DocumentVerifyCoordinatorError>(() => service.CaptureAsync(ActorId, CorrelationId));
        Assert.Equal("tenant_context_missing", error.Code);
    }

    [Fact]
    public async Task NoActiveSession_is_thrown_when_a_step_runs_without_a_prior_capture()
    {
        var service = NewService(out _, out _, out _);

        var error = await Assert.ThrowsAsync<DocumentVerifyCoordinatorError>(() => service.RunMrzAsync("dcs_nonexistent", ActorId, CorrelationId));
        Assert.Equal("no_active_session", error.Code);
    }

    [Fact]
    public async Task Capture_then_every_step_succeeds_end_to_end_with_a_genuinely_consistent_document()
    {
        var service = NewService(out var sessionStore, out var authenticityGateway, out var identificationGateway);

        var (sessionId, captureOutcome) = await service.CaptureAsync(ActorId, CorrelationId);
        Assert.Equal("success", captureOutcome.Outcome);
        Assert.Equal("synthetic-fixture", captureOutcome.Provenance.Source);

        var mrz = await service.RunMrzAsync(sessionId, ActorId, CorrelationId);
        Assert.Equal("structurally-valid", mrz.State);
        Assert.Equal("mrz", mrz.Provenance.Source);
        Assert.True(mrz.Confidence > 0.9);

        var barcode = await service.RunBarcodeAsync(sessionId, ActorId, CorrelationId);
        Assert.Equal("structurally-valid", barcode.State);
        Assert.Equal("barcode", barcode.Provenance.Source);

        var ocr = await service.RunOcrAsync(sessionId, ActorId, CorrelationId);
        Assert.Equal("read", ocr.State);
        Assert.Equal("ocr", ocr.Provenance.Source);

        var identify = await service.IdentifyDocumentAsync(sessionId, ActorId, CorrelationId);
        Assert.Equal("consistent", identify.State);
        Assert.Equal(1, identificationGateway.CallCount);
        Assert.Equal("A12345678", identificationGateway.LastObservedFields!["PassportNumber"]);

        var nfc = await service.RunNfcAsync(sessionId, ActorId, CorrelationId);
        Assert.Equal("not-checked", nfc.State);
        Assert.Equal("unavailable", nfc.Provenance.Source);

        var authenticity = await service.CheckAuthenticityAsync(sessionId, ActorId, CorrelationId);
        Assert.Equal("authentic", authenticity.State);
        Assert.Equal(1, authenticityGateway.CallCount);
        Assert.False(authenticityGateway.LastCrossComparison!.RequiresManualReview);

        var selfie = await service.CompareSelfieAsync(sessionId, ActorId, CorrelationId);
        Assert.Equal("authentic", selfie.State);
        Assert.Equal("synthetic-fixture", selfie.Provenance.Source);

        // AC02: a genuine match was actually found (not just "nothing contradicted").
        var fieldEvidence = sessionStore.ListFieldEvidence(TenantId, sessionId);
        Assert.Contains(fieldEvidence, f => f.FieldName == "document_number" && f.Source == FieldEvidenceSource.Mrz);
        Assert.Contains(fieldEvidence, f => f.FieldName == "document_number" && f.Source == FieldEvidenceSource.Ocr);
    }

    [Fact]
    public async Task RunNfcAsync_never_fabricates_a_chip_read_and_appends_no_field_evidence()
    {
        var service = NewService(out var sessionStore, out _, out _);
        var (sessionId, _) = await service.CaptureAsync(ActorId, CorrelationId);

        var outcome = await service.RunNfcAsync(sessionId, ActorId, CorrelationId);

        Assert.Equal("not-checked", outcome.State);
        Assert.Equal("unavailable", outcome.Provenance.Source);
        Assert.Equal(0.0, outcome.Confidence);
        Assert.Empty(sessionStore.ListFieldEvidence(TenantId, sessionId).Where(f => f.Source == FieldEvidenceSource.Nfc));
    }

    /// <summary>AC02 ("cross-source mismatch is visible") / AC03's own "manual review" scenario,
    /// proven together: a genuinely disagreeing second observation (simulating, e.g., a later OCR
    /// re-read that disagrees with the original MRZ read) is injected directly through the SAME
    /// shared <see cref="IDocumentSessionStore"/> the coordinator itself uses - a legitimate
    /// second independent record under the SAME comparison field name, not a special test hook in
    /// production code - and <see cref="DocumentVerifyCoordinatorService.CheckAuthenticityAsync"/>
    /// genuinely detects it.</summary>
    [Fact]
    public async Task A_genuine_cross_source_mismatch_raises_a_conflict_and_routes_the_session_to_manual_review()
    {
        var service = NewService(out var sessionStore, out var authenticityGateway, out _);
        var (sessionId, _) = await service.CaptureAsync(ActorId, CorrelationId);
        await service.RunMrzAsync(sessionId, ActorId, CorrelationId);

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        sessionStore.AppendFieldEvidence(
            TenantId, sessionId, sourceEvidenceSequenceNumber: 1, fieldName: "document_number",
            rawValue: "Z99999999", normalizedValue: "Z99999999", confidence: 0.95,
            source: FieldEvidenceSource.Ocr, boundingBox: null, extractedAt: now, classification: EvidenceClassification.Personal);

        var authenticity = await service.CheckAuthenticityAsync(sessionId, ActorId, CorrelationId);

        Assert.Equal("inconclusive", authenticity.State);
        Assert.True(authenticityGateway.LastCrossComparison!.RequiresManualReview);
        Assert.Contains("document_number", authenticityGateway.LastCrossComparison!.FieldsRequiringReview);

        var session = sessionStore.GetSession(TenantId, sessionId);
        Assert.Equal(DocumentSessionStatus.ManualReviewPending, session.Status);

        var conflicts = sessionStore.ListConflicts(TenantId, sessionId);
        var conflict = Assert.Single(conflicts, c => c.FieldName == "document_number");
        Assert.Equal(ManualReviewState.PendingReview, conflict.ReviewState);

        // Resolving the conflict returns the session out of manual review (BIO-DOC-001-AC05).
        sessionStore.ResolveConflict(TenantId, sessionId, "document_number", "A12345678", "reviewer:qa", now);
        session = sessionStore.GetSession(TenantId, sessionId);
        Assert.NotEqual(DocumentSessionStatus.ManualReviewPending, session.Status);
    }

    /// <summary>AC04/AC05's own "accepted/rejected PERSON proposal" scenario. A session only
    /// produces PERSON-field proposals once SOME other, later step has linked a real person to it
    /// (BIO-DOC-001-AC01's pre-person onboarding path) - simulated here the same way a genuine
    /// future onboarding flow would, directly against the shared store.</summary>
    [Fact]
    public async Task A_person_linked_session_produces_PERSON_proposals_one_of_which_is_accepted_and_one_left_unaccepted()
    {
        var service = NewService(out var sessionStore, out _, out _);
        var (sessionId, _) = await service.CaptureAsync(ActorId, CorrelationId);

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var session = sessionStore.GetSession(TenantId, sessionId);
        sessionStore.LinkPerson(TenantId, sessionId, "per_00000000-0000-7000-8000-000000000099", session.Version, now);

        await service.RunOcrAsync(sessionId, ActorId, CorrelationId);

        var proposals = sessionStore.ListPersonFieldChangeProposals(TenantId, sessionId);
        Assert.NotEmpty(proposals);
        Assert.Contains(proposals, p => p.PersonField == "surname");
        Assert.Contains(proposals, p => p.PersonField == "given_names");
        Assert.All(proposals, p => Assert.Null(p.AppliedAt));

        var surnameProposal = proposals.First(p => p.PersonField == "surname");
        var accepted = surnameProposal.Accept("reviewer:qa", now);
        Assert.NotNull(accepted.AppliedAt);
        Assert.Equal("reviewer:qa", accepted.AppliedBy);

        // The ORIGINAL proposal is untouched (records are immutable) - leaving it, and the
        // given_names proposal, unaccepted is exactly the "rejected" (never accepted) half of
        // this scenario; no separate "reject" state exists in BIO-DOC-001's own domain model.
        Assert.Null(surnameProposal.AppliedAt);
        var givenNamesProposal = proposals.First(p => p.PersonField == "given_names");
        Assert.Null(givenNamesProposal.AppliedAt);
    }

    [Fact]
    public async Task Replaying_the_same_step_twice_appends_further_evidence_without_corrupting_state()
    {
        var service = NewService(out var sessionStore, out _, out _);
        var (sessionId, _) = await service.CaptureAsync(ActorId, CorrelationId);

        var first = await service.RunMrzAsync(sessionId, ActorId, CorrelationId);
        var second = await service.RunMrzAsync(sessionId, ActorId, CorrelationId);

        Assert.Equal("structurally-valid", first.State);
        Assert.Equal("structurally-valid", second.State);
        Assert.NotEqual(first.Provenance.FieldEvidenceSequenceNumbers, second.Provenance.FieldEvidenceSequenceNumbers);

        var mrzFieldEvidence = sessionStore.ListFieldEvidence(TenantId, sessionId).Where(f => f.Source == FieldEvidenceSource.Mrz).ToList();
        // Every MRZ field appended twice (once per RunMrzAsync call) - replay accumulates evidence,
        // it does not deduplicate or overwrite (ADR-0001 Decision 3.3: append-only).
        Assert.Equal(mrzFieldEvidence.Count, mrzFieldEvidence.Select(f => f.FieldName).Distinct().Count() * 2);
    }

    /// <summary>"Concurrent review" - two concurrent <see cref="DocumentVerifyCoordinatorService.CheckAuthenticityAsync"/>
    /// calls for the SAME session never collide on BIO-DOC-008's own version-sequencing protocol
    /// (the exact race <see cref="DocumentVerifyLockRegistry"/> exists to close - see that type's
    /// own remarks).</summary>
    [Fact]
    public async Task Concurrent_CheckAuthenticityAsync_calls_for_the_same_session_never_collide_on_version_number()
    {
        var service = NewService(out _, out _, out _);
        var (sessionId, _) = await service.CaptureAsync(ActorId, CorrelationId);
        await service.RunMrzAsync(sessionId, ActorId, CorrelationId);
        await service.RunOcrAsync(sessionId, ActorId, CorrelationId);

        var results = await Task.WhenAll(
            service.CheckAuthenticityAsync(sessionId, ActorId, CorrelationId),
            service.CheckAuthenticityAsync(sessionId, ActorId, CorrelationId));

        Assert.All(results, r => Assert.Equal("authentic", r.State));
    }
}
