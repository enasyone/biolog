using System.Text.Json.Serialization;
using Biolog.Features.Transport.Domain;

namespace Biolog.Features.Transport.Api;

// --- Shared ---

public sealed record VersionedTransportRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }
}

public sealed record RouteStopEntryRequest
{
    [JsonPropertyName("stop_id")]
    public string? StopId { get; init; }

    [JsonPropertyName("sequence_order")]
    public int SequenceOrder { get; init; }

    [JsonPropertyName("estimated_offset_minutes")]
    public int? EstimatedOffsetMinutes { get; init; }
}

public sealed record RouteStopEntryResponse
{
    [JsonPropertyName("stop_id")]
    public required string StopId { get; init; }

    [JsonPropertyName("sequence_order")]
    public required int SequenceOrder { get; init; }

    [JsonPropertyName("estimated_offset_minutes")]
    public int? EstimatedOffsetMinutes { get; init; }

    public static RouteStopEntryResponse From(RouteStopEntry entry) => new()
    {
        StopId = entry.StopId,
        SequenceOrder = entry.SequenceOrder,
        EstimatedOffsetMinutes = entry.EstimatedOffsetMinutes,
    };
}

// --- Route ---

public sealed record CreateRouteRequest
{
    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("stops")]
    public IReadOnlyList<RouteStopEntryRequest>? Stops { get; init; }
}

public sealed record UpdateRouteRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("stops")]
    public IReadOnlyList<RouteStopEntryRequest>? Stops { get; init; }
}

public sealed record RouteResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("stops")]
    public required IReadOnlyList<RouteStopEntryResponse> Stops { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static RouteResponse From(RouteRecord r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        CampusId = r.CampusId,
        Name = r.Name,
        Status = r.Status.ToWireString(),
        Stops = [.. r.Stops.Select(RouteStopEntryResponse.From)],
        Version = r.Version,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        UpdatedBy = r.UpdatedBy,
    };
}

public sealed record RouteListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<RouteResponse> Data { get; init; }
}

// --- Vehicle ---

public sealed record CreateVehicleRequest
{
    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("capacity")]
    public int? Capacity { get; init; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; init; }
}

public sealed record UpdateVehicleRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("capacity")]
    public int? Capacity { get; init; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; init; }
}

public sealed record VehicleResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("capacity")]
    public int? Capacity { get; init; }

    [JsonPropertyName("device_id")]
    public string? DeviceId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static VehicleResponse From(VehicleRecord r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        CampusId = r.CampusId,
        Name = r.Name,
        Capacity = r.Capacity,
        DeviceId = r.DeviceId,
        Status = r.Status.ToWireString(),
        Version = r.Version,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        UpdatedBy = r.UpdatedBy,
    };
}

public sealed record VehicleListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<VehicleResponse> Data { get; init; }
}

// --- Stop ---

public sealed record CreateStopRequest
{
    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed record UpdateStopRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed record StopResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("campus_id")]
    public string? CampusId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required string UpdatedAt { get; init; }

    [JsonPropertyName("updated_by")]
    public required string UpdatedBy { get; init; }

    public static StopResponse From(StopRecord r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        CampusId = r.CampusId,
        Name = r.Name,
        Description = r.Description,
        Status = r.Status.ToWireString(),
        Version = r.Version,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        UpdatedBy = r.UpdatedBy,
    };
}

public sealed record StopListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<StopResponse> Data { get; init; }
}

// --- PersonAssignment ---

public sealed record CreatePersonAssignmentRequest
{
    [JsonPropertyName("person_id")]
    public string? PersonId { get; init; }

    [JsonPropertyName("route_id")]
    public string? RouteId { get; init; }

    [JsonPropertyName("stop_id")]
    public string? StopId { get; init; }

    [JsonPropertyName("valid_from")]
    public string? ValidFrom { get; init; }

    [JsonPropertyName("valid_until")]
    public string? ValidUntil { get; init; }
}

public sealed record EndPersonAssignmentRequest
{
    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("end_at")]
    public string? EndAt { get; init; }
}

public sealed record PersonAssignmentResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("route_id")]
    public required string RouteId { get; init; }

    [JsonPropertyName("stop_id")]
    public required string StopId { get; init; }

    [JsonPropertyName("valid_from")]
    public required string ValidFrom { get; init; }

    [JsonPropertyName("valid_until")]
    public string? ValidUntil { get; init; }

    [JsonPropertyName("effective_status")]
    public required string EffectiveStatus { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("created_by")]
    public required string CreatedBy { get; init; }

    public static PersonAssignmentResponse From(PersonAssignment r, string now) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        PersonId = r.PersonId,
        RouteId = r.RouteId,
        StopId = r.StopId,
        ValidFrom = r.ValidFrom,
        ValidUntil = r.ValidUntil,
        EffectiveStatus = TransportStatusEvaluator.EffectiveStatus(r, now).ToWireString(),
        Version = r.Version,
        CreatedAt = r.CreatedAt,
        CreatedBy = r.CreatedBy,
    };
}

public sealed record PersonAssignmentListResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<PersonAssignmentResponse> Data { get; init; }
}
