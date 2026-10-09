using Biolog.Features.AttendanceSessions.Domain;
using Biolog.Features.AttendanceSessions.Tenancy;

namespace Biolog.Features.AttendanceSessions.Tests;

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

/// <summary>Defaults to "actor has no permission"; a test that proves an authorized path constructs this with <see cref="GrantedActorIds"/> set.</summary>
public sealed class FakePermissionGateway : IPermissionGateway
{
    public HashSet<string> GrantedActorIds { get; } = [];

    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(GrantedActorIds.Contains(actorId));
}

/// <summary>Test double for <see cref="IAttendanceEventQueryGateway"/> - a caller registers whichever evidence a test needs via <see cref="EvidenceByPersonAndDate"/>.</summary>
public sealed class FakeAttendanceEventQueryGateway : IAttendanceEventQueryGateway
{
    public Dictionary<(string PersonId, string Date), List<AttendanceEvidenceRecord>> EvidenceByPersonAndDate { get; } = [];

    public Task<IReadOnlyList<AttendanceEvidenceRecord>> ListForPersonOnDateAsync(string personId, string date, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttendanceEvidenceRecord>>(
            EvidenceByPersonAndDate.TryGetValue((personId, date), out var found) ? found : []);
}
