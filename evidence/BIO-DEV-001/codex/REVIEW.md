# BIO-DEV-001 independent review

**Verdict: FAIL**

Reviewed implementation commit `78c00cc` and builder-evidence commit `9e61e7f` against BIO-DEV-001-AC01 through AC04 and ADR-0002/0003.

## Findings

### QA-01 — High — the shipped HTTP registry has no successful path (AC01)

`TenantContextMiddleware.InvokeAsync` unconditionally assigns `accessor.Context = null`. Every register, get, and status endpoint then calls `DeviceService.RequireTenant`, so every request returns `tenant_context_missing`. The integration tests intentionally assert only this failure. Consequently the delivered API cannot register or retrieve any device and provides no positive AC01 behaviour through the composed host.

Reproduce:

```sh
grep -n 'accessor.Context = null' api/Biolog.Api/Tenancy/TenantContextMiddleware.cs
grep -n 'RequireTenant' api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs
```

### QA-02 — High — audit failure leaves an unaudited committed status change (AC03)

`ChangeStatusAsync` commits `_repository.UpdateStatusAsync` before awaiting `_auditSink.RecordAsync`. If the audit sink throws, the caller receives failure but the new status and incremented version remain in the repository. Retrying with the original expected version then conflicts. This violates AC03's requirement that status changes are audited; an audit outage creates an actual status change without an audit event. The implementation documentation explicitly confirms this behaviour.

Reproduce:

```sh
grep -nE 'UpdateStatusAsync|RecordAsync' api/Biolog.Api/Features/DeviceRegistry/DeviceService.cs
grep -n 'already committed' docs/DEVICE-REGISTRY.md
```

## Category verdicts

| Category | Verdict | Evidence |
|---|---|---|
| Functional | FAIL | QA-01: all composed-host endpoints fail; no positive registry path |
| Security | FAIL | QA-02: audit outage permits an unaudited status mutation |
| Tenant Isolation | PASS | Service derives tenant from context and repository rechecks record tenant |
| Offline / failure recovery | FAIL | QA-02: audit failure leaves divergent mutation and audit state |
| Concurrency | PASS | Compare-and-set update rejects competing writers |
| Regression | PASS | Builder structural suite passed 25/25; .NET execution was environment-blocked by testhost socket permission |

## Commands run

- `bash tests/foundation/BIO-DEV-001.test.sh` — 25 passed, 0 failed.
- `dotnet test ... --no-restore` for unit and integration suites — both aborted before test execution because the sandbox denied the test host's loopback socket bind.
- `bash evidence/BIO-DEV-001/codex/independent-review.sh` — expected 0 passed, 2 failed.

Both High findings block acceptance.
