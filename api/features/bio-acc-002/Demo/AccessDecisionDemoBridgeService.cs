using Biolog.Features.AccessDecisions.Audit;
using Biolog.Features.AccessDecisions.Domain;
using Biolog.Features.AccessDecisions.Identifiers;

namespace Biolog.Features.AccessDecisions.Demo;

/// <summary>
/// AC05's own bridge: <c>web/features/access/live.ts</c> already POSTs a headerless request to
/// <c>/api/v1/access/primary-action</c> ("Create policy") - BIO-UX-010's own scaffolding,
/// previously genuinely unwired (it generically failed <c>offline</c> - no such backend route
/// existed). <c>view-access-events</c> is deliberately NOT answered here - BIO-ACC-003 already
/// owns and maps that route (see <see cref="Api.AccessDecisionEndpoints"/>'s own header for the
/// discovered collision). This bridge's one action does two real things in sequence: finds-or-
/// publishes a real BIO-ACC-001 demo policy, then computes and persists a real
/// <see cref="AccessDecisionEvent"/> against it through this story's own domain logic - a genuine
/// round trip, not a canned string.
///
/// <b>Why a dedicated bridge instead of routing through <see cref="AccessDecisionService"/>.</b>
/// That service requires a real resolved tenant (Scoped) AND a real ADR-0006-provisioned,
/// permission-granted actor - a headerless demo POST resolves neither. This bridge is registered
/// Singleton and depends only on Singleton-safe collaborators (the in-memory repositories/audit
/// sink and <see cref="IAccessDecisionDemoSeedProvisioner"/>, itself Singleton - see that
/// interface's own header), against the fixed demo tenant/person set BIO-ATT-001's/BIO-VIS-001's/
/// BIO-TRN-001's/BIO-TRN-002's own bridges already use (same ids, reused by convention, not
/// reinvented).
///
/// <b>Biometric evidence is deliberately not exercised here.</b> BIO-BIO-002/003 (fingerprint/face
/// ENROLMENT) are not declared dependencies of this story, so the demo person has no enrolled
/// reference to compare against - <see cref="AccessDecisionService"/>'s own real evidence-gathering
/// path is proven against fakes/the real gateways in this feature's own test suite instead (AC01),
/// where an unenrolled-person outcome is exactly as real and meaningful as a matched one (see
/// <see cref="BiometricComparisonOutcome.NotEnrolled"/>'s own header). This bridge exercises the
/// OTHER half of AC01/AC02/AC03 end to end: gathering a (possibly empty) evidence set, deriving
/// assurance, and evaluating a real published policy for a real decision record.
/// </summary>
internal sealed class AccessDecisionDemoBridgeService(
    IAccessDecisionEventRepository eventRepository,
    IAccessDecisionAuditSink auditSink,
    IAccessDecisionDemoSeedProvisioner demoSeedProvisioner)
{
    public const string DemoTenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    public const string DemoUiActorId = "svc_access_decisions_ui_bridge";
    public const string DemoPolicyKey = "demo-main-gate";

    private static readonly string[] DemoPersonIds =
    [
        "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d",
        "per_0199e0c2-1111-7222-8333-444455556666",
        "per_0199e0c2-2222-7333-9444-555566667777",
    ];

    private int _callCount;

    /// <summary>
    /// "Create policy" (route-manifest.ts's primary action). Finds the fixed demo policy or
    /// publishes it for real the first time (a genuine BIO-ACC-001 aggregate create+publish, not a
    /// stub), then deterministically cycles three demo people, computing a genuine real access
    /// decision for whichever one is due next through the real (demo-scoped) policy evaluation
    /// path, and returns a message describing both real results.
    /// </summary>
    public async Task<string> CreatePolicyAndComputeDemoDecisionAsync(CancellationToken cancellationToken)
    {
        var snapshot = await demoSeedProvisioner.EnsureDemoPolicyAsync(DemoTenantId, cancellationToken);

        var callIndex = Interlocked.Increment(ref _callCount);
        var personId = DemoPersonIds[(callIndex - 1) % DemoPersonIds.Length];
        var occurredAt = UtcInstant.Now();

        var query = new PolicyDecisionQuery
        {
            PolicyKey = DemoPolicyKey,
            PersonId = personId,
            ProfileKey = null,
            CredentialClass = "demo_badge",
            LocationId = "demo_gate_main",
            OccurredAtUtc = occurredAt,
            PresentedCardStatus = CardStatus.Active.ToWireString(),
            PresentedAssuranceLevel = AssuranceLevel.Basic.ToWireString(),
        };
        var policyResult = await demoSeedProvisioner.EvaluateDemoPolicyAsync(DemoTenantId, DemoPolicyKey, query, cancellationToken);

        var decision = AccessDecisionCombiner.Combine(
            CanonicalId.NewEventId(), DemoTenantId, personId, null, query.CredentialClass, query.LocationId,
            CardStatus.Active, [], policyResult, occurredAt, UtcInstant.Now(), CanonicalId.NewEventId(), idempotencyKey: null);

        await eventRepository.CreateAsync(decision, cancellationToken);
        await WriteAuditAsync(decision, cancellationToken);

        return $"Access: demo policy '{snapshot.PolicyKey}' is published (version {snapshot.PublishedVersionNumber}, {snapshot.RuleCount} rule(s)) — decision for {personId} at {query.LocationId} is {decision.Decision.ToWireString()} ({decision.ReasonCode}) (live).";
    }

    private async Task WriteAuditAsync(AccessDecisionEvent decision, CancellationToken cancellationToken)
    {
        var outcome = decision.Decision == AccessDecisionOutcome.Allow ? AuditOutcome.Allowed : AuditOutcome.Denied;
        var auditEvent = AccessDecisionAuditEventBuilder.Build(
            DemoTenantId,
            new AuditActor(ActorType.ServiceAccount, DemoUiActorId),
            "access_decision.computed",
            new AuditTarget("access_decision_event", decision.EventId),
            outcome,
            decision.ReasonCode,
            CanonicalId.NewEventId(),
            null,
            new Dictionary<string, (object?, string)>());

        await auditSink.WriteAsync(auditEvent, cancellationToken);
    }
}
