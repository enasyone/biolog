using System.Collections.Concurrent;

namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database
/// ADR-0002 Decision 4.1 describes, keyed by <c>(TenantId, PersonId, Role)</c>. See
/// <see cref="IPersonRoleAssignmentRepository"/> for why this class is <c>internal</c> and what
/// belt-and-braces validation it applies independently of <see cref="RoleAssignmentService"/>.
/// </summary>
internal sealed class InMemoryPersonRoleAssignmentRepository : IPersonRoleAssignmentRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId, RoleType Role), PersonRoleAssignment> _assignments = new();

    public Task<PersonRoleAssignment?> GetAsync(string tenantId, string personId, RoleType role, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _assignments.TryGetValue((tenantId, personId, role), out var assignment);
        return Task.FromResult(assignment);
    }

    public Task<IReadOnlyList<PersonRoleAssignment>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersonRoleAssignment> items =
        [
            .. _assignments.Values.Where(a => a.TenantId == tenantId && a.PersonId == personId).OrderBy(a => a.Role.ToWireString(), StringComparer.Ordinal),
        ];
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<PersonRoleAssignment>> ListActiveForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersonRoleAssignment> items =
        [
            .. _assignments.Values.Where(a => a.TenantId == tenantId && a.PersonId == personId && a.Status == RoleAssignmentStatus.Active),
        ];
        return Task.FromResult(items);
    }

    public Task<PersonRoleAssignment> CreateAsync(PersonRoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (assignment.Status != RoleAssignmentStatus.Active || assignment.Version != 1)
        {
            throw RbacError.RoleAssignmentInvalidInitialState(assignment.PersonId, assignment.Role, assignment.Status, assignment.Version);
        }

        var key = (assignment.TenantId, assignment.PersonId, assignment.Role);
        if (!_assignments.TryAdd(key, assignment))
        {
            // A genuine natural-key collision - never legitimately reachable via
            // RoleAssignmentService, which always checks for an existing record under the same
            // per-key lock before ever constructing a new one.
            throw RbacError.RoleAssignmentInvalidInitialState(assignment.PersonId, assignment.Role, assignment.Status, assignment.Version);
        }

        return Task.FromResult(assignment);
    }

    public Task<PersonRoleAssignment> ReplaceAsync(PersonRoleAssignment expected, PersonRoleAssignment updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.TenantId, expected.PersonId, expected.Role);
        if (!_assignments.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw RbacError.RoleAssignmentNotFound(expected.PersonId, expected.Role);
        }

        if (updated.TenantId != expected.TenantId || updated.PersonId != expected.PersonId || updated.Role != expected.Role)
        {
            // Unreachable via `expected with { ... }` (these fields are get-only) - catches two
            // unrelated instances being passed together, mirroring every sibling repository's
            // identical dual-boundary check.
            throw RbacError.RoleAssignmentIdentityImmutable(expected.PersonId, expected.Role);
        }

        if (!_assignments.TryUpdate(key, updated, expected))
        {
            var latest = _assignments.TryGetValue(key, out var a) ? a : current;
            throw RbacError.RoleAssignmentConcurrencyConflict(expected.PersonId, expected.Role, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
