namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// BIO-IDN-005-QA-01's fix: a genuinely real, tenant-scoped contact-data source, owned entirely
/// by this story - mirroring BIO-IDN-003's own precedent for "institutional identifier" (a
/// sibling aggregate linked by <see cref="PersonId"/>, never a field bolted onto PERSON itself).
/// PERSON (BIO-IDN-001) has no email/phone/address field at all; this feature does not touch
/// that project (no <c>ProjectReference</c> exists, and BIO-IDN-001 is already ACCEPTED and
/// merged) - it owns and stores the contact data ENROLMENT STAFF supply specifically for
/// duplicate-matching purposes.
///
/// <b>Not a communications system of record.</b> This type exists solely to let
/// <see cref="DuplicateReviewService"/> compare two people's on-file contact data
/// (<see cref="DuplicateSignalSource.ContactData"/>) - it is never used to actually contact
/// anyone, verify an address, or send anything. There is exactly one current record per person
/// (natural key <c>(TenantId, PersonId)</c>), not a history - unlike BIO-IDN-003's
/// <c>InstitutionalIdentifier</c>, nothing in this story's ACs requires preserving prior contact
/// values once corrected, so a set simply CAS-replaces the current record.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied here from the start, as
/// every aggregate in this story already does). <see cref="PersonId"/>/<see cref="TenantId"/>
/// are the identity of this aggregate and are <c>get</c>-only - immutable for the object's whole
/// life, including across a <c>with</c> expression. The constructor is <c>internal</c>;
/// <c>InternalsVisibleTo</c> grants the same access already established for
/// <see cref="DuplicateCandidate"/>.
///
/// <b>Normalization.</b> <see cref="Email"/> is stored trimmed and lower-cased (email addresses
/// are conventionally case-insensitive in the local part's domain and almost always so in
/// practice); <see cref="Phone"/> is stored exactly as entered (trimmed) - comparison normalizes
/// it by stripping every non-digit character (see
/// <see cref="DuplicateReviewService"/>'s signal-matching logic), a deliberate simplification
/// that does not attempt international dialing-code/country-code reconciliation.
/// </summary>
public sealed record PersonContactPoint
{
    public string TenantId { get; }
    public string PersonId { get; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal PersonContactPoint(string tenantId, string personId)
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
    }
}
