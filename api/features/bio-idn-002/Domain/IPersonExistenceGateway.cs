namespace Biolog.Features.PersonProfileRegistry.Domain;

/// <summary>
/// BIO-IDN-002-QA-01 (Seyi round 1, High): before this fix, <see cref="PersonProfileService.AssignAsync"/>
/// validated only that <c>person_id</c> had canonical <c>per_</c> SYNTAX - it never proved the
/// PERSON actually existed in the resolved tenant. An independent probe assigned (and
/// permanently audited) a profile for a tenant-B caller against a wholly synthetic person id
/// that was never created anywhere, and the identical path would let tenant B attach its own
/// profile data to a real tenant-A PERSON id (canonical identifiers carry no authority,
/// ADR-0001 §1.5). Both are orphan/false-linked operational history, and both violate
/// this story's own AC04 premise ("attach to an EXISTING person").
///
/// This is the "narrow, read-only BIO-IDN-001 existence gateway" the review's required
/// closure asks for: a minimal interface THIS project owns (matching the same "feature
/// defines the abstraction it needs; the host wires a concrete implementation" seam
/// <see cref="Tenancy.ICurrentTenantProvider"/> already uses for tenant context). It is
/// deliberately the smallest possible surface - one method, a boolean, nothing about a
/// person's own fields - so this project gains no broader coupling to BIO-IDN-001 than the
/// one fact it actually needs.
///
/// <c>Biolog.Api</c>'s <c>BioIdn002Module</c> implements this by wrapping BIO-IDN-001's own
/// <c>PersonService.GetAsync</c> directly (both modules are registered in the same host and
/// see the same resolved tenant context for a given request) - which gets this gateway's own
/// tenant scoping "for free": BIO-IDN-001's <c>PersonService</c> already resolves the current
/// tenant itself and its repository already asserts a record's own tenant match, so a person
/// that exists only in a DIFFERENT tenant already reads as "does not exist" through this
/// gateway with no extra logic here - the exact "return the same not-found response for
/// missing and foreign-tenant identifiers" the review requires.
/// </summary>
internal interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}
