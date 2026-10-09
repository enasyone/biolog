namespace Biolog.Features.OfflineEnrolment.Domain;

/// <summary>
/// BIO-MOB-005-AC03: "Duplicate/conflict checks re-run centrally after sync." Runs synchronously,
/// server-side, within the same request that accepted a device's enrolment-package submission -
/// "after sync" in the literal sense the AC names: the check happens once the package has
/// actually reached the central service, never on the device itself (the device has no visibility
/// into any other device's or any other session's submissions, so it structurally cannot run this
/// check offline).
///
/// <b>Deliberately independent of BIO-IDN-005's <c>DuplicateReviewService</c> and BIO-BIO-006's
/// <c>FingerprintDuplicateReviewEntry</c> queue</b> - neither is a declared dependency of this
/// story, and BIO-IDN-005's own service is <c>internal</c> to its project (unreachable without an
/// <c>InternalsVisibleTo</c> grant it does not extend here). This mirrors BIO-BIO-006's own
/// precedent exactly: its README states outright that AC03-shaped wording "only requires
/// 'possible duplicate routes to human review,' not specifically to BIO-IDN-005's own queue" -
/// the same reasoning applies here. This service scans only this feature's own
/// <see cref="EnrolmentPackage"/> records (pre-person, raw-submission-level dedup), not
/// PersonRegistry - consistent with materializing a person record being out of this story's
/// scope (see <c>EnrolmentPackage.cs</c>'s own header).
///
/// Two signal sources, deliberately simple (no fuzzy matching, no scoring, no per-tenant
/// tuning - none of that is asked for by this story's own AC text, and inventing it would be
/// scope creep beyond "duplicate/conflict checks re-run"):
/// <list type="bullet">
/// <item><see cref="DuplicateSignalSource.NameAndDateOfBirth"/>: both packages have a non-empty
/// <c>born_on</c> AND their normalized first+last name match exactly.</item>
/// <item><see cref="DuplicateSignalSource.InstitutionalIdentifier"/>: both packages have a
/// non-empty, exactly matching <c>institutional_identifier</c>.</item>
/// </list>
/// The first match found wins (one candidate raised per newly submitted package, pointing at the
/// first already-accepted package it matches) - a package can only ever be held for one review
/// at a time, keeping AC04's human resolution queue one-decision-per-package.
/// </summary>
public sealed class EnrolmentDuplicateReviewService
{
    public DuplicateSignalSource? FindMatch(EnrolmentPackage candidate, IReadOnlyList<EnrolmentPackage> acceptedPackages, out string matchedPackageId)
    {
        matchedPackageId = "";

        var candidateBornOn = candidate.FieldValues.GetValueOrDefault(KnownFields.BornOn);
        var candidateFirstName = Normalize(candidate.FieldValues.GetValueOrDefault(KnownFields.FirstName));
        var candidateLastName = Normalize(candidate.FieldValues.GetValueOrDefault(KnownFields.LastName));
        var candidateInstitutionalId = Normalize(candidate.FieldValues.GetValueOrDefault(KnownFields.InstitutionalIdentifier));

        foreach (var existing in acceptedPackages)
        {
            if (existing.Id == candidate.Id)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(candidateInstitutionalId))
            {
                var existingInstitutionalId = Normalize(existing.FieldValues.GetValueOrDefault(KnownFields.InstitutionalIdentifier));
                if (!string.IsNullOrEmpty(existingInstitutionalId) && existingInstitutionalId == candidateInstitutionalId)
                {
                    matchedPackageId = existing.Id;
                    return DuplicateSignalSource.InstitutionalIdentifier;
                }
            }

            if (!string.IsNullOrEmpty(candidateBornOn) && !string.IsNullOrEmpty(candidateFirstName) && !string.IsNullOrEmpty(candidateLastName))
            {
                var existingBornOn = existing.FieldValues.GetValueOrDefault(KnownFields.BornOn);
                var existingFirstName = Normalize(existing.FieldValues.GetValueOrDefault(KnownFields.FirstName));
                var existingLastName = Normalize(existing.FieldValues.GetValueOrDefault(KnownFields.LastName));

                if (candidateBornOn == existingBornOn && candidateFirstName == existingFirstName && candidateLastName == existingLastName)
                {
                    matchedPackageId = existing.Id;
                    return DuplicateSignalSource.NameAndDateOfBirth;
                }
            }
        }

        return null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
