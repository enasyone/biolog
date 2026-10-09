using Biolog.Features.WorkforceAttendance.Domain;
using Biolog.Features.WorkforceAttendance.Tenancy;

namespace Biolog.Features.WorkforceAttendance.Tests;

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

/// <summary>Defaults to "every actor holds every permission"; a test that specifically proves the authorization gate constructs this with <paramref name="grantedActorPermissions"/> set.</summary>
public sealed class FakePermissionGateway(IReadOnlySet<(string ActorId, string PermissionKey)>? grantedActorPermissions = null) : IPermissionGateway
{
    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(grantedActorPermissions is null || grantedActorPermissions.Contains((actorId, permissionKey)));
}

/// <summary>Same contract as <see cref="FakePermissionGateway"/>, but mutable mid-test - for a single long-lived <c>TestServer</c> host where a test flips the grant after the host is already running, rather than tearing the host down to swap in a different immutable fake.</summary>
public sealed class MutableFakePermissionGateway : IPermissionGateway
{
    public bool Grant { get; set; } = true;

    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(Grant);
}

/// <summary>Returns exactly the events a test configures for one (personId, date) pair - never filters or interprets them itself, the same "gateway is a dumb pipe" shape every sibling fake uses.</summary>
public sealed class FakeAttendanceEventGateway : IAttendanceEventGateway
{
    private readonly Dictionary<(string PersonId, string Date), List<ClockEventView>> _events = new();

    public FakeAttendanceEventGateway Add(string personId, string date, ClockEventView evt)
    {
        var key = (personId, date);
        if (!_events.TryGetValue(key, out var list))
        {
            list = [];
            _events[key] = list;
        }

        list.Add(evt);
        return this;
    }

    public Task<IReadOnlyList<ClockEventView>> ListForPersonOnDateAsync(string personId, string date, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClockEventView>>(_events.TryGetValue((personId, date), out var list) ? [.. list] : []);
}
