namespace Biolog.Features.Authentication;

/// <summary>
/// AC01: provider configuration is tenant-scoped - every lookup/list/mutation this project
/// exposes takes a tenant id and only ever returns/affects that tenant's own rows (see
/// <see cref="InMemoryAuthProviderConfigRepository"/> and its dedicated cross-tenant-isolation
/// tests). <see cref="ProviderId"/> is a plain <see cref="Guid"/> string, not an ADR-0001
/// canonical id - none of ADR-0001 §1.2's six registered prefixes (ten/per/crd/dev/dcs/evt)
/// cover an auth-provider-config id, and minting a seventh needs a registry entry, a
/// superseding/amending ADR and Kemi's recorded approval - out of this foundation story's
/// scope, mirroring BIO-RPT-001's identical <c>NotificationRule.RuleId</c> precedent.
/// </summary>
public sealed record AuthProviderConfig(
    string ProviderId,
    string TenantId,
    AuthProviderType ProviderType,
    string DisplayName,
    bool Enabled,
    MfaRequirement Mfa,
    IReadOnlyList<ClaimMappingRule> ClaimMappingRules,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AuthProviderRegistrationRequest(
    AuthProviderType ProviderType,
    string DisplayName,
    MfaRequirement Mfa,
    IReadOnlyList<ClaimMappingRule> ClaimMappingRules);

public sealed record AuthProviderStatusChangeRequest(bool Enabled, int ExpectedVersion);
