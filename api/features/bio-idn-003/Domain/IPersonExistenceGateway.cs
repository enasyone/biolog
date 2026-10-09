namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// Applies BIO-IDN-002-QA-01's lesson from the start, not after a review round finds the same
/// class of gap here too. BIO-IDN-002's round-1 review found that validating only
/// <c>person_id</c>'s canonical <c>per_</c> SYNTAX (never its EXISTENCE in the resolved tenant)
/// let a probe assign, and permanently audit, an identifier for a wholly synthetic or
/// foreign-tenant person id - orphan/false-linked operational history that defeated that
/// story's own "attach to an EXISTING person" premise. This story's premise is identical
/// ("matriculation, employee and other business identifiers linked to PERSON"), so
/// <see cref="InstitutionalIdentifierService.AssignAsync"/> proves existence through this
/// gateway before anything else, exactly as BIO-IDN-002's remediation does.
///
/// This is the same "narrow, read-only BIO-IDN-001 existence gateway" shape BIO-IDN-002
/// established: a minimal interface THIS project owns (matching the same "feature defines the
/// abstraction it needs; the host wires a concrete implementation" seam
/// <see cref="Tenancy.ICurrentTenantProvider"/> already uses for tenant context) - deliberately
/// the smallest possible surface, one method, a boolean, nothing about a person's own fields -
/// so this project gains no broader coupling to BIO-IDN-001 than the one fact it actually
/// needs, and still no <c>ProjectReference</c> to BIO-IDN-001's project.
///
/// <c>Biolog.Api</c>'s <c>BioIdn003Module</c> implements this by wrapping BIO-IDN-001's own
/// <c>PersonService.GetAsync</c> directly (both modules are registered in the same host and
/// see the same resolved tenant context for a given request) - which gets this gateway's own
/// tenant scoping "for free": a person that exists only in a DIFFERENT tenant already reads as
/// "does not exist" through this gateway with no extra logic here - the same "return the same
/// not-found response for missing and foreign-tenant identifiers" behaviour BIO-IDN-002's
/// remediation proved through its own real-host tests.
/// </summary>
internal interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}
