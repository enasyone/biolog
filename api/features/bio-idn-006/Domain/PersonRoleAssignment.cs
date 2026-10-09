namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// BIO-IDN-006-AC01/AC02: a PERSON (BIO-IDN-001) holds a <see cref="RoleType"/> within one
/// tenant. Natural key <c>(TenantId, PersonId, RoleType)</c> - the same "no canonical id of its
/// own kind" discipline every sibling aggregate in this epic applies; a person may hold several
/// roles at once (each its own record), and a role held in one tenant grants nothing in another
/// (AC02's tenant-scoping starts here, at the assignment itself, before
/// <see cref="RolePermissionGrant"/> is even consulted).
///
/// <b>Governed transition, never a delete</b> (AC04's audit trail depends on this): revoking a
/// role moves <see cref="Status"/> to <see cref="RoleAssignmentStatus.Revoked"/> - terminal, the
/// record and its history survive forever, mirroring BIO-IDN-003's <c>IdentifierStatus</c>.
/// Re-assigning the SAME role after a revoke reactivates the SAME natural-key record (CAS via
/// <see cref="Version"/>) rather than creating a duplicate - there is only ever one record per
/// <c>(TenantId, PersonId, RoleType)</c>.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start).
/// <see cref="TenantId"/>/<see cref="PersonId"/>/<see cref="Role"/> are the identity of this
/// aggregate and are <c>get</c>-only - immutable for the object's whole life, including across a
/// <c>with</c> expression. The constructor is <c>internal</c>; <c>InternalsVisibleTo</c> grants
/// the same access every sibling story's own aggregate already establishes.
/// </summary>
public sealed record PersonRoleAssignment
{
    public string TenantId { get; }
    public string PersonId { get; }
    public RoleType Role { get; }
    public required RoleAssignmentStatus Status { get; init; }
    public string? RevokedAt { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal PersonRoleAssignment(string tenantId, string personId, RoleType role)
    {
        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!Identifiers.CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        TenantId = tenantId;
        PersonId = personId;
        Role = role;
    }
}
