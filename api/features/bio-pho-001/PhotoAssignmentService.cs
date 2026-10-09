using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;
using Biolog.Features.Photography.Identifiers;
using Biolog.Features.Photography.Tenancy;

namespace Biolog.Features.Photography;

/// <summary>
/// Application service for BIO-PHO-001-AC04 ("person-photo assignment is versioned and
/// audited"). Same audit-atomicity pattern as <see cref="PhotoMasterService"/>, plus ADR-0001
/// §3.2's real compare-and-set table, extended sensibly to the first-ever assignment (a
/// create, which has no prior version to compare against):
///
/// | Situation | Outcome |
/// |---|---|
/// | No assignment yet, caller supplies no <c>expectedVersion</c> | Create at version 1 |
/// | No assignment yet, caller supplies an <c>expectedVersion</c> | 409 <c>concurrency_conflict</c> (nothing exists to match) |
/// | Assignment exists, <c>expectedVersion</c> equals stored version | Replace; version becomes <c>version + 1</c> |
/// | Assignment exists, <c>expectedVersion</c> differs | 409 <c>concurrency_conflict</c> |
/// | Assignment exists, <c>expectedVersion</c> absent | 428 <c>precondition_required</c> |
///
/// Uses its own <see cref="PhotoAssignmentLockRegistry"/>, not
/// <see cref="PhotoMasterLockRegistry"/> - see that class's header for why.
/// </summary>
internal sealed class PhotoAssignmentService(
    ICurrentTenantProvider tenantProvider,
    IPhotoAssignmentRepository assignmentRepository,
    IPhotoMasterRepository masterRepository,
    IPersonExistenceGateway personGateway,
    IPhotoAuditSink auditSink,
    PhotoAssignmentLockRegistry lockRegistry)
{
    public async Task<PhotoAssignment> AssignAsync(
        string personId, int masterVersion, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!CanonicalId.IsPersonId(personId) || !await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw PhotoError.PersonNotFound(personId);
        }

        if (await masterRepository.GetVersionAsync(tenantId, personId, masterVersion, cancellationToken) is null)
        {
            throw PhotoError.MasterNotFound(personId, masterVersion);
        }

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);

        var current = await assignmentRepository.GetAsync(tenantId, personId, cancellationToken);
        var now = UtcInstant.Now();

        if (current is null)
        {
            if (expectedVersion is not null)
            {
                throw PhotoError.AssignmentConcurrencyConflict(personId, expectedVersion.Value, actual: 0);
            }

            var created = new PhotoAssignment(personId, tenantId)
            {
                CurrentMasterVersion = masterVersion,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await AuditAsync(
                tenantId, actor, "photo.assignment.update", personId, "photo_assignment_created", correlationId, causationId: null,
                detail: new Dictionary<string, (object?, string)>
                {
                    ["previous_master_version"] = (null, "internal"),
                    ["new_master_version"] = (masterVersion, "internal"),
                    ["new_version"] = (1, "internal"),
                },
                cancellationToken);

            return await assignmentRepository.CreateAsync(created, CancellationToken.None);
        }

        if (expectedVersion is null)
        {
            throw PhotoError.PreconditionRequired(personId);
        }

        if (expectedVersion.Value != current.Version)
        {
            throw PhotoError.AssignmentConcurrencyConflict(personId, expectedVersion.Value, current.Version);
        }

        var updated = current with
        {
            CurrentMasterVersion = masterVersion,
            Version = current.Version + 1,
            UpdatedAt = now,
        };

        await AuditAsync(
            tenantId, actor, "photo.assignment.update", personId, "photo_assignment_updated", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)>
            {
                ["previous_master_version"] = (current.CurrentMasterVersion, "internal"),
                ["new_master_version"] = (masterVersion, "internal"),
                ["new_version"] = (updated.Version, "internal"),
            },
            cancellationToken);

        return await assignmentRepository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public Task<PhotoAssignment?> GetAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return assignmentRepository.GetAsync(tenantId, personId, cancellationToken);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string reasonCode, string correlationId, string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = PhotoAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("photo_assignment", personId), AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail);

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
