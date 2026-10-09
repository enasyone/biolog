using Biolog.Features.DuplicateReview.Domain;

namespace Biolog.Features.DuplicateReview.Tests;

/// <summary>
/// Test double for <see cref="IIdentifierAttributeGateway"/>. In production this is backed by
/// BIO-IDN-003's own real <c>InstitutionalIdentifierService.ListAsync(personId)</c>, filtered to
/// Active records - see that gateway interface's header. Keyed by person id so different tests
/// can give different people different (or overlapping) identifier values.
/// </summary>
public sealed class FakeIdentifierAttributeGateway(IReadOnlyDictionary<string, IReadOnlyList<IdentifierAttribute>> byPerson) : IIdentifierAttributeGateway
{
    public Task<IReadOnlyList<IdentifierAttribute>> ListActiveValuesAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(byPerson.TryGetValue(personId, out var values) ? values : (IReadOnlyList<IdentifierAttribute>)[]);
}
