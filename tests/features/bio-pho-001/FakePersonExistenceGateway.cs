using Biolog.Features.Photography.Domain;

namespace Biolog.Features.Photography.Tests;

/// <summary>Test double for <see cref="IPersonExistenceGateway"/>. Defaults to "every person exists"; a test that specifically proves the existence check constructs this with <paramref name="existingPersonIds"/> set.</summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}
