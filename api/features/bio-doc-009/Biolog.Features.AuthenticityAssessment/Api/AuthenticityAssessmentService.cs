using Biolog.Features.AuthenticityAssessment.Audit;
using Biolog.Features.AuthenticityAssessment.Demo;
using Biolog.Features.AuthenticityAssessment.Model;
using Biolog.Features.EvidenceComparison.Audit;
using Biolog.Features.EvidenceComparison.Model;

namespace Biolog.Features.AuthenticityAssessment.Api;

/// <summary>
/// Application-layer orchestration for the BIO-DOC-009 HTTP surface (BIO-DOC-009-AC06): ties the
/// store, the audit sink, and (until a real capture/upload pipeline exists - none does yet, see
/// <see cref="SyntheticAuthenticityFixtures"/>'s own remarks) the three named synthetic demo
/// document sessions together. Cycles deterministically through the three sessions by call count -
/// the SAME "deterministic-cycle" mechanism <c>web/lib/module-port.ts</c>'s own mock adapter and
/// this story's frontend sibling (<c>evidence.ts</c>'s <c>runEvidenceScenario</c>) already use, so
/// a human demo or an automated test against this endpoint is reproducible, not flaky.
///
/// <para>
/// <see cref="Get"/>/<see cref="ApplyOverride"/> are NOT currently reachable from any HTTP route
/// (Zainab's BIO-DOC-009-F01 security review - see <see cref="AuthenticityEndpoints"/>'s own
/// remarks for the full reasoning) - kept here as the tested seam a later story, one that builds a
/// real trusted-actor-provenance/RBAC/tenant-context boundary, maps real endpoints onto without
/// this service's own shape changing. See <c>AuthenticityAssessmentServiceTests</c> for direct,
/// non-HTTP coverage proving both still work correctly.
/// </para>
/// </summary>
public sealed class AuthenticityAssessmentService
{
    private static readonly IReadOnlyList<(string SessionId, Func<string, int, IEvidenceComparisonAuditSink, IVersionStore, AuthenticityAssessmentInputs> Build)> DemoScenarios =
    [
        ("demo-session-fully-verified", SyntheticAuthenticityFixtures.FullyVerified),
        ("demo-session-barcode-only", SyntheticAuthenticityFixtures.BarcodeOnly),
        ("demo-session-conflict-detected", SyntheticAuthenticityFixtures.ConflictDetected),
    ];

    private readonly object _gate = new();
    private readonly IAuthenticityAssessmentStore _store;
    private readonly IAuthenticityAssessmentAuditSink _auditSink;
    private readonly Dictionary<string, int> _demoEvidenceVersionBySession = new();
    private int _demoCallCount;

    // This instance's own isolated BIO-DOC-008 version store/audit sink for the synthetic demo
    // fixtures - never a static/shared instance (see SyntheticAuthenticityFixtures's own remarks
    // for the desynchronisation bug that shape caused, found by AuthenticityAssessmentServiceTests).
    private readonly IVersionStore _demoVersionStore = new InMemoryVersionStore();
    private readonly IEvidenceComparisonAuditSink _demoAuditSink = new InMemoryEvidenceComparisonAuditSink();

    public AuthenticityAssessmentService(IAuthenticityAssessmentStore store, IAuthenticityAssessmentAuditSink auditSink)
    {
        _store = store;
        _auditSink = auditSink;
    }

    /// <summary>Computes the next deterministic demo assessment, saves/updates it in the store,
    /// and returns the resulting aggregate.</summary>
    public Model.AuthenticityAssessment ComputeNextDemoAssessment(string actorId, string correlationId)
    {
        lock (_gate)
        {
            var index = _demoCallCount % DemoScenarios.Count;
            _demoCallCount += 1;
            var (sessionId, build) = DemoScenarios[index];

            var evidenceVersion = _demoEvidenceVersionBySession.TryGetValue(sessionId, out var current) ? current + 1 : 1;
            _demoEvidenceVersionBySession[sessionId] = evidenceVersion;

            var inputs = build(sessionId, evidenceVersion, _demoAuditSink, _demoVersionStore);
            var result = AuthenticityAssessmentResult.Compute(inputs, actorId, AuthenticityAssessmentClock.Now(), correlationId, _auditSink);

            var existing = _store.Get(sessionId);
            if (existing is null)
            {
                var created = Model.AuthenticityAssessment.FromResult(result);
                _store.Save(created);
                return created;
            }

            existing.ReplaceResult(result, existing.Version);
            return existing;
        }
    }

    /// <summary>
    /// BIO-OPS-005 (Kemi's ruling, point 3): the real-evidence counterpart to
    /// <see cref="ComputeNextDemoAssessment"/> - added ALONGSIDE it, never replacing it, since
    /// whatever already exercises the demo cycle keeps doing so unchanged. Takes a genuinely
    /// composed <see cref="AuthenticityAssessmentInputs"/> (a real <see cref="CrossComparisonResult"/>
    /// plus real per-engine structural-check outcomes, assembled by BIO-OPS-005's own coordinator -
    /// this method itself still only consumes this story's own declared dependencies, BIO-DOC-006/008)
    /// instead of cycling a fixed, synthetic demo scenario. Same store/audit-sink/create-or-replace
    /// shape as the demo path.
    /// </summary>
    public Model.AuthenticityAssessment ComputeFromEvidence(AuthenticityAssessmentInputs inputs, string actorId, string correlationId)
    {
        lock (_gate)
        {
            var result = AuthenticityAssessmentResult.Compute(inputs, actorId, AuthenticityAssessmentClock.Now(), correlationId, _auditSink);

            var existing = _store.Get(inputs.DocumentSessionId);
            if (existing is null)
            {
                var created = Model.AuthenticityAssessment.FromResult(result);
                _store.Save(created);
                return created;
            }

            existing.ReplaceResult(result, existing.Version);
            return existing;
        }
    }

    public Model.AuthenticityAssessment? Get(string documentSessionId) => _store.Get(documentSessionId);

    public Model.AuthenticityAssessment ApplyOverride(
        string documentSessionId,
        AuthenticityDisposition disposition,
        string reason,
        string overriddenBy,
        string correlationId,
        int expectedVersion)
    {
        var assessment = _store.Get(documentSessionId) ?? throw AuthenticityAssessmentError.AssessmentNotFound(documentSessionId);

        try
        {
            assessment.ApplyManualOverride(
                disposition, reason, overriddenBy, AuthenticityAssessmentClock.Now(), correlationId, expectedVersion, _auditSink);
        }
        catch (AuthenticityAssessmentConcurrencyException)
        {
            throw AuthenticityAssessmentError.ConcurrencyConflict(documentSessionId, expectedVersion, assessment.Version);
        }
        catch (AuthenticityAssessmentAuditUnavailableException)
        {
            throw AuthenticityAssessmentError.AuditUnavailable();
        }

        return assessment;
    }
}
