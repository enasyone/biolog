using Biolog.Features.CardIssuance.Identifiers;

namespace Biolog.Features.CardIssuance.Domain;

/// <summary>
/// A physical card issue transaction (BIO-CRD-005). Mutable and CAS-versioned per ADR-0001
/// §3.1/§3.2 (unlike this epic's other card-designer records - <c>PhotoMaster</c>,
/// <c>BrandAsset</c>, BIO-CRD-004's <c>SignatureAsset</c> - which are all immutable/append-only):
/// a card issue genuinely changes state over its own life (encoded, printed, verified,
/// activated, deactivated), and AC04's "activation is transactional" requirement is exactly
/// the compare-and-set guarantee ADR-0001 Decision 3 defines for a mutable aggregate.
///
/// <b>No canonical id of its own kind.</b> ADR-0001's own identifier registry table already
/// anticipates this exact shape: "Credential | crd | tenant plane | Logical credential. A
/// physical card issue is a child record, not this id." No story that mints a <c>crd_</c>
/// Credential id is a declared dependency of this story, so rather than assuming access to an
/// aggregate this story cannot reach, a card issue is keyed by the natural composite
/// <c>(TenantId, PersonId, CardClass, SequenceNumber)</c> - the same "no canonical id of its
/// own kind, keyed by a natural/composite key" choice <c>PhotoMaster</c>, <c>BrandAsset</c> and
/// <c>SignatureAsset</c> each made for an analogous reason. A later story that DOES own real
/// credential issuance can add a <c>CredentialId</c> reference field here without breaking
/// anything, since ADR-0001 itself describes the physical card issue as the child record.
///
/// <b><see cref="CardClass"/></b> is a plain, non-blank business identifier (ADR-0001 §1.6:
/// "business identifiers ... attributes of a record, never its primary key"), not a canonical
/// enum this story has standing to define - no AC enumerates an exact taxonomy of card
/// classes, and tenant policy may need to add one this story cannot anticipate.
///
/// <b>AC01/AC02 (sequence):</b> <see cref="SequenceNumber"/> starts at 1 (rendered "001" on
/// the wire - see <see cref="Api.CardIssueDtos"/>) and is assigned by
/// <see cref="ICardIssueRepository.AppendAsync"/>'s optimistic-retry scheme, the same pattern
/// BIO-CRD-004's own <c>SignatureAsset</c> versioning uses - never reused or reset, since a
/// card issue record is never deleted (the same "governed deletion, no delete operation"
/// principle <c>Person</c>'s own AC04 already established for this codebase: retiring a card
/// is <see cref="CardIssueStatus.Deactivated"/>, never a row removal).
///
/// <b>AC03 (encode/print/verify):</b> <see cref="EncodedAt"/>/<see cref="PrintedAt"/>/
/// <see cref="VerifiedAt"/> are independent, optional, set-once timestamps - each "may precede
/// activation" (the AC's own wording: "may", not "must"), and none is required before
/// <see cref="ActivateAsync"/> succeeds. Re-recording a step that already happened is refused
/// (<see cref="CardIssueError.StepAlreadyRecorded"/>) - a genuine re-encode/re-print/re-verify
/// event is a new physical card, hence a new card issue, not a correction to this one.
///
/// <b>AC04 (transactional activation, at most one active card):</b> <see cref="Status"/> is
/// this record's own lifecycle (<see cref="CardIssueStatus"/>); the "at most one active per
/// (person, card class)" invariant is enforced by <see cref="CardIssueService.ActivateAsync"/>
/// under a per-(person, card class) lock (the same audit-atomicity locking pattern every
/// mutable aggregate in this codebase uses), not by this record alone - a single record cannot
/// see its siblings.
///
/// <b>AC05 (provenance):</b> <see cref="Provenance"/>, captured once at issuance - see that
/// type's own header.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start) - every
/// field is <c>get</c>-only or <c>init</c>-only on a record whose own constructor is
/// <c>internal</c>, restricted to this assembly, <c>Biolog.Api</c> and this project's own test
/// assembly via <c>InternalsVisibleTo</c>.
/// </summary>
public sealed record CardIssue
{
    public string TenantId { get; }
    public string PersonId { get; }
    public string CardClass { get; }
    public int SequenceNumber { get; }
    public required CardIssueStatus Status { get; init; }
    public required int Version { get; init; }
    public required CardIssueProvenance Provenance { get; init; }
    public string? EncodedAt { get; init; }
    public string? PrintedAt { get; init; }
    public string? VerifiedAt { get; init; }
    public string? ActivatedAt { get; init; }
    public string? DeactivatedAt { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal CardIssue(string tenantId, string personId, string cardClass, int sequenceNumber)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (string.IsNullOrWhiteSpace(cardClass))
        {
            throw new ArgumentException("cardClass must not be blank", nameof(cardClass));
        }

        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), sequenceNumber, "a card issue's sequence number starts at 1");
        }

        TenantId = tenantId;
        PersonId = personId;
        CardClass = cardClass;
        SequenceNumber = sequenceNumber;
    }
}
