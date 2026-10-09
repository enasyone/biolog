namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// BIO-IDN-005-AC01: "Candidate duplicates can arise from identifiers/contact data and later
/// biometric signals."
///
/// <b>BIO-IDN-005-QA-01 (Seyi round 1, High).</b> The original round 1 submission treated
/// <see cref="NameAndDateOfBirth"/> as this story's "contact data" signal, since PERSON
/// (BIO-IDN-001) has no email/phone/address field at all. The review correctly rejected this: a
/// name and a date of birth are identity/demographic attributes, not contact information, and a
/// lower-level implementation note cannot redefine an approved acceptance criterion. Rather than
/// escalate for a scope change (BIO-IDN-001 is already ACCEPTED and merged; reopening it is a
/// cross-story governance question this story does not need to raise), this story now owns a
/// genuinely real, tenant-scoped contact-data source of its own -
/// <see cref="PersonContactPoint"/> - mirroring the precedent BIO-IDN-003 already set for
/// "institutional identifier" (a sibling aggregate linked by <c>PersonId</c>, never a field
/// bolted onto PERSON itself). <see cref="ContactData"/> fires when the subject and another
/// person's <see cref="PersonContactPoint"/> share a normalized, non-blank email or phone value.
///
/// <see cref="Biometric"/> is registered now, deliberately unreachable in this story - no
/// biometric capture story exists anywhere in this codebase yet (confirmed by a repo-wide search
/// before designing this feature). It exists so a FUTURE biometric-matching story (BIO-BIO-001/
/// 002, the same "duplicate-review" conflict group as this one -
/// governance/feature-path-ownership.json) can raise a candidate carrying this value through
/// <see cref="DuplicateReviewService"/>'s existing internal signal-accumulation path
/// (<c>RaiseOrAugmentAsync</c>) without this story needing to guess at that story's own matching
/// algorithm or data shape - the extensibility point is the enum member and the accumulation
/// design, not a stub method or an unused endpoint.
/// </summary>
public enum DuplicateSignalSource
{
    NameAndDateOfBirth,
    InstitutionalIdentifierValue,
    ContactData,
    Biometric,
}

public static class DuplicateSignalSourceExtensions
{
    public static string ToWireString(this DuplicateSignalSource source) => source switch
    {
        DuplicateSignalSource.NameAndDateOfBirth => "NAME_AND_DATE_OF_BIRTH",
        DuplicateSignalSource.InstitutionalIdentifierValue => "INSTITUTIONAL_IDENTIFIER_VALUE",
        DuplicateSignalSource.ContactData => "CONTACT_DATA",
        DuplicateSignalSource.Biometric => "BIOMETRIC",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out DuplicateSignalSource source)
    {
        switch (value)
        {
            case "NAME_AND_DATE_OF_BIRTH": source = DuplicateSignalSource.NameAndDateOfBirth; return true;
            case "INSTITUTIONAL_IDENTIFIER_VALUE": source = DuplicateSignalSource.InstitutionalIdentifierValue; return true;
            case "CONTACT_DATA": source = DuplicateSignalSource.ContactData; return true;
            case "BIOMETRIC": source = DuplicateSignalSource.Biometric; return true;
            default: source = default; return false;
        }
    }
}
