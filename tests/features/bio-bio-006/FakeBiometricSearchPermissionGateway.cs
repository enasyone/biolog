using Biolog.Features.FingerprintSearch.Domain;

namespace Biolog.Features.FingerprintSearch.Tests;

/// <summary>Test double for <see cref="IBiometricSearchPermissionGateway"/> - the production
/// implementation lives in <c>Biolog.Api.Modules.BioBio006Module</c> and wraps BIO-IDN-006's real
/// <c>PermissionService</c>, which this feature project deliberately has no reference to (see
/// this feature's csproj remarks).</summary>
public sealed class FakeBiometricSearchPermissionGateway : IBiometricSearchPermissionGateway
{
    private readonly HashSet<(string TenantId, string ActorId, string PermissionKey)> _granted = [];

    public FakeBiometricSearchPermissionGateway Grant(string tenantId, string actorId, string permissionKey)
    {
        _granted.Add((tenantId, actorId, permissionKey));
        return this;
    }

    public Task<bool> HasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_granted.Contains((tenantId, actorId, permissionKey)));
}
