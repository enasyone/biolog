# BIO-OPS-009 independent review — round 2

**Verdict: PASS** at remediation commit `2e34975` / evidence commit `6ca0e00`.

## Remediation

- **SY-OPS009-01 closed:** the rehearsal captures complete ordered `id/name` and `id/label`
  snapshots before backup and compares restored full rows exactly. A same-ID/non-key mutation is now
  discriminated by regression evidence.
- **SY-OPS009-02 closed:** a single `EXIT` cleanup removes all three standalone restore containers,
  all three restore volumes and the compose project; `INT`/`TERM` explicitly terminate so `EXIT`
  runs. Both restore helpers reject pre-existing volume names. The unrestricted failure-after-Phase-6
  test reports non-zero exit and no residual restore resource.
- Femi's adjacent restart/RPO findings are also closed: Azurite restart is attempted even after
  archive failure, and per-store RPO durations derive from each backup manifest timestamp.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 independent control/tenant restore | PASS — separate dumps/restores and exact full-row comparison. |
| AC02 restore to new local containers | PASS — fresh-volume refusal, standalone containers, documented procedure and all-path cleanup. |
| AC03 photo/object restore | PASS — restored blob SHA-256 values match and post-backup blob absence is verified. |
| AC04 local RPO/RTO | PASS — per-store manifest-derived RPO and measured RTO with explicit non-production-SLO disclaimer. |

## Independent checks

- `python3 evidence/BIO-OPS-009/codex/adversarial-rehearsal-check.py` — 5/5.
- Bash 3.2 syntax checks for rehearsal/backup/restore scripts — PASS.
- `git diff --check` — PASS.
- Exact unrestricted foundation suite reported by god: 19/19, including live failure cleanup and
  failed-archive restart tests; clean teardown confirmed.

## Category verdicts

- Functional/data integrity: PASS
- Security: NOT APPLICABLE
- Tenant isolation: PASS
- Offline/recovery: PASS
- Concurrency: NOT APPLICABLE for the isolated rehearsal project
- Regression: PASS
