using Biolog.Features.Integrations.Identifiers;

namespace Biolog.Features.Integrations.Domain;

/// <summary>BIO-INT-002-AC01: which external system this mapping imports from.</summary>
public enum SourceSystemType
{
    Sis,
    Hr,
    Payroll,
    Erp,
    CsvExcel,
}

public static class SourceSystemTypeExtensions
{
    public static string ToWireString(this SourceSystemType value) => value switch
    {
        SourceSystemType.Sis => "sis",
        SourceSystemType.Hr => "hr",
        SourceSystemType.Payroll => "payroll",
        SourceSystemType.Erp => "erp",
        SourceSystemType.CsvExcel => "csv_excel",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out SourceSystemType result)
    {
        switch (value)
        {
            case "sis": result = SourceSystemType.Sis; return true;
            case "hr": result = SourceSystemType.Hr; return true;
            case "payroll": result = SourceSystemType.Payroll; return true;
            case "erp": result = SourceSystemType.Erp; return true;
            case "csv_excel": result = SourceSystemType.CsvExcel; return true;
            default: result = default; return false;
        }
    }
}

/// <summary>
/// BIO-INT-002-AC02: which side is the system of record for this one integration.
/// <see cref="SourceIsMaster"/> is the normal inbound-import flow (AC01/03/04/05). When a
/// mapping is <see cref="BiologIsMaster"/>, BIOLOG is authoritative and
/// <see cref="ImportBatchService.ApplyAsync"/> rejects every inbound apply for it outright
/// (<see cref="ImportError.BiologIsMasterForThisIntegration"/>) - the only meaningful,
/// testable effect a master/downstream flag can have on a framework whose only operation is
/// "accept inbound data": never silently accept and discard what should have been rejected.
/// </summary>
public enum SyncDirection
{
    SourceIsMaster,
    BiologIsMaster,
}

public static class SyncDirectionExtensions
{
    public static string ToWireString(this SyncDirection value) => value switch
    {
        SyncDirection.SourceIsMaster => "source_is_master",
        SyncDirection.BiologIsMaster => "biolog_is_master",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out SyncDirection result)
    {
        switch (value)
        {
            case "source_is_master": result = SyncDirection.SourceIsMaster; return true;
            case "biolog_is_master": result = SyncDirection.BiologIsMaster; return true;
            default: result = default; return false;
        }
    }
}

/// <summary>One field-level correspondence between the external source's own column/attribute name and a BIO-IDN-001 PERSON field this story is authorized to populate at creation time (<see cref="IPersonGateway.CreateAsync"/>'s own <c>CreatePersonFields</c>).</summary>
public sealed record FieldMapping(string SourceField, string PersonField);

/// <summary>
/// BIO-INT-002-AC01: one versioned configuration for a single external integration. CAS-
/// versioned (ADR-0001 Decision 3.2), <c>Id</c> a plain, non-canonical-registry UUIDv7 (see
/// <see cref="CanonicalId"/>'s header) - the same <c>AuthProviderConfig</c> precedent BIO-INT-001
/// already established for an identical reason.
///
/// Construction is <c>internal</c> (mirrors BIO-IDN-001-QA-01's <c>Person</c> fix): only
/// <see cref="ImportMappingService"/> may mint or transition one, never a hand-built record
/// handed straight to the repository.
/// </summary>
public sealed record ImportSourceMapping
{
    public string Id { get; }
    public string TenantId { get; }
    public required SourceSystemType SourceSystem { get; init; }
    public required SyncDirection SyncDirection { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<FieldMapping> FieldMappings { get; init; }
    public required bool Active { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal ImportSourceMapping(string id, string tenantId)
    {
        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
    }
}
