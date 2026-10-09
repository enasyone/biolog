namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// BIO-IDN-005-AC01's "identifiers" signal source, read through this project's own narrow,
/// read-only seam onto BIO-IDN-003's real store - the same shape
/// <see cref="IPersonAttributeGateway"/> uses for BIO-IDN-001. Deliberately no
/// <c>ProjectReference</c> to BIO-IDN-003's project.
///
/// <c>Biolog.Api</c>'s <c>BioIdn005Module</c> implements it by wrapping BIO-IDN-003's own
/// <c>InstitutionalIdentifierService.ListAsync(personId)</c> directly, filtered to
/// <c>Active</c> records only - a RETIRED value being legitimately reassigned to a different
/// person (BIO-IDN-003's own "Retire-then-reassign" correction pattern, see
/// <c>InstitutionalIdentifier</c>'s header) is that story's own designed behaviour, not a
/// duplicate signal this feature should raise.
/// </summary>
internal interface IIdentifierAttributeGateway
{
    Task<IReadOnlyList<IdentifierAttribute>> ListActiveValuesAsync(string personId, CancellationToken cancellationToken = default);
}

/// <summary>The narrow slice of a BIO-IDN-003 institutional identifier this feature compares - never a credential id (BIO-IDN-003-AC04 boundary), never retired history.</summary>
public sealed record IdentifierAttribute(string Type, string? CustomLabel, string Value);
