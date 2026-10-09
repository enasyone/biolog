namespace Biolog.Features.PersonRegistry.Domain;

/// <summary>
/// The universal PERSON aggregate (BIO-IDN-001, Master Technical Specification §4: "PERSON
/// is the permanent aggregate. Student/Staff/Employee/Visitor/Contractor/Alumni/Parent and
/// tenant-defined profiles attach to PERSON.").
///
/// <b>AC01 - exists independently of roles:</b> this record carries no role, profile type or
/// profile reference of any kind. "Student", "Staff", "Visitor" and every other profile type
/// IDENTITY.md names are BIO-IDN-002's concern ("configurable person profiles ... attach to
/// one person"); attaching a profile never mints a second PERSON, and this type has nothing a
/// profile-attachment story would need to touch to add its own attachment records elsewhere.
///
/// <b>AC02 - tenant boundary is mandatory:</b> <see cref="TenantId"/> is required, set once at
/// registration from the resolved tenant context (never a caller-supplied value - see
/// <see cref="PersonService"/>), and never changes for the life of the record. There is no
/// "move a person to another tenant" operation; ADR-0002's "no cross-tenant joins" boundary
/// means a person who needs a presence in a second tenant is a second, independent PERSON
/// there, not a cross-tenant reference from this one.
///
/// <b>AC03 - identity lifecycle states:</b> <see cref="Status"/> is one of the eight states
/// the Master Technical Specification names (<see cref="PersonStatus"/>). This story's own
/// transition table (enforced by <see cref="PersonService.UpdateStatusAsync"/>) is a
/// deliberately conservative baseline:
///
/// <code>
/// PRE_ENROLLED  -> VERIFIED, ARCHIVED
/// VERIFIED      -> ACTIVE, ARCHIVED
/// ACTIVE        -> SUSPENDED, EXPIRED, FORMER_ALUMNI, REVOKED, ARCHIVED
/// SUSPENDED     -> ACTIVE, REVOKED, ARCHIVED
/// EXPIRED       -> ACTIVE, ARCHIVED
/// FORMER_ALUMNI -> ACTIVE, ARCHIVED
/// REVOKED       -> ARCHIVED
/// ARCHIVED      -> (terminal - no outbound transition)
/// </code>
///
/// This table exists so "identity lifecycle states are implemented" is a real, tested,
/// enforced state machine rather than a bare enum field nothing ever validates against - but
/// it is deliberately NOT the full governance this epic ultimately needs.
/// BIO-IDN-004 ("Implement identity lifecycle and emergency suspension", AC01: "Allowed
/// identity transitions are explicit") owns naming and narrowing the FINAL transition table,
/// emitting the credential/access revocation events an emergency suspension requires (its
/// AC02), and gating reactivation behind permission + an audit reason (its AC03). This
/// story's table is a conservative superset a later, more restrictive table can narrow
/// without contradicting anything tested here - it was not designed to be the last word on,
/// for example, whether ACTIVE may re-enter from every listed predecessor without an
/// additional permission check; that policy question belongs to IDN-004, not this aggregate.
///
/// <b>AC04 - governed deletion, no orphaned history:</b> there is no delete operation
/// anywhere in this feature - not on <see cref="PersonService"/>, not on
/// <see cref="IPersonRepository"/> (a structural test proves this adversarially). The only
/// way a PERSON's active life ends is the same governed, audited, CAS-protected transition
/// every other status change uses: to <see cref="PersonStatus.Archived"/>. The record and its
/// <see cref="Id"/> persist forever after that - archiving is BIOLOG's deletion, not a prelude
/// to one - so a future story attaching a profile, credential, biometric or event record to a
/// <c>per_…</c> id can never be left pointing at a row that stopped existing.
///
/// <b>AC05 - all mutations are audited:</b> both mutations this story defines - registration
/// and status change - go through <see cref="PersonService.AuditAsync"/>, implementing
/// ADR-0003 Decision 7's audit-atomicity pattern (per-aggregate lock across the whole
/// read-validate-audit-commit sequence, audit before commit, <c>ExistsAsync</c> verification
/// on an ambiguous write throw, <c>CancellationToken.None</c> on the commit once the audit is
/// confirmed) exactly as established by BIO-TEN-001, BIO-TEN-002 and BIO-DEV-001.
///
/// <b>Name fields</b> mirror the six dynamic card fields the Master Technical Specification
/// §7 names verbatim ("Title, First Name, Middle Name, Last Name/Surname, Full Name and
/// Preferred/Display Name") minus <c>FullName</c>, which this record deliberately does NOT
/// store: it is a derived display value (see <see cref="PersonNameFormatter"/>), and storing
/// it alongside its own components would create a second source of truth that could silently
/// disagree with them after a correction - out of scope in this story, since no AC requires a
/// name-correction endpoint (see docs/features/bio-idn-001/README.md "Design decisions and
/// scope boundaries").
///
/// <b><see cref="BornOn"/></b> follows ADR-0001 §2.2's naming rule for a calendar date with no
/// instant ("a date of birth ... ends in <c>_on</c>"), stored as a plain <c>YYYY-MM-DD</c>
/// string, optional (a person's date of birth is not always known at enrolment time, and this
/// story does not want to force a placeholder value into a field ADR-0001 forbids being
/// naive/guessed).
///
/// <b>Construction is internal (BIO-IDN-001-QA-01, Seyi round 1).</b> Before this, every
/// property here used a public object-initializer (<c>{ get; init; }</c> with no declared
/// constructor), so any code that could reference this type at all - not just
/// <see cref="PersonService"/> - could write <c>new Person { TenantId = "ten_...", Status =
/// PersonStatus.Archived, ... }</c> and hand the result straight to
/// <see cref="IPersonRepository.CreateAsync"/>/<c>ReplaceAsync</c>, skipping every rule this
/// class's own header documents (tenant boundary, governed lifecycle, audit). An independent
/// executable probe reproduced exactly this: create in tenant A, replace with a copy whose
/// <see cref="TenantId"/> is tenant B and whose <see cref="Status"/> jumps straight to
/// <see cref="PersonStatus.Archived"/> - no <see cref="PersonService"/>, no lock, no audit
/// event. <see cref="Id"/> and <see cref="TenantId"/> now have <c>get;</c>-only accessors (not
/// even <c>init</c>) - truly fixed for the object's whole life, including across a <c>with</c>
/// expression - and the constructor itself is <c>internal</c>, restricted to this assembly (in
/// practice, only <see cref="PersonService"/>). <c>InternalsVisibleTo</c> in this project's
/// <c>.csproj</c> grants the same access to <c>Biolog.Api</c> (for DI registration in
/// <c>BioIdn001Module</c>) and <c>Biolog.Features.PersonRegistry.Tests</c> (an explicit,
/// auditable test seam, not a backdoor) - mirroring BIO-TEN-002-QA-03's identical fix for
/// <c>TenantContext</c> exactly. <see cref="Id"/>/<see cref="TenantId"/> are also validated at
/// construction (defense-in-depth against an internal bug, not a re-implementation of the
/// canonical registry - see <c>Identifiers.CanonicalId</c>'s header).
///
/// <see cref="IPersonRepository.ReplaceAsync"/> and <see cref="IPersonRepository.CreateAsync"/>
/// are themselves now also <c>internal</c> (same seam), and
/// <see cref="InMemoryPersonRepository"/> independently re-validates identity/tenant
/// immutability and lifecycle legality at the persistence boundary - not merely trusting that
/// construction being restricted is enough - the same "validated at the request boundary AND
/// the repository boundary" discipline <c>CredentialReferenceValidator</c> (BIO-DEV-001-QA-04)
/// established.
/// </summary>
public sealed record Person
{
    public string Id { get; }
    public string TenantId { get; }
    public string? Title { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public required string LastName { get; init; }
    public string? PreferredName { get; init; }
    public string? BornOn { get; init; }
    public required PersonStatus Status { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal Person(string id, string tenantId)
    {
        if (!Identifiers.CanonicalId.IsPersonId(id))
        {
            throw new ArgumentException($"'{id}' is not a well-formed person id (ADR-0001)", nameof(id));
        }

        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
    }
}
