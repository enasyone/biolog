# BIO-FND-003 Independent Review — Seyi

Date: 2026-09-20  
Reviewed implementation commit: `935524a`  
Reviewed branch head: `9bcced8`  
Verdict: **FAIL**

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 | PASS (structural) | Default Compose resolution contains control MySQL, first tenant MySQL, Redis, RabbitMQ, Azurite and Mailpit. Docker daemon access was denied in this sandbox, so live start was not independently repeated. |
| AC02 | PASS (structural) | `multi-tenant` adds a second MySQL with a different database, published port and named volume; it is absent by default. |
| AC03 | FAIL | Every current service defines a healthcheck, but the runtime wait gate treats `no-healthcheck` as ready. |
| AC04 | FAIL | Named volumes and reset commands are documented, but targeted reset suppresses Docker failures and reports success without cleanup. |
| AC05 | PASS | Compose resolves with synthetic local defaults; independent tracked-path and secret scans are clean. |

## Findings

### BIO-FND-003-QA-01 — readiness gate accepts missing healthchecks

Severity: High  
File: `scripts/wait-for-infra.sh`

The readiness loop explicitly accepts both `healthy` and `no-healthcheck`:

```sh
if [ "$status" != "healthy" ] && [ "$status" != "no-healthcheck" ]; then
```

A controlled Docker shim returned one started service with status `no-healthcheck`; `wait-for-infra.sh --timeout 1` exited `0` immediately. AC03 requires readiness to be gated by healthchecks, so absence of a healthcheck must fail closed, not count as ready. Static validation currently catches the committed Compose shape, but the runtime bootstrap gate independently reports a false success when the health contract is missing.

### BIO-FND-003-QA-02 — targeted reset reports success when Docker cleanup fails

Severity: High  
File: `scripts/infra-reset.sh`

For targeted resets, failures from `docker compose stop`, `docker compose rm`, and `docker volume rm` are all discarded with `|| true`. With a Docker shim that returns failure for every command, this command still exits `0` and prints completion:

```sh
scripts/infra-reset.sh redis
```

This can leave containers or tenant data intact while telling the operator that the volume was removed. AC04's reset workflow must propagate cleanup failure or verify that the target volume no longer exists.

## Passing probes

- Both default and multi-tenant Compose configurations parse.
- Required default services exist.
- Second tenant is profile-gated and uses distinct database, volume and port.
- Every committed service currently declares a healthcheck and named volume.
- Missing service/healthcheck/volume/default credential mutations are rejected by static validation.
- Unknown reset targets and readiness flags fail.
- Secret and machine-path scans are clean.
- Builder suite: 8/8 passed, 1 live test skipped because the Docker daemon is inaccessible.

## Environment limitation

`docker info` failed with permission denied for the local Docker socket. Therefore this review does not claim an independent live container start, health convergence, cross-tenant SQL write/read test, or teardown. The structural and controlled failure-path evidence is sufficient to reproduce the two blocking defects without daemon access.

## Category verdicts

- Functional: FAIL.
- Security: PASS for synthetic credentials and secret scanning.
- Tenant isolation: PASS structurally; live data-plane verification unavailable.
- Offline/recovery: FAIL due reset false success.
- Concurrency: NOT APPLICABLE.
- Regression: FAIL.

Reproduction:

```sh
bash evidence/BIO-FND-003/codex/independent-review.sh
```

Expected review exit status: `1`. No implementation files were changed.
