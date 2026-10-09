using Biolog.Features.BiometricRetention.Audit;
using Biolog.Features.BiometricRetention.Domain;

namespace Biolog.Features.BiometricRetention.Service;

/// <summary>
/// BIO-BIO-009-AC02/AC04: orchestrates deleting one enrolled record and propagating that deletion
/// to its own modality's 1:N gallery cache. Implements the same audit-atomicity pattern as every
/// prior BIO-BIO mutating service (ADR-0003 Decision 7): a per-record lock
/// (<see cref="BiometricRetentionLockRegistry"/>) held across the whole audit-then-delete
/// sequence, audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="IBiometricRetentionAuditSink.ExistsAsync"/>, and passes
/// <see cref="CancellationToken.None"/> to the gateway call once the audit write is confirmed.
///
/// <b>Permission gating lives entirely at the HTTP endpoint layer.</b> See
/// <see cref="RetentionPolicyService"/>'s own header for the full ADR-0006 rationale - identical
/// here. By the time a caller reaches this service, authorization has already been decided.
///
/// <b>Two audit events per request, not one.</b> A "requested" event is written and confirmed
/// durable BEFORE <see cref="ITemplateDeletionGateway"/> is ever called (so a request that is
/// itself genuine is on the record even if the gateway then fails or the process dies) and a
/// second "completed"/"not found" event is written afterward, carrying the resolved
/// <see cref="DeletionOutcome"/>. This mirrors ADR-0003 Decision 1's own rule that an
/// audit trail must show both ends of an action, not only its final state.
///
/// <b>Why the caller never supplies a <c>PersonId</c>.</b> Zainab's round-1 finding
/// (ZA-BIO009-01): a caller-supplied <c>PersonId</c> independent of <c>RecordId</c> would let a
/// caller delete one person's record while evicting a DIFFERENT person's gallery entry. Fixed by
/// construction - <see cref="TemplateDeletionRequest"/> carries no <c>PersonId</c> at all;
/// <see cref="ITemplateDeletionGateway"/>'s delete methods return the deleted record's OWN
/// <c>PersonId</c> (resolved from the record itself, by the sibling story's own service, before
/// deletion), and gallery eviction uses only that verified value.
///
/// <b>ZB-BIO009-06: the gallery-cleanup obligation is durable, recorded BEFORE the source record
/// is deleted.</b> Round 2 resolved the deleted record's own <c>PersonId</c> from the gateway's
/// delete call itself - if gallery eviction then threw, the person id lived only on this method's
/// own stack and was lost forever; if eviction returned <c>false</c>, this service still reported
/// <see cref="DeletionOutcome.Deleted"/>, leaving a biometric gallery entry searchable after a
/// "successful" deletion with no way to find it again. Fixed by resolving the owner via
/// <see cref="ITemplateDeletionGateway"/>'s new read-only <c>Resolve*OwnerAsync</c> peeks and
/// recording it in <see cref="IPendingGalleryCleanupStore"/> BEFORE calling the delete methods at
/// all; gallery eviction gets a few bounded in-process retries, and if it still has not converged
/// the pending record is left in place (never removed) and the outcome is
/// <see cref="DeletionOutcome.DeletedPendingGalleryCleanup"/>, not <see cref="DeletionOutcome.Deleted"/>
/// - <see cref="ReconcilePendingGalleryCleanupAsync"/> is the recovery path a later call (or an
/// operator) drives to convergence, never silently dropped.
/// </summary>
internal sealed class BiometricDeletionService(
    ITemplateDeletionGateway gateway,
    IBiometricRetentionAuditSink auditSink,
    BiometricRetentionLockRegistry lockRegistry,
    IPendingGalleryCleanupStore pendingCleanupStore,
    Func<string>? newId = null,
    Func<string>? nowUtc = null)
{
    private readonly Func<string> _newId = newId ?? (() => Guid.NewGuid().ToString());
    private readonly Func<string> _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));

    public async Task<BiometricDeletionResult> DeleteAsync(TemplateDeletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lockKey = $"{request.TenantId}|{request.Kind}|{request.RecordId}";
        using var lockHandle = await lockRegistry.AcquireAsync(lockKey, cancellationToken);

        var requestedAt = _nowUtc();
        await AuditAsync(
            request.TenantId, "biometric_retention.deletion_requested", request.RecordId, request.Reason, request.ActorId,
            requestedAt, cancellationToken);

        var resolvedPersonId = request.Kind switch
        {
            BiometricRecordKind.FingerprintSession => await gateway.ResolveFingerprintSessionOwnerAsync(request.TenantId, request.RecordId, CancellationToken.None),
            BiometricRecordKind.FaceReference => await gateway.ResolveFaceReferenceOwnerAsync(request.TenantId, request.RecordId, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "unknown biometric record kind"),
        };

        if (resolvedPersonId is null)
        {
            await AuditAsync(request.TenantId, "biometric_retention.deletion_not_found", request.RecordId, null, request.ActorId, _nowUtc(), CancellationToken.None);
            return new BiometricDeletionResult { Outcome = DeletionOutcome.NotFound, GalleryPropagated = null };
        }

        // Durable: recorded before the source record is deleted, so a throw or a failed
        // eviction below can never lose this person id (ZB-BIO009-06).
        await pendingCleanupStore.RecordAsync(
            new PendingGalleryCleanup
            {
                TenantId = request.TenantId,
                Kind = request.Kind,
                PersonId = resolvedPersonId,
                RecordId = request.RecordId,
                CreatedAtUtc = requestedAt,
            },
            CancellationToken.None);

        DeletionOutcome deleteOutcome;
        try
        {
            (deleteOutcome, _) = request.Kind switch
            {
                BiometricRecordKind.FingerprintSession => await gateway.DeleteFingerprintSessionAsync(
                    request.TenantId, request.RecordId, request.Reason, request.ActorId, CancellationToken.None),
                BiometricRecordKind.FaceReference => await gateway.DeleteFaceReferenceAsync(request.TenantId, request.RecordId, CancellationToken.None),
                _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "unknown biometric record kind"),
            };
        }
        catch
        {
            // Round 4 (god's dispatch): an ambiguous delete-then-throw is NOT the same as
            // "confirmed never deleted" - the prior round discarded the pending obligation
            // either way, which could strand a genuinely-deleted record's gallery entry forever
            // if the delete itself actually committed before throwing. Verify via the same
            // read-only peek used to resolve the owner above (ADR-0003 Decision 7 element 4's
            // "ambiguous write throw -> verify, never assume" discipline, applied to deletion):
            // only a CONFIRMED "the record still exists" result justifies discarding the
            // speculative pending record; anything else (the record is gone, or verification
            // itself fails) must leave it in place for later reconciliation.
            var confirmedNotDeleted = false;
            try
            {
                var stillPresent = request.Kind switch
                {
                    BiometricRecordKind.FingerprintSession => await gateway.ResolveFingerprintSessionOwnerAsync(request.TenantId, request.RecordId, CancellationToken.None),
                    BiometricRecordKind.FaceReference => await gateway.ResolveFaceReferenceOwnerAsync(request.TenantId, request.RecordId, CancellationToken.None),
                    _ => null,
                };
                confirmedNotDeleted = stillPresent is not null;
            }
            catch
            {
                // Verification itself failed - genuinely ambiguous, not confirmed either way.
            }

            if (confirmedNotDeleted)
            {
                await pendingCleanupStore.RemoveAsync(request.TenantId, request.Kind, resolvedPersonId, CancellationToken.None);
            }

            throw;
        }

        if (deleteOutcome == DeletionOutcome.NotFound)
        {
            // Lost a race with a concurrent delete between the resolve above and here.
            await pendingCleanupStore.RemoveAsync(request.TenantId, request.Kind, resolvedPersonId, CancellationToken.None);
            await AuditAsync(request.TenantId, "biometric_retention.deletion_not_found", request.RecordId, null, request.ActorId, _nowUtc(), CancellationToken.None);
            return new BiometricDeletionResult { Outcome = DeletionOutcome.NotFound, GalleryPropagated = null };
        }

        var galleryPropagated = await TryEvictGalleryAsync(request.TenantId, request.Kind, resolvedPersonId, CancellationToken.None);
        if (galleryPropagated)
        {
            await pendingCleanupStore.RemoveAsync(request.TenantId, request.Kind, resolvedPersonId, CancellationToken.None);
        }

        var completedAt = _nowUtc();
        var finalOutcome = galleryPropagated ? DeletionOutcome.Deleted : DeletionOutcome.DeletedPendingGalleryCleanup;
        var completionAction = galleryPropagated
            ? "biometric_retention.deletion_completed"
            : "biometric_retention.deletion_completed_pending_gallery_cleanup";
        // CancellationToken.None, not the caller's token: once the "requested" audit event is
        // confirmed durable, this operation is past its own point of no return (ADR-0003
        // Decision 7, element 5) - a caller cancellation here must not leave a genuinely
        // attempted (and possibly already-completed) deletion with no completion record.
        await AuditAsync(request.TenantId, completionAction, request.RecordId, $"galleryPropagated={galleryPropagated}", request.ActorId, completedAt, CancellationToken.None);

        return new BiometricDeletionResult { Outcome = finalOutcome, GalleryPropagated = galleryPropagated };
    }

    /// <summary>BIO-BIO-009-AC04: retries every tenant's still-pending gallery-cleanup
    /// obligations to convergence. The one recovery path for a
    /// <see cref="DeletionOutcome.DeletedPendingGalleryCleanup"/> result - a later call here
    /// (operator-driven or scheduled) is what eventually removes a stale gallery entry, never a
    /// silent drop.</summary>
    public async Task<GalleryCleanupReconciliationResult> ReconcilePendingGalleryCleanupAsync(string tenantId, CancellationToken cancellationToken)
    {
        var pending = await pendingCleanupStore.ListAsync(tenantId, cancellationToken);
        var resolved = 0;
        var stillPending = 0;
        foreach (var entry in pending)
        {
            var converged = await TryEvictGalleryAsync(entry.TenantId, entry.Kind, entry.PersonId, cancellationToken);
            if (converged)
            {
                await pendingCleanupStore.RemoveAsync(entry.TenantId, entry.Kind, entry.PersonId, cancellationToken);
                await AuditAsync(entry.TenantId, "biometric_retention.gallery_cleanup_reconciled", entry.RecordId, null, "system", _nowUtc(), cancellationToken);
                resolved++;
            }
            else
            {
                stillPending++;
            }
        }

        return new GalleryCleanupReconciliationResult { Resolved = resolved, StillPending = stillPending };
    }

    /// <summary>Bounded in-process retry before leaving the durable pending record (already
    /// stored by the caller) as the recovery path - a throw is treated the same as a `false`
    /// result, never surfaced here.</summary>
    private async Task<bool> TryEvictGalleryAsync(string tenantId, BiometricRecordKind kind, string personId, CancellationToken cancellationToken, int attempts = 3)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var removed = kind switch
                {
                    BiometricRecordKind.FingerprintSession => await gateway.RemoveFromFingerprintGalleryAsync(tenantId, personId, cancellationToken),
                    BiometricRecordKind.FaceReference => await gateway.RemoveFromFaceGalleryAsync(tenantId, personId, cancellationToken),
                    _ => false,
                };
                if (removed)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Swallow and retry within budget; the durable pending record is the safety net.
            }
        }

        return false;
    }

    /// <summary>ADR-0003 Decision 7, element 4 - identical shape to
    /// <see cref="RetentionPolicyService"/>'s own private helper (each feature's own small
    /// primitive, not shared, per this codebase's established convention).</summary>
    private async Task AuditAsync(
        string tenantId, string action, string targetId, string? reason, string actorId, string nowUtc,
        CancellationToken cancellationToken)
    {
        var eventId = _newId();
        var auditEvent = new BiometricRetentionAuditEvent
        {
            EventId = eventId,
            TenantId = tenantId,
            Action = action,
            TargetId = targetId,
            Reason = reason,
            ActorId = actorId,
            OccurredAtUtc = nowUtc,
        };

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
            exists = await auditSink.ExistsAsync(eventId, cancellationToken);
        }
        catch (Exception)
        {
            throw BiometricRetentionException.AuditUnavailable("verification could not confirm durability.");
        }

        if (!exists)
        {
            throw BiometricRetentionException.AuditUnavailable("the event was not found on verification.");
        }
    }
}
