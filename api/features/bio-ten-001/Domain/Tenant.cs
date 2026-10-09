namespace Biolog.Features.TenantRegistry.Domain;

/// <summary>
/// BIO-TEN-001: the control-plane tenant registry record. Configuration, licensing and
/// routing metadata only (BIO-TEN-001-AC02) - the fixed, named property list below IS the
/// enforcement: there is no free-form bag a caller could smuggle a person/credential/
/// biometric field into, and <see cref="Api.TenantRequests"/> rejects any JSON property that
/// does not map onto one of these names (see its <c>UnmappedMemberHandling.Disallow</c>).
///
/// Carries <c>id</c>/<c>version</c>/<c>created_at</c>/<c>updated_at</c> per ADR-0001 Decision
/// 3.1's mutable-aggregate envelope, but deliberately carries no <c>tenant_id</c>: this
/// record IS the tenant boundary (identifier-registry.json: type "tenant", plane "control"),
/// never a tenant-plane record filtered by one (ADR-0002 §1.4).
///
/// <see cref="RoutingRegion"/> is a coarse, non-secret placement hint (e.g. "eu-west"), never
/// a database name, schema, host, connection string or binding key - ADR-0002 §4.1 requires
/// the real tenant-to-database binding to be looked up, never supplied over this or any other
/// channel. Wiring an actual <c>TenantDataBinding</c> record is out of this story's scope.
/// </summary>
public sealed record Tenant
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required TenantStatus Status { get; init; }
    public required string LicensePlan { get; init; }
    public required int LicenseSeats { get; init; }
    public required string RoutingRegion { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
}
