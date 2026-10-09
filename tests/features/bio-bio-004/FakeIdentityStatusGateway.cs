using Biolog.Features.FingerprintVerification.Domain;

namespace Biolog.Features.FingerprintVerification.Tests;

/// <summary>Test double for <see cref="IIdentityStatusGateway"/> - the production implementation
/// lives in <c>Biolog.Api.Modules.BioBio004Module</c> and wraps BIO-IDN-001's real
/// <c>PersonService</c>, which this feature project deliberately has no reference to (see this
/// feature's csproj remarks).</summary>
public sealed class FakeIdentityStatusGateway : IIdentityStatusGateway
{
    private readonly Dictionary<string, IdentityStatus> _statuses = [];

    public FakeIdentityStatusGateway Set(string personId, IdentityStatus status)
    {
        _statuses[personId] = status;
        return this;
    }

    public Task<IdentityStatus?> GetStatusAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_statuses.TryGetValue(personId, out var status) ? status : (IdentityStatus?)null);
}
