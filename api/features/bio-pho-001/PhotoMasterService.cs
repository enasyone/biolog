using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;
using Biolog.Features.Photography.Identifiers;
using Biolog.Features.Photography.Tenancy;

namespace Biolog.Features.Photography;

/// <summary>
/// Application service for BIO-PHO-001-AC01 (immutable/versioned master). Implements ADR-0003
/// Decision 7's audit-atomicity pattern - established across BIO-TEN-001/002, BIO-DEV-001 and
/// every BIO-IDN story - applied fresh here: <see cref="AppendAsync"/> holds an in-process,
/// per-person-id lock (<see cref="PhotoMasterLockRegistry"/>) across the whole
/// read-next-version-validate-audit-commit sequence, audits BEFORE committing, resolves an
/// ambiguous audit-write throw via <see cref="IPhotoAuditSink.ExistsAsync"/> rather than
/// assuming failure, and passes <see cref="CancellationToken.None"/> to the repository commit
/// once the audit write is confirmed durable.
///
/// <b>Registration lifetime and locking.</b> Registered Scoped (reads a per-request
/// <see cref="ICurrentTenantProvider"/>) - see <see cref="PhotoMasterLockRegistry"/>'s header
/// for why the lock table lives in its own injected Singleton instead.
///
/// <b>Tenant context.</b> Reads <see cref="ICurrentTenantProvider.TenantId"/> itself
/// (<see cref="RequireTenant"/>), never accepts one from a caller, and throws
/// <see cref="PhotoError.TenantContextMissing"/> when none is resolved - the same fail-closed
/// rule every sibling tenant-plane story applies.
///
/// <b>Actor identity (ADR-0006 Decision 1/4).</b> <see cref="AppendAsync"/>'s caller supplies
/// <see cref="AuditActor"/> purely for audit ATTRIBUTION - nothing about who may append a
/// photo master is gated on the asserted identity (every caller with API access may append a
/// master for any person in the resolved tenant, uniformly). This is Decision 1's safe
/// "audit-attribution use" shape, the same disclosed-residual category ADR-0006 Decision 4
/// already catalogues for BIO-IDN-001 through BIO-IDN-005 - a forged actor id can misattribute
/// who claims responsibility, but crosses no privilege-escalation boundary, since there is no
/// differential authority here for a forged identity to escalate into. It does not trigger a
/// security review under the same convention that correctly scoped one to BIO-IDN-006 alone.
///
/// <b>Audit trail privacy (ADR-0003 Decision 3).</b> "Photo reference" is <c>personal</c>
/// (allowlist only); raw image bytes are never carried in any form. This service's audit
/// <c>Detail</c> only ever carries <c>internal</c> technical metadata (format, dimensions,
/// size, version) - see <see cref="PhotoAuditEventBuilder"/>'s header.
/// </summary>
internal sealed class PhotoMasterService(
    ICurrentTenantProvider tenantProvider,
    IPhotoMasterRepository repository,
    IPersonExistenceGateway personGateway,
    IPhotoAuditSink auditSink,
    PhotoMasterLockRegistry lockRegistry)
{
    public async Task<PhotoMaster> AppendAsync(
        string personId, byte[] content, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!CanonicalId.IsPersonId(personId))
        {
            throw PhotoError.PersonNotFound(personId);
        }

        if (!await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw PhotoError.PersonNotFound(personId);
        }

        var inspected = PhotoRasterInspector.Inspect(content);
        if (inspected is null)
        {
            throw PhotoError.UnsupportedFormat();
        }

        var (format, widthPx, heightPx) = inspected.Value;

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var latest = await repository.GetLatestAsync(tenantId, personId, cancellationToken);
        var nextVersion = (latest?.Version ?? 0) + 1;

        var master = new PhotoMaster(personId, tenantId, nextVersion, format, widthPx, heightPx, content, UtcInstant.Now());

        await AuditAsync(
            tenantId, actor, "photo.master.append", personId, "photo_master_appended", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)>
            {
                ["version"] = (nextVersion, "internal"),
                ["format"] = (format.ToWireString(), "internal"),
                ["width_px"] = (widthPx, "internal"),
                ["height_px"] = (heightPx, "internal"),
                ["size_bytes"] = (content.Length, "internal"),
            },
            cancellationToken);

        // Once the audit write is confirmed, the audit event is the real point of no
        // return: a cancellation signal arriving in the gap before the commit must not be
        // allowed to abort it, or the durable Allowed event would outlive a change that
        // never touched the store.
        return await repository.AppendAsync(master, CancellationToken.None);
    }

    public Task<PhotoMaster?> GetLatestAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetLatestAsync(tenantId, personId, cancellationToken);
    }

    public Task<PhotoMaster?> GetVersionAsync(string personId, int version, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetVersionAsync(tenantId, personId, version, cancellationToken);
    }

    public Task<IReadOnlyList<PhotoMaster>> ListVersionsAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListVersionsAsync(tenantId, personId, cancellationToken);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string reasonCode, string correlationId, string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = PhotoAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("photo_master", personId), AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return; // confirmed: the write completed without throwing
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
            throw PhotoError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw PhotoError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as a
        // success, so the caller's subsequent commit makes this event's claim true.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw PhotoError.TenantContextMissing();
}
