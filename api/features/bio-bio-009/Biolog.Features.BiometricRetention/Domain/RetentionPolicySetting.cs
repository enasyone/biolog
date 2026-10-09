namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>BIO-BIO-009-AC01: one tenant/category retention configuration. Immutable - a change
/// replaces the stored value, it never mutates one in place.</summary>
internal sealed record RetentionPolicySetting
{
    public required int RetentionDays { get; init; }

    /// <summary>Whether this value was explicitly configured (<c>true</c>) or is the
    /// category's own unconfigured default (<c>false</c>) - lets a caller distinguish "a tenant
    /// chose this" from "nobody has configured this tenant yet", the same distinction
    /// BIO-BIO-008's own <c>ILivenessPolicy</c> makes for its own unconfigured default.</summary>
    public required bool IsExplicitlyConfigured { get; init; }
}
