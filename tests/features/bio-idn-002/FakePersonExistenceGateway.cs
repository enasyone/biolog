using Biolog.Features.PersonProfileRegistry.Domain;

namespace Biolog.Features.PersonProfileRegistry.Tests;

/// <summary>
/// Test double for <see cref="IPersonExistenceGateway"/>. In production this is backed by
/// BIO-IDN-001's own real, tenant-scoped <c>PersonService.GetAsync</c> - see
/// <c>tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs</c> for the real
/// shipped-host proof. Defaults to "every person exists", so existing tests that are not
/// themselves testing this gateway do not need to change; a test that specifically exercises
/// BIO-IDN-002-QA-01 constructs this with <paramref name="existingPersonIds"/> set to prove
/// the negative case.
/// </summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}
