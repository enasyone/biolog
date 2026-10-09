namespace Biolog.Features.PersonProfileRegistry.Domain;

/// <summary>
/// A profile PERSON (BIO-IDN-001) holds in this tenant (BIO-IDN-002, Master Technical
/// Specification §4: "Student/Staff/Employee/Visitor/Contractor/Alumni/Parent and
/// tenant-defined profiles attach to PERSON").
///
/// <b>AC01 - multiple simultaneous profiles:</b> nothing about this type or
/// <see cref="IPersonProfileRepository"/> limits a person to one profile - the repository's
/// only uniqueness rule is <c>(PersonId, ProfileType)</c>, so a person may hold a
/// <see cref="ProfileType.Student"/> profile and an <see cref="ProfileType.Employee"/> profile
/// at the same time (a student worker), each with its own independent
/// <see cref="Status"/>/<see cref="ExpiresAt"/>.
///
/// <b>AC02 - organisation/campus/branch targeting:</b> <see cref="OrganisationId"/>,
/// <see cref="CampusId"/> and <see cref="BranchId"/> are optional, format-validated-only
/// strings, NOT canonical identifiers backed by a dedicated organisational-hierarchy
/// aggregate - none exists yet (no ADR or contract defines Organisation/Campus/Branch as a
/// registered entity; the Master Technical Specification §2 names the hierarchy
/// conceptually - "Tenant -> Organisation/Subsidiary -> Campus/Branch -> Building -> Zone ->
/// Access Point/Device" - without specifying its own storage). This story validates only that
/// a supplied value is a non-blank, reasonably-bounded string; it does not verify the target
/// exists, matching BIO-TEN-002's identical choice to validate a caller-supplied tenant id's
/// SHAPE only, never mint or verify it against another story's store.
///
/// <b>AC03 - activation/expiry is independent:</b> <see cref="ActivatedAt"/> and
/// <see cref="ExpiresAt"/> belong to THIS profile alone - a second profile for the same
/// person, or the person's own <c>PersonStatus</c>, is never consulted or affected.
/// <see cref="IsCurrentlyInEffect"/> computes "in effect right now" from
/// <see cref="Status"/> and <see cref="ExpiresAt"/> at read time rather than a background job
/// mutating <see cref="Status"/> when a clock crosses <see cref="ExpiresAt"/> - see
/// <see cref="ProfileStatus"/>'s header for why.
///
/// <b>AC04 - no duplicate PERSON for a second role:</b> this type and
/// <see cref="PersonProfileService"/> have no "create a person" operation at all - every
/// profile attaches to an EXISTING <see cref="PersonId"/> (shape-validated, never minted
/// here). Adding a second role to an existing person is exactly the operation this service
/// exists to do: attach a second <see cref="PersonProfile"/>, never construct a second
/// <c>Person</c>.
///
/// <b>Construction is internal (BIO-IDN-001-QA-01's lesson, applied here from the start, not
/// after a review round).</b> <see cref="PersonId"/>, <see cref="TenantId"/>,
/// <see cref="Type"/> and <see cref="CustomLabel"/> are the identity of this aggregate and are
/// <c>get</c>-only - immutable for the object's whole life, including across a <c>with</c>
/// expression, exactly like BIO-IDN-001's <c>Person.Id</c>/<c>TenantId</c>. The constructor is
/// <c>internal</c>, restricted to this assembly, <c>Biolog.Api</c> (DI registration) and this
/// project's own test assembly via <c>InternalsVisibleTo</c> - see
/// <see cref="IPersonProfileRepository"/> for why the repository is internal too, and
/// independently re-validates at the persistence boundary rather than trusting restricted
/// access alone.
///
/// <b>No canonical id of its own kind:</b> unlike PERSON (<c>per_</c>, a registered ADR-0001
/// type), a profile has no dedicated identifier. Its natural key is
/// <c>(PersonId, Type)</c> - the same "hostname as natural key" shape BIO-TEN-002's
/// <c>TenantDomain</c> used, applied here because nothing about a profile needs to be named
/// independently of the person and type that define it, and adding a seventh registered
/// identifier type would need an amending ADR and Kemi's approval for no expressed need.
/// </summary>
public sealed record PersonProfile
{
    public string PersonId { get; }
    public string TenantId { get; }
    public ProfileType Type { get; }
    public string? CustomLabel { get; }
    public string? OrganisationId { get; init; }
    public string? CampusId { get; init; }
    public string? BranchId { get; init; }
    public required ProfileStatus Status { get; init; }
    public required string ActivatedAt { get; init; }
    public string? ExpiresAt { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    /// <summary>BIO-IDN-002-AC03: computed, never stored - see class header.</summary>
    public bool IsCurrentlyInEffect(string nowUtc) =>
        Status == ProfileStatus.Active && (ExpiresAt is null || string.CompareOrdinal(nowUtc, ExpiresAt) < 0);

    internal PersonProfile(string personId, string tenantId, ProfileType type, string? customLabel)
    {
        if (!Identifiers.CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (type == ProfileType.TenantDefined && string.IsNullOrWhiteSpace(customLabel))
        {
            throw new ArgumentException("custom_label is required when type is TENANT_DEFINED", nameof(customLabel));
        }

        if (type != ProfileType.TenantDefined && customLabel is not null)
        {
            throw new ArgumentException("custom_label must be null unless type is TENANT_DEFINED", nameof(customLabel));
        }

        PersonId = personId;
        TenantId = tenantId;
        Type = type;
        CustomLabel = customLabel;
    }
}
