using Biolog.Features.PersonLifecycle;
using Biolog.Features.PersonLifecycle.Api;
using Biolog.Features.PersonLifecycle.Audit;
using Biolog.Features.PersonLifecycle.Domain;
using Biolog.Features.PersonLifecycle.Identifiers;
using Biolog.Features.PersonLifecycle.Tenancy;

const string tenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
const string personId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
var actor = new AuditActor(ActorType.User, "synthetic_admin");

// Round-two regression: a rejected suspension must emit nothing.
var rejectedRevocations = new InMemoryCredentialAccessRevocationEventSink();
var rejected = new PersonLifecycleService(
    new FixedTenant(tenantId),
    new RejectingGateway(new PersonSnapshot(personId, tenantId, PersonStatus.Active, 2)),
    new InMemoryPersonLifecycleAuditSink(), rejectedRevocations,
    new InMemoryPendingSuspensionReconciliationStore(), new PersonLifecycleLockRegistry());
try
{
    await rejected.TransitionAsync(personId, Request("SUSPENDED", 1), null, actor, CanonicalId.NewEventId());
    throw new Exception("expected stale suspension rejection");
}
catch (PersonLifecycleError error) when (error.Code == "concurrency_conflict") { }
if (rejectedRevocations.Events.Count != 0)
    throw new Exception("rejected suspension emitted a false revocation");
Console.WriteLine("PASS: rejected suspension emitted no credential/access revocation");

// Round-three regression: pending work must survive reactivation and remain reconcilable.
var sharedGateway = new CommittingGateway(new PersonSnapshot(personId, tenantId, PersonStatus.Active, 1));
var sharedPending = new InMemoryPendingSuspensionReconciliationStore();
var audit = new InMemoryPersonLifecycleAuditSink();
var failedPublish = new PersonLifecycleService(
    new FixedTenant(tenantId), sharedGateway, audit, new UnavailableRevocationSink(), sharedPending, new PersonLifecycleLockRegistry());
try
{
    await failedPublish.TransitionAsync(personId, Request("SUSPENDED", 1), null, actor, CanonicalId.NewEventId());
    throw new Exception("expected pending reconciliation");
}
catch (PersonLifecycleError error) when (error.Code == "revocation_pending_reconciliation") { }

var recoveredRevocations = new InMemoryCredentialAccessRevocationEventSink();
var recovered = new PersonLifecycleService(
    new FixedTenant(tenantId), sharedGateway, audit, recoveredRevocations, sharedPending, new PersonLifecycleLockRegistry());
await recovered.TransitionAsync(
    personId, Request("ACTIVE", 2, "synthetic security review"), PermissionResolver.ReactivatePersonPermission,
    actor, CanonicalId.NewEventId());
if (!await recovered.HasPendingRevocationsAsync(personId))
    throw new Exception("reactivation erased pending reconciliation");
await recovered.ReconcilePendingRevocationsAsync(personId, actor, CanonicalId.NewEventId());
if (await recovered.HasPendingRevocationsAsync(personId) || recoveredRevocations.Events.Count != 1)
    throw new Exception("durable reconciliation did not converge exactly once");
Console.WriteLine("PASS: pending revocation survived reactivation and reconciled exactly once");

// New failure window: supplementary audit runs after commit but before MarkPendingAsync.
var auditFailureGateway = new CommittingGateway(new PersonSnapshot(personId, tenantId, PersonStatus.Active, 1));
var auditFailurePending = new InMemoryPendingSuspensionReconciliationStore();
var auditFailureRevocations = new InMemoryCredentialAccessRevocationEventSink();
var auditFailureService = new PersonLifecycleService(
    new FixedTenant(tenantId), auditFailureGateway, new UnavailableAuditSink(), auditFailureRevocations,
    auditFailurePending, new PersonLifecycleLockRegistry());
try
{
    await auditFailureService.TransitionAsync(personId, Request("SUSPENDED", 1), null, actor, CanonicalId.NewEventId());
    throw new Exception("expected supplementary audit failure");
}
catch (PersonLifecycleError error) when (error.Code == "audit_unavailable") { }

if (auditFailureGateway.Current.Status == PersonStatus.Suspended &&
    !await auditFailureService.HasPendingRevocationsAsync(personId) &&
    auditFailureRevocations.Events.Count == 0)
{
    Console.Error.WriteLine("FAIL: suspension committed, but audit failure prevented both pending tracking and revocation emission");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("PASS: every committed suspension retained guaranteed revocation work despite audit failure");

static TransitionRequest Request(string target, int version, string? reason = null) =>
    new() { TargetStatus = target, Version = version, Reason = reason };

file sealed class FixedTenant(string tenantId) : ICurrentTenantProvider
{
    public string? TenantId => tenantId;
}

file sealed class CommittingGateway(PersonSnapshot current) : IPersonLifecycleGateway
{
    public PersonSnapshot Current { get; private set; } = current;
    public Task<PersonSnapshot?> GetSnapshotAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PersonSnapshot?>(Current.PersonId == personId ? Current : null);
    public Task<LifecycleChangeResult> ChangeStatusAsync(
        string personId, PersonStatus targetStatus, int expectedVersion, string actorId, string actorTypeWire,
        string correlationId, CancellationToken cancellationToken = default)
    {
        Current = Current with { Status = targetStatus, Version = Current.Version + 1 };
        return Task.FromResult(LifecycleChangeResult.Ok(Current));
    }
}

file sealed class RejectingGateway(PersonSnapshot current) : IPersonLifecycleGateway
{
    public Task<PersonSnapshot?> GetSnapshotAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PersonSnapshot?>(current.PersonId == personId ? current : null);
    public Task<LifecycleChangeResult> ChangeStatusAsync(
        string personId, PersonStatus targetStatus, int expectedVersion, string actorId, string actorTypeWire,
        string correlationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(LifecycleChangeResult.Failed("concurrency_conflict"));
}

file sealed class UnavailableRevocationSink : ICredentialAccessRevocationEventSink
{
    public Task PublishAsync(CredentialAccessRevocationEvent revocationEvent, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("synthetic revocation transport outage");
    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("synthetic revocation transport outage");
    public Task<bool> ExistsForSuspensionAsync(string tenantId, string personId, int suspendedAtVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

file sealed class UnavailableAuditSink : IPersonLifecycleAuditSink
{
    public Task WriteAsync(PersonLifecycleAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("synthetic audit outage");
    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("synthetic audit outage");
    public Task<IReadOnlyList<PersonLifecycleAuditEvent>> ListByPersonAsync(
        string tenantId, string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PersonLifecycleAuditEvent>>([]);
}
