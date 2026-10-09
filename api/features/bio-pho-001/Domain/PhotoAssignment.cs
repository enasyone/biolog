namespace Biolog.Features.Photography.Domain;

/// <summary>
/// Which <see cref="PhotoMaster"/> VERSION is currently the active photo for a PERSON
/// (BIO-PHO-001-AC04: "person-photo assignment is versioned and audited"). Unlike
/// <see cref="PhotoMaster"/>, this IS a mutable tenant aggregate under ADR-0001 §3.1/§3.2 - it
/// is not on §3.3's append-only list, and it is exactly the kind of pointer that needs to
/// change over time (a person is re-photographed; the assignment moves to point at the new
/// master version without needing a new master lineage of its own).
///
/// <see cref="Version"/> here IS the real optimistic-concurrency token §3.1 defines: starts at
/// 1 on the first assignment, increments by exactly 1 on each committed reassignment, and
/// every mutating write states the version it believes it is changing
/// (<see cref="PhotoAssignmentService.AssignAsync"/>'s <c>expectedVersion</c> parameter,
/// compare-and-set against <see cref="IPhotoAssignmentRepository.ReplaceAsync"/>) - this is
/// the opposite immutability shape from <see cref="PhotoMaster"/> in the same feature, which is
/// the point: AC01 and AC04 name two different ADR-0001 categories on purpose, and this type
/// is why a person-child aggregate can be either.
///
/// "Audited" (AC04): every committed change to <see cref="CurrentMasterVersion"/> is recorded
/// via <see cref="Audit.PhotoAuditEvent"/> BEFORE the repository commit (ADR-0003 Decision 7's
/// audit-atomicity pattern, held under <see cref="PhotoAssignmentLockRegistry"/>) - see
/// <see cref="PhotoAssignmentService"/>'s header.
///
/// <b>No canonical id of its own kind</b> - same reasoning as <see cref="PhotoMaster"/>'s
/// header. Natural key: <c>PersonId</c> alone (at most one assignment per person).
/// </summary>
public sealed record PhotoAssignment
{
    public string PersonId { get; }
    public string TenantId { get; }
    public required int? CurrentMasterVersion { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal PhotoAssignment(string personId, string tenantId)
    {
        if (!Identifiers.CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        PersonId = personId;
        TenantId = tenantId;
    }
}
