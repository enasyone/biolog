# BIO-OPS-009 independent review — round 1

**Verdict: FAIL** at implementation commit `2ad57cd` / evidence commit `5b1e3b0`.

The unrestricted happy-path rehearsal is genuine and exercises separate MySQL dumps, actual volume
loss, new standalone restore containers, Azurite blob hashing, post-backup loss, and local RPO/RTO.
Two High assurance gaps remain.

## Findings

### SY-OPS009-01 — High — database restore integrity checks only primary keys

Phase 7 queries only `SELECT id` from each restored table. It never compares the restored `name` or
`label` values with the pre-backup values. A dump/restore that preserves keys but corrupts, truncates
or replaces every payload value still produces exactly `1,2,` and `1,2,3,` and reports PASS. This
does not support the evidence claim that every row matches exactly or adequately verify independent
database recovery.

Required remediation: capture a deterministic pre-backup logical digest or canonical full-row
export for each database, compare it byte-for-byte/logically after restore, and add a mutation test
showing a changed non-key value makes verification fail.

### SY-OPS009-02 — High — failed/interrupted rehearsals leak restore resources and may reuse volumes

The script has no `EXIT`/`INT`/`TERM` trap. `fail()` calls only `compose down -v`, which cannot remove
the standalone restore containers or their separately named volumes. Any failure or interruption
after the first restore therefore leaves containers/volumes behind; fixed container names then block
the next run. Both restore helpers also call `docker volume create` without rejecting a pre-existing
name—Docker treats that as success—so their “always fresh” guarantee is false and stale data can be
silently reused.

Required remediation: install idempotent signal/exit cleanup before resource creation; track and
remove every standalone container/volume on all paths; refuse pre-existing container/volume names
before restore. Add a deterministic injected-failure test after the first restore and assert no
resources remain, plus a pre-existing-volume negative test.

## Reproduction

```sh
python3 evidence/BIO-OPS-009/codex/adversarial-rehearsal-check.py
```

Result: 0 passed, 5 failed.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 independent database restore | FAIL — restore runs, but payload integrity is not verified. |
| AC02 restore to new containers | FAIL — happy path uses new names, but helpers can reuse existing volumes and failure paths leak resources. |
| AC03 object evidence restore | PASS — restored blobs are checked against pre-backup SHA-256 and post-backup blob absence is verified. |
| AC04 local RPO/RTO | PASS — observed values and non-production-SLO disclaimer are recorded. |

## Category verdicts

- Functional/data integrity: FAIL
- Security: NOT APPLICABLE
- Tenant isolation: PASS for separate database artifacts/restores
- Offline/recovery: FAIL
- Concurrency: NOT APPLICABLE
- Regression: FAIL (failure cleanup/fresh-volume guarantees)
