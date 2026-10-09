namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// BIO-IDN-003-AC02: "Uniqueness rules are configurable per type." A tenant's institutional
/// identifier VALUES are, by default, unique within their own <c>(Type, CustomLabel)</c> scope
/// tenant-wide (across every person) - the sensible default for a matriculation or staff
/// number. A tenant may explicitly disable that enforcement for a given type via
/// <see cref="IdentifierUniquenessRuleService.SetRuleAsync"/> (e.g. a free-text tenant-defined
/// "notes reference" that legitimately repeats). No rule record existing for a
/// <c>(TenantId, Type, CustomLabel)</c> means the DEFAULT applies -
/// <see cref="InstitutionalIdentifierService.AssignAsync"/> treats a missing rule as
/// <see cref="Enforced"/> = <c>true</c>, never as "no policy" - a business identifier
/// uniqueness invariant should fail closed (favour rejecting a genuine duplicate over silently
/// admitting one) the same way every other fail-closed decision in this codebase does.
///
/// <b>Deliberately no compare-and-set on this record.</b> Every other mutable aggregate in this
/// codebase (<c>Person</c>, <c>PersonProfile</c>, <c>TenantDomain</c>...) requires a caller to
/// supply the version they last read, because a caller mutating THAT SPECIFIC record has a
/// legitimate reason to prove they saw its current state first (e.g. revoking the profile they
/// just looked at). A per-type uniqueness toggle has no such caller-specific intent to protect -
/// it is a blunt, tenant-wide admin setting, not a record a caller "owns" a view of. The
/// per-<c>(TenantId, Type, CustomLabel)</c> lock in <see cref="IdentifierUniquenessRuleLockRegistry"/>
/// already fully serializes writers to this exact key, so a compare-and-set would be redundant
/// defense with no real race to defend against - see
/// <see cref="IdentifierUniquenessRuleService.SetRuleAsync"/>. <see cref="Version"/> still
/// exists and still increments on every write (ADR-0001 Decision 3: every mutable aggregate
/// carries one), purely as an observability/audit trail of how many times a tenant has flipped
/// this setting - it is simply never checked against a caller-supplied expectation.
///
/// <b>Construction is internal</b>, matching every other aggregate in this project.
/// </summary>
public sealed record IdentifierUniquenessRule
{
    public string TenantId { get; }
    public IdentifierType Type { get; }
    public string? CustomLabel { get; }
    public required bool Enforced { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal IdentifierUniquenessRule(string tenantId, IdentifierType type, string? customLabel)
    {
        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (type == IdentifierType.TenantDefined && string.IsNullOrWhiteSpace(customLabel))
        {
            throw new ArgumentException("custom_label is required when type is TENANT_DEFINED", nameof(customLabel));
        }

        if (type != IdentifierType.TenantDefined && customLabel is not null)
        {
            throw new ArgumentException("custom_label must be null unless type is TENANT_DEFINED", nameof(customLabel));
        }

        TenantId = tenantId;
        Type = type;
        CustomLabel = customLabel;
    }
}
