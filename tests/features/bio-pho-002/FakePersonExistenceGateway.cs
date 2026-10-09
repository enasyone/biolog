using Biolog.Features.PhotoBooth.Domain;

namespace Biolog.Features.PhotoBooth.Tests;

/// <summary>Test double for <see cref="IPersonExistenceGateway"/>. Defaults to "every person exists".</summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}
