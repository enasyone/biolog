namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// Applies BIO-IDN-002-QA-01's lesson from the start: a role assignment that never proves the
/// PERSON it names actually exists in the resolved tenant would let a caller permanently audit a
/// role grant for a wholly synthetic or foreign-tenant person id - orphan/false-linked
/// operational history, the exact class of gap that story's own review found.
/// <see cref="RoleAssignmentService.AssignAsync"/> proves existence through this gateway before
/// anything else, exactly as every sibling story's own remediation does.
///
/// The same "narrow, read-only BIO-IDN-001 existence gateway" shape BIO-IDN-002/003/004/005 all
/// established: a minimal interface THIS project owns, one method, a boolean, nothing about a
/// person's own fields - no <c>ProjectReference</c> to BIO-IDN-001's project.
///
/// <c>Biolog.Api</c>'s <c>BioIdn006Module</c> implements this by wrapping BIO-IDN-001's own
/// <c>PersonService.GetAsync</c> directly - the same tenant-scoping-for-free reasoning every
/// prior gateway's header documents.
/// </summary>
internal interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}
