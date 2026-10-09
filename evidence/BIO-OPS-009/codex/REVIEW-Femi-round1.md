# BIO-OPS-009 — Femi performance/resilience review

**Target implementation:** `2ad57cd`  
**Evidence:** `5b1e3b0`  
**Verdict:** FAIL

The happy-path rehearsal is genuine and the restore checks are useful: three independent runs restored
separate MySQL dumps and Azurite data into fresh containers, verified exact pre-backup rows/blob hashes,
and removed the created resources. The recorded recovery time objective (RTO) values are 32, 32 and
31 seconds. The explicit non-production-service-level-objective disclaimer is appropriate.

## High — FEMI-OPS009-01: failed Azurite snapshot can leave the source service stopped

`backup-azurite.sh` stops a running Azurite container, then executes the volume archive under
`set -e`. If `docker run ... tar` fails (disk full, image/runtime failure, permission failure, or
interruption), the shell exits before reaching `docker start`. There is no `EXIT`/signal trap or other
finally-style restart. The metadata-consistency fix therefore introduces an avoidable outage exactly
when a backup fails.

Required remediation: install cleanup/restart handling before stopping the container, preserve the
archive command's failure status, and prove through failure injection that Azurite is restarted after
an archive failure. A restart-readiness failure must also return failure rather than only warn.

## High — FEMI-OPS009-02: the result does not measure achieved RPO

The result labels a timestamp taken *before* the sequential MySQL and Azurite backups as
`rpo_reference_at`, then reports a hard-coded count of one deliberately lost item per store. That
describes test data loss, but it does not calculate recovery point objective (RPO) time. The stores
have different actual recovery points because their backups complete at different times; their
per-backup manifest timestamps are written but ignored by the result.

Required remediation: record the actual per-store recovery-point timestamp (or a deliberately defined
consistent common point), calculate each observed RPO duration through disaster declaration, and keep
the exact lost-row/blob verification as supporting impact evidence. Add an executable assertion over
the derived values rather than hard-coded loss counts alone.

Independent checks:

- Structural/regression suite: 10/10 PASS, live Docker check skipped because this managed environment
  cannot reach the Docker daemon.
- Orchestrator unrestricted run: 11/11 PASS including live end-to-end rehearsal and clean teardown.
- Three retained live runs: verification PASS; RTO 32/32/31 seconds.
- Metadata JSON: valid.

AC01-AC03 pass on the exercised happy path. AC04 and failure resilience fail until the findings above
are remediated.
