using System.Text.Json.Serialization;
using Biolog.Features.OperationalDashboard.Domain;

namespace Biolog.Features.OperationalDashboard.Api;

public sealed record IdentityPanelResponse
{
    [JsonPropertyName("total")] public required int? Total { get; init; }
    [JsonPropertyName("active")] public required int? Active { get; init; }
    [JsonPropertyName("suspended")] public required int? Suspended { get; init; }
    [JsonPropertyName("other")] public required int? Other { get; init; }
    [JsonPropertyName("freshness")] public required string Freshness { get; init; }
    [JsonPropertyName("observed_at")] public required string ObservedAt { get; init; }
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    public static IdentityPanelResponse From(IdentityPanel panel) => new()
    {
        Total = panel.Total,
        Active = panel.Active,
        Suspended = panel.Suspended,
        Other = panel.Other,
        Freshness = panel.Freshness.ToWireString(),
        ObservedAt = panel.ObservedAt,
        Reason = panel.Reason,
    };
}

public sealed record AccessPanelResponse
{
    [JsonPropertyName("total")] public required int? Total { get; init; }
    [JsonPropertyName("allowed")] public required int? Allowed { get; init; }
    [JsonPropertyName("denied")] public required int? Denied { get; init; }
    [JsonPropertyName("freshness")] public required string Freshness { get; init; }
    [JsonPropertyName("observed_at")] public required string ObservedAt { get; init; }
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    public static AccessPanelResponse From(AccessPanel panel) => new()
    {
        Total = panel.Total,
        Allowed = panel.Allowed,
        Denied = panel.Denied,
        Freshness = panel.Freshness.ToWireString(),
        ObservedAt = panel.ObservedAt,
        Reason = panel.Reason,
    };
}

public sealed record ProductionPanelResponse
{
    [JsonPropertyName("total")] public required int? Total { get; init; }
    [JsonPropertyName("passed")] public required int? Passed { get; init; }
    [JsonPropertyName("failed")] public required int? Failed { get; init; }
    [JsonPropertyName("rejected")] public required int? Rejected { get; init; }
    [JsonPropertyName("in_progress")] public required int? InProgress { get; init; }
    [JsonPropertyName("cancelled")] public required int? Cancelled { get; init; }
    [JsonPropertyName("freshness")] public required string Freshness { get; init; }
    [JsonPropertyName("observed_at")] public required string ObservedAt { get; init; }
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    public static ProductionPanelResponse From(ProductionPanel panel) => new()
    {
        Total = panel.Total,
        Passed = panel.Passed,
        Failed = panel.Failed,
        Rejected = panel.Rejected,
        InProgress = panel.InProgress,
        Cancelled = panel.Cancelled,
        Freshness = panel.Freshness.ToWireString(),
        ObservedAt = panel.ObservedAt,
        Reason = panel.Reason,
    };
}

public sealed record UnavailablePanelResponse
{
    [JsonPropertyName("freshness")] public required string Freshness { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("observed_at")] public required string ObservedAt { get; init; }

    public static UnavailablePanelResponse From(UnavailablePanel panel) => new()
    {
        Freshness = panel.Freshness.ToWireString(),
        Reason = panel.Reason,
        ObservedAt = panel.ObservedAt,
    };
}

public sealed record OperationalDashboardSnapshotResponse
{
    [JsonPropertyName("tenant_id")] public required string TenantId { get; init; }
    [JsonPropertyName("computed_at")] public required string ComputedAt { get; init; }
    [JsonPropertyName("identity")] public required IdentityPanelResponse Identity { get; init; }
    [JsonPropertyName("card")] public required UnavailablePanelResponse Card { get; init; }
    [JsonPropertyName("device")] public required UnavailablePanelResponse Device { get; init; }
    [JsonPropertyName("sync")] public required UnavailablePanelResponse Sync { get; init; }
    [JsonPropertyName("access")] public required AccessPanelResponse Access { get; init; }
    [JsonPropertyName("production")] public required ProductionPanelResponse Production { get; init; }

    public static OperationalDashboardSnapshotResponse From(OperationalDashboardSnapshot snapshot) => new()
    {
        TenantId = snapshot.TenantId,
        ComputedAt = snapshot.ComputedAt,
        Identity = IdentityPanelResponse.From(snapshot.Identity),
        Card = UnavailablePanelResponse.From(snapshot.Card),
        Device = UnavailablePanelResponse.From(snapshot.Device),
        Sync = UnavailablePanelResponse.From(snapshot.Sync),
        Access = AccessPanelResponse.From(snapshot.Access),
        Production = ProductionPanelResponse.From(snapshot.Production),
    };
}
