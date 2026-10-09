using Biolog.Features.FaceEnrolment.Domain;

namespace Biolog.Features.FaceEnrolment.Tests;

/// <summary>Test double for <see cref="IPhotoMasterExistenceGateway"/> - the production
/// implementation lives in <c>Biolog.Api.Modules.BioBio003Module</c> and wraps BIO-PHO-001's real
/// <c>PhotoMasterService</c>, which this feature project deliberately has no reference to (see
/// this feature's csproj remarks).</summary>
public sealed class FakePhotoMasterExistenceGateway : IPhotoMasterExistenceGateway
{
    private readonly HashSet<(string PersonId, int Version)> _existing = [];

    public FakePhotoMasterExistenceGateway Add(string personId, int version)
    {
        _existing.Add((personId, version));
        return this;
    }

    public Task<bool> ExistsAsync(string personId, int photoMasterVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(_existing.Contains((personId, photoMasterVersion)));
}
