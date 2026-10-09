using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.Integrations.Audit;
using Biolog.Features.Integrations.Domain;
using Biolog.Features.Integrations.Identifiers;
using Biolog.Features.Integrations.Tenancy;

namespace Biolog.Features.Integrations;

/// <summary>One input row: the external system's own identifier for the person, plus its raw field values keyed by the SOURCE's own field name (matched against the mapping's own <see cref="FieldMapping.SourceField"/>).</summary>
public sealed record ImportRowInput(string ExternalKey, IReadOnlyDictionary<string, string?> SourceFieldValues);

/// <summary>
/// BIO-INT-002-AC03 (dry-run/conflict preview), AC04 (no silent overwrite of conflicting
/// identity evidence), AC05 (idempotent reprocessing), AC02 (master/downstream per
/// integration).
///
/// <b>AC04 - the central design decision of this whole story.</b> BIO-IDN-001's own
/// <c>PersonService</c> exposes exactly <c>RegisterAsync</c> (create) and
/// <c>UpdateStatusAsync</c> (lifecycle only) - there is NO method anywhere to correct a PERSON's
/// own name/DOB/etc. fields after creation (verified directly against
/// <c>api/features/bio-idn-001/PersonService.cs</c>, not assumed). This story therefore NEVER
/// overwrites an existing PERSON's profile fields, by construction, not merely "tries not to":
/// <list type="bullet">
/// <item>No existing <see cref="ExternalIdentityLink"/> for an external key -&gt; a brand-new
/// PERSON is created via <see cref="IPersonGateway.CreateAsync"/> (a real, uncontested gap - this
/// path has always existed on BIO-IDN-001). Outcome <see cref="RowOutcome.Created"/>.</item>
/// <item>Already linked, every incoming field matches the current PERSON value (or the current
/// value is simply unset) -&gt; nothing to write at all, since even a pure "fill in a blank
/// field" requires a PERSON-mutation method this story does not have. Outcome
/// <see cref="RowOutcome.Unchanged"/> - detected, never applied.</item>
/// <item>Already linked, at least one incoming field DISAGREES with the current PERSON value
/// -&gt; recorded as a <see cref="FieldConflict"/>, never written, surfaced to a human reviewer
/// via this batch's own <see cref="ImportRowOutcome"/>. Outcome <see cref="RowOutcome.Conflict"/>.</item>
/// </list>
/// This fully satisfies AC04 (nothing is EVER silently overwritten) without touching
/// BIO-IDN-001's own shared <c>Person.cs</c>/<c>PersonService.cs</c>, which would need Kemi's
/// recorded approval. <b>Disclosed, deliberate scope boundary:</b> conflict RESOLUTION (actually
/// applying a human-reviewed correction, or even a harmless fill-in of a blank field) needs a
/// PERSON profile-mutator this story is not authorized to add - that is future scope for
/// whichever story next owns BIO-IDN-001's own aggregate, not something built here. See
/// <c>docs/features/bio-int-002/README.md</c> for the full record.
///
/// <b>AC02.</b> <see cref="SyncDirection.BiologIsMaster"/> rejects both <see cref="PreviewAsync"/>
/// and <see cref="ApplyAsync"/> outright (<see cref="ImportError.BiologIsMasterForThisIntegration"/>)
/// - BIOLOG is the system of record for that integration, so an external system pushing data in
/// is rejected, never silently accepted and discarded.
///
/// <b>AC05.</b> <see cref="ApplyAsync"/> follows ADR-0004/BIO-ARC-004's documented idempotency
/// contract exactly as BIO-MOB-003's own <c>OfflineMutationIntakeService</c> already established:
/// claim, compare the stored fingerprint (a reused key with DIFFERENT content is a client bug,
/// <c>409 idempotency_key_reuse</c>, never silently treated as the original), replay a completed
/// claim's own stored result rather than re-executing, release an in-flight claim that fails
/// before producing a result so a genuine retry can proceed.
///
/// <see cref="PreviewAsync"/> is a pure read - no lock, no audit, no idempotency-store
/// interaction - never mutates anything (AC03's own "preview" guarantee).
///
/// <b>F01 round-1 remediation, Zainab's own refinement (ZAINAB-INT-002-F01).</b> Two gaps beyond
/// Seyi's claim-ownership finding: (1) the claim was only Released on a failure inside the rows
/// loop - a failure in batch construction, audit, or persistence afterward left the claim stuck
/// <c>InFlight</c> forever, since nothing ever completed or released it. Fixed: the SAME
/// try/catch now covers the full create-link-batch-audit-persist sequence, so ANY failure
/// anywhere in it releases the claim for a genuine retry. (2) A retry after a per-row partial
/// failure (a PERSON durably created, then that row's OWN link-write throws) had no protection
/// against creating a SECOND, different, orphaned PERSON for the same external key on retry -
/// fixed by <see cref="IOrphanedPersonLedger"/>, see its own header for the full design (mirrors
/// BIO-VIS-002's F03/F04 "adoption" backstop).
///
/// <b>F02 round-1 remediation (ZAINAB-INT-002-F02).</b> <see cref="ImportSourceMapping.Active"/>
/// existed but was never checked - an administrator's kill switch did nothing. Both
/// <see cref="PreviewAsync"/> and <see cref="ApplyAsync"/> now reject a disabled mapping
/// (<see cref="ImportError.MappingDisabled"/>).
///
/// <b>Round-2 remediation (Seyi round-2 FAIL / Zainab round-2 FAIL, three findings).</b>
/// (A) Orphan adoption was cancellation-unsafe (the caller's own token could abort
/// <see cref="IOrphanedPersonLedger.RecordAsync"/> right after a durable PERSON create, losing the
/// only reference to it) AND content-blind (a later retry with CHANGED field values could adopt a
/// mismatched orphan and report a clean <c>Created</c>). Fixed: the record write now uses
/// <see cref="CancellationToken.None"/> with the same ambiguous-failure verification
/// <see cref="AuditAsync"/> already establishes, and adoption now runs the SAME field-comparison
/// <see cref="ClassifyRowAsync"/> uses for an already-linked row before linking - a disagreement
/// becomes a <see cref="RowOutcome.Conflict"/>, never a silent adoption.
/// (B) A failure AFTER the rows loop (batch/audit/persist) released the claim, but a retry's own
/// re-classification saw the now-existing link and reported <c>Unchanged</c> - the original
/// <c>Created</c> provenance for that idempotency attempt was lost. Fixed by
/// <see cref="IImportRowProvenanceLedger"/>, see its own header.
/// (C) <see cref="ApplyAsync"/> read <c>Active</c>/<c>SyncDirection</c> BEFORE acquiring the
/// per-mapping lock <see cref="ImportMappingService.UpdateAsync"/> also uses to commit a disable -
/// a TOCTOU race. Fixed: the gating mapping fetch now happens ONLY after the lock is held, so the
/// authoritative check always sees whatever the most recent committed config actually is.
///
/// <b>Round-4 remediation (Seyi round-3 FAIL = Zainab round-3 FAIL finding 1; Zainab round-3 FAIL
/// finding 2, ZAINAB-INT-002-F04; Zainab's own residual durability note).</b>
/// (1) Round-2's finding-C fix moved the authoritative mapping re-read and its guard checks to
/// AFTER the lock was acquired, but still BEFORE the try/catch that releases the idempotency claim
/// on failure - so a disabled mapping, a direction flipped to <c>biolog_is_master</c>, the mapping
/// disappearing, or cancellation while acquiring the lock/doing that read all threw AFTER this
/// caller won the claim but BEFORE anything could release it, stranding the key <c>InFlight</c>
/// forever (even after the underlying problem was fixed). Fixed: lock acquisition, the fresh
/// mapping read, and both guard checks are now INSIDE the same release-on-failure
/// <c>try</c>/<c>catch</c> as the rows loop and batch/audit/persist - nothing that can throw after
/// claim ownership sits outside it.
/// (2) <see cref="IImportRowProvenanceLedger"/> was keyed only by <c>(TenantId, IdempotencyKey,
/// ExternalKey)</c> - missing <c>MappingId</c> let a reused idempotency key against a DIFFERENT
/// mapping incorrectly replay the first mapping's outcome for a matching external key. Fixed by
/// adding <c>MappingId</c> to its key (see its own header). The OTHER half of this finding - two
/// rows in ONE request sharing an external key, where row two would replay row one's just-recorded
/// outcome without evaluating its own content - is closed here, not in the ledger: both
/// <see cref="PreviewAsync"/> and <see cref="ApplyAsync"/> now reject a request containing
/// duplicate external keys up front (<see cref="ImportError.DuplicateExternalKeysInRequest"/>),
/// before any row is processed. This, combined with <c>MappingId</c> in the ledger's key, is
/// sufficient without a separate per-row content fingerprint: <see cref="ComputeFingerprint"/>'s
/// own whole-batch check (run before the claim loop) already rejects a reused idempotency key
/// whose <c>(mappingId, rows)</c> content differs AT ALL from the original claim's - so by the time
/// the provenance ledger is ever consulted, this exact <c>(idempotencyKey, mappingId, rows)</c>
/// combination is already confirmed identical to whatever the original attempt was for. Once
/// cross-mapping reuse and within-request duplicate keys are both closed, there is no remaining
/// scenario where <c>(tenantId, idempotencyKey, mappingId, externalKey)</c> could legitimately
/// correspond to two different row contents.
/// (3) <see cref="RecordRowProvenanceBestEffortAsync"/>'s own silent swallow-any-failure stance
/// contradicted treating "Created" provenance as a guaranteed remediation. Replaced with
/// <see cref="RecordRowProvenanceSafelyAsync"/>: the same ambiguous-failure verification
/// <see cref="RecordOrphanedPersonSafelyAsync"/> already establishes, but if verification confirms
/// the write genuinely never landed, it now RE-THROWS - failing the whole attempt and releasing
/// the claim - rather than silently completing with a provenance gap.
///
/// <b>Round-5 remediation (ZAINAB-INT-002-F05).</b> (3) above still lost "Created" provenance on a
/// GENUINE provenance-write failure: that write ran AFTER <see cref="IExternalIdentityLinkStore.CreateAsync"/>
/// had already durably created the link, so releasing the claim on failure left a row with a real
/// link but no provenance record. A retry's <see cref="ClassifyRowAsync"/> then found that link and
/// returned <c>Unchanged</c> - never re-entering the <c>Created</c> branch, so it never consulted
/// the orphan ledger that could have shown this PERSON was created by the failed attempt. Fixed by
/// REORDERING, not by adding new machinery: provenance is now recorded BEFORE the link is created.
/// If the write fails now, no link exists yet - a retry finds no link, correctly re-enters the
/// <c>Created</c> branch, and adopts the already-created PERSON from <see cref="IOrphanedPersonLedger"/>
/// instead of losing its provenance. The replay branch (a recorded outcome already exists) now also
/// idempotently ensures the link exists before continuing, in case THIS exact attempt's own link
/// write - not its provenance write - was what failed and triggered the retry.
///
/// <b>Round-6 remediation (Seyi round-6 FAIL, ZAINAB-INT-002-F06, architecture-checkpoint finding -
/// see <see cref="IImportRowProvenanceLedger"/>'s own header for the full record).</b>
/// <see cref="IIdempotencyStore.Release"/> deletes the claim AND its own stored
/// <c>RequestFingerprint</c> entirely, but <see cref="IImportRowProvenanceLedger"/>'s recorded row
/// outcomes had no fingerprint of their own and were never cleaned up - so a released attempt's
/// AC05 content-reuse protection vanished with the claim while its recovery state survived. A later
/// request reusing the SAME key/mapping/external-key with DIFFERENT row content could win a fresh
/// claim and hit the replay branch, which trusted stale provenance without ever re-evaluating
/// content. Fixed by carrying the attempt's own whole-batch <see cref="ComputeFingerprint"/> value
/// alongside each recorded outcome: a replay now compares it against the CURRENT request's own
/// fingerprint first - a match replays as today, a mismatch falls through to fresh classification
/// instead of a silent adopt. This is god's own recorded decision: extend the existing durable-
/// ledger shape with one more field, not a separate new ledger, not a change to the shared
/// <see cref="IIdempotencyStore"/> contract, and not a cross-store transactional rewrite (this
/// codebase has no shared transactional substrate across the claim store, orphan ledger, provenance
/// ledger, link store and audit sink - building one is Kemi-scale, the same proportionality
/// standard BIO-VIS-002's own outbox-pattern question was held to).
///
/// <b>Round-7 remediation (Zainab round-6 FAIL, ZAINAB-INT-002-F07, confirmed by Seyi - a narrow,
/// mechanical bug one layer below the recovery-chain pattern, not another instance of it; god's own
/// recorded call after both reviewers weighed in).</b> See <see cref="ComputeFingerprint"/>'s own
/// header for the full record: the fingerprint's prior raw-delimiter serialization was not
/// injective, so a released claim could be reused with genuinely different but colliding-encoded
/// content and still pass round 6's fingerprint-match check. Fixed by hashing a canonical JSON
/// serialization instead.
/// </summary>
internal sealed class ImportBatchService(
    ICurrentTenantProvider tenantProvider, IImportSourceMappingRepository mappingRepository, IImportBatchRepository batchRepository,
    IExternalIdentityLinkStore linkStore, IPersonGateway personGateway, IImportAuditSink auditSink, ImportLockRegistry lockRegistry,
    IPermissionGateway permissionGateway, IIdempotencyStore idempotencyStore, IOrphanedPersonLedger orphanedPersonLedger,
    IImportRowProvenanceLedger rowProvenanceLedger,
    TimeSpan? idempotencyWaitTimeout = null, TimeSpan? idempotencyWaitPollInterval = null)
{
    public const string ApplyPermission = "integrations.import_apply";
    private const string ApplyOperation = "integrations.import_batch.apply";

    // BIO-INT-002-F01's own remediation: how long a claim-loser (a concurrent caller whose
    // Idempotency-Key is already owned by someone else's in-flight attempt) waits for that
    // owner's result before giving up with a 503 (ADR-0004/BIO-ARC-004's documented "claim-loser
    // waits bounded then idempotency_wait_timeout" contract). Both are injectable (constructor
    // parameters, defaulting to production-sized values) so a test can prove the timeout path
    // deterministically and fast, without actually waiting multiple real seconds.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);

    public async Task<ImportBatch> PreviewAsync(
        string mappingId, IReadOnlyList<ImportRowInput> rows, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        // Round-4 remediation (Zainab round-3 FAIL, ZAINAB-INT-002-F04): reject duplicate external
        // keys within the request itself before any row is processed - see this class's own
        // header for why this is required even for a pure read.
        ValidateNoDuplicateExternalKeys(rows);

        var mapping = await mappingRepository.GetAsync(tenantId, mappingId, cancellationToken) ?? throw ImportError.MappingNotFound(mappingId);
        if (mapping.SyncDirection == SyncDirection.BiologIsMaster)
        {
            throw ImportError.BiologIsMasterForThisIntegration(mappingId);
        }

        // F02's own remediation (Zainab round-1 FAIL): the administrative kill switch must stop
        // preview too, not just apply.
        if (!mapping.Active)
        {
            throw ImportError.MappingDisabled(mappingId);
        }

        var outcomes = new List<ImportRowOutcome>();
        foreach (var row in rows)
        {
            outcomes.Add(await ClassifyRowAsync(tenantId, mapping, row, cancellationToken));
        }

        return new ImportBatch
        {
            Id = CanonicalId.NewLocalId(),
            TenantId = tenantId,
            MappingId = mappingId,
            Mode = ImportBatchMode.DryRun,
            SubmittedAt = UtcInstant.Now(),
            RowOutcomes = outcomes,
        };
    }

    public async Task<(ImportBatch Batch, bool Replayed)> ApplyAsync(
        string mappingId, IReadOnlyList<ImportRowInput> rows, string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ApplyPermission, cancellationToken))
        {
            throw ImportError.PermissionRequired(ApplyPermission);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw ImportError.IdempotencyKeyRequired();
        }

        // Round-4 remediation (Zainab round-3 FAIL, ZAINAB-INT-002-F04): reject duplicate external
        // keys within the request itself before any row is processed, the claim is even attempted,
        // or any write happens - see this class's own header for the full reasoning.
        ValidateNoDuplicateExternalKeys(rows);

        // Round-2 remediation (Zainab round-2 FAIL, finding C): this early fetch exists ONLY to
        // fail fast on an unknown mapping id before touching the idempotency store. It must NEVER
        // gate SyncDirection/Active - those checks happen again, authoritatively, after the
        // per-mapping lock is held below (same lock ImportMappingService.UpdateAsync uses to
        // commit a disable), so a concurrent disable that commits before this caller acquires the
        // lock is never missed by a caller acting on a stale pre-lock snapshot.
        if (await mappingRepository.GetAsync(tenantId, mappingId, cancellationToken) is null)
        {
            throw ImportError.MappingNotFound(mappingId);
        }

        var fingerprint = ComputeFingerprint(mappingId, rows);

        // BIO-INT-002-F01's own remediation (Seyi round-1 FAIL): the original code checked only
        // `claimed.State == Completed` - a shared InFlight record from a CONCURRENT caller looked
        // identical to a claim this caller itself just won, so a second caller fell through and
        // executed a second time, silently replacing the first caller's durable result.
        // `IdempotencyClaim.IsNewClaim` is the missing ownership signal: only the true owner ever
        // breaks out of this loop to execute below. Every other caller (someone else's claim is
        // in flight) waits, bounded by `_idempotencyWaitTimeout`, re-attempting its own `Claim`
        // each iteration - which naturally becomes a fresh ownership attempt if the original
        // owner's attempt fails and releases the key, or keeps observing the owner's own progress
        // (InFlight-by-someone-else, then eventually Completed) otherwise.
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        while (true)
        {
            var claim = idempotencyStore.Claim(tenantId, ApplyOperation, idempotencyKey, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                throw ImportError.IdempotencyKeyReused(idempotencyKey);
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return ((ImportBatch)claim.Record.Result!, Replayed: true);
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw ImportError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        // Round-4 remediation (Seyi round-3 FAIL = Zainab round-3 FAIL finding 1): lock
        // acquisition, the fresh authoritative mapping read, and both guard checks now ALL live
        // INSIDE the same release-on-failure try/catch as the rows loop and batch/audit/persist.
        // Round-2's own fix put these checks after the lock but still BEFORE this try - so a
        // disabled mapping, a direction flip, a vanished mapping, or cancellation here threw AFTER
        // claim ownership but BEFORE anything could release it, stranding the key InFlight
        // forever. Only `idempotencyStore.Complete` below, once everything has durably succeeded,
        // exits this try without releasing.
        ImportBatch created;
        try
        {
            using var _ = await lockRegistry.AcquireAsync(tenantId, mappingId, cancellationToken);

            // The ONLY authoritative mapping read - taken fresh, under the lock, so it can never
            // be a stale snapshot from before a concurrent ImportMappingService.UpdateAsync (same
            // lock) committed a disable or a direction change. Everything below uses THIS
            // mapping, never the pre-lock one.
            var mapping = await mappingRepository.GetAsync(tenantId, mappingId, cancellationToken) ?? throw ImportError.MappingNotFound(mappingId);
            if (mapping.SyncDirection == SyncDirection.BiologIsMaster)
            {
                throw ImportError.BiologIsMasterForThisIntegration(mappingId);
            }

            if (!mapping.Active)
            {
                throw ImportError.MappingDisabled(mappingId);
            }

            var outcomes = new List<ImportRowOutcome>();
            foreach (var row in rows)
            {
                // Round-2 remediation (Zainab round-2 FAIL, finding B, see
                // IImportRowProvenanceLedger's own header): this exact idempotency attempt may
                // have already fully processed this row before a LATER failure (batch/audit/
                // persist) released the claim for this retry. Replay that recorded outcome
                // verbatim - never re-classify, never re-write - so the row's original
                // Created/Conflict/Unchanged provenance survives even though a link may already
                // exist from the earlier pass.
                //
                // Round-6 remediation (Seyi round-6 FAIL, ZAINAB-INT-002-F06, see this ledger's
                // own header): a release deletes the idempotency claim's own fingerprint, so a
                // LATER request reusing this same key/mapping/external-key with DIFFERENT content
                // could otherwise win a fresh claim and land here with no protection. The stored
                // entry now carries the fingerprint of the attempt that recorded it - only trust
                // the replay if that fingerprint matches THIS request's own. A mismatch means a
                // genuine different-content key reuse: fall through to fresh classification below,
                // exactly as if no provenance record existed at all - never silently adopt stale
                // content under a disagreeing request.
                var recordedEntry = await rowProvenanceLedger.FindAsync(tenantId, idempotencyKey, mappingId, row.ExternalKey, cancellationToken);
                if (recordedEntry is not null && recordedEntry.Fingerprint == fingerprint)
                {
                    var recordedOutcome = recordedEntry.Outcome;

                    // Round-5 remediation (ZAINAB-INT-002-F05): provenance is now recorded BEFORE
                    // the link write (see below), so a replayed Created outcome may still be
                    // missing its link if THIS exact attempt's own link write is what failed and
                    // triggered this retry. Idempotently ensure the link exists before continuing
                    // - never re-create the PERSON, never re-decide the outcome, just make the
                    // link durable to match what provenance already recorded.
                    if (recordedOutcome.Outcome == RowOutcome.Created && recordedOutcome.PersonId is not null
                        && await linkStore.FindAsync(tenantId, mappingId, row.ExternalKey, cancellationToken) is null)
                    {
                        await linkStore.CreateAsync(new ExternalIdentityLink
                        {
                            TenantId = tenantId,
                            MappingId = mappingId,
                            ExternalKey = row.ExternalKey,
                            PersonId = recordedOutcome.PersonId,
                            CreatedAt = UtcInstant.Now(),
                        }, cancellationToken);
                    }

                    outcomes.Add(recordedOutcome);
                    continue;
                }

                var outcome = await ClassifyRowAsync(tenantId, mapping, row, cancellationToken);
                if (outcome.Outcome == RowOutcome.Created)
                {
                    // AC04: the ONLY write this service ever performs - creating a brand-new
                    // PERSON for an external key with no existing link. Unchanged/Conflict rows
                    // are classified only, never written (see this class's own header).
                    //
                    // F01 remediation (ZAINAB-INT-002-F01, see IOrphanedPersonLedger's own
                    // header): check for an already-orphaned PERSON from a PRIOR partial-failure
                    // attempt FIRST - adopt it (link against it) rather than minting a second one.
                    var orphanedPersonId = await orphanedPersonLedger.FindAsync(tenantId, mappingId, row.ExternalKey, cancellationToken);
                    string? personId = null;
                    if (orphanedPersonId is not null)
                    {
                        // Round-2 remediation (Seyi round-2 FAIL, finding A): adoption is NOT
                        // content-blind - fetch the orphan's actual current fields and run the
                        // SAME comparison ClassifyRowAsync uses for an already-linked row. A
                        // disagreement must surface as Conflict, never as a silent Created that
                        // links a mismatched identity.
                        var orphanedPerson = await personGateway.GetAsync(orphanedPersonId, cancellationToken);
                        if (orphanedPerson is null)
                        {
                            outcome = outcome with
                            {
                                Outcome = RowOutcome.Conflict,
                                PersonId = orphanedPersonId,
                                Conflicts = [new FieldConflict("person_id", orphanedPersonId, null)],
                            };
                        }
                        else
                        {
                            var orphanConflicts = CompareFields(mapping, row, orphanedPerson);
                            if (orphanConflicts.Count > 0)
                            {
                                outcome = outcome with { Outcome = RowOutcome.Conflict, PersonId = orphanedPersonId, Conflicts = orphanConflicts };
                            }
                            else
                            {
                                personId = orphanedPersonId;
                            }
                        }
                    }
                    else
                    {
                        var fields = ProjectToPersonFields(mapping, row);
                        var person = await personGateway.CreateAsync(fields, actor.Id, actor.Type.ToWireString(), correlationId, cancellationToken);
                        personId = person.Id;

                        // Round-2 remediation (Seyi round-2 FAIL, finding A): once a PERSON has
                        // been durably created, recording that fact must never be abortable by
                        // the caller's own token - CancellationToken.None, with the same
                        // ambiguous-failure verification AuditAsync already establishes. If this
                        // throws AND the entry genuinely never landed, the row (and the whole
                        // attempt) fails and releases, but a real PERSON the ledger never heard
                        // about would otherwise be unrecoverable - this is why the write itself
                        // must not be cancellable.
                        await RecordOrphanedPersonSafelyAsync(tenantId, mappingId, row.ExternalKey, personId);
                    }

                    if (personId is not null)
                    {
                        outcome = outcome with { PersonId = personId };
                    }
                }

                outcomes.Add(outcome);

                // Round-2 remediation (Zainab round-2 FAIL, finding B), upgraded in round-4
                // (Zainab's own residual durability note), REORDERED in round-5
                // (ZAINAB-INT-002-F05, see this class's own header): provenance is now recorded
                // BEFORE the link write below, not after. If this throws, no link exists yet for
                // this row - a retry's ClassifyRowAsync then finds no link, correctly re-enters
                // the Created branch, and adopts the already-created PERSON from the orphan
                // ledger instead of losing its "Created" provenance to a stale Unchanged read.
                await RecordRowProvenanceSafelyAsync(tenantId, idempotencyKey, mappingId, row.ExternalKey, outcome, fingerprint);

                if (outcome.PersonId is not null && outcome.Outcome == RowOutcome.Created)
                {
                    await linkStore.CreateAsync(new ExternalIdentityLink
                    {
                        TenantId = tenantId,
                        MappingId = mappingId,
                        ExternalKey = row.ExternalKey,
                        PersonId = outcome.PersonId,
                        CreatedAt = UtcInstant.Now(),
                    }, cancellationToken);
                }
            }

            var batch = new ImportBatch
            {
                Id = CanonicalId.NewLocalId(),
                TenantId = tenantId,
                MappingId = mappingId,
                Mode = ImportBatchMode.Applied,
                SubmittedAt = UtcInstant.Now(),
                RowOutcomes = outcomes,
                IdempotencyKey = idempotencyKey,
            };

            await AuditAsync(tenantId, actor, "integration.import_batch_applied", batch.Id, "import_batch_applied", correlationId,
                new Dictionary<string, (object?, string)>
                {
                    ["mapping_id"] = (mappingId, "internal"),
                    ["row_count"] = (rows.Count, "internal"),
                    ["created_count"] = (outcomes.Count(o => o.Outcome == RowOutcome.Created), "internal"),
                    ["conflict_count"] = (outcomes.Count(o => o.Outcome == RowOutcome.Conflict), "internal"),
                },
                cancellationToken);

            created = await batchRepository.CreateAsync(batch, CancellationToken.None);
        }
        catch
        {
            // Nothing CONFIRMED durable for this idempotency key has been completed - released
            // rather than completed, so a genuine retry (not a replay) can proceed. Any PERSON
            // actually created above survives in IOrphanedPersonLedger for that retry to adopt,
            // never duplicated.
            idempotencyStore.Release(tenantId, ApplyOperation, idempotencyKey);
            throw;
        }

        idempotencyStore.Complete(tenantId, ApplyOperation, idempotencyKey, created);
        return (created, Replayed: false);
    }

    /// <summary>
    /// Round-4 remediation (Zainab round-3 FAIL, ZAINAB-INT-002-F04): a request containing two
    /// rows with the same external key would otherwise let row two silently replay row one's
    /// just-recorded <see cref="IImportRowProvenanceLedger"/> entry without ever evaluating its
    /// own content - defeating conflict detection for row two. Rejected up front, in both
    /// <see cref="PreviewAsync"/> and <see cref="ApplyAsync"/>, before any row is processed.
    /// </summary>
    private static void ValidateNoDuplicateExternalKeys(IReadOnlyList<ImportRowInput> rows)
    {
        var duplicates = rows
            .GroupBy(r => r.ExternalKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw ImportError.DuplicateExternalKeysInRequest(duplicates);
        }
    }

    public Task<ImportBatch?> GetBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return batchRepository.GetAsync(tenantId, batchId, cancellationToken);
    }

    public Task<IReadOnlyList<ImportBatch>> ListBatchesAsync(string mappingId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return batchRepository.ListByMappingAsync(tenantId, mappingId, cancellationToken);
    }

    private async Task<ImportRowOutcome> ClassifyRowAsync(string tenantId, ImportSourceMapping mapping, ImportRowInput row, CancellationToken cancellationToken)
    {
        var existingLink = await linkStore.FindAsync(tenantId, mapping.Id, row.ExternalKey, cancellationToken);
        if (existingLink is null)
        {
            return new ImportRowOutcome { ExternalKey = row.ExternalKey, Outcome = RowOutcome.Created };
        }

        var person = await personGateway.GetAsync(existingLink.PersonId, cancellationToken);
        if (person is null)
        {
            // The linked PERSON row is gone from this read (should not happen - PERSON has no
            // delete, only Archived - but a cross-tenant/race read returning null is handled the
            // same defensive way BIO-VIS-002 treats an analogous "linked record vanished" case:
            // surfaced as a conflict for a human to look at, never silently re-created as if the
            // link never existed.
            return new ImportRowOutcome
            {
                ExternalKey = row.ExternalKey,
                Outcome = RowOutcome.Conflict,
                PersonId = existingLink.PersonId,
                Conflicts = [new FieldConflict("person_id", existingLink.PersonId, null)],
            };
        }

        var conflicts = CompareFields(mapping, row, person);

        return new ImportRowOutcome
        {
            ExternalKey = row.ExternalKey,
            Outcome = conflicts.Count > 0 ? RowOutcome.Conflict : RowOutcome.Unchanged,
            PersonId = person.Id,
            Conflicts = conflicts,
        };
    }

    /// <summary>
    /// AC04's own field-disagreement rule, shared by two call sites: an already-linked row
    /// (<see cref="ClassifyRowAsync"/>) and - round-2 remediation (Seyi round-2 FAIL, finding A) -
    /// adopting an orphaned PERSON in <see cref="ApplyAsync"/>, which must run this SAME
    /// comparison before linking rather than adopting blindly.
    /// </summary>
    private static IReadOnlyList<FieldConflict> CompareFields(ImportSourceMapping mapping, ImportRowInput row, PersonView person)
    {
        var conflicts = new List<FieldConflict>();
        foreach (var fieldMapping in mapping.FieldMappings)
        {
            row.SourceFieldValues.TryGetValue(fieldMapping.SourceField, out var incomingValue);
            var currentValue = ReadPersonField(person, fieldMapping.PersonField);

            if (string.IsNullOrEmpty(incomingValue))
            {
                continue; // nothing incoming for this field - never a conflict.
            }

            if (string.IsNullOrEmpty(currentValue) || string.Equals(currentValue, incomingValue, StringComparison.Ordinal))
            {
                continue; // a pure fill-in of a blank field, or an exact match - not a conflict,
                          // but still never WRITTEN (AC04 - see this class's own header).
            }

            conflicts.Add(new FieldConflict(fieldMapping.PersonField, currentValue, incomingValue));
        }

        return conflicts;
    }

    private static string? ReadPersonField(PersonView person, string personField) => personField switch
    {
        "title" => person.Title,
        "first_name" => person.FirstName,
        "middle_name" => person.MiddleName,
        "last_name" => person.LastName,
        "preferred_name" => person.PreferredName,
        "born_on" => person.BornOn,
        _ => null,
    };

    private static CreatePersonFields ProjectToPersonFields(ImportSourceMapping mapping, ImportRowInput row)
    {
        string? Value(string personField)
        {
            var sourceField = mapping.FieldMappings.FirstOrDefault(m => m.PersonField == personField)?.SourceField;
            return sourceField is null ? null : row.SourceFieldValues.GetValueOrDefault(sourceField);
        }

        var firstName = Value("first_name");
        var lastName = Value("last_name");
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            throw ImportError.Validation([
                new Api.ValidationErrorItem($"rows[{row.ExternalKey}]", "missing_required_person_fields",
                    "a new person requires at least first_name and last_name to be mapped and present"),
            ]);
        }

        return new CreatePersonFields
        {
            Title = Value("title"),
            FirstName = firstName,
            MiddleName = Value("middle_name"),
            LastName = lastName,
            PreferredName = Value("preferred_name"),
            BornOn = Value("born_on"),
        };
    }

    /// <summary>
    /// ADR-0004/BIO-ARC-004's idempotency contract: the fingerprint binds every claim this
    /// operation's content makes - mapping id plus every row's own content - so a reused key with
    /// DIFFERENT rows is caught as a client bug (BIO-MOB-003's own identical discipline), never
    /// silently treated as the original request.
    ///
    /// <b>Round-7 remediation (Zainab round-6 FAIL, ZAINAB-INT-002-F07, confirmed by Seyi).</b> The
    /// previous implementation joined external keys/field names/values with raw, unescaped
    /// <c>; : , =</c> delimiters - not injective. A field map <c>{"a": "b,c=d"}</c> and
    /// <c>{"a": "b", "c": "d"}</c> both produced the literal segment <c>a=b,c=d</c> and therefore
    /// the SAME fingerprint, even though they are different content. After a claim release (the
    /// round-6/F06 fingerprint-bound provenance fix), a request carrying one of these
    /// colliding-but-different payloads could pass the fingerprint-match check and incorrectly
    /// replay stale provenance - defeating that exact protection. Fixed by hashing a canonical JSON
    /// serialization instead of hand-rolled delimiter concatenation: JSON's own string quoting/
    /// escaping rules make the serialized form unambiguous regardless of what characters appear in
    /// an external key, field name, or field value - two distinct (mappingId, rows) inputs can
    /// never serialize to the same JSON text. Row and field order are still sorted first so the
    /// fingerprint stays deterministic regardless of input ordering.
    /// </summary>
    // Declared internal (not private), same convention as every other story's purely-additive
    // test-visibility widening, so a test can assert on fingerprint output directly rather than
    // only through ApplyAsync's own observable behavior.
    internal static string ComputeFingerprint(string mappingId, IReadOnlyList<ImportRowInput> rows)
    {
        var canonicalRows = rows
            .OrderBy(r => r.ExternalKey, StringComparer.Ordinal)
            .Select(r => new
            {
                externalKey = r.ExternalKey,
                fields = r.SourceFieldValues
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new { key = kv.Key, value = kv.Value })
                    .ToArray(),
            })
            .ToArray();

        var canonical = new { mappingId, rows = canonicalRows };
        var json = JsonSerializer.Serialize(canonical);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string targetId, string reasonCode, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = ImportAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("import_batch", targetId), AuditOutcome.Allowed, reasonCode, correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw ImportError.AuditUnavailable();
            }
        }
    }

    /// <summary>
    /// Round-2 remediation (Seyi round-2 FAIL, finding A): once <paramref name="personId"/> is a
    /// durably created PERSON, recording it must never be abortable by the caller's own
    /// <paramref name="cancellationToken"/> - uses <see cref="CancellationToken.None"/>
    /// unconditionally, with the same ambiguous-failure verification <see cref="AuditAsync"/>
    /// already establishes (on throw, check whether the write actually landed anyway before
    /// treating it as a genuine failure).
    /// </summary>
    private async Task RecordOrphanedPersonSafelyAsync(string tenantId, string mappingId, string externalKey, string personId)
    {
        try
        {
            await orphanedPersonLedger.RecordAsync(tenantId, mappingId, externalKey, personId, CancellationToken.None);
        }
        catch
        {
            var recorded = await orphanedPersonLedger.FindAsync(tenantId, mappingId, externalKey, CancellationToken.None);
            if (recorded != personId)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Round-2 remediation (Zainab round-2 FAIL, finding B), upgraded in round-4 (Zainab's own
    /// residual durability note, see <see cref="IImportRowProvenanceLedger"/>'s own header): this
    /// is no longer silently best-effort. Uses <see cref="CancellationToken.None"/> (the row's own
    /// create/link work is already durably done; recording that provenance must not be abortable
    /// by the caller's token either) with the same ambiguous-failure verification
    /// <see cref="RecordOrphanedPersonSafelyAsync"/> establishes - but if verification confirms
    /// the write genuinely never landed, this RE-THROWS, failing the whole attempt (and releasing
    /// the idempotency claim) rather than silently completing with a provenance gap. Preserving
    /// "Created" provenance across a retry is being treated as a required invariant, not an
    /// optional nice-to-have.
    ///
    /// Round-6 remediation (ZAINAB-INT-002-F06, see <see cref="IImportRowProvenanceLedger"/>'s own
    /// header): also carries <paramref name="fingerprint"/> - the current attempt's whole-batch
    /// fingerprint - alongside the outcome, so a LATER replay can tell a genuine same-content retry
    /// apart from a different-content key reuse.
    /// </summary>
    private async Task RecordRowProvenanceSafelyAsync(string tenantId, string idempotencyKey, string mappingId, string externalKey, ImportRowOutcome outcome, string fingerprint)
    {
        try
        {
            await rowProvenanceLedger.RecordAsync(tenantId, idempotencyKey, mappingId, externalKey, outcome, fingerprint, CancellationToken.None);
        }
        catch
        {
            var recorded = await rowProvenanceLedger.FindAsync(tenantId, idempotencyKey, mappingId, externalKey, CancellationToken.None);
            if (recorded is null || !ReferenceEquals(recorded.Outcome, outcome) || recorded.Fingerprint != fingerprint)
            {
                throw;
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw ImportError.TenantContextMissing();
}
