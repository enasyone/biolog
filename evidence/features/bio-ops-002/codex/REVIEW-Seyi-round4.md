# BIO-OPS-002 independent acceptance — round 4

Verdict: **PASS**

Reviewed remediation `d3c6d95` plus `203b41b` as merged into `feature/BIO-OPS-002` at `9aebfbc`.

## Required-category verdicts

| Category | Verdict |
|---|---|
| Functional | PASS by code/test trace; runtime execution is sandbox-blocked |
| Security | PASS for canonical audit shape and derived actor attribution |
| Tenant isolation | PASS by code/test trace |
| Offline/recovery | PASS under the accepted, narrowly scoped ADR-0003 Decision 8 exception |
| Concurrency | Not applicable to the immutable signed-context selection itself; audit ambiguity uses `ExistsAsync` |
| Regression | PASS by source trace; builder reports 292/292 integration and 211/211 unit |

## Closed findings

- AC01: beta resolves only to its separately configured tenant and fails closed when absent.
- AC02: the real-host integration suite retains a tenant-B attempt to use a tenant-A person identifier and returns the same not-found result as an unknown identifier.
- AC03: the shell browser regression changes visible navigation after role selection; backend permissions remain tenant/actor scoped.
- AC04 shape/order: the endpoint now uses the shared `AuditEvent`, canonical correlation identifier, `internal` classification, complete required fields, a closed detail-key set, and actor attribution derived from the previous role. The web action awaits successful audit delivery before committing the cookie.
- AC05: unknown host and unconfigured beta regressions fail closed.

## Resolved governance gate

The initial round-4 tree at `9aebfbc` did not contain the claimed architecture ruling, so acceptance correctly paused. Commits `7435abf` and `6d6ab93`, merged at `aa1c744`, now add ADR-0003 Decision 8 and contract-freeze snapshot 1.1.11. Decision 8 names exactly this demo-session mechanism, acknowledges that it reopens Decision 7's independent-lifetime boundary, and accepts it as a narrow exception because no authorization decision depends on the record, the token lifetime is bounded, and the audited behavior is already-approved synthetic self-selection.

The exception expressly cannot be reused for real credentials, grants, or other non-synthetic effects. `scripts/check-contract-freeze.py` passes against snapshot 1.1.11.

## Verification

- `bash tests/features/bio-ops-002/BIO-OPS-002.acceptance.sh`: PASS, all AC01–AC05 gates.
- `python3 scripts/check-contract-freeze.py`: PASS, 39 artifacts and 83 future stories.
- Local .NET execution remains unavailable because MSBuild cannot bind its named pipe in this sandbox. Builder evidence reports the focused and full suites passing, but that report is not used as independent runtime proof.
