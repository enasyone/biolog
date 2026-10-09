using Biolog.Features.ImportConflictReview.Api;
using Biolog.Features.ImportConflictReview.Audit;
using Biolog.Features.ImportConflictReview.Domain;
using Biolog.Features.ImportConflictReview.Identifiers;
using Biolog.Features.ImportConflictReview.Tenancy;

namespace Biolog.Features.ImportConflictReview;

/// <summary>
/// Application service for BIO-INT-005 ("Implement external import conflict review").
/// Implements ADR-0003 Decision 7's audit-atomicity pattern exactly as established across every
/// sibling story in this epic: every mutating method holds an in-process, per-row lock
/// (<see cref="ImportConflictReviewLockRegistry"/>) across its read-validate-audit-commit
/// sequence, audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="IImportConflictReviewAuditSink.ExistsAsync"/> rather than assuming failure, and
/// passes <see cref="CancellationToken.None"/> to the repository commit once the audit write is
/// confirmed durable.
///
/// Registered Scoped, like every sibling identity-wave service, for the identical reason: this
/// service reads a per-request <see cref="ICurrentTenantProvider"/>, so it cannot be Singleton -
/// the lock table lives in its own Singleton class, <see cref="ImportConflictReviewLockRegistry"/>.
///
/// <b>AC01 - conflict compares source versus BIOLOG values.</b> <see cref="ScanAsync"/> reads
/// BIO-INT-002's own already-computed field comparisons through <see cref="IImportConflictGateway"/>
/// - this service never re-derives or re-compares a field value itself, only routes an
/// already-detected disagreement to a human review queue.
///
/// <b>AC02 - operator can keep/use source/escalate according to permission.</b>
/// <see cref="DecideAsync"/> requires <see cref="DecidePermission"/>
/// (checked via <see cref="IPermissionGateway"/>, the same ADR-0006 Decision 3 real-RBAC seam
/// BIO-INT-002's own <c>ApplyAsync</c> and BIO-INT-004's own subscription-write endpoints use)
/// and a verified provenance proof (enforced at the endpoint, see <c>ImportConflictReviewEndpoints.DecideAsync</c>).
///
/// <b>AC03 - decision is audited.</b> Every raise, augment, withdraw and decide writes an
/// append-only <see cref="ImportConflictReviewAuditEvent"/> before committing.
///
/// <b>AC04 - no automatic destructive overwrite of verified identity evidence.</b> Nothing in
/// this class, this project, or any endpoint it maps calls into BIO-IDN-001's <c>PersonService</c>
/// to mutate a PERSON record - this project has no <c>ProjectReference</c> to BIO-IDN-001's (or
/// BIO-INT-002's) project at all, so it structurally cannot. See
/// <see cref="ImportConflictReviewStatus"/>'s own header for why even <see cref="ImportConflictReviewStatus.UseSource"/>
/// never writes anything - it records a human's decision, not an executed correction.
///
/// <b>Never re-decides a decided row via scan.</b> Once a row has a terminal decision
/// (<see cref="ImportConflictReviewStatusExtensions.IsTerminalDecision"/>), a later
/// <see cref="ScanAsync"/> that would otherwise re-raise, augment or withdraw it leaves the
/// record untouched - a human already reviewed this row, and a routine rescan must never
/// silently reopen or relitigate that call (identical discipline to BIO-IDN-005's own
/// <c>DuplicateReviewService</c>). A reviewer can still change a decision by calling
/// <see cref="DecideAsync"/> again directly (fully audited either way) - this restriction is
/// only against an automated scan overriding a human's own choice.
///
/// <b>Withdrawal is not a decision.</b> <see cref="ImportConflictReviewStatus.Withdrawn"/> is
/// set only by <see cref="ScanAsync"/> itself, when a row's most recent import outcome is no
/// longer a conflict - see that status's own header. A withdrawn row that conflicts again on a
/// later import is re-raised back to <see cref="ImportConflictReviewStatus.PendingReview"/>,
/// never left stale.
///
/// <b>Audit trail privacy.</b> This feature's own audit <c>Detail</c> never carries a compared
/// field VALUE - only the external key, person id, mapping id and the WIRE LABEL of which
/// person field disagreed (see <see cref="ImportConflictReviewAuditEventBuilder"/>'s header).
/// </summary>
internal sealed class ImportConflictReviewService(
    ICurrentTenantProvider tenantProvider,
    IImportConflictReviewRepository repository,
    IImportConflictReviewAuditSink auditSink,
    ImportConflictReviewLockRegistry lockRegistry,
    IImportConflictGateway conflictGateway,
    IPermissionGateway permissionGateway)
{
    public const string DecidePermission = "integrations.conflict_review_decide";

    /// <summary>
    /// ZB-INT005-01/ZB-INT005-02 (Zainab, High, round 1): scan genuinely mutates state (raises/
    /// augments/withdraws <see cref="Domain.ImportConflictReview"/> rows) and writes audit
    /// events, and list exposes exact existing-vs-incoming identity field VALUES, person/
    /// external identifiers and decision notes - BOTH are real authorization decisions, not
    /// audit-attribution-only reads the way BIO-INT-002's own <c>PreviewAsync</c> is (that method
    /// never mutates or audits anything at all - the ADR-0006 Decision 2 exemption this class's
    /// own original header incorrectly borrowed by endpoint-shape analogy, without re-justifying
    /// it against THIS story's own actual side effects and actual data sensitivity). Fixed by
    /// gating both behind this one read permission (mirrors BIO-TEN-003's own
    /// <c>TenantFeatureConfigurationEndpoints</c> precedent exactly: its GET route requires
    /// BOTH a permission grant AND a verified provenance proof, the identical treatment as its
    /// own write route - a sensitive read is not exempt from either check just because it is a
    /// read). The provenance proof itself is verified at the endpoint layer
    /// (<c>ImportConflictReviewEndpoints.ScanAsync</c>/<c>ListAsync</c>), the same split this
    /// class's own <see cref="DecideAsync"/> already uses.
    /// </summary>
    public const string ReadPermission = "integrations.conflict_review_read";

    /// <summary>
    /// AC01: reduces this mapping's full applied-batch outcome history down to the MOST RECENT
    /// snapshot per external key (a later import attempt's own view of a row always supersedes
    /// an earlier one), then raises a new review, augments an existing non-terminal one, or
    /// withdraws one whose conflict no longer exists - see this class's own header.
    /// </summary>
    public async Task<IReadOnlyList<Domain.ImportConflictReview>> ScanAsync(
        string mappingId, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ReadPermission, cancellationToken))
        {
            throw ImportConflictReviewError.PermissionRequired(ReadPermission);
        }

        if (string.IsNullOrWhiteSpace(mappingId))
        {
            throw ImportConflictReviewError.MappingIdRequired();
        }

        var outcomes = await conflictGateway.ListRowOutcomesAsync(mappingId, cancellationToken);
        var latestPerKey = outcomes
            .GroupBy(o => o.ExternalKey, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(o => o.SubmittedAt, StringComparer.Ordinal).First());

        foreach (var snapshot in latestPerKey)
        {
            using var _ = await lockRegistry.AcquireAsync(tenantId, mappingId, snapshot.ExternalKey, cancellationToken);
            var existing = await repository.GetAsync(tenantId, mappingId, snapshot.ExternalKey, cancellationToken);

            if (snapshot.Outcome == RowOutcomeSnapshot.Conflict)
            {
                await RaiseOrAugmentAsync(tenantId, mappingId, snapshot, existing, actor, correlationId, cancellationToken);
            }
            else if (existing is not null && existing.Status == ImportConflictReviewStatus.PendingReview)
            {
                await WithdrawAsync(tenantId, existing, actor, correlationId, cancellationToken);
            }
            // Else: nothing to do - no conflict and nothing on file, or the row already has a
            // terminal decision (never touched by a scan, see this class's own header).
        }

        return await repository.ListForMappingAsync(tenantId, mappingId, cancellationToken);
    }

    /// <summary>AC02's review queue: every conflict review for this mapping, tenant-scoped. ZB-INT005-02: requires <see cref="ReadPermission"/> - see this class's own header.</summary>
    public async Task<IReadOnlyList<Domain.ImportConflictReview>> ListForMappingAsync(
        string mappingId, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ReadPermission, cancellationToken))
        {
            throw ImportConflictReviewError.PermissionRequired(ReadPermission);
        }

        if (string.IsNullOrWhiteSpace(mappingId))
        {
            throw ImportConflictReviewError.MappingIdRequired();
        }

        return await repository.ListForMappingAsync(tenantId, mappingId, cancellationToken);
    }

    /// <summary>AC02/AC03/AC04: records a human review decision. Never writes to PERSON - see class header.</summary>
    public async Task<Domain.ImportConflictReview> DecideAsync(
        string mappingId, string externalKey, DecideImportConflictReviewRequest request, AuditActor actor, string correlationId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, DecidePermission, cancellationToken))
        {
            throw ImportConflictReviewError.PermissionRequired(DecidePermission);
        }

        var errors = new List<ValidationErrorItem>();
        if (string.IsNullOrWhiteSpace(mappingId) || string.IsNullOrWhiteSpace(externalKey))
        {
            errors.Add(new ValidationErrorItem("mapping_id", "required", "mapping_id and external_key are both required"));
        }

        if (!ImportConflictReviewStatusExtensions.TryParseDecision(request.Decision, out var decision))
        {
            errors.Add(new ValidationErrorItem("decision", "invalid_format", "decision must be KEEP_EXISTING, USE_SOURCE or ESCALATED"));
        }

        var reasonCode = ValidateReasonCode(request.ReasonCode, errors);
        var notes = ValidateNotes(request.Notes, errors);

        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, mappingId, externalKey, cancellationToken);
        var current = await repository.GetAsync(tenantId, mappingId, externalKey, cancellationToken)
            ?? throw ImportConflictReviewError.NotFound(mappingId, externalKey);

        if (expectedVersion != current.Version)
        {
            throw ImportConflictReviewError.ConcurrencyConflict(mappingId, externalKey, expectedVersion, current.Version);
        }

        var now = UtcInstant.Now();
        var updated = current with
        {
            Status = decision,
            DecidedAt = now,
            DecidedByActorId = actor.Id,
            DecisionReasonCode = reasonCode,
            DecisionNotes = notes,
            LastSeenAt = now,
            Version = current.Version + 1,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "import_conflict_review.decide", mappingId, externalKey, reasonCode!, correlationId, cancellationToken);

        // Guaranteed to succeed: the per-row lock held since entry means no other writer in
        // this process could have changed `current` since it was read. CancellationToken is
        // None: the audit event above is already confirmed durable (see class header).
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    private async Task RaiseOrAugmentAsync(
        string tenantId, string mappingId, ImportRowSnapshot snapshot, Domain.ImportConflictReview? existing, AuditActor actor, string correlationId,
        CancellationToken cancellationToken)
    {
        var now = UtcInstant.Now();

        if (existing is null)
        {
            var review = new Domain.ImportConflictReview(tenantId, mappingId, snapshot.ExternalKey)
            {
                PersonId = snapshot.PersonId,
                Conflicts = snapshot.Conflicts,
                Status = ImportConflictReviewStatus.PendingReview,
                SourceBatchId = snapshot.BatchId,
                RaisedAt = now,
                LastSeenAt = now,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await AuditAsync(tenantId, actor, "import_conflict_review.raise", mappingId, snapshot.ExternalKey, "import_conflict_review_raised", correlationId, cancellationToken);
            await repository.CreateAsync(review, CancellationToken.None);
            return;
        }

        if (existing.Status.IsTerminalDecision())
        {
            // A human already decided this row - a routine rescan must never silently reopen
            // or relitigate that call (see class header).
            return;
        }

        var conflictsUnchanged = existing.Status == ImportConflictReviewStatus.PendingReview
            && new HashSet<ImportFieldConflict>(existing.Conflicts).SetEquals(snapshot.Conflicts);
        if (conflictsUnchanged)
        {
            // Nothing new to record - avoid audit noise on every rescan re-detecting the same,
            // already-known conflict.
            return;
        }

        var wasWithdrawn = existing.Status == ImportConflictReviewStatus.Withdrawn;
        var updated = existing with
        {
            PersonId = snapshot.PersonId,
            Conflicts = snapshot.Conflicts,
            Status = ImportConflictReviewStatus.PendingReview,
            SourceBatchId = snapshot.BatchId,
            RaisedAt = wasWithdrawn ? now : existing.RaisedAt,
            LastSeenAt = now,
            Version = existing.Version + 1,
            UpdatedAt = now,
        };

        var action = wasWithdrawn ? "import_conflict_review.reopen" : "import_conflict_review.augment";
        var reasonCode = wasWithdrawn ? "import_conflict_review_reopened" : "import_conflict_review_augmented";
        await AuditAsync(tenantId, actor, action, mappingId, snapshot.ExternalKey, reasonCode, correlationId, cancellationToken);
        await repository.ReplaceAsync(existing, updated, CancellationToken.None);
    }

    private async Task WithdrawAsync(string tenantId, Domain.ImportConflictReview existing, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var now = UtcInstant.Now();
        var updated = existing with { Status = ImportConflictReviewStatus.Withdrawn, LastSeenAt = now, Version = existing.Version + 1, UpdatedAt = now };

        await AuditAsync(tenantId, actor, "import_conflict_review.withdraw", existing.MappingId, existing.ExternalKey, "import_conflict_review_withdrawn", correlationId, cancellationToken);
        await repository.ReplaceAsync(existing, updated, CancellationToken.None);
    }

    /// <summary>
    /// Durably records this action as an <see cref="ImportConflictReviewAuditEvent"/>, resolving
    /// an ambiguous <see cref="IImportConflictReviewAuditSink.WriteAsync"/> throw via
    /// <see cref="IImportConflictReviewAuditSink.ExistsAsync"/> before treating it as a real
    /// failure (ADR-0003 Decision 7, element 4). Never includes a compared field VALUE - only
    /// the external key, person id and mapping id (see class header "Audit trail privacy").
    /// </summary>
    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string mappingId, string externalKey, string reasonCode, string correlationId,
        CancellationToken cancellationToken)
    {
        var target = new AuditTarget("import_conflict_review", $"{mappingId}/{externalKey}");
        var detail = new Dictionary<string, (object?, string)>
        {
            ["mapping_id"] = (mappingId, "internal"),
            ["external_key"] = (externalKey, "internal"),
        };

        var auditEvent = ImportConflictReviewAuditEventBuilder.Build(
            tenantId, actor, action, target, AuditOutcome.Allowed, reasonCode, correlationId, causationId: null, detail);

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
            throw ImportConflictReviewError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw ImportConflictReviewError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as success.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw ImportConflictReviewError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw ImportConflictReviewError.Validation(errors);
        }
    }

    private static string? ValidateReasonCode(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem("reason_code", "required", "reason_code is required to record a review decision"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 100)
        {
            errors.Add(new ValidationErrorItem("reason_code", "out_of_range", "reason_code must be at most 100 characters"));
            return null;
        }

        return trimmed;
    }

    private static string? ValidateNotes(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > 1000)
        {
            errors.Add(new ValidationErrorItem("notes", "out_of_range", "notes must be at most 1000 characters"));
            return null;
        }

        return trimmed;
    }
}
