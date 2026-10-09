using Biolog.Features.PersonRegistry.Domain;
using Biolog.Features.Rbac.Domain;
using PersonUtcInstant = Biolog.Features.PersonRegistry.Identifiers.UtcInstant;
using RbacUtcInstant = Biolog.Features.Rbac.Identifiers.UtcInstant;

namespace Biolog.Api.Tenancy;

/// <summary>
/// TD-WEB-PROVENANCE-BRIDGE round 6 - the actor-id half of round 5's demo-session shape.
/// Kemi's ruling (2026-10-07, folded into the ADR-0002 amendment this TD already owes): round
/// 5 made the demo session's TENANT confinement backend-enforced, but
/// <c>web/lib/demo-identity.ts</c>'s <c>deriveDemoActorId</c> still minted
/// <c>demo-actor-{roleId}</c> - not a well-formed canonical BIOLOG identifier (ADR-0001
/// Decision 1). <see cref="Identifiers.CanonicalId.IsPersonId"/> (bio-idn-001's own copy,
/// <see cref="Person"/>'s internal constructor) rejects that shape before any RBAC grant lookup
/// ever runs, so no story could ever get a real "granted" result through this bridge - only
/// ever the baked-in rejection, never a genuine positive proof.
///
/// <b>Smaller fix than it sounds, no new backend mechanism.</b> The web side now mints three
/// FIXED, hardcoded <c>per_&lt;UUIDv7&gt;</c> literals, one per demo role
/// (<c>web/lib/demo-identity.ts</c>'s own <c>DEMO_PERSON_ID_BY_ROLE</c>) instead of a
/// format-invalid string. This class is the other half: three matching, equally fixed Person
/// rows (bio-idn-001) and PersonRoleAssignment rows (bio-idn-006), each with a distinct
/// <see cref="RoleType"/> matching that demo role's intended privilege, all scoped to the one
/// hardcoded <c>WebSessionBridge:SyntheticTenantId</c> round 5 already established - a static
/// seed fixture, the same spirit as that one hardcoded synthetic tenant id, now three hardcoded
/// synthetic person ids. Quarantined to that one synthetic tenant only, same as round 5.
///
/// <b>Deliberately bypasses <see cref="PersonService"/>/<see cref="RoleAssignmentService"/>.</b>
/// Both expect an ambient, request-scoped tenant context and produce an audit trail for a real
/// administrative action - neither fits a fixed, process-startup fixture with no real actor
/// behind it. This writes directly to the repositories instead (the same "seed data, not
/// provisioning logic" distinction Kemi's ruling drew), each row constructed in exactly the
/// initial state its own repository's <c>CreateAsync</c> already requires
/// (<see cref="InMemoryPersonRepository"/>: <see cref="PersonStatus.PreEnrolled"/>, version 1;
/// <see cref="InMemoryPersonRoleAssignmentRepository"/>: <see cref="RoleAssignmentStatus.Active"/>,
/// version 1) - no lifecycle transition needed, since the only consumers of these rows are
/// <see cref="IPersonExistenceGateway.ExistsAsync"/> (existence only, status-agnostic) and the
/// RBAC grant lookup keyed on <c>(TenantId, PersonId, Role)</c>.
///
/// <b>Still quarantined to exactly the one primary tenant, even after OPS002-F01.</b> See
/// <see cref="SeedIfConfigured"/>'s own header for why seeding into the optional
/// <c>WebSessionBridge:SyntheticTenantIdBeta</c> tenant too was tried and reverted, not simply
/// never attempted.
///
/// <b>Role mapping, not identical to the web shell's own role names.</b> "admin" (System
/// Administrator, full <c>nav:administration</c>) maps to <see cref="RoleType.TenantAdmin"/>,
/// deliberately NOT <see cref="RoleType.SuperAdmin"/> - that role implicitly holds every
/// permission unconditionally and is never consulted via a real
/// <see cref="RolePermissionGrant"/> lookup (see that enum's own header), which would prove
/// nothing about the real grant-lookup path this bridge exists to exercise. "staff"
/// (Registration Staff - enrolment, cards, credentials) maps to
/// <see cref="RoleType.Enrolment"/>. "viewer" (Report Viewer - reports/audit only) maps to
/// <see cref="RoleType.ReadOnly"/>, the lowest-privilege baseline role, matching that role's own
/// deliberately narrow web-side permission set.
///
/// <b>No <see cref="RolePermissionGrant"/> rows seeded here.</b> Out of scope for this fix -
/// TD-WEB-PROVENANCE-BRIDGE-REVERIFY's own obligation on BIO-RPT-004/BIO-TEN-003 (Ngozi/Tunde)
/// provisions the minimum grants each of those stories' own reverify needs, against these same
/// three person/role rows.
///
/// <b>Fail-closed, inert when unconfigured</b> - same discipline as every piece of this TD:
/// <see cref="SeedIfConfigured"/> is a no-op unless <c>WebSessionBridge:SyntheticTenantId</c> is
/// configured (the same config key <see cref="DemoSessionTenantResolver"/> reads), so a host
/// that never enables the demo-session bridge never gains these rows either. Idempotent by
/// construction (checks <see cref="IPersonRepository.GetAsync"/> first) so re-running this
/// against an already-seeded store - e.g. a second <c>WebApplicationFactory</c> in the same
/// test run that happens to share an in-memory singleton - is a safe no-op, not a duplicate-key
/// exception.
/// </summary>
public static class DemoSessionIdentitySeed
{
    private sealed record DemoIdentity(string PersonId, RoleType Role);

    private static readonly IReadOnlyList<DemoIdentity> Identities =
    [
        new DemoIdentity("per_01a113f6-6268-730c-a5ce-44d97f0aa7f5", RoleType.TenantAdmin), // admin
        new DemoIdentity("per_01a113f6-6268-7c44-a9e5-3aee6ff59416", RoleType.Enrolment),   // staff
        new DemoIdentity("per_01a113f6-6268-757e-926c-4a1b5f98b45b", RoleType.ReadOnly),    // viewer
    ];

    /// <summary>
    /// Deliberately still seeds the ONE primary <c>WebSessionBridge:SyntheticTenantId</c> only
    /// - NOT also the optional <c>WebSessionBridge:SyntheticTenantIdBeta</c>
    /// <see cref="Tenancy.DemoSessionTenantResolver"/>'s own OPS002-F01 fix added. A first pass
    /// at this fix tried seeding the SAME three fixed demo identities into both tenants for
    /// symmetry; that broke in practice and was reverted: <see cref="InMemoryPersonRepository"/>
    /// keys <see cref="Person"/> rows by <see cref="Person.Id"/> ALONE (a single, process-
    /// lifetime, cross-tenant dictionary, per that class's own header) - NOT by
    /// <c>(TenantId, PersonId)</c> the way <see cref="InMemoryPersonRoleAssignmentRepository"/>
    /// does, so creating the "same" person id under a second tenant id throws a genuine
    /// <c>person_id_collision</c>, not a benign no-op. Minting three NEW, distinct person ids
    /// for beta would dodge that collision, but the web side's own
    /// <c>deriveDemoActorId(roleId)</c> is deliberately tenant-agnostic (role-only by design,
    /// per that function's own header) - making beta's identities actually reachable would
    /// need a second, tenant-aware actor-derivation mechanism on the web side too, well beyond
    /// OPS002-F01's own two named findings. Left as a genuinely separate, currently-empty
    /// tenant instead when beta is configured: still a real, isolated backend data context
    /// (AC01's own requirement), and arguably a STRONGER proof of AC02's cross-tenant isolation
    /// than a seeded-but-separate-identity design would be, not a regression.
    /// </summary>
    public static async Task SeedIfConfigured(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var tenantId = configuration["WebSessionBridge:SyntheticTenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var people = services.GetRequiredService<IPersonRepository>();
        var roleAssignments = services.GetRequiredService<IPersonRoleAssignmentRepository>();

        foreach (var identity in Identities)
        {
            if (await people.GetAsync(tenantId, identity.PersonId, cancellationToken) is null)
            {
                var now = PersonUtcInstant.Now();
                await people.CreateAsync(
                    new Person(identity.PersonId, tenantId)
                    {
                        FirstName = "Demo",
                        LastName = "Session",
                        Status = PersonStatus.PreEnrolled,
                        Version = 1,
                        CreatedAt = now,
                        UpdatedAt = now,
                    },
                    cancellationToken);
            }

            if (await roleAssignments.GetAsync(tenantId, identity.PersonId, identity.Role, cancellationToken) is null)
            {
                var now = RbacUtcInstant.Now();
                await roleAssignments.CreateAsync(
                    new PersonRoleAssignment(tenantId, identity.PersonId, identity.Role)
                    {
                        Status = RoleAssignmentStatus.Active,
                        Version = 1,
                        CreatedAt = now,
                        UpdatedAt = now,
                    },
                    cancellationToken);
            }
        }
    }
}
