using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;
using Biolog.Features.Photography.Tenancy;

namespace Biolog.Features.Photography;

/// <summary>
/// Application service for BIO-PHO-001-AC02 (derivatives reference the master) and AC03
/// (regeneration does not replace the master). Same audit-atomicity pattern as
/// <see cref="PhotoMasterService"/> - see its header.
///
/// <b>AC03's actual proof.</b> <see cref="RegenerateAsync"/> touches
/// <see cref="IPhotoMasterRepository"/> through exactly one call, the read-only
/// <see cref="IPhotoMasterRepository.GetVersionAsync"/> - that interface has no write method
/// this service (or anything else) could call even by mistake. The only write this method ever
/// performs is <see cref="IPhotoDerivativeRepository.UpsertAsync"/>, which can only ever affect
/// a <see cref="PhotoDerivative"/> row. See <see cref="IPhotoDerivativeRepository"/>'s header
/// for the full structural argument, and
/// <c>tests/features/bio-pho-001/PhotoDerivativeServiceTests.cs</c>'s
/// <c>AC03_regenerating_a_derivative_many_times_never_changes_the_masters_own_content_or_version</c>
/// for the regression test.
///
/// Reuses <see cref="PhotoMasterLockRegistry"/> (not a separate registry) - see that class's
/// header for why regeneration and master-append serialize on the same lock.
/// </summary>
internal sealed class PhotoDerivativeService(
    ICurrentTenantProvider tenantProvider,
    IPhotoMasterRepository masterRepository,
    IPhotoDerivativeRepository derivativeRepository,
    IPhotoDerivativeGenerator generator,
    IPhotoAuditSink auditSink,
    PhotoMasterLockRegistry lockRegistry)
{
    public async Task<PhotoDerivative> RegenerateAsync(
        string personId, int masterVersion, DerivativeKind kind, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var master = await masterRepository.GetVersionAsync(tenantId, personId, masterVersion, cancellationToken)
            ?? throw PhotoError.MasterNotFound(personId, masterVersion);

        var (format, widthPx, heightPx, content) = generator.Generate(master, kind);
        var derivative = new PhotoDerivative(
            personId, tenantId, masterVersion, kind, format, widthPx, heightPx, content,
            Identifiers.UtcInstant.Now(), MetadataOnlyPhotoDerivativeGenerator.GeneratorTag);

        await AuditAsync(
            tenantId, actor, "photo.derivative.regenerate", personId, "photo_derivative_regenerated", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)>
            {
                ["master_version"] = (masterVersion, "internal"),
                ["kind"] = (kind.ToWireString(), "internal"),
                ["width_px"] = (widthPx, "internal"),
                ["height_px"] = (heightPx, "internal"),
            },
            cancellationToken);

        return await derivativeRepository.UpsertAsync(derivative, CancellationToken.None);
    }

    public Task<PhotoDerivative?> GetAsync(string personId, int masterVersion, DerivativeKind kind, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return derivativeRepository.GetAsync(tenantId, personId, masterVersion, kind, cancellationToken);
    }

    public Task<IReadOnlyList<PhotoDerivative>> ListForMasterAsync(string personId, int masterVersion, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return derivativeRepository.ListForMasterAsync(tenantId, personId, masterVersion, cancellationToken);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string reasonCode, string correlationId, string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = PhotoAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("photo_derivative", personId), AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail);

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
            throw PhotoError.AuditUnavailable();
        }

        if (!exists)
        {
            throw PhotoError.AuditUnavailable();
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw PhotoError.TenantContextMissing();
}
