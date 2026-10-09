namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// BIO-BIO-009-AC01: "Retention is configurable by tenant/category subject to policy." Tenant-
/// scoped and keyed by <see cref="RetentionCategory"/> - the same tenant/workflow-keyed shape
/// BIO-BIO-008's own <c>ILivenessPolicy</c> established for its own different question.
/// </summary>
internal interface IRetentionPolicy
{
    /// <summary>Never throws for an unconfigured pair - returns the category's own documented
    /// default with <see cref="RetentionPolicySetting.IsExplicitlyConfigured"/> false.</summary>
    Task<RetentionPolicySetting> GetAsync(string tenantId, RetentionCategory category, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="BiometricRetentionException.InvalidRetentionDays"/> for a
    /// non-positive value (AC01's own policy constraint) before anything is stored.</summary>
    Task SetAsync(string tenantId, RetentionCategory category, int retentionDays, CancellationToken cancellationToken);
}

/// <summary>Each category's own unconfigured default, in calendar days - the baseline every
/// unconfigured tenant gets before anyone has opened Settings &gt; Retention &amp; Privacy for
/// that tenant. Matches that page's own existing default options (`web/app/settings/page.tsx`'s
/// `biometricRetentionDays` default of 90) so a newly-provisioned tenant's backend policy agrees
/// with the UI's own displayed default on day one.</summary>
internal static class RetentionDefaults
{
    public const int FingerprintTemplateDays = 90;
    public const int FaceTemplateDays = 90;
    public const int RawImageDays = 30;

    public static int For(RetentionCategory category) => category switch
    {
        RetentionCategory.FingerprintTemplate => FingerprintTemplateDays,
        RetentionCategory.FaceTemplate => FaceTemplateDays,
        RetentionCategory.RawImage => RawImageDays,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "unknown retention category"),
    };
}
