using System.Collections.Concurrent;
using Biolog.Features.TenantFeatureConfig.Api;
using Biolog.Features.TenantFeatureConfig.Audit;
using Biolog.Features.TenantFeatureConfig.Domain;
using Biolog.Features.TenantFeatureConfig.Identifiers;

namespace Biolog.Features.TenantFeatureConfig;

/// <summary>
/// Application service for BIO-TEN-003. Mirrors BIO-TEN-001's own <c>TenantService</c>
/// concurrency/audit discipline exactly (per-tenant lock held across the entire
/// read-validate-audit-commit sequence; audit written and CONFIRMED durable BEFORE the
/// registry commit; once confirmed, the commit itself runs under
/// <see cref="CancellationToken.None"/> so a cancellation arriving in that gap cannot abort a
/// commit whose audit event already durably claims success) - see that class's own header
/// comment for the full three-round history this copies rather than re-derives.
///
/// <b>AC02 - 1:N requires explicit enablement.</b> <see cref="BiometricsFeatureConfig.Enabled"/>
/// and <see cref="BiometricsFeatureConfig.Face1NIdentificationEnabled"/> are always updated as
/// two independent fields in <see cref="UpdateAsync"/> - enabling the parent Biometrics module
/// never implicitly flips Face-1N on. <see cref="IsFace1NIdentificationEnabledSync"/> is the
/// synchronous read path <c>IFaceSearchFeatureFlagPolicy.IsEnabledForTenant</c> requires (it is
/// called synchronously, not awaited, by <c>FaceSearchService.SearchAsync</c>) - backed by an
/// in-memory cache updated on every successful commit, the same shape
/// <c>SyntheticFaceSearchFeatureFlagPolicy</c> already used.
///
/// <b>AC04 - fail closed.</b> <see cref="GetEffectiveAsync"/> returns
/// <see cref="TenantFeatureConfiguration.Default"/> (every module/subfeature <c>false</c>) for
/// a tenant with no configuration ever written - never a 404/exception. The same default
/// backs <see cref="IsFace1NIdentificationEnabledSync"/>'s empty-cache case.
/// </summary>
public sealed class TenantFeatureConfigurationService(
    ITenantFeatureConfigurationStore store, ITenantFeatureConfigurationAuditSink auditSink) : ITenantFeatureGateway
{
    // One semaphore per tenant id - see TenantService.LockTenantAsync's own header comment
    // for why an entry per tenant that is never evicted is not a concern at this scale.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tenantLocks = new(StringComparer.Ordinal);

    // BIO-TEN-003-AC02: the synchronous-read cache backing IsFace1NIdentificationEnabledSync.
    // Updated on every successful commit in UpdateAsync; an unknown tenant id is simply absent
    // (TryGetValue returns false), which IsFace1NIdentificationEnabledSync below treats as
    // disabled - the fail-closed default, never a lookup failure a caller could mishandle.
    private readonly ConcurrentDictionary<string, bool> _face1NCache = new(StringComparer.Ordinal);

    private async Task<IDisposable> LockTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        var gate = _tenantLocks.GetOrAdd(tenantId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    /// <summary>BIO-TEN-003-AC04: the fail-closed effective read. Never throws for a tenant
    /// with no configuration ever written - returns the synthesized, all-disabled default.</summary>
    public async Task<TenantFeatureConfiguration> GetEffectiveAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        var stored = await store.GetAsync(tenantId, cancellationToken);
        return stored ?? TenantFeatureConfiguration.Default(tenantId);
    }

    /// <summary>
    /// BIO-TEN-003-AC03: versioned and audited. Every field on <paramref name="request"/> is
    /// optional - <c>null</c> leaves that field unchanged; only fields that actually differ
    /// from the current value are audited or committed (the identical no-op discipline
    /// BIO-TEN-001's own <c>TenantService.UpdateAsync</c> applies) - a request that changes
    /// nothing commits nothing and audits nothing.
    /// </summary>
    public async Task<TenantFeatureConfiguration> UpdateAsync(
        string tenantId, UpdateTenantFeatureConfigurationRequest request, AuditActor actor, string correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantId(tenantId);
        using var _ = await LockTenantAsync(tenantId, cancellationToken);

        var current = await store.GetAsync(tenantId, cancellationToken) ?? TenantFeatureConfiguration.Default(tenantId);

        var errors = new List<ValidationErrorItem>();
        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
        }

        ThrowIfInvalid(errors);

        if (request.Version!.Value != current.Version)
        {
            throw TenantFeatureConfigurationError.ConcurrencyConflict(tenantId, request.Version.Value, current.Version);
        }

        var newBiometrics = current.Biometrics with
        {
            Enabled = request.Biometrics?.Enabled ?? current.Biometrics.Enabled,
            Face1NIdentificationEnabled = request.Biometrics?.Face1NIdentificationEnabled ?? current.Biometrics.Face1NIdentificationEnabled,
        };
        var newDocumentVerify = current.DocumentVerify with
        {
            Enabled = request.DocumentVerify?.Enabled ?? current.DocumentVerify.Enabled,
            CaptureEnabled = request.DocumentVerify?.CaptureEnabled ?? current.DocumentVerify.CaptureEnabled,
            MrzEnabled = request.DocumentVerify?.MrzEnabled ?? current.DocumentVerify.MrzEnabled,
            BarcodeEnabled = request.DocumentVerify?.BarcodeEnabled ?? current.DocumentVerify.BarcodeEnabled,
            OcrEnabled = request.DocumentVerify?.OcrEnabled ?? current.DocumentVerify.OcrEnabled,
            AuthenticityEnabled = request.DocumentVerify?.AuthenticityEnabled ?? current.DocumentVerify.AuthenticityEnabled,
            DocumentIdentificationEnabled = request.DocumentVerify?.DocumentIdentificationEnabled ?? current.DocumentVerify.DocumentIdentificationEnabled,
            NfcEnabled = request.DocumentVerify?.NfcEnabled ?? current.DocumentVerify.NfcEnabled,
            SelfieComparisonEnabled = request.DocumentVerify?.SelfieComparisonEnabled ?? current.DocumentVerify.SelfieComparisonEnabled,
        };
        var newCardProduction = request.CardProductionEnabled ?? current.CardProductionEnabled;
        var newVisitor = request.VisitorEnabled ?? current.VisitorEnabled;
        var newAttendance = request.AttendanceEnabled ?? current.AttendanceEnabled;
        var newTransport = request.TransportEnabled ?? current.TransportEnabled;

        var changed = new Dictionary<string, (object?, string)>();
        if (newBiometrics.Enabled != current.Biometrics.Enabled) changed["biometrics.enabled"] = (newBiometrics.Enabled, "internal");
        if (newBiometrics.Face1NIdentificationEnabled != current.Biometrics.Face1NIdentificationEnabled)
            changed["biometrics.face_1n_identification_enabled"] = (newBiometrics.Face1NIdentificationEnabled, "internal");
        if (newCardProduction != current.CardProductionEnabled) changed["card_production_enabled"] = (newCardProduction, "internal");
        if (newVisitor != current.VisitorEnabled) changed["visitor_enabled"] = (newVisitor, "internal");
        if (newAttendance != current.AttendanceEnabled) changed["attendance_enabled"] = (newAttendance, "internal");
        if (newTransport != current.TransportEnabled) changed["transport_enabled"] = (newTransport, "internal");
        if (newDocumentVerify.Enabled != current.DocumentVerify.Enabled) changed["document_verify.enabled"] = (newDocumentVerify.Enabled, "internal");
        if (newDocumentVerify.CaptureEnabled != current.DocumentVerify.CaptureEnabled) changed["document_verify.capture_enabled"] = (newDocumentVerify.CaptureEnabled, "internal");
        if (newDocumentVerify.MrzEnabled != current.DocumentVerify.MrzEnabled) changed["document_verify.mrz_enabled"] = (newDocumentVerify.MrzEnabled, "internal");
        if (newDocumentVerify.BarcodeEnabled != current.DocumentVerify.BarcodeEnabled) changed["document_verify.barcode_enabled"] = (newDocumentVerify.BarcodeEnabled, "internal");
        if (newDocumentVerify.OcrEnabled != current.DocumentVerify.OcrEnabled) changed["document_verify.ocr_enabled"] = (newDocumentVerify.OcrEnabled, "internal");
        if (newDocumentVerify.AuthenticityEnabled != current.DocumentVerify.AuthenticityEnabled) changed["document_verify.authenticity_enabled"] = (newDocumentVerify.AuthenticityEnabled, "internal");
        if (newDocumentVerify.DocumentIdentificationEnabled != current.DocumentVerify.DocumentIdentificationEnabled) changed["document_verify.document_identification_enabled"] = (newDocumentVerify.DocumentIdentificationEnabled, "internal");
        if (newDocumentVerify.NfcEnabled != current.DocumentVerify.NfcEnabled) changed["document_verify.nfc_enabled"] = (newDocumentVerify.NfcEnabled, "internal");
        if (newDocumentVerify.SelfieComparisonEnabled != current.DocumentVerify.SelfieComparisonEnabled) changed["document_verify.selfie_comparison_enabled"] = (newDocumentVerify.SelfieComparisonEnabled, "internal");

        if (changed.Count == 0)
        {
            // No-op update: nothing changed, so nothing is committed or audited, and the
            // version does not move - same posture BIO-TEN-001's UpdateStatusAsync takes.
            return current;
        }

        var now = UtcInstant.Now();
        var wasNeverWritten = current.Version == 0;
        var updated = current with
        {
            Biometrics = newBiometrics,
            CardProductionEnabled = newCardProduction,
            VisitorEnabled = newVisitor,
            AttendanceEnabled = newAttendance,
            TransportEnabled = newTransport,
            DocumentVerify = newDocumentVerify,
            Version = current.Version + 1,
            CreatedAt = wasNeverWritten ? now : current.CreatedAt,
            UpdatedAt = now,
        };

        await AuditAsync(actor, "tenant_feature_configuration.update", tenantId, "feature_configuration_updated", correlationId, changed, cancellationToken);

        // Once the audit write is confirmed, the audit event is the real point of no return -
        // see TenantService's own header for why the commit now runs under CancellationToken.None.
        var committed = wasNeverWritten
            ? await store.CreateAsync(updated, CancellationToken.None)
            : await store.ReplaceAsync(current, updated, CancellationToken.None);

        // Round-2 remediation (ZB-TEN003-02/BIO-TEN-003-F02, High). Stored values stay two
        // independent fields (AC02), but the CACHED, EFFECTIVE policy is their AND: a disabled
        // parent Biometrics module must fail closed for Face 1:N too, even when the child flag
        // itself is still true. Keeps the synchronous Face-1N cache in lockstep with every
        // committed write, so IsFace1NIdentificationEnabledSync never observes a stale value.
        _face1NCache[tenantId] = committed.Biometrics.Enabled && committed.Biometrics.Face1NIdentificationEnabled;

        return committed;
    }

    public async Task<bool> IsModuleEnabledAsync(string tenantId, TenantModule module, CancellationToken cancellationToken = default)
    {
        var config = await GetEffectiveAsync(tenantId, cancellationToken);
        return config.IsModuleEnabled(module);
    }

    public async Task<bool> IsDocumentVerifySubfeatureEnabledAsync(string tenantId, DocumentVerifySubfeature subfeature, CancellationToken cancellationToken = default)
    {
        var config = await GetEffectiveAsync(tenantId, cancellationToken);
        return config.DocumentVerify.IsEnabled(subfeature);
    }

    /// <summary>Round-2 remediation (ZB-TEN003-02/BIO-TEN-003-F02, High): effective enablement is the parent-AND-child policy, not the raw child flag.</summary>
    public async Task<bool> IsFace1NIdentificationEnabledAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var config = await GetEffectiveAsync(tenantId, cancellationToken);
        return config.Biometrics.Enabled && config.Biometrics.Face1NIdentificationEnabled;
    }

    /// <summary>
    /// BIO-TEN-003-AC02: the synchronous read <c>IFaceSearchFeatureFlagPolicy.IsEnabledForTenant</c>
    /// requires (see <c>Integration/TenantFeatureFlagPolicyAdapter.cs</c>). An unknown tenant id
    /// (never written to, or never updated since this process started) is absent from the
    /// cache, which <c>ConcurrentDictionary.TryGetValue</c> reports as <c>false</c> - the same
    /// fail-closed default <see cref="GetEffectiveAsync"/> gives the async path.
    ///
    /// Round-2 remediation (ZB-TEN003-02/BIO-TEN-003-F02, High): the cached value is already the
    /// parent-AND-child effective policy (set in <see cref="UpdateAsync"/>), so this read needs
    /// no further combination - a disabled parent Biometrics module fails closed here too.
    /// </summary>
    public bool IsFace1NIdentificationEnabledSync(string tenantId) =>
        _face1NCache.TryGetValue(tenantId, out var enabled) && enabled;

    private static void ValidateTenantId(string tenantId)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw TenantFeatureConfigurationError.InvalidTenantId(tenantId);
        }
    }

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw TenantFeatureConfigurationError.Validation(errors);
        }
    }

    /// <summary>Mirrors <c>TenantService.AuditAsync</c> exactly - audit written and verified
    /// BEFORE the commit; a <see cref="ITenantFeatureConfigurationAuditSink.WriteAsync"/> throw
    /// is resolved via <see cref="ITenantFeatureConfigurationAuditSink.ExistsAsync"/> rather
    /// than assumed to mean "never happened" (BIO-TEN-001-QA-01 round 4's finding, copied
    /// rather than re-derived).</summary>
    private async Task AuditAsync(
        AuditActor actor, string action, string tenantId, string reasonCode, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = TenantFeatureConfigurationAuditEventBuilder.Build(
            actor, action, tenantId, new AuditTarget("tenant_feature_configuration", tenantId), AuditOutcome.Allowed, reasonCode,
            correlationId, causationId: null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw TenantFeatureConfigurationError.AuditUnavailable();
        }

        if (!exists)
        {
            throw TenantFeatureConfigurationError.AuditUnavailable();
        }
    }
}
