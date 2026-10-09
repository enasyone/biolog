// Test doubles for BIO-RPT-003. SYNTHETIC fixtures only.
using Biolog.Features.AuditInvestigation.Audit;
using Biolog.Features.AuditInvestigation.Domain;
using Biolog.Features.AuditInvestigation.Tenancy;

namespace Biolog.Features.AuditInvestigation.Tests;

internal sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId => tenantId;
}

internal sealed class FakeAuditExportPermissionGateway : IAuditExportPermissionGateway
{
    private readonly HashSet<string> _granted = new(StringComparer.Ordinal);

    public FakeAuditExportPermissionGateway Grant(string personId, string permissionKey)
    {
        _granted.Add(Key(personId, permissionKey));
        return this;
    }

    public Task<bool> HasPermissionAsync(string personId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_granted.Contains(Key(personId, permissionKey)));

    private static string Key(string personId, string permissionKey) => $"{personId}:{permissionKey}";
}

/// <summary>A synthetic <see cref="IAuditEventSource"/> - stands in for a real sibling feature's own sink so this story's aggregation/filter/redaction logic can be tested without depending on any other feature project.</summary>
internal sealed class FakeAuditEventSource(string sourceFeature, IReadOnlyList<NormalizedAuditRecord> records) : IAuditEventSource
{
    public string SourceFeature => sourceFeature;

    public IReadOnlyList<NormalizedAuditRecord> ListForTenant(string tenantId) =>
        [.. records.Where(r => r.TenantId == tenantId)];
}

internal static class TestData
{
    public const string TenantA = "ten_00000000-0000-7000-8000-00000000000a";
    public const string TenantB = "ten_00000000-0000-7000-8000-00000000000b";

    public static NormalizedAuditRecord Record(
        string sourceFeature,
        string eventId,
        string action,
        string reasonCode,
        string occurredAt,
        string classification,
        string tenantId = TenantA,
        ActorType actorType = ActorType.User,
        string actorId = "actor-1",
        string? personId = null,
        AuditDeviceRef? device = null,
        IReadOnlyDictionary<string, object?>? detail = null,
        IReadOnlyList<string>? withheld = null) => new()
    {
        SourceFeature = sourceFeature,
        EventId = eventId,
        TenantId = tenantId,
        Actor = new AuditActor(actorType, actorId),
        Device = device,
        PersonId = personId,
        Action = action,
        Target = new AuditTarget("thing", eventId),
        Outcome = AuditOutcome.Allowed,
        ReasonCode = reasonCode,
        CorrelationId = "evt_00000000-0000-7000-8000-000000000001",
        CausationId = null,
        OccurredAt = occurredAt,
        ReceivedAt = occurredAt,
        Classification = classification,
        Withheld = withheld ?? [],
        Detail = detail ?? new Dictionary<string, object?>(),
    };
}
