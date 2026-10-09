namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// Storage for <see cref="PersonRoleAssignment"/>, keyed by <c>(TenantId, PersonId, Role)</c>.
/// <c>internal</c> from the start (BIO-IDN-001-QA-01's lesson) - <c>InternalsVisibleTo</c>
/// grants access to <c>Biolog.Api</c> (DI registration) and this project's own test assembly.
/// <see cref="InMemoryPersonRoleAssignmentRepository"/> independently re-validates identity
/// immutability and initial state at the persistence boundary - the same dual-boundary
/// discipline every sibling repository in this epic applies.
/// </summary>
internal interface IPersonRoleAssignmentRepository
{
    Task<PersonRoleAssignment?> GetAsync(string tenantId, string personId, RoleType role, CancellationToken cancellationToken = default);

    /// <summary>Every role this person holds or has ever held, tenant-scoped - AC04's audit-visible history.</summary>
    Task<IReadOnlyList<PersonRoleAssignment>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    /// <summary>Every ACTIVE role held by every person in the tenant - the population PermissionService.HasPermissionAsync needs to resolve a person's current authority.</summary>
    Task<IReadOnlyList<PersonRoleAssignment>> ListActiveForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    Task<PersonRoleAssignment> CreateAsync(PersonRoleAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expected"/> (ADR-0001 Decision 3.2).</summary>
    Task<PersonRoleAssignment> ReplaceAsync(PersonRoleAssignment expected, PersonRoleAssignment updated, CancellationToken cancellationToken = default);
}
