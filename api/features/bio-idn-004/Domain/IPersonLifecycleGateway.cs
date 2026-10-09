namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>Read-only snapshot of a PERSON's lifecycle state, as reported by <see cref="IPersonLifecycleGateway"/> - deliberately the smallest useful projection of BIO-IDN-001's own <c>Person</c>, never a full copy of it (no PII fields at all).</summary>
public sealed record PersonSnapshot(string PersonId, string TenantId, PersonStatus Status, int Version);

/// <summary>
/// Outcome of <see cref="IPersonLifecycleGateway.ChangeStatusAsync"/>. A Result record, not an
/// exception - see that method's header for why.
/// </summary>
public sealed record LifecycleChangeResult
{
    public required bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public PersonSnapshot? Updated { get; init; }

    public static LifecycleChangeResult Ok(PersonSnapshot updated) => new() { Success = true, Updated = updated };

    public static LifecycleChangeResult Failed(string errorCode) => new() { Success = false, ErrorCode = errorCode };
}

/// <summary>
/// Applies BIO-IDN-002-QA-01's lesson from the start, extended for a WRITE this time. BIO-IDN-002
/// and BIO-IDN-003 each defined a narrow, READ-only <c>IPersonExistenceGateway</c> onto
/// BIO-IDN-001's real store; this story's whole subject is PERSON's own <see cref="PersonStatus"/>,
/// so the seam must also let it WRITE - through the same "feature defines the abstraction it
/// needs; the host wires a concrete implementation" shape, still with NO <c>ProjectReference</c>
/// to BIO-IDN-001's project.
///
/// <c>Biolog.Api</c>'s <c>BioIdn004Module</c> implements this by wrapping BIO-IDN-001's own
/// <c>PersonService.GetAsync</c>/<c>UpdateStatusAsync</c> directly (both modules resolve from
/// the same scoped DI container per request, so this gateway gets BIO-IDN-001's own tenant
/// scoping "for free," exactly as the read-only gateways do).
///
/// <b>Why a Result record, not an exception.</b> BIO-IDN-001's own <c>PersonService</c> throws
/// its own <c>PersonError</c> - a type this project cannot reference, catch, or pattern-match on
/// without a <c>ProjectReference</c> it deliberately does not have. <see cref="LifecycleChangeResult"/>
/// carries a small, stable set of well-known <c>ErrorCode</c> strings
/// (<c>person_not_found</c>, <c>concurrency_conflict</c>, <c>invalid_status_transition</c>,
/// <c>tenant_context_missing</c>) that <c>Biolog.Api</c>'s adapter translates BIO-IDN-001's own
/// error into, and this project's own <c>PersonLifecycleService</c> translates back into its
/// own <c>PersonLifecycleError</c> - never a leaked cross-project exception type.
///
/// <b>Audit-atomicity across a foreign write boundary.</b> Unlike every prior story's repository
/// commit (an in-memory dictionary write with no ambiguous-throw window once its own audit is
/// confirmed), <see cref="ChangeStatusAsync"/> is a call into ANOTHER feature's already-audited,
/// already-locked service - BIO-IDN-001's own <c>UpdateStatusAsync</c> already performs its own
/// complete audit-before-commit sequence internally. From this project's perspective the call is
/// synchronous and unambiguous: it either returns a success snapshot (the status genuinely,
/// durably changed, with BIO-IDN-001's own <c>person.status_change</c> audit event already
/// written) or a failure result (nothing changed). There is no "did it durably land despite
/// throwing" ambiguity to resolve here the way an audit SINK write has - so
/// <c>PersonLifecycleService</c> calls this FIRST, before writing its own supplementary
/// lifecycle-event record, not after (see that class's header "Audit ordering" for the full
/// reasoning, and why that is the opposite order from BIO-IDN-002/003's audit-before-commit
/// discipline for their own self-contained aggregates).
/// </summary>
internal interface IPersonLifecycleGateway
{
    Task<PersonSnapshot?> GetSnapshotAsync(string personId, CancellationToken cancellationToken = default);

    Task<LifecycleChangeResult> ChangeStatusAsync(
        string personId, PersonStatus targetStatus, int expectedVersion, string actorId, string actorTypeWire, string correlationId,
        CancellationToken cancellationToken = default);
}
