using System.Collections.Concurrent;
using Biolog.Features.CardProduction.Audit;

namespace Biolog.Features.CardProduction.Domain;

/// <summary>
/// BIO-CRD-007-AC03: a signed, time-limited authorisation for a specific offline production
/// station to produce ONE specific physical card without live connectivity, then report the
/// result once connectivity resumes. <see cref="PackageId"/> is a plain opaque bearer-token id
/// (a GUID), NOT a canonical BIOLOG identifier (ADR-0001's six registered types) - a package is
/// an ephemeral authorisation artifact, the same "unregistered type" treatment an idempotency
/// key already receives in this codebase, never a durable business entity of its own kind.
///
/// Mutable and CAS-versioned only in the single, narrow sense that <see cref="Consumed"/> flips
/// exactly once (ADR-0001 §3.2's compare-and-set discipline, applied to a boolean instead of a
/// richer state machine because that is the ONLY thing about a package that ever changes).
/// </summary>
public sealed record ProductionPackage
{
    public required string PackageId { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required string CardClass { get; init; }
    public required int SequenceNumber { get; init; }
    public required string PrinterRef { get; init; }
    public required AuditActor Operator { get; init; }
    public required string WorkstationRef { get; init; }
    public string? ReprintReason { get; init; }
    public required string IssuedAt { get; init; }
    public required string ExpiresAt { get; init; }
    public required int Version { get; init; }
    public required bool Consumed { get; init; }
    public string? ConsumedAt { get; init; }
}

public interface IProductionPackageStore
{
    ProductionPackage Create(ProductionPackage package);

    ProductionPackage? Get(string tenantId, string packageId);

    /// <summary>Compare-and-set replace (ADR-0001 §3.2) - used to flip <see cref="ProductionPackage.Consumed"/> exactly once.</summary>
    ProductionPackage Replace(ProductionPackage expected, ProductionPackage updated);
}

internal sealed class InMemoryProductionPackageStore : IProductionPackageStore
{
    private readonly ConcurrentDictionary<string, ProductionPackage> _byKey = new(StringComparer.Ordinal);

    public ProductionPackage Create(ProductionPackage package)
    {
        var key = Key(package.TenantId, package.PackageId);
        if (!_byKey.TryAdd(key, package))
        {
            throw new ProductionError($"production package collision for id '{package.PackageId}'", "production_package_collision", 500);
        }

        return package;
    }

    public ProductionPackage? Get(string tenantId, string packageId) =>
        _byKey.TryGetValue(Key(tenantId, packageId), out var package) ? package : null;

    public ProductionPackage Replace(ProductionPackage expected, ProductionPackage updated)
    {
        var key = Key(expected.TenantId, expected.PackageId);

        if (!_byKey.TryGetValue(key, out var current))
        {
            throw ProductionError.PackageNotFound(expected.PackageId);
        }

        if (!_byKey.TryUpdate(key, updated, expected))
        {
            var latest = _byKey.TryGetValue(key, out var p) ? p : current;
            throw ProductionError.ConcurrencyConflict(expected.Version, latest.Version);
        }

        return updated;
    }

    private static string Key(string tenantId, string packageId) => $"{tenantId}\u0000{packageId}";
}
