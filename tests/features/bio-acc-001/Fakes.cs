using Biolog.Features.AccessPolicy.Domain;
using Biolog.Features.AccessPolicy.Tenancy;

namespace Biolog.Features.AccessPolicy.Tests;

/// <summary>Test double for <see cref="ICurrentTenantProvider"/> - see BIO-IDN-001's identically-named class for the full rationale.</summary>
public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Test double for <see cref="IPersonExistenceGateway"/>. Defaults to "every person exists"; a test that specifically proves the existence check constructs this with <paramref name="existingPersonIds"/> set.</summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}

/// <summary>Test double for <see cref="IIdentityStatusGateway"/>. Returns the configured status for a person id, or <c>null</c> for one not in the map (unresolvable, per that interface's own header).</summary>
public sealed class FakeIdentityStatusGateway(IReadOnlyDictionary<string, IdentityStatus>? statuses = null) : IIdentityStatusGateway
{
    public Task<IdentityStatus?> GetStatusAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(statuses is not null && statuses.TryGetValue(personId, out var status) ? status : (IdentityStatus?)null);
}

/// <summary>Test double for <see cref="IPermissionGateway"/>. Defaults to "every actor holds every permission"; a test that specifically proves the authorization gate constructs this with <paramref name="grantedActorPermissions"/> set.</summary>
public sealed class FakePermissionGateway(IReadOnlySet<(string ActorId, string PermissionKey)>? grantedActorPermissions = null) : IPermissionGateway
{
    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(grantedActorPermissions is null || grantedActorPermissions.Contains((actorId, permissionKey)));
}
