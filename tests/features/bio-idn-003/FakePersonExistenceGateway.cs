using Biolog.Features.InstitutionalIdentifiers.Domain;

namespace Biolog.Features.InstitutionalIdentifiers.Tests;

/// <summary>
/// Test double for <see cref="IPersonExistenceGateway"/>. In production this is backed by
/// BIO-IDN-001's own real, tenant-scoped <c>PersonService.GetAsync</c> - see
/// <c>tests/dotnet/Biolog.IntegrationTests/InstitutionalIdentifierHostChannelTests.cs</c> for
/// the real shipped-host proof. Defaults to "every person exists", so tests that are not
/// themselves exercising this gateway do not need to change; a test that specifically proves
/// the existence check constructs this with <paramref name="existingPersonIds"/> set.
/// </summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}
