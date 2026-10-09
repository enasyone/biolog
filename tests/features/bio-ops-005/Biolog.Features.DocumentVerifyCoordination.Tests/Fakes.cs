using Biolog.Features.DocumentVerifyCoordination.Gateways;
using Biolog.Features.DocumentVerifyCoordination.Tenancy;
using Biolog.Features.EvidenceComparison.Model;

namespace Biolog.Features.DocumentVerifyCoordination.Tests;

internal sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>
/// A test double standing in for the real BIO-DOC-009 adapter (which lives in
/// <c>Biolog.Api.Modules</c>, not reachable from this feature-level test project). Derives its
/// own reported state honestly from the REAL <see cref="CrossComparisonResult"/> the coordinator
/// actually computed and passed in - it does not fabricate a disposition independent of that
/// input, so these tests still prove the coordinator's own comparison/conflict-raising logic,
/// just without exercising BIO-DOC-009's own policy engine too (that engine's own test suite
/// already covers it).
/// </summary>
internal sealed class FakeAuthenticityAssessmentGateway : IAuthenticityAssessmentGateway
{
    public int CallCount { get; private set; }
    public CrossComparisonResult? LastCrossComparison { get; private set; }

    public Task<AuthenticityAssessmentSnapshot> ComputeFromEvidenceAsync(
        string documentSessionId,
        EngineCheckInput mrz,
        EngineCheckInput barcode,
        EngineCheckInput ocr,
        CrossComparisonResult crossComparison,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        CallCount += 1;
        LastCrossComparison = crossComparison;

        var state = crossComparison.RequiresManualReview ? "inconclusive" : "authentic";
        var confidence = crossComparison.RequiresManualReview ? 0.5 : 1.0;
        return Task.FromResult(new AuthenticityAssessmentSnapshot(state, confidence, "fake authenticity snapshot"));
    }
}

internal sealed class FakeDocumentIdentificationGateway : IDocumentIdentificationGateway
{
    public int CallCount { get; private set; }
    public IReadOnlyDictionary<string, string>? LastObservedFields { get; private set; }

    public Task<IdentificationSnapshot> IdentifyFromEvidenceAsync(
        string documentSessionId,
        string entryId,
        IReadOnlyDictionary<string, string> observedFields,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        CallCount += 1;
        LastObservedFields = observedFields;
        return Task.FromResult(new IdentificationSnapshot("consistent", 1.0, "fake identification snapshot"));
    }
}
