# BIO-INT-002 round-six review

Verdict: FAIL — architecture checkpoint required

Reviewed `e8ba907` / `2b44b37`.

## Blocking finding F06 — released claims forget the fingerprint while provenance survives

The provenance-before-link reorder closes F05 for an identical retry. However, `IIdempotencyStore.Release` deletes the claim and its request fingerprint, while `IImportRowProvenanceLedger` retains the old outcome keyed only by tenant, idempotency key, mapping, and external key.

Deterministic failure sequence:

1. Request A creates a PERSON, records orphan and `Created` provenance, then the link write fails.
2. The catch releases the idempotency claim, deleting request A's fingerprint. Provenance survives by design.
3. Request B reuses the same key/mapping/external key with different PERSON field content. Because the claim was deleted, it wins a fresh claim and no fingerprint-reuse error is possible.
4. The replay branch finds request A's provenance, skips classification/content comparison, and creates the link to request A's PERSON. Request B's changed content is silently ignored and reported as `Created`.

This contradicts AC05's different-content key-reuse rejection and reintroduces content-blind adoption through the provenance replay path. The existing fingerprint tests cover only a completed/in-flight claim, not reuse after release with surviving recovery state.

Because this is another consecutive failure in the same create–orphan–provenance–link–audit recovery chain, the recorded checkpoint applies: stop adding local recovery patches and ask whether the chain needs one atomic/transactional write boundary or a durable failed-attempt record that retains the fingerprint with its recovery state.

Independent structural harness: 67 passed, 0 failed; it does not exercise changed-content reuse after a post-provenance link failure.
