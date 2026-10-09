using Biolog.Features.ImportConflictReview.Api;
using Biolog.Features.ImportConflictReview.Audit;
using Biolog.Features.ImportConflictReview.Domain;
using Biolog.Features.ImportConflictReview.Identifiers;
using Xunit;

namespace Biolog.Features.ImportConflictReview.Tests;

/// <summary>BIO-INT-005's core behaviour: AC01 (conflict compares source vs. BIOLOG), AC02/AC03 (permissioned, audited human decisions), AC04 (no automatic destructive overwrite - structurally, never a PERSON write).</summary>
public sealed class ImportConflictReviewServiceTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string MappingId = "map_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static ImportConflictReviewService NewService(
        IReadOnlyList<ImportRowSnapshot> rowOutcomes,
        IImportConflictReviewRepository? repository = null,
        InMemoryImportConflictReviewAuditSink? auditSink = null,
        ImportConflictReviewLockRegistry? lockRegistry = null,
        IReadOnlySet<string>? deniedPermissionKeys = null) =>
        new(
            new FakeCurrentTenantProvider(TenantId),
            repository ?? new InMemoryImportConflictReviewRepository(),
            auditSink ?? new InMemoryImportConflictReviewAuditSink(),
            lockRegistry ?? new ImportConflictReviewLockRegistry(),
            new FakeImportConflictGateway(rowOutcomes),
            new FakePermissionGateway(deniedPermissionKeys));

    private static ImportRowSnapshot Snapshot(string externalKey, RowOutcomeSnapshot outcome, string batchId, string submittedAt, IReadOnlyList<ImportFieldConflict>? conflicts = null) =>
        new()
        {
            ExternalKey = externalKey,
            Outcome = outcome,
            PersonId = PersonA,
            Conflicts = conflicts ?? [],
            BatchId = batchId,
            SubmittedAt = submittedAt,
        };

    private static readonly ImportFieldConflict LastNameConflict = new("last_name", "Okafor", "Okeke");
    private static readonly ImportFieldConflict LastNameConflictV2 = new("last_name", "Okafor", "Okonkwo");

    // --- AC01: conflict compares source vs. BIOLOG values ---

    [Fact]
    public async Task Scan_raises_a_pending_review_carrying_the_exact_existing_and_incoming_values_for_a_conflicting_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])]);

        var reviews = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var review = Assert.Single(reviews);
        Assert.Equal(ImportConflictReviewStatus.PendingReview, review.Status);
        Assert.Equal("ext-1", review.ExternalKey);
        var conflict = Assert.Single(review.Conflicts);
        Assert.Equal("last_name", conflict.PersonField);
        Assert.Equal("Okafor", conflict.ExistingValue);
        Assert.Equal("Okeke", conflict.IncomingValue);
    }

    [Fact]
    public async Task Scan_raises_nothing_for_a_row_with_no_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Created, "batch-1", "2026-10-07T00:00:00.000Z")]);

        var reviews = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        Assert.Empty(reviews);
    }

    [Fact]
    public async Task Scan_uses_only_the_most_recent_batchs_outcome_for_a_given_external_key()
    {
        var ct = TestContext.Current.CancellationToken;
        // An earlier batch saw a conflict; a LATER reimport resolved it (Unchanged now) - the
        // review queue must reflect the latest state, not the earlier conflict.
        var service = NewService(
        [
            Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict]),
            Snapshot("ext-1", RowOutcomeSnapshot.Unchanged, "batch-2", "2026-10-07T01:00:00.000Z"),
        ]);

        var reviews = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        Assert.Empty(reviews);
    }

    [Fact]
    public async Task Scan_is_idempotent_no_audit_noise_when_rescanning_an_unchanged_pending_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var auditSink = new InMemoryImportConflictReviewAuditSink();
        var snapshot = Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict]);
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService([snapshot], repository, auditSink);

        await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);
        var afterFirstScan = auditSink.Events.Count;
        await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(afterFirstScan, auditSink.Events.Count);
    }

    [Fact]
    public async Task Scan_augments_a_pending_review_when_the_conflicting_value_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service1 = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        await service1.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var service2 = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-2", "2026-10-07T01:00:00.000Z", [LastNameConflictV2])], repository);
        var reviews = await service2.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var review = Assert.Single(reviews);
        Assert.Equal(ImportConflictReviewStatus.PendingReview, review.Status);
        Assert.Equal("Okonkwo", Assert.Single(review.Conflicts).IncomingValue);
        Assert.Equal(2, review.Version);
    }

    [Fact]
    public async Task Scan_withdraws_a_pending_review_once_a_later_batch_no_longer_shows_a_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service1 = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        await service1.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var service2 = NewService(
        [
            Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict]),
            Snapshot("ext-1", RowOutcomeSnapshot.Unchanged, "batch-2", "2026-10-07T01:00:00.000Z"),
        ], repository);
        var reviews = await service2.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var review = Assert.Single(reviews);
        Assert.Equal(ImportConflictReviewStatus.Withdrawn, review.Status);
    }

    [Fact]
    public async Task Scan_reopens_a_withdrawn_review_back_to_pending_when_it_conflicts_again_later()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service1 = NewService(
        [
            Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict]),
            Snapshot("ext-1", RowOutcomeSnapshot.Unchanged, "batch-2", "2026-10-07T01:00:00.000Z"),
        ], repository);
        await service1.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct); // -> Withdrawn

        var service2 = NewService(
        [
            Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict]),
            Snapshot("ext-1", RowOutcomeSnapshot.Unchanged, "batch-2", "2026-10-07T01:00:00.000Z"),
            Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-3", "2026-10-07T02:00:00.000Z", [LastNameConflictV2]),
        ], repository);
        var reviews = await service2.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var review = Assert.Single(reviews);
        Assert.Equal(ImportConflictReviewStatus.PendingReview, review.Status);
    }

    [Fact]
    public async Task Scan_never_reopens_or_relitigates_a_row_that_already_has_a_human_decision()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service1 = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        var raised = await service1.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);
        var decided = await service1.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "KEEP_EXISTING", ReasonCode = "ops_review", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);
        Assert.Equal(ImportConflictReviewStatus.KeepExisting, decided.Status);

        // A later batch changes the conflicting value again - a routine rescan must never
        // reopen or relitigate the human's own KEEP_EXISTING call.
        var service2 = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-2", "2026-10-07T01:00:00.000Z", [LastNameConflictV2])], repository);
        var reviews = await service2.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var review = Assert.Single(reviews);
        Assert.Equal(ImportConflictReviewStatus.KeepExisting, review.Status);
        Assert.Equal("Okeke", Assert.Single(review.Conflicts).IncomingValue); // untouched, not re-augmented
    }

    // --- AC02/AC03: operator decides according to permission, decision is audited ---

    [Theory]
    [InlineData("KEEP_EXISTING", ImportConflictReviewStatus.KeepExisting)]
    [InlineData("USE_SOURCE", ImportConflictReviewStatus.UseSource)]
    [InlineData("ESCALATED", ImportConflictReviewStatus.Escalated)]
    public async Task Decide_records_each_of_the_three_permitted_outcomes(string wireDecision, ImportConflictReviewStatus expectedStatus)
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        var raised = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var decided = await service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = wireDecision, ReasonCode = "ops_review", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(expectedStatus, decided.Status);
        Assert.Equal(Actor.Id, decided.DecidedByActorId);
        Assert.Equal("ops_review", decided.DecisionReasonCode);
        Assert.NotNull(decided.DecidedAt);
    }

    [Fact]
    public async Task Decide_without_the_required_permission_is_rejected_and_never_changes_the_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var setupService = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        var raised = await setupService.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var service = NewService([], repository, deniedPermissionKeys: new HashSet<string> { ImportConflictReviewService.DecidePermission });
        var error = await Assert.ThrowsAsync<ImportConflictReviewError>(() => service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "KEEP_EXISTING", ReasonCode = "ops_review", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("permission_required", error.Code);
        var stillPending = await repository.GetAsync(TenantId, MappingId, "ext-1", ct);
        Assert.Equal(ImportConflictReviewStatus.PendingReview, stillPending!.Status);
    }

    [Fact]
    public async Task Scan_without_the_required_read_permission_is_rejected_and_never_mutates_anything()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService(
            [Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository,
            deniedPermissionKeys: new HashSet<string> { ImportConflictReviewService.ReadPermission });

        var error = await Assert.ThrowsAsync<ImportConflictReviewError>(() => service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("permission_required", error.Code);
        Assert.Empty(await repository.ListForMappingAsync(TenantId, MappingId, ct));
    }

    [Fact]
    public async Task List_without_the_required_read_permission_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var setupService = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        await setupService.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var service = NewService([], repository, deniedPermissionKeys: new HashSet<string> { ImportConflictReviewService.ReadPermission });
        var error = await Assert.ThrowsAsync<ImportConflictReviewError>(() => service.ListForMappingAsync(MappingId, Actor, ct));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task Decide_with_a_stale_version_is_rejected_as_a_concurrency_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<ImportConflictReviewError>(() => service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "KEEP_EXISTING", ReasonCode = "ops_review", Version = 999 },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task Decide_requires_a_reason_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        var raised = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<ImportConflictReviewError>(() => service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "KEEP_EXISTING", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Decide_rejects_an_unknown_decision_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        var raised = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<ImportConflictReviewError>(() => service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "DELETE_PERSON", ReasonCode = "ops_review", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Decide_on_a_row_with_no_review_record_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService([]);

        var error = await Assert.ThrowsAsync<ImportConflictReviewError>(() => service.DecideAsync(
            MappingId, "never-raised", new DecideImportConflictReviewRequest { Decision = "KEEP_EXISTING", ReasonCode = "ops_review", Version = 1 },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("import_conflict_review_not_found", error.Code);
    }

    [Fact]
    public async Task Decide_can_be_called_again_to_change_an_already_decided_row_a_human_may_always_override_a_prior_human_decision()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        var raised = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);
        var first = await service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "ESCALATED", ReasonCode = "needs_a_second_opinion", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        var second = await service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "KEEP_EXISTING", ReasonCode = "second_opinion_resolved_it", Version = first.Version },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(ImportConflictReviewStatus.KeepExisting, second.Status);
        Assert.Equal("second_opinion_resolved_it", second.DecisionReasonCode);
    }

    [Fact]
    public async Task Every_raise_and_decide_writes_an_append_only_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var auditSink = new InMemoryImportConflictReviewAuditSink();
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository, auditSink);
        var raised = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);
        Assert.Contains(auditSink.Events, e => e.Action == "import_conflict_review.raise");

        await service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "KEEP_EXISTING", ReasonCode = "ops_review", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Contains(auditSink.Events, e => e.Action == "import_conflict_review.decide");
    }

    // --- AC04: no automatic destructive overwrite - structural, never a PERSON write ---

    [Fact]
    public async Task Use_source_records_the_decision_but_never_mutates_the_conflict_values_themselves()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService([Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict])], repository);
        var raised = await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var decided = await service.DecideAsync(
            MappingId, "ext-1", new DecideImportConflictReviewRequest { Decision = "USE_SOURCE", ReasonCode = "source_is_correct", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        // The conflict's own existing/incoming values are preserved verbatim - deciding
        // USE_SOURCE records the human's choice, it never rewrites history or applies anything.
        Assert.Equal(ImportConflictReviewStatus.UseSource, decided.Status);
        var conflict = Assert.Single(decided.Conflicts);
        Assert.Equal("Okafor", conflict.ExistingValue);
        Assert.Equal("Okeke", conflict.IncomingValue);
    }

    [Fact]
    public async Task List_returns_every_review_raised_for_a_mapping()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryImportConflictReviewRepository();
        var service = NewService(
        [
            Snapshot("ext-1", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflict]),
            Snapshot("ext-2", RowOutcomeSnapshot.Conflict, "batch-1", "2026-10-07T00:00:00.000Z", [LastNameConflictV2]),
        ], repository);
        await service.ScanAsync(MappingId, Actor, CanonicalId.NewEventId(), ct);

        var listed = await service.ListForMappingAsync(MappingId, Actor, ct);

        Assert.Equal(2, listed.Count);
    }
}
