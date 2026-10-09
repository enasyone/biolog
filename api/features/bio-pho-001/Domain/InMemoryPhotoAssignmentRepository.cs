using System.Collections.Concurrent;

namespace Biolog.Features.Photography.Domain;

/// <summary>In-memory placeholder for <see cref="IPhotoAssignmentRepository"/> - same ADR-0005 rationale as <see cref="InMemoryPhotoMasterRepository"/>.</summary>
internal sealed class InMemoryPhotoAssignmentRepository : IPhotoAssignmentRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), PhotoAssignment> _byPerson = new();

    public Task<PhotoAssignment?> GetAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_byPerson.TryGetValue((tenantId, personId), out var assignment) ? assignment : null);
    }

    public Task<PhotoAssignment> CreateAsync(PhotoAssignment assignment, CancellationToken cancellationToken = default)
    {
        if (!_byPerson.TryAdd((assignment.TenantId, assignment.PersonId), assignment))
        {
            throw PhotoError.AssignmentAlreadyExists(assignment.PersonId);
        }

        return Task.FromResult(assignment);
    }

    public Task<PhotoAssignment> ReplaceAsync(PhotoAssignment expectedCurrent, PhotoAssignment updated, CancellationToken cancellationToken = default)
    {
        var key = (expectedCurrent.TenantId, expectedCurrent.PersonId);
        if (!_byPerson.TryGetValue(key, out var stored) || stored.Version != expectedCurrent.Version)
        {
            var actual = _byPerson.TryGetValue(key, out var current) ? current.Version : 0;
            throw PhotoError.AssignmentConcurrencyConflict(expectedCurrent.PersonId, expectedCurrent.Version, actual);
        }

        _byPerson[key] = updated;
        return Task.FromResult(updated);
    }
}
