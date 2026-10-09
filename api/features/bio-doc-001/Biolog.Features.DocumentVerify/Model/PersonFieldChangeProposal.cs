namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// A proposed change to one field on a PERSON record, derived from document evidence
/// (BIO-DOC-001-AC04: "document results never silently overwrite PERSON fields").
///
/// <para>
/// <b>How AC04 is enforced, not merely documented.</b> No type in this feature - not this one,
/// not <see cref="DocumentSession"/>, not any evidence record - has a method that writes to a
/// Person aggregate; this codebase does not even have a Person domain module yet for one to
/// write to. The only artifact this feature ever produces "toward" a person is this record, and
/// it is inert by construction:
/// <list type="bullet">
/// <item><see cref="Propose"/> is the only factory and it always returns an instance with
/// <see cref="AppliedAt"/> and <see cref="AppliedBy"/> null - there is no constructor path that
/// produces an already-applied proposal.</item>
/// <item>The only way to obtain an applied value is <see cref="Accept"/>, which requires a
/// non-empty <paramref name="acceptedBy"/> actor and returns a NEW record - the original,
/// unapplied proposal is left untouched (records are immutable), so a caller holding a reference
/// to the original can never observe it silently becoming "applied" out from under it.</item>
/// <item><see cref="Accept"/> throws if the field evidence it was built from is
/// <see cref="RequiresManualReview"/> (a <see cref="DocumentConflict"/> exists for this field) -
/// a conflicted field cannot be auto-accepted; a human must resolve the conflict first
/// (AC05) and accept explicitly afterward.</item>
/// </list>
/// Applying an accepted proposal to the actual PERSON record is a Person-domain write, owned by
/// whichever future story builds that aggregate; this record is the auditable, non-destructive
/// handoff to it - it is deliberately not this feature's job to perform that write, since this
/// feature has no Person aggregate to write to and inventing one here would be exactly the kind
/// of shared-contract overreach the builder prompt's "do not modify shared contracts... without
/// Kemi approval" constraint exists to prevent.
/// </para>
/// </summary>
public sealed record PersonFieldChangeProposal
{
    // QA-03 (Chioma acceptance audit) applied proactively here too, even though the finding named
    // only DocumentEvidence/DocumentFieldEvidence/BoundingBox: the identical bypass
    // (`new PersonFieldChangeProposal { AppliedAt = "...", AppliedBy = "..." }`) would have been
    // strictly worse for this type specifically, since it would let a caller fabricate an
    // already-applied proposal without ever calling Accept - directly defeating the "never
    // silently overwrite PERSON fields" guarantee this type exists to enforce, not just weakening
    // an integrity claim the way the other three findings did. Every `init` is `private`;
    // `AppliedAt`/`AppliedBy` are additionally reachable from `Accept`'s `this with { ... }`
    // because that expression executes as a member of this type, same as `Propose`'s constructor.
    public string TenantId { get; private init; } = null!;
    public string DocumentSessionId { get; private init; } = null!;
    public string PersonId { get; private init; } = null!;

    /// <summary>Name of the PERSON field this proposes to set, e.g. <c>"date_of_birth"</c>.</summary>
    public string PersonField { get; private init; } = null!;

    public string ProposedValue { get; private init; } = null!;

    /// <summary>Provenance: which <see cref="DocumentFieldEvidence.SequenceNumber"/> in the same
    /// session this proposal is derived from.</summary>
    public int SourceFieldEvidenceSequenceNumber { get; private init; }

    /// <summary>True when a <see cref="DocumentConflict"/> exists for <see cref="PersonField"/>
    /// in this session - <see cref="Accept"/> refuses while this is true (AC05 gates AC04).</summary>
    public bool RequiresManualReview { get; private init; }

    public string ProposedAt { get; private init; } = null!;

    /// <summary>Null until <see cref="Accept"/> produces an accepted copy. Never null without
    /// <see cref="AppliedBy"/> also being non-null, and vice versa.</summary>
    public string? AppliedAt { get; private init; }

    /// <summary>The actor (reviewer id, or an explicit automated-policy id) who accepted this
    /// proposal. Required by <see cref="Accept"/> - there is no anonymous or implicit acceptance.</summary>
    public string? AppliedBy { get; private init; }

    // See DocumentEvidence's identical private constructor for why this is required, not
    // cosmetic. For THIS type specifically the stakes are highest of all four: without it,
    // `new PersonFieldChangeProposal { AppliedAt = "...", AppliedBy = "..." }` would let a caller
    // fabricate an already-applied proposal with no Accept call at all - the exact "silently
    // overwrite PERSON fields" outcome this type's whole design exists to make unrepresentable.
    private PersonFieldChangeProposal()
    {
    }

    public static PersonFieldChangeProposal Propose(
        string tenantId,
        string documentSessionId,
        string personId,
        string personField,
        string proposedValue,
        int sourceFieldEvidenceSequenceNumber,
        bool requiresManualReview,
        string proposedAt)
    {
        DocumentEvidence.RequireNonEmpty(tenantId, nameof(tenantId));
        DocumentEvidence.RequireNonEmpty(documentSessionId, nameof(documentSessionId));
        DocumentEvidence.RequireNonEmpty(personId, nameof(personId));
        DocumentEvidence.RequireNonEmpty(personField, nameof(personField));
        ArgumentNullException.ThrowIfNull(proposedValue);
        if (sourceFieldEvidenceSequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceFieldEvidenceSequenceNumber), sourceFieldEvidenceSequenceNumber, "must be >= 1");
        }
        DocumentEvidence.RequireNonEmpty(proposedAt, nameof(proposedAt));

        return new PersonFieldChangeProposal
        {
            TenantId = tenantId,
            DocumentSessionId = documentSessionId,
            PersonId = personId,
            PersonField = personField,
            ProposedValue = proposedValue,
            SourceFieldEvidenceSequenceNumber = sourceFieldEvidenceSequenceNumber,
            RequiresManualReview = requiresManualReview,
            ProposedAt = proposedAt,
            AppliedAt = null,
            AppliedBy = null,
        };
    }

    /// <summary>
    /// Returns a NEW, accepted copy of this proposal. Does not mutate <c>this</c> - the original,
    /// unapplied instance remains exactly as proposed.
    /// </summary>
    public PersonFieldChangeProposal Accept(string acceptedBy, string acceptedAt)
    {
        DocumentEvidence.RequireNonEmpty(acceptedBy, nameof(acceptedBy));
        DocumentEvidence.RequireNonEmpty(acceptedAt, nameof(acceptedAt));
        if (RequiresManualReview)
        {
            throw new InvalidOperationException(
                $"person_field_proposal_requires_manual_review: '{PersonField}' on session '{DocumentSessionId}' has an open conflict; resolve it before accepting.");
        }
        if (AppliedAt is not null)
        {
            throw new InvalidOperationException("person_field_proposal_already_applied");
        }

        return this with { AppliedAt = acceptedAt, AppliedBy = acceptedBy };
    }
}
