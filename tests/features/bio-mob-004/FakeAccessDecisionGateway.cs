using Biolog.Features.MobileVerification.Audit;
using Biolog.Features.MobileVerification.Domain;

namespace Biolog.Features.MobileVerification.Tests;

/// <summary>
/// Test double for <see cref="IAccessDecisionGateway"/> - BIO-ACC-002's own <c>AccessDecisionService</c>
/// (and its own dependency graph: PersonRegistry, AccessPolicy, FingerprintVerification,
/// FaceVerification) is deliberately NOT stood up here. This project owns only BIO-MOB-004's own
/// endpoints/role gate/override recording; BIO-ACC-002's own decision correctness (including its
/// idempotent-replay guarantee) is that story's own already-reviewed responsibility
/// (evidence/BIO-ACC-002), not re-proven by this test project. The real in-process wiring
/// (`BioMob004Module.AccessDecisionGatewayAdapter`) is exercised by the full-host `dotnet build`
/// (compiles against BIO-ACC-002's actual internal types) plus manual end-to-end evidence.
/// </summary>
public sealed class FakeAccessDecisionGateway : IAccessDecisionGateway
{
    public List<(string TenantId, VerificationDecisionQuery Query, AuditActor Actor, string CorrelationId)> DecideCalls { get; } = [];
    public VerificationDecisionResult? NextResult { get; set; }
    public Exception? NextException { get; set; }
    public IReadOnlyList<VerificationDecisionResult> HistoryToReturn { get; set; } = [];

    public Task<VerificationDecisionResult> DecideAsync(string tenantId, VerificationDecisionQuery query, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        DecideCalls.Add((tenantId, query, actor, correlationId));
        if (NextException is not null)
        {
            throw NextException;
        }

        return Task.FromResult(NextResult ?? DefaultResult(query));
    }

    public Task<IReadOnlyList<VerificationDecisionResult>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(HistoryToReturn);

    private static VerificationDecisionResult DefaultResult(VerificationDecisionQuery query) => new()
    {
        EventId = $"evt_{Guid.CreateVersion7():D}",
        PersonId = query.PersonId,
        Decision = "allow",
        ReasonCode = "policy_matched",
        PresentedAssuranceLevel = "BASIC",
        OccurredAt = "2026-09-30T00:00:00.000Z",
        RecordedAt = "2026-09-30T00:00:00.100Z",
        CorrelationId = "evt_default_correlation_0000000",
        IdempotencyKey = query.IdempotencyKey,
    };
}
