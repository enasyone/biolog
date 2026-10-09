namespace Biolog.Features.TenantDomainRegistry.Domain;

/// <summary>
/// BIO-TEN-002-AC01: the control-plane mapping from a verified hostname to a tenant.
/// <c>contracts/schemas/tenant-domain.schema.json</c>'s required fields
/// (<c>hostname, tenant_id, status, is_primary</c>) are exactly this record's properties,
/// plus <see cref="Version"/>/<see cref="CreatedAt"/>/<see cref="UpdatedAt"/> for optimistic
/// concurrency (ADR-0001 Decision 3.1's mutable-aggregate envelope - the schema does not
/// forbid additional properties, and these three are versioning metadata, not identifiers).
///
/// <b>Deliberately no canonical id of its own kind (no <c>dom_</c> prefix).</b>
/// <see cref="Hostname"/> is this record's natural key: it is already required to be
/// globally unique (BIO-TEN-002-AC04), and ADR-0001 §1.2 registers exactly six identifier
/// types (tenant, person, credential, device, document session, event) - "a feature story may
/// not invent a prefix locally" without a registry entry, a superseding/amending ADR and
/// Kemi's recorded approval. Minting a <c>dom_</c> id here would be exactly that. If a later
/// story needs a domain identity independent of its hostname (e.g. to support renaming a
/// domain without losing its history), that is a registry amendment, out of this story's
/// scope - hostname-as-key is sufficient for every AC01-AC05 requirement as written.
///
/// A hostname's <see cref="TenantId"/> is immutable for the life of the record: this story
/// implements no "transfer to another tenant" operation, and a <c>RETIRED</c> record's
/// hostname is not freed for another tenant to claim (see
/// <see cref="TenantDomainService"/>'s class header, "Deferred: hostname reuse after retire").
/// This is what makes it safe for <see cref="TenantDomainService"/> to read a hostname's
/// tenant id once, outside any lock, purely to choose which tenant-scoped lock to take,
/// without re-validating it afterward - the value cannot have changed underneath it.
/// </summary>
public sealed record TenantDomain
{
    public required string Hostname { get; init; }
    public required string TenantId { get; init; }
    public required TenantDomainStatus Status { get; init; }
    public required bool IsPrimary { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
}
