using Biolog.Features.Transport.Domain;
using Biolog.Features.Transport.Tenancy;

namespace Biolog.Features.Transport.Tests;

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Defaults to "every person exists"; a test that specifically proves the existence check constructs this with <paramref name="existingPersonIds"/> set.</summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}

/// <summary>Defaults to "every device exists"; a test that specifically proves the existence check constructs this with <paramref name="existingDeviceIds"/> set.</summary>
public sealed class FakeDeviceExistenceGateway(IReadOnlySet<string>? existingDeviceIds = null) : IDeviceExistenceGateway
{
    public Task<bool> ExistsAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingDeviceIds is null || existingDeviceIds.Contains(deviceId));
}

/// <summary>Defaults to "every actor holds every permission"; a test that specifically proves the authorization gate constructs this with <paramref name="grantedActorPermissions"/> set.</summary>
public sealed class FakePermissionGateway(IReadOnlySet<(string ActorId, string PermissionKey)>? grantedActorPermissions = null) : IPermissionGateway
{
    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(grantedActorPermissions is null || grantedActorPermissions.Contains((actorId, permissionKey)));
}
