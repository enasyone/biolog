namespace Biolog.Features.PersonRegistry.Domain;

/// <summary>
/// The single source of truth for BIO-IDN-001-AC03's baseline lifecycle transition table (see
/// <see cref="Person"/>'s class header for the full table and its intentionally conservative
/// scope relative to BIO-IDN-004). Extracted into its own type - rather than living only
/// inside <see cref="PersonService"/> as it did before BIO-IDN-001-QA-01 (Seyi round 1) - so
/// <see cref="InMemoryPersonRepository"/> can enforce the identical rule at the persistence
/// boundary too, the same "validated at both the request boundary AND the repository
/// boundary" defense-in-depth <c>CredentialReferenceValidator</c> (BIO-DEV-001-QA-04)
/// established: a caller that somehow reaches the repository directly - even one with the
/// <c>internal</c> access this story's fix now requires - cannot slip an illegal status jump
/// past by skipping <see cref="PersonService"/>.
/// </summary>
public static class PersonLifecycleTransitions
{
    private static readonly IReadOnlyDictionary<PersonStatus, IReadOnlySet<PersonStatus>> Allowed =
        new Dictionary<PersonStatus, IReadOnlySet<PersonStatus>>
        {
            [PersonStatus.PreEnrolled] = new HashSet<PersonStatus> { PersonStatus.Verified, PersonStatus.Archived },
            [PersonStatus.Verified] = new HashSet<PersonStatus> { PersonStatus.Active, PersonStatus.Archived },
            [PersonStatus.Active] = new HashSet<PersonStatus>
            {
                PersonStatus.Suspended, PersonStatus.Expired, PersonStatus.FormerAlumni, PersonStatus.Revoked, PersonStatus.Archived,
            },
            [PersonStatus.Suspended] = new HashSet<PersonStatus> { PersonStatus.Active, PersonStatus.Revoked, PersonStatus.Archived },
            [PersonStatus.Expired] = new HashSet<PersonStatus> { PersonStatus.Active, PersonStatus.Archived },
            [PersonStatus.FormerAlumni] = new HashSet<PersonStatus> { PersonStatus.Active, PersonStatus.Archived },
            [PersonStatus.Revoked] = new HashSet<PersonStatus> { PersonStatus.Archived },
            [PersonStatus.Archived] = new HashSet<PersonStatus>(), // terminal
        };

    public static bool IsAllowed(PersonStatus from, PersonStatus to) => Allowed[from].Contains(to);
}
