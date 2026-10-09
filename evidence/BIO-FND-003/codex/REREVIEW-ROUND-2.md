# BIO-FND-003 Independent Re-review — Round 2

Date: 2026-09-20  
Reviewed fix commit: `fba5227`  
Reviewed branch head: `c6cf432`  
Verdict: **FAIL**

## Prior findings

- QA-01: **CLOSED**. `no-healthcheck` is no longer accepted as ready; the controlled readiness test now times out/fails.
- QA-02 original reproduction: **CLOSED**. When every Docker cleanup command fails, targeted reset returns non-zero and does not print completion.

## New blocking finding

### BIO-FND-003-QA-03 — volume inspection errors are mistaken for an absent volume

Severity: High  
File: `scripts/infra-reset.sh`

`remove_volume_if_present` currently uses:

```sh
if docker volume inspect "$volume" >/dev/null 2>&1; then
  docker volume rm ...
fi
return 0
```

Every non-zero inspection result takes the same success path. A controlled Docker shim allowed container stop/removal but made `docker volume inspect` fail with `permission denied`; `scripts/infra-reset.sh redis` exited `0` and printed `done` without removing or confirming absence of the volume.

The function comment explicitly says inspection failures other than “no such volume” must be fatal, but the implementation does not distinguish the error. It must capture the inspection diagnostic (or otherwise verify absence) and return non-zero for daemon, permission, transport, or other inspection failures.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 required services | PASS structurally |
| AC02 multi-tenant isolation topology | PASS structurally |
| AC03 healthchecks/readiness | PASS |
| AC04 named volumes/reset | FAIL |
| AC05 synthetic local credentials | PASS |

## Regression summary

- Independent suite: all probes passed except the inspection-error reset boundary.
- Builder suite: 11/11 passed, 1 live test skipped because Docker socket access remains unavailable in the sandbox.
- Live container claims were not independently made.

## Category verdicts

- Functional: FAIL.
- Security: PASS.
- Tenant isolation: PASS structurally; live verification unavailable.
- Offline/recovery: FAIL.
- Concurrency: NOT APPLICABLE.
- Regression: FAIL.

Reproduction:

```sh
bash evidence/BIO-FND-003/codex/independent-review.sh
```

Expected exit status: `1`. No implementation files were changed.
