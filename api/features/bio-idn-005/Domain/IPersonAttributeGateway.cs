namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// BIO-IDN-005-AC01's "contact data" signal source, read through this project's own narrow,
/// read-only seam onto BIO-IDN-001's real store - the same "feature owns the abstraction; the
/// host wires a concrete implementation" shape <see cref="Tenancy.ICurrentTenantProvider"/> and
/// every sibling story's own existence gateway (BIO-IDN-002/003/004's
/// <c>IPersonExistenceGateway</c>) already use. Deliberately no <c>ProjectReference</c> to
/// BIO-IDN-001's project.
///
/// Unlike BIO-IDN-002/003/004's existence-only gateway, this one returns the actual comparable
/// fields <see cref="DuplicateReviewService.ScanForPersonAsync"/> needs (name, date of birth) -
/// never the person's lifecycle <c>Status</c>, which this feature has no reason to read.
/// <c>Biolog.Api</c>'s <c>BioIdn005Module</c> implements it by wrapping BIO-IDN-001's own
/// <c>PersonService.GetAsync</c>/<c>ListAsync</c> directly (already tenant-scoped, same scoped
/// DI container per request - the same "gets tenant scoping for free" reasoning every prior
/// gateway's header documents; BIO-IDN-005-AC04's cross-tenant prohibition is enforced this way,
/// not by any filtering in this feature).
/// </summary>
internal interface IPersonAttributeGateway
{
    Task<PersonAttributes?> GetAsync(string personId, CancellationToken cancellationToken = default);

    /// <summary>Every OTHER person in the resolved tenant, excluding <paramref name="excludingPersonId"/> - never a different tenant's persons (see class header).</summary>
    Task<IReadOnlyList<PersonAttributes>> ListOthersAsync(string excludingPersonId, CancellationToken cancellationToken = default);
}

/// <summary>The narrow slice of BIO-IDN-001's PERSON this feature compares - never the full aggregate, never its lifecycle status.</summary>
public sealed record PersonAttributes(string PersonId, string FirstName, string LastName, string? BornOn);
