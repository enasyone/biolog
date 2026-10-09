namespace Biolog.Features.PersonProfileRegistry.Domain;

/// <summary>
/// Storage for <see cref="PersonProfile"/>, keyed by <c>(PersonId, Type)</c>. Deliberately has
/// NO delete/remove method (mirrors BIO-IDN-001-AC04's "governed deletion" ethos: revoking a
/// profile - the only lifecycle exit - never removes the record, so operational history that
/// referenced it is never orphaned).
///
/// <b><c>internal</c>, from the start (applying BIO-IDN-001-QA-01's lesson before a review
/// round finds it here too).</b> A public repository plus a public aggregate constructor would
/// let any code that can reference this assembly bypass <see cref="PersonProfileService"/>
/// entirely - skip the tenant boundary, the duplicate-profile-type check and the audit trail,
/// exactly as an independent probe proved against BIO-IDN-001's PERSON aggregate. This
/// interface, its implementation, and <see cref="PersonProfile"/>'s constructor are all
/// <c>internal</c>; <c>InternalsVisibleTo</c> in this project's <c>.csproj</c> grants the same
/// access to <c>Biolog.Api</c> (DI registration) and
/// <c>Biolog.Features.PersonProfileRegistry.Tests</c> (an explicit, auditable test seam).
/// <see cref="InMemoryPersonProfileRepository"/> also independently re-validates identity
/// immutability and initial state at the persistence boundary, not merely trusting restricted
/// access - the same dual-boundary discipline <c>CredentialReferenceValidator</c>
/// (BIO-DEV-001-QA-04) established.
///
/// Every read and write method takes <c>tenantId</c> explicitly and asserts a matching
/// record's own <see cref="PersonProfile.TenantId"/> before returning it - the same
/// belt-and-braces rule every sibling tenant-plane story's repository applies.
/// </summary>
internal interface IPersonProfileRepository
{
    Task<PersonProfile> CreateAsync(PersonProfile profile, CancellationToken cancellationToken = default);

    Task<PersonProfile?> GetAsync(string tenantId, string personId, ProfileType type, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersonProfile>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 Decision 3.2).</summary>
    Task<PersonProfile> ReplaceAsync(PersonProfile expectedCurrent, PersonProfile updated, CancellationToken cancellationToken = default);
}
