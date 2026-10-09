namespace Biolog.Features.Integrations.Domain;

/// <summary>One recorded row outcome together with the whole-batch fingerprint of the attempt that recorded it - see <see cref="IImportRowProvenanceLedger"/>'s own header, round-6 remediation.</summary>
internal sealed record ImportRowProvenanceEntry(ImportRowOutcome Outcome, string Fingerprint);

/// <summary>
/// BIO-INT-002-F01's own remediation, second half (Zainab round-2 FAIL, ZAINAB-INT-002-F01). Closes
/// the gap <see cref="IOrphanedPersonLedger"/> deliberately does not: once a row's PERSON+link work
/// has already durably succeeded, a LATER failure in batch construction/audit/persistence releases
/// the idempotency claim for a genuine retry. Without this ledger, that retry's own
/// <see cref="ImportBatchService.ClassifyRowAsync"/> finds the link already present and reports the
/// row <see cref="RowOutcome.Unchanged"/> - the fact that THIS exact idempotency attempt originally
/// created a brand-new identity for that row is lost from the final batch/audit record entirely.
///
/// <b>Scope: per-attempt, not global.</b> Keyed by <c>(TenantId, IdempotencyKey, MappingId, ExternalKey)</c>.
/// A genuinely different client request (a different idempotency key) must always re-derive its own
/// row outcomes fresh; only a RETRY of the exact same attempt replays a recorded outcome.
///
/// <b>Round-4 remediation (ZAINAB-INT-002-F04, Zainab round-3 FAIL).</b> The key was originally
/// <c>(TenantId, IdempotencyKey, ExternalKey)</c> - missing <c>MappingId</c>. An idempotency key is
/// scoped per-operation, not per-mapping, so after one attempt failed and released its key, that
/// SAME key could legitimately be reused later against a DIFFERENT mapping; a matching external key
/// under the new mapping would then incorrectly replay the FIRST mapping's recorded outcome/PersonId.
/// <c>MappingId</c> is now part of the key to close that. The companion half of F04 - two rows in ONE
/// request sharing an external key, where row two would otherwise replay row one's just-recorded
/// outcome without ever evaluating its own content - is closed instead by
/// <see cref="ImportBatchService"/> rejecting duplicate external keys within a single request
/// up front (see its own class header for why this, combined with <c>MappingId</c> here, is
/// sufficient without a separate per-row content fingerprint).
///
/// <b>The fix.</b> <see cref="ImportBatchService.ApplyAsync"/> records each row's final decided
/// <see cref="ImportRowOutcome"/> here the moment it is decided (immediately after any create/link
/// work for that row completes), and on every pass through the row loop checks here FIRST - a hit
/// means this exact row, under this exact idempotency key and mapping, was already fully processed
/// by an earlier attempt; its outcome is replayed verbatim, with no re-classification and no re-write.
///
/// <b>Durability (round-4 remediation, Zainab's own residual durability note).</b> Recording is no
/// longer silently best-effort: <see cref="ImportBatchService"/>'s own call site verifies an
/// ambiguous write failure the same way <c>AuditAsync</c> does, and if the write genuinely never
/// landed, the whole attempt fails and releases its idempotency claim rather than silently
/// completing with a provenance gap - preserving "Created" provenance across a retry is being
/// treated as a required invariant, not an optional nice-to-have.
///
/// <b>Round-6 remediation (Seyi round-6 FAIL, ZAINAB-INT-002/F06, architecture-checkpoint finding).</b>
/// <see cref="IIdempotencyStore.Release"/> deletes the claim record AND its own stored
/// <c>RequestFingerprint</c> entirely, but this ledger's recorded outcomes had no fingerprint of
/// their own and were never cleaned up - so a released attempt's AC05 content-reuse protection
/// vanished with the claim, while this ledger's recovery state survived. A later request reusing the
/// SAME (tenant, idempotency key, mapping, external key) with DIFFERENT row content could win a
/// fresh claim (no live record left to compare against) and then hit the replay branch below, which
/// trusted the stored outcome without ever re-evaluating content - silently adopting the ORIGINAL
/// attempt's PERSON under the NEW attempt's (disagreeing) content, exactly the AC04 silent-overwrite
/// problem reappearing through a path none of rounds 2-5 touched.
///
/// Fixed by carrying one more field alongside each recorded outcome: the whole-batch
/// <see cref="ImportBatchService.ComputeFingerprint"/> value of the attempt that recorded it (already
/// computed once per <see cref="ImportBatchService.ApplyAsync"/> call - no new per-row hash). On
/// replay, <see cref="ImportBatchService.ApplyAsync"/> now compares the STORED fingerprint against
/// the CURRENT request's own fingerprint before trusting the replay: a match proceeds exactly as
/// today (the already-tested resume-after-failure path); a mismatch means this is a genuine
/// different-content key reuse - the row falls through to fresh classification (and the orphan-
/// adoption content-check path, if applicable) instead of being silently replayed, exactly as if no
/// provenance record existed at all. This is deliberately the SAME durable-ledger shape already used
/// twice in this remediation, carrying one more field - not a new ledger, not a change to the shared
/// <see cref="IIdempotencyStore"/> contract, and not a cross-store transactional rewrite (god's own
/// recorded decision: this codebase has no shared transactional substrate across the claim store,
/// orphan ledger, provenance ledger, link store and audit sink, and building one is Kemi-scale -
/// the same proportionality standard BIO-VIS-002's own outbox-pattern question was held to).
/// </summary>
internal interface IImportRowProvenanceLedger
{
    Task RecordAsync(string tenantId, string idempotencyKey, string mappingId, string externalKey, ImportRowOutcome outcome, string fingerprint, CancellationToken cancellationToken = default);

    Task<ImportRowProvenanceEntry?> FindAsync(string tenantId, string idempotencyKey, string mappingId, string externalKey, CancellationToken cancellationToken = default);
}
