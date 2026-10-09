using Biolog.Features.DuplicateReview.Domain;

namespace Biolog.Features.DuplicateReview.Tests;

/// <summary>
/// Test double for <see cref="IPersonAttributeGateway"/>. In production this is backed by
/// BIO-IDN-001's own real, tenant-scoped <c>PersonService.GetAsync</c>/<c>ListAsync</c> - see
/// <c>tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs</c> for the real
/// shipped-host proof. Every person in <paramref name="persons"/> is treated as belonging to
/// the SAME resolved tenant (mirroring the real adapter's tenant-scoped source); a test that
/// specifically proves cross-tenant exclusion does so at the real-host layer instead, since
/// tenant scoping is not this gateway's own job (see that interface's header).
/// </summary>
public sealed class FakePersonAttributeGateway(IReadOnlyList<PersonAttributes> persons) : IPersonAttributeGateway
{
    public Task<PersonAttributes?> GetAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(persons.FirstOrDefault(p => p.PersonId == personId));

    public Task<IReadOnlyList<PersonAttributes>> ListOthersAsync(string excludingPersonId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PersonAttributes>>([.. persons.Where(p => p.PersonId != excludingPersonId)]);
}
