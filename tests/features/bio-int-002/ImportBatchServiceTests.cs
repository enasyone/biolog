using Biolog.Features.Integrations.Audit;
using Biolog.Features.Integrations.Domain;
using Xunit;

namespace Biolog.Features.Integrations.Tests;

public sealed class ImportBatchServiceTests
{
    private const string TenantId = "ten_00000000-0000-7000-8000-000000000001";
    private static readonly AuditActor Actor = new(ActorType.User, "user-1");
    private static readonly string CorrelationId = Identifiers.CanonicalId.NewEventId();

    private sealed record Harness(
        ImportMappingService MappingService, ImportBatchService BatchService, FakePersonGateway PersonGateway,
        IExternalIdentityLinkStore LinkStore, IIdempotencyStore IdempotencyStore, FaultInjectingImportAuditSink AuditSink,
        IOrphanedPersonLedger OrphanedPersonLedger, FaultInjectingExternalIdentityLinkStore LinkStoreFaultInjector,
        IImportRowProvenanceLedger RowProvenanceLedger, GateableImportSourceMappingRepository MappingRepositoryGate,
        FaultInjectingImportRowProvenanceLedger RowProvenanceLedgerFaultInjector);

    /// <summary><paramref name="grantedBatchPermissions"/> gates only the returned <see cref="ImportBatchService"/> (preview/apply) under test. The fixture's own <see cref="ImportMappingService"/> always has full permissions, since setting up a mapping to test AGAINST is test arrangement, not the thing being tested. <paramref name="idempotencyWaitTimeout"/>/<paramref name="idempotencyWaitPollInterval"/> (BIO-INT-002-F01's own remediation) let a concurrency test run with a short, deterministic wait bound instead of the production 5s default. The real <see cref="IExternalIdentityLinkStore"/> is always wrapped in <see cref="FaultInjectingExternalIdentityLinkStore"/> (transparent unless a test arms it) so <see cref="Harness.LinkStore"/> and <see cref="Harness.LinkStoreFaultInjector"/> share the same underlying store. Likewise the real <see cref="IImportSourceMappingRepository"/> is always wrapped in <see cref="GateableImportSourceMappingRepository"/> (round-2 remediation, finding C's own test hook - transparent unless a test arms it).</summary>
    private static Harness Build(
        IReadOnlySet<(string, string)>? grantedBatchPermissions = null,
        TimeSpan? idempotencyWaitTimeout = null, TimeSpan? idempotencyWaitPollInterval = null)
    {
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);
        var mappingRepositoryGate = new GateableImportSourceMappingRepository(new InMemoryImportSourceMappingRepository());
        var batchRepository = new InMemoryImportBatchRepository();
        var linkStoreFaultInjector = new FaultInjectingExternalIdentityLinkStore(new InMemoryExternalIdentityLinkStore());
        var personGateway = new FakePersonGateway();
        var auditSinkFaultInjector = new FaultInjectingImportAuditSink(new InMemoryImportAuditSink());
        var lockRegistry = new ImportLockRegistry();
        var idempotencyStore = new InMemoryIdempotencyStore();
        var orphanedPersonLedger = new InMemoryOrphanedPersonLedger();
        var rowProvenanceLedgerFaultInjector = new FaultInjectingImportRowProvenanceLedger(new InMemoryImportRowProvenanceLedger());

        var mappingService = new ImportMappingService(tenantProvider, mappingRepositoryGate, auditSinkFaultInjector, lockRegistry, new FakePermissionGateway());
        var batchService = new ImportBatchService(
            tenantProvider, mappingRepositoryGate, batchRepository, linkStoreFaultInjector, personGateway, auditSinkFaultInjector, lockRegistry,
            new FakePermissionGateway(grantedBatchPermissions), idempotencyStore, orphanedPersonLedger, rowProvenanceLedgerFaultInjector,
            idempotencyWaitTimeout, idempotencyWaitPollInterval);

        return new Harness(
            mappingService, batchService, personGateway, linkStoreFaultInjector, idempotencyStore, auditSinkFaultInjector,
            orphanedPersonLedger, linkStoreFaultInjector, rowProvenanceLedgerFaultInjector, mappingRepositoryGate,
            rowProvenanceLedgerFaultInjector);
    }

    private static IReadOnlyList<FieldMapping> DefaultFieldMappings() =>
        [new FieldMapping("ext_first_name", "first_name"), new FieldMapping("ext_last_name", "last_name")];

    private static async Task<ImportSourceMapping> CreateMappingAsync(Harness h, SyncDirection direction = SyncDirection.SourceIsMaster) =>
        await h.MappingService.CreateAsync(new CreateMappingFields(SourceSystemType.Sis, direction, "Main SIS", DefaultFieldMappings()), Actor, CorrelationId);

    private static ImportRowInput Row(string externalKey, string firstName, string lastName) =>
        new(externalKey, new Dictionary<string, string?> { ["ext_first_name"] = firstName, ["ext_last_name"] = lastName });

    // --- AC03: dry-run/conflict preview exists ---

    [Fact]
    public async Task PreviewAsync_a_brand_new_external_key_is_classified_created_and_nothing_is_written()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        var batch = await h.BatchService.PreviewAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], Actor);

        Assert.Equal(ImportBatchMode.DryRun, batch.Mode);
        Assert.Equal(RowOutcome.Created, Assert.Single(batch.RowOutcomes).Outcome);
        Assert.Equal(0, h.PersonGateway.CreateCallCount); // AC03: preview never writes
    }

    [Fact]
    public async Task PreviewAsync_never_creates_an_external_identity_link()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        await h.BatchService.PreviewAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], Actor);

        Assert.Null(await h.LinkStore.FindAsync(TenantId, mapping.Id, "ext-1"));
    }

    // --- AC04: imports do not silently overwrite conflicting identity evidence ---

    [Fact]
    public async Task ApplyAsync_an_already_linked_person_with_a_disagreeing_field_is_flagged_as_conflict_and_never_written()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        // First apply creates the person.
        var firstBatch = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-1", Actor, CorrelationId);
        var personId = Assert.Single(firstBatch.Batch.RowOutcomes).PersonId!;

        // A later apply for the SAME external key, but a DIFFERENT last name.
        var secondBatch = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Byron")], "idem-2", Actor, CorrelationId);
        var outcome = Assert.Single(secondBatch.Batch.RowOutcomes);

        Assert.Equal(RowOutcome.Conflict, outcome.Outcome);
        Assert.Equal(personId, outcome.PersonId);
        var conflict = Assert.Single(outcome.Conflicts);
        Assert.Equal("last_name", conflict.PersonField);
        Assert.Equal("Lovelace", conflict.ExistingValue);
        Assert.Equal("Byron", conflict.IncomingValue);

        // AC04's own guarantee: the PERSON was never touched a second time.
        Assert.Equal(1, h.PersonGateway.CreateCallCount);
        var person = await h.PersonGateway.GetAsync(personId);
        Assert.Equal("Lovelace", person!.LastName);
    }

    [Fact]
    public async Task ApplyAsync_an_already_linked_person_with_matching_fields_is_unchanged_and_never_written()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-1", Actor, CorrelationId);
        var secondBatch = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-2", Actor, CorrelationId);

        Assert.Equal(RowOutcome.Unchanged, Assert.Single(secondBatch.Batch.RowOutcomes).Outcome);
        Assert.Equal(1, h.PersonGateway.CreateCallCount);
    }

    [Fact]
    public async Task ApplyAsync_a_pure_fill_in_of_a_previously_blank_field_is_unchanged_not_a_conflict()
    {
        var h = Build();
        var mapping = await h.MappingService.CreateAsync(
            new CreateMappingFields(SourceSystemType.Sis, SyncDirection.SourceIsMaster, "Main SIS",
                [new FieldMapping("ext_first_name", "first_name"), new FieldMapping("ext_last_name", "last_name"), new FieldMapping("ext_preferred", "preferred_name")]),
            Actor, CorrelationId);

        var row1 = new ImportRowInput("ext-1", new Dictionary<string, string?> { ["ext_first_name"] = "Ada", ["ext_last_name"] = "Lovelace" });
        await h.BatchService.ApplyAsync(mapping.Id, [row1], "idem-1", Actor, CorrelationId);

        // Second apply supplies a preferred_name the person never had before - a fill-in, not a
        // disagreement (the current value is simply unset).
        var row2 = new ImportRowInput("ext-1", new Dictionary<string, string?> { ["ext_first_name"] = "Ada", ["ext_last_name"] = "Lovelace", ["ext_preferred"] = "Countess" });
        var secondBatch = await h.BatchService.ApplyAsync(mapping.Id, [row2], "idem-2", Actor, CorrelationId);

        Assert.Equal(RowOutcome.Unchanged, Assert.Single(secondBatch.Batch.RowOutcomes).Outcome);
    }

    // --- AC02: BIOLOG can be master or downstream per integration ---

    [Fact]
    public async Task ApplyAsync_is_rejected_outright_when_the_mapping_is_configured_biolog_is_master()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h, SyncDirection.BiologIsMaster);

        var error = await Assert.ThrowsAsync<ImportError>(() => h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-1", Actor, CorrelationId));

        Assert.Equal("biolog_is_master", error.Code);
        Assert.Equal(0, h.PersonGateway.CreateCallCount);
    }

    [Fact]
    public async Task PreviewAsync_is_also_rejected_outright_when_the_mapping_is_configured_biolog_is_master()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h, SyncDirection.BiologIsMaster);

        var error = await Assert.ThrowsAsync<ImportError>(() => h.BatchService.PreviewAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], Actor));

        Assert.Equal("biolog_is_master", error.Code);
    }

    // --- AC05: idempotent reprocessing is supported ---

    [Fact]
    public async Task ApplyAsync_the_same_idempotency_key_with_identical_rows_replays_the_original_result_without_reprocessing()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        var rows = new[] { Row("ext-1", "Ada", "Lovelace") };

        var first = await h.BatchService.ApplyAsync(mapping.Id, rows, "idem-1", Actor, CorrelationId);
        var second = await h.BatchService.ApplyAsync(mapping.Id, rows, "idem-1", Actor, CorrelationId);

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Batch.Id, second.Batch.Id);
        Assert.Equal(1, h.PersonGateway.CreateCallCount); // never reprocessed - the person was created exactly once
    }

    [Fact]
    public async Task ApplyAsync_the_same_idempotency_key_with_different_rows_is_rejected_as_reused()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-1", Actor, CorrelationId);
        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-2", "Grace", "Hopper")], "idem-1", Actor, CorrelationId));

        Assert.Equal("idempotency_key_reused", error.Code);
    }

    // --- Post-review remediation round 1 (BIO-INT-002-F01, Seyi round-1 FAIL) ---

    [Fact]
    public async Task ApplyAsync_two_genuinely_concurrent_callers_with_the_same_idempotency_key_execute_exactly_once()
    {
        // Reproduces Seyi's exact interleaving: two concurrent ApplyAsync calls for the SAME
        // idempotency key, released together via a start gate (not Task.Delay-based timing), both
        // racing to claim. Before the fix, the second caller fell through on State alone and
        // executed a second time, silently replacing the first caller's stored result. After the
        // fix, exactly one caller ever becomes the owner (IsNewClaim) and executes; the other waits
        // for and replays that owner's own result.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        var rows = new[] { Row("ext-1", "Ada", "Lovelace") };

        var start = new TaskCompletionSource();
        Task<(ImportBatch Batch, bool Replayed)> Race() => Task.Run(async () =>
        {
            await start.Task;
            return await h.BatchService.ApplyAsync(mapping.Id, rows, "idem-race", Actor, CorrelationId);
        });

        var taskA = Race();
        var taskB = Race();
        start.SetResult();
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, h.PersonGateway.CreateCallCount); // the PERSON was created exactly once, never twice
        Assert.Equal(results[0].Batch.Id, results[1].Batch.Id); // both callers observe the SAME stable batch
        Assert.True(results[0].Replayed || results[1].Replayed); // exactly one caller executed, the other replayed
        Assert.False(results[0].Replayed && results[1].Replayed); // never BOTH replayed - someone had to actually execute
        Assert.Single(h.AuditSink.All(), e => e.Action == "integration.import_batch_applied"); // exactly one audit write, never a silent second one
    }

    [Fact]
    public async Task ApplyAsync_a_second_caller_times_out_if_the_owners_claim_never_completes()
    {
        // The owner's own attempt genuinely never finishes within this test (FakePersonGateway's
        // CreateGate is never released) - a second concurrent caller for the same key must wait,
        // bounded, then fail closed with idempotency_wait_timeout rather than waiting forever or
        // (the F01 bug) executing a second time regardless. The wait bound is configured short so
        // this test runs fast and deterministically, not by luck of timing.
        var h = Build(idempotencyWaitTimeout: TimeSpan.FromMilliseconds(150), idempotencyWaitPollInterval: TimeSpan.FromMilliseconds(15));
        var mapping = await CreateMappingAsync(h);
        var rows = new[] { Row("ext-1", "Ada", "Lovelace") };
        h.PersonGateway.CreateGate = new TaskCompletionSource();

        var ownerTask = h.BatchService.ApplyAsync(mapping.Id, rows, "idem-timeout", Actor, CorrelationId);
        await h.PersonGateway.CreateEntered.Task; // deterministic: the owner has genuinely started executing, not merely claimed

        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, rows, "idem-timeout", Actor, CorrelationId));
        Assert.Equal("idempotency_wait_timeout", error.Code);

        // Release the owner so the first call can finish cleanly - no orphaned background task.
        h.PersonGateway.CreateGate.SetResult();
        var owner = await ownerTask;
        Assert.False(owner.Replayed);
        Assert.Equal(1, h.PersonGateway.CreateCallCount); // the timed-out caller never executed at all
    }

    // --- Post-review remediation round 1, Zainab's own refinement (ZAINAB-INT-002-F01) ---

    [Fact]
    public async Task ApplyAsync_when_the_link_write_fails_after_the_person_was_created_the_claim_is_released_not_stuck()
    {
        // Gap (1a): before this fix, only a failure INSIDE the rows loop released the claim.
        // Here the failure IS inside the rows loop (the link write), so this also doubles as a
        // baseline proof that a genuine partial failure still releases cleanly, before the next
        // test proves the actual duplicate-PERSON risk that failure creates.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        h.LinkStoreFaultInjector.ThrowOnNextCreate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-partial", Actor, CorrelationId));

        Assert.Equal(1, h.PersonGateway.CreateCallCount); // the PERSON really was created
        Assert.Null(await h.LinkStore.FindAsync(TenantId, mapping.Id, "ext-1")); // but no link exists yet
        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-partial")); // released, not stuck InFlight
    }

    [Fact]
    public async Task ApplyAsync_a_retry_after_a_partial_link_failure_adopts_the_orphaned_person_instead_of_creating_a_second_one()
    {
        // The real bug (1b): a retry with no orphan protection would see no link for "ext-1" and
        // create a SECOND, different PERSON. With IOrphanedPersonLedger, the retry adopts the
        // SAME PersonId the first (failed) attempt already created.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        h.LinkStoreFaultInjector.ThrowOnNextCreate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-partial", Actor, CorrelationId));
        var orphanedPersonId = await h.OrphanedPersonLedger.FindAsync(TenantId, mapping.Id, "ext-1");
        Assert.NotNull(orphanedPersonId);

        // Retry with a fresh idempotency key (a genuinely new client attempt), link writes now succeed.
        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-retry", Actor, CorrelationId);

        Assert.Equal(1, h.PersonGateway.CreateCallCount); // still only ONE PersonId ever minted for "ext-1"
        Assert.Equal(orphanedPersonId, Assert.Single(retried.Batch.RowOutcomes).PersonId); // the retry's link points at the adopted (not a new) PersonId
        Assert.Equal(orphanedPersonId, (await h.LinkStore.FindAsync(TenantId, mapping.Id, "ext-1"))!.PersonId);
    }

    [Fact]
    public async Task ApplyAsync_when_the_audit_write_fails_after_rows_are_processed_the_claim_is_released_not_stuck()
    {
        // Gap (1a)'s own distinct half: a failure AFTER the rows loop (batch construction/audit/
        // persist) must ALSO release the claim - the original fix's try/catch did not reach this
        // far, so this exact failure left the idempotency key permanently InFlight.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        h.AuditSink.ThrowOnNextWrite = true;

        await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-audit-fail", Actor, CorrelationId));

        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-audit-fail")); // released, never stuck

        // A genuine retry with the SAME key must now be able to proceed (not wait-timeout against
        // a phantom stuck claim) and adopt the already-created PERSON rather than duplicating it.
        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-audit-fail", Actor, CorrelationId);
        Assert.False(retried.Replayed);
        Assert.Equal(1, h.PersonGateway.CreateCallCount);
    }

    // --- Post-review remediation round 2 (BIO-INT-002-F01, Seyi round-2 FAIL / Zainab round-2 FAIL) ---

    [Fact]
    public async Task ApplyAsync_a_retry_after_a_late_audit_failure_preserves_the_original_created_provenance_not_unchanged()
    {
        // Round-2 remediation, finding B: the row's PERSON+link work already fully succeeded on
        // attempt 1; only the LATER audit write failed. A naive retry would re-classify this row
        // via ClassifyRowAsync, see the link already exists, and report Unchanged - losing the
        // fact that THIS idempotency attempt originally created a brand-new identity. The
        // IImportRowProvenanceLedger must make the retry replay the original Created outcome
        // verbatim, with no re-write at all.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        h.AuditSink.ThrowOnNextWrite = true;

        await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-provenance", Actor, CorrelationId));
        var personIdAfterFirstAttempt = (await h.LinkStore.FindAsync(TenantId, mapping.Id, "ext-1"))!.PersonId;

        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-provenance", Actor, CorrelationId);
        var outcome = Assert.Single(retried.Batch.RowOutcomes);

        Assert.Equal(RowOutcome.Created, outcome.Outcome); // NOT Unchanged - the original provenance survived
        Assert.Equal(personIdAfterFirstAttempt, outcome.PersonId);
        Assert.Equal(1, h.PersonGateway.CreateCallCount); // never re-created
    }

    [Fact]
    public async Task ApplyAsync_when_recording_the_orphaned_person_is_cancelled_a_retry_still_adopts_rather_than_duplicates()
    {
        // Round-2 remediation, finding A (cancellation window): before the fix,
        // orphanedPersonLedger.RecordAsync used the CALLER's token. If cancelled right after the
        // PERSON was durably created but before/during that record write, the entry never lands
        // and a retry mints a SECOND PERSON. The fix uses CancellationToken.None unconditionally
        // for that specific write, so it must survive even when the caller's own token is already
        // cancelled by the time this call would have happened.
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        using var cts = new CancellationTokenSource();
        // FakePersonGateway.CreateGate/CreateEntered let us deterministically land the cancel
        // request while PERSON creation is in flight (not by timing). FakePersonGateway itself
        // never observes the token, so CreateAsync completes normally once released; the
        // already-cancelled token is then genuinely observed by the very next cancellable call -
        // linkStore.CreateAsync - reproducing "PERSON created, THEN the link write is cancelled".
        // RecordOrphanedPersonSafelyAsync's own CancellationToken.None must still have recorded
        // the orphan before that point, regardless.
        h.PersonGateway.CreateGate = new TaskCompletionSource();
        var applyTask = h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-cancel", Actor, CorrelationId, cts.Token);
        await h.PersonGateway.CreateEntered.Task;
        cts.Cancel();
        h.PersonGateway.CreateGate.SetResult();

        // The attempt itself may or may not observe the cancellation elsewhere in its own flow
        // (acceptable); what must NOT happen is a retry minting a second PERSON because the
        // orphan record was lost to that same cancellation.
        try { await applyTask; } catch { /* either outcome for THIS call is fine - see below */ }

        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-cancel-retry", Actor, CorrelationId);

        Assert.Equal(1, h.PersonGateway.CreateCallCount); // only ONE PersonId was ever minted for "ext-1"
        Assert.Equal(RowOutcome.Created, Assert.Single(retried.Batch.RowOutcomes).Outcome);
    }

    [Fact]
    public async Task ApplyAsync_adopting_an_orphan_whose_content_now_disagrees_with_the_incoming_row_is_a_conflict_not_a_silent_creation()
    {
        // Round-2 remediation, finding A (content-blind adoption): a failed attempt created
        // "Ada Lovelace" and orphaned that PersonId. A LATER request for the same external key
        // carries DIFFERENT content ("Ada Byron"). Adoption must run the same field comparison an
        // already-linked row gets and surface a Conflict - never silently link a mismatched
        // identity and report a clean Created.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        h.LinkStoreFaultInjector.ThrowOnNextCreate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-orphan", Actor, CorrelationId));
        var orphanedPersonId = await h.OrphanedPersonLedger.FindAsync(TenantId, mapping.Id, "ext-1");
        Assert.NotNull(orphanedPersonId);

        // A later request for the SAME external key, DIFFERENT last name.
        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Byron")], "idem-orphan-retry", Actor, CorrelationId);
        var outcome = Assert.Single(retried.Batch.RowOutcomes);

        Assert.Equal(RowOutcome.Conflict, outcome.Outcome);
        Assert.Equal(orphanedPersonId, outcome.PersonId);
        var conflict = Assert.Single(outcome.Conflicts);
        Assert.Equal("last_name", conflict.PersonField);
        Assert.Equal("Lovelace", conflict.ExistingValue);
        Assert.Equal("Byron", conflict.IncomingValue);
        Assert.Null(await h.LinkStore.FindAsync(TenantId, mapping.Id, "ext-1")); // never linked - the mismatch is unresolved
        Assert.Equal(1, h.PersonGateway.CreateCallCount); // and never duplicated either
    }

    [Fact]
    public async Task ApplyAsync_rechecks_active_after_acquiring_the_lock_so_a_concurrent_disable_that_already_committed_wins_over_a_stale_pre_lock_read()
    {
        // Round-2 remediation, finding C (TOCTOU): ApplyAsync's own early mapping fetch is held
        // right after it returns its (currently Active=true) snapshot - deterministic via
        // GateableImportSourceMappingRepository, not timing. While it is held, a concurrent
        // "admin" disable commits through the SAME per-mapping lock ApplyAsync will acquire next.
        // Only once the authoritative post-lock re-read is checked can this possibly see the
        // disable - proving the decision is never made on the stale pre-lock snapshot.
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        h.MappingRepositoryGate.GateFirstRead = new TaskCompletionSource();
        var applyTask = h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-toctou", Actor, CorrelationId);
        await h.MappingRepositoryGate.FirstReadEntered.Task; // deterministic: the early read has happened and is now held

        await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version, mapping.SyncDirection, mapping.DisplayName, mapping.FieldMappings, Active: false),
            Actor, CorrelationId);

        h.MappingRepositoryGate.GateFirstRead.SetResult(); // release the held (now-stale) early read

        var error = await Assert.ThrowsAsync<ImportError>(() => applyTask);
        Assert.Equal("import_mapping_disabled", error.Code);
        Assert.Equal(0, h.PersonGateway.CreateCallCount); // no write ever began on the stale snapshot
    }

    // --- Post-review remediation round 1 (BIO-INT-002-F02, Zainab round-1 FAIL) ---

    [Fact]
    public async Task ApplyAsync_against_a_disabled_mapping_is_rejected()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version, mapping.SyncDirection, mapping.DisplayName, mapping.FieldMappings, Active: false),
            Actor, CorrelationId);

        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-1", Actor, CorrelationId));

        Assert.Equal("import_mapping_disabled", error.Code);
        Assert.Equal(0, h.PersonGateway.CreateCallCount); // the kill switch actually stopped the write
    }

    [Fact]
    public async Task PreviewAsync_against_a_disabled_mapping_is_rejected()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version, mapping.SyncDirection, mapping.DisplayName, mapping.FieldMappings, Active: false),
            Actor, CorrelationId);

        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.PreviewAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], Actor));

        Assert.Equal("import_mapping_disabled", error.Code);
    }

    [Fact]
    public async Task ApplyAsync_re_enabling_a_disabled_mapping_restores_normal_behavior()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        var disabled = await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version, mapping.SyncDirection, mapping.DisplayName, mapping.FieldMappings, Active: false),
            Actor, CorrelationId);
        var reenabled = await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(disabled.Version, disabled.SyncDirection, disabled.DisplayName, disabled.FieldMappings, Active: true),
            Actor, CorrelationId);
        Assert.True(reenabled.Active);

        var batch = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-1", Actor, CorrelationId);

        Assert.Equal(RowOutcome.Created, Assert.Single(batch.Batch.RowOutcomes).Outcome);
    }

    [Fact]
    public async Task ApplyAsync_without_an_idempotency_key_is_rejected()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "", Actor, CorrelationId));

        Assert.Equal("idempotency_key_required", error.Code);
    }

    // --- Permission gating (ADR-0006) ---

    [Fact]
    public async Task ApplyAsync_without_the_apply_permission_is_rejected()
    {
        var h = Build(new HashSet<(string, string)>());
        var mapping = await CreateMappingAsync(h);

        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-1", Actor, CorrelationId));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task PreviewAsync_requires_no_permission_only_an_asserted_actor()
    {
        var h = Build(new HashSet<(string, string)>()); // no permissions granted at all
        var mapping = await CreateMappingAsync(h);

        var batch = await h.BatchService.PreviewAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], Actor);

        Assert.Equal(RowOutcome.Created, Assert.Single(batch.RowOutcomes).Outcome);
    }

    [Fact]
    public async Task ApplyAsync_against_an_unknown_mapping_id_throws_not_found()
    {
        var h = Build();
        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync("does-not-exist", [Row("ext-1", "Ada", "Lovelace")], "idem-1", Actor, CorrelationId));

        Assert.Equal("import_mapping_not_found", error.Code);
    }

    // --- Post-review remediation round 4 (Seyi round-3 FAIL = Zainab round-3 FAIL finding 1; Zainab round-3 FAIL finding 2, ZAINAB-INT-002-F04; residual durability note) ---

    [Fact]
    public async Task ApplyAsync_rejected_by_a_disabled_mapping_after_claiming_releases_the_claim_and_a_later_retry_succeeds()
    {
        // Finding 1: round-2's fix moved the authoritative Active/SyncDirection check to AFTER
        // the lock but still BEFORE the release-on-failure try/catch - so this exact rejection
        // threw AFTER claim ownership but BEFORE anything could release it, stranding the key
        // InFlight forever (even once the mapping was re-enabled). No concurrency needed to
        // reproduce - a plain disabled mapping at the time of the call already took this path.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version, mapping.SyncDirection, mapping.DisplayName, mapping.FieldMappings, Active: false),
            Actor, CorrelationId);

        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-strand-disabled", Actor, CorrelationId));
        Assert.Equal("import_mapping_disabled", error.Code);
        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-strand-disabled")); // released, not stuck InFlight

        var reenabled = await h.MappingService.GetAsync(mapping.Id) ?? throw new InvalidOperationException("test setup");
        await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(reenabled.Version, reenabled.SyncDirection, reenabled.DisplayName, reenabled.FieldMappings, Active: true),
            Actor, CorrelationId);

        // A genuine retry with the SAME key, now that the mapping is fixed, must succeed - not
        // wait out the bounded timeout against a phantom stuck claim.
        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-strand-disabled", Actor, CorrelationId);
        Assert.False(retried.Replayed);
        Assert.Equal(1, h.PersonGateway.CreateCallCount);
    }

    [Fact]
    public async Task ApplyAsync_rejected_by_a_direction_change_to_biolog_is_master_after_claiming_releases_the_claim_and_a_later_retry_succeeds()
    {
        // Finding 1, second case: a mapping flipped to biolog_is_master between the early fetch
        // and the authoritative post-lock re-read must ALSO release the claim, not strand it.
        var h = Build();
        var mapping = await CreateMappingAsync(h, SyncDirection.SourceIsMaster);
        await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(mapping.Version, SyncDirection.BiologIsMaster, mapping.DisplayName, mapping.FieldMappings, Active: true),
            Actor, CorrelationId);

        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-strand-direction", Actor, CorrelationId));
        Assert.Equal("biolog_is_master", error.Code);
        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-strand-direction")); // released, not stuck InFlight

        var flipped = await h.MappingService.GetAsync(mapping.Id) ?? throw new InvalidOperationException("test setup");
        await h.MappingService.UpdateAsync(mapping.Id,
            new UpdateMappingFields(flipped.Version, SyncDirection.SourceIsMaster, flipped.DisplayName, flipped.FieldMappings, Active: true),
            Actor, CorrelationId);

        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-strand-direction", Actor, CorrelationId);
        Assert.False(retried.Replayed);
        Assert.Equal(1, h.PersonGateway.CreateCallCount);
    }

    [Fact]
    public async Task ApplyAsync_a_reused_idempotency_key_against_a_different_mapping_never_replays_the_first_mappings_outcome()
    {
        // Finding 2(a) (ZAINAB-INT-002-F04): an idempotency key is scoped per-operation, not
        // per-mapping. After mapping A's attempt fails (late, post-row-loop) and releases its
        // key, that SAME key is reused against a DIFFERENT mapping B with a matching external
        // key. Without MappingId in IImportRowProvenanceLedger's key, mapping B's apply would
        // incorrectly replay mapping A's recorded outcome/PersonId, skipping classification and
        // link creation for mapping B entirely.
        var h = Build();
        var mappingA = await CreateMappingAsync(h);
        var mappingB = await CreateMappingAsync(h);

        h.AuditSink.ThrowOnNextWrite = true;
        await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mappingA.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-shared", Actor, CorrelationId));
        var personIdUnderMappingA = (await h.LinkStore.FindAsync(TenantId, mappingA.Id, "ext-1"))!.PersonId;
        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-shared")); // released - the key is free to reuse

        // Reuse the SAME idempotency key against mapping B, same external key.
        var resultUnderMappingB = await h.BatchService.ApplyAsync(mappingB.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-shared", Actor, CorrelationId);
        var outcomeUnderMappingB = Assert.Single(resultUnderMappingB.Batch.RowOutcomes);

        Assert.Equal(RowOutcome.Created, outcomeUnderMappingB.Outcome);
        Assert.NotEqual(personIdUnderMappingA, outcomeUnderMappingB.PersonId); // mapping B got its OWN new PERSON, never mapping A's
        Assert.Equal(2, h.PersonGateway.CreateCallCount); // one per mapping - never skipped via a cross-mapping replay
    }

    // --- Post-review remediation round 6 (BIO-INT-002-F06, Seyi round-6 FAIL, architecture-checkpoint finding) ---

    [Fact]
    public async Task ApplyAsync_a_released_claim_reused_with_different_content_for_the_same_external_key_never_silently_replays_stale_provenance()
    {
        // F06's exact gap: IIdempotencyStore.Release deletes the claim AND its own fingerprint
        // entirely, so a request reusing the SAME key/mapping/external-key with DIFFERENT field
        // content wins a FRESH claim (nothing live to compare against) and would otherwise hit the
        // replay branch, which (before this fix) trusted the stale provenance without ever
        // re-evaluating content - silently linking the new, disagreeing request to the original
        // attempt's PERSON and reporting a clean Created. The fix carries the recording attempt's
        // own fingerprint alongside the outcome: a mismatch now falls through to fresh
        // classification instead - which, since the orphan is still genuinely unresolved (no link
        // for this external key yet), lands in the already-existing content-aware ADOPTION check
        // and correctly surfaces a Conflict, never a silent Created.
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        // Request A: PERSON+orphan+provenance all succeed, but the link write fails - releasing
        // the claim and deleting its own live fingerprint.
        h.LinkStoreFaultInjector.ThrowOnNextCreate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-reuse-diff-content", Actor, CorrelationId));
        var personIdFromRequestA = await h.OrphanedPersonLedger.FindAsync(TenantId, mapping.Id, "ext-1");
        Assert.NotNull(personIdFromRequestA);
        Assert.Null(await h.LinkStore.FindAsync(TenantId, mapping.Id, "ext-1")); // link genuinely never landed
        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-reuse-diff-content")); // released - free to reuse

        // Request B: SAME idempotency key, SAME mapping, SAME external key - but DIFFERENT
        // field content. Must never be silently linked to request A's PERSON as a clean Created.
        var resultB = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Grace", "Hopper")], "idem-reuse-diff-content", Actor, CorrelationId);
        var outcomeB = Assert.Single(resultB.Batch.RowOutcomes);

        Assert.Equal(RowOutcome.Conflict, outcomeB.Outcome); // never a silent Created
        Assert.Equal(personIdFromRequestA, outcomeB.PersonId); // correctly identifies WHICH identity disagreed
        Assert.Contains(outcomeB.Conflicts, c => c.PersonField == "first_name" && c.ExistingValue == "Ada" && c.IncomingValue == "Grace");
        Assert.Null(await h.LinkStore.FindAsync(TenantId, mapping.Id, "ext-1")); // never linked under the disagreeing content
        Assert.Equal(1, h.PersonGateway.CreateCallCount); // never minted a second PERSON either
    }

    // --- Post-review remediation round 7 (BIO-INT-002-F07, Zainab round-6 FAIL, confirmed by Seyi) ---

    [Fact]
    public void ComputeFingerprint_a_field_value_containing_the_old_schemes_own_separator_syntax_no_longer_collides_with_an_equivalent_split_across_two_fields()
    {
        // The exact collision Zainab's review demonstrated: the OLD raw-delimiter scheme joined
        // field entries as "key=value" separated by "," - so a SINGLE field whose own value
        // happens to contain that exact "key=value" syntax could serialize identically to two
        // genuinely separate fields. Both produced the literal segment "ext_first_name=b,ext_last_name=d".
        var oneFieldEmbeddingTheOldSeparatorSyntax = new ImportRowInput("ext-1", new Dictionary<string, string?> { ["ext_first_name"] = "b,ext_last_name=d" });
        var twoSeparateFields = new ImportRowInput("ext-1", new Dictionary<string, string?> { ["ext_first_name"] = "b", ["ext_last_name"] = "d" });

        var fingerprintA = ImportBatchService.ComputeFingerprint("mapping-1", [oneFieldEmbeddingTheOldSeparatorSyntax]);
        var fingerprintB = ImportBatchService.ComputeFingerprint("mapping-1", [twoSeparateFields]);

        Assert.NotEqual(fingerprintA, fingerprintB);
    }

    [Fact]
    public void ComputeFingerprint_a_field_value_containing_the_old_schemes_own_row_separator_no_longer_collides_with_an_equivalent_second_row()
    {
        // A second, distinct collision shape under the OLD scheme: rows were joined with ";", so
        // a SINGLE row's own field value embedding that exact "externalKey:field=value" syntax
        // could serialize identically to genuinely TWO separate rows.
        var oneRowEmbeddingAnotherRowsOldSyntax = new ImportRowInput("ext-1", new Dictionary<string, string?> { ["a"] = "x;ext-2:a=y" });
        var twoSeparateRows = new[]
        {
            new ImportRowInput("ext-1", new Dictionary<string, string?> { ["a"] = "x" }),
            new ImportRowInput("ext-2", new Dictionary<string, string?> { ["a"] = "y" }),
        };

        var fingerprintOneRow = ImportBatchService.ComputeFingerprint("mapping-1", [oneRowEmbeddingAnotherRowsOldSyntax]);
        var fingerprintTwoRows = ImportBatchService.ComputeFingerprint("mapping-1", twoSeparateRows);

        Assert.NotEqual(fingerprintOneRow, fingerprintTwoRows);
    }

    [Fact]
    public async Task ApplyAsync_a_released_claim_reused_with_formerly_colliding_content_is_treated_as_genuinely_different_not_silently_replayed()
    {
        // End-to-end regression for F07: request A and request B's rows would have produced the
        // SAME fingerprint under the old raw-delimiter scheme (both serialize their field segment
        // as "ext_first_name=Ada,ext_last_name=X,extra=Y"), even though they are genuinely
        // different content - A supplies "ext_last_name" with a value that embeds a second,
        // unmapped "extra=Y" pair; B supplies "ext_last_name" and "extra" as two genuinely
        // separate keys. Both rows keep first_name/last_name present so request A's own creation
        // legitimately succeeds before the injected link-write failure. After A's attempt
        // releases, B must be routed through fresh classification (F06's own path - the key isn't
        // live to flat-reject against), not a silent replay of A's stale provenance.
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        h.LinkStoreFaultInjector.ThrowOnNextCreate = true;
        var rowsA = new[] { new ImportRowInput("ext-1", new Dictionary<string, string?> { ["ext_first_name"] = "Ada", ["ext_last_name"] = "X,extra=Y" }) };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.BatchService.ApplyAsync(mapping.Id, rowsA, "idem-formerly-colliding", Actor, CorrelationId));
        var personIdFromRequestA = await h.OrphanedPersonLedger.FindAsync(TenantId, mapping.Id, "ext-1");
        Assert.NotNull(personIdFromRequestA);
        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-formerly-colliding")); // released

        var rowsB = new[] { new ImportRowInput("ext-1", new Dictionary<string, string?> { ["ext_first_name"] = "Ada", ["ext_last_name"] = "X", ["extra"] = "Y" }) };
        var resultB = await h.BatchService.ApplyAsync(mapping.Id, rowsB, "idem-formerly-colliding", Actor, CorrelationId);
        var outcomeB = Assert.Single(resultB.Batch.RowOutcomes);

        Assert.Equal(RowOutcome.Conflict, outcomeB.Outcome); // never a silent Created despite the old scheme's fingerprint collision
        Assert.Equal(personIdFromRequestA, outcomeB.PersonId);
        Assert.Contains(outcomeB.Conflicts, c => c.PersonField == "last_name" && c.ExistingValue == "X,extra=Y" && c.IncomingValue == "X");
        Assert.Null(await h.LinkStore.FindAsync(TenantId, mapping.Id, "ext-1")); // never linked under the disagreeing content
        Assert.Equal(1, h.PersonGateway.CreateCallCount); // never minted a second PERSON either
    }

    [Fact]
    public async Task ApplyAsync_a_released_claim_reused_with_the_SAME_content_still_replays_normally()
    {
        // The positive counterpart to the test above: a genuine same-content retry after a release
        // must keep working exactly as every prior round's own resume-after-failure tests already
        // prove - the fingerprint comparison this fix adds must never reject a legitimate retry.
        var h = Build();
        var mapping = await CreateMappingAsync(h);

        h.AuditSink.ThrowOnNextWrite = true;
        await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-reuse-same-content", Actor, CorrelationId));

        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-reuse-same-content", Actor, CorrelationId);
        var outcome = Assert.Single(retried.Batch.RowOutcomes);

        Assert.Equal(RowOutcome.Created, outcome.Outcome); // the original provenance still replays
        Assert.Equal(1, h.PersonGateway.CreateCallCount); // never re-created
    }

    [Fact]
    public async Task ApplyAsync_rejects_a_request_with_duplicate_external_keys()
    {
        // Finding 2(b): two rows sharing an external key in ONE request would otherwise let row
        // two silently replay row one's just-recorded provenance outcome without ever evaluating
        // its own (possibly different) content, defeating conflict detection for row two.
        // Rejected up front instead - see ImportBatchService's own class header.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        var rows = new[] { Row("ext-1", "Ada", "Lovelace"), Row("ext-1", "Someone", "Else") };

        var error = await Assert.ThrowsAsync<ImportError>(() =>
            h.BatchService.ApplyAsync(mapping.Id, rows, "idem-dup", Actor, CorrelationId));

        Assert.Equal("duplicate_external_keys", error.Code);
        Assert.Equal(0, h.PersonGateway.CreateCallCount); // no row was processed at all
        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-dup")); // never even claimed
    }

    [Fact]
    public async Task PreviewAsync_rejects_a_request_with_duplicate_external_keys()
    {
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        var rows = new[] { Row("ext-1", "Ada", "Lovelace"), Row("ext-1", "Someone", "Else") };

        var error = await Assert.ThrowsAsync<ImportError>(() => h.BatchService.PreviewAsync(mapping.Id, rows, Actor));

        Assert.Equal("duplicate_external_keys", error.Code);
    }

    [Fact]
    public async Task ApplyAsync_when_row_provenance_recording_genuinely_fails_the_whole_attempt_fails_and_releases_the_claim()
    {
        // Residual durability note: provenance recording is no longer silently best-effort. A
        // genuine (verified, not ambiguous) failure to record it must fail the WHOLE attempt and
        // release the idempotency claim, rather than silently completing with a provenance gap -
        // preserving "Created" provenance across a retry is a required invariant, not optional.
        var h = Build();
        var mapping = await CreateMappingAsync(h);
        h.RowProvenanceLedgerFaultInjector.ThrowOnNextRecord = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-provenance-fail", Actor, CorrelationId));

        Assert.Null(h.IdempotencyStore.Get(TenantId, "integrations.import_batch.apply", "idem-provenance-fail")); // released, not a silent partial success

        // A genuine retry (provenance recording now working) must still succeed and adopt the
        // already-created PERSON rather than duplicating it.
        var retried = await h.BatchService.ApplyAsync(mapping.Id, [Row("ext-1", "Ada", "Lovelace")], "idem-provenance-fail", Actor, CorrelationId);
        Assert.Equal(1, h.PersonGateway.CreateCallCount);

        // Round-5 remediation (ZAINAB-INT-002-F05): the retry must report this row as Created -
        // NOT Unchanged - referencing the SAME PersonId the first attempt durably created, and the
        // final batch's own audit record must say created_count = 1, not 0. Provenance is now
        // recorded BEFORE the link write, so a retry after a provenance failure finds no link yet,
        // correctly re-enters the Created branch, and adopts the orphaned PERSON - never silently
        // downgrading to Unchanged.
        var rowOutcome = Assert.Single(retried.Batch.RowOutcomes);
        Assert.Equal(RowOutcome.Created, rowOutcome.Outcome);
        Assert.Equal("per_fake_1", rowOutcome.PersonId);

        var appliedAuditEvent = Assert.Single(h.AuditSink.All(), e => e.Action == "integration.import_batch_applied");
        Assert.Equal(1, appliedAuditEvent.Detail["created_count"]);
    }
}
