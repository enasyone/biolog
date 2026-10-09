# BIO-VIS-002 independent review — Seyi

Date: 2026-10-06
Builder commit reviewed: `45f4736` (implementation `7a37ac9`)

## Verdict

FAIL — cross-feature side effects can leave live visitor cards without a credential record, or checked-out visitors with still-active credentials. AC02, AC03, and AC05 are not failure-atomic.

## BIO-VIS-002-F01 — grant failure strands an activated card

Severity: High / blocking

`GrantAsync` performs these operations in order:

1. BIO-CRD-005 `IssueAsync`
2. BIO-CRD-005 `ActivateAsync`
3. visitor-credential audit write
4. local credential repository create

If the audit sink fails durably, or the repository create fails after the audit, the method throws after the backing card is already Active. No credential row exists, so `GetActiveForVisitorAsync` cannot find or revoke it. A retry issues and activates another card because the duplicate check consults only the missing local credential row.

There is no compensation, pending state, shared transaction/outbox, or reconciliation path. Existing tests cover only the all-success path.

## BIO-VIS-002-F02 — checkout failure can leave active access after the visit ends

Severity: High / blocking

`CheckOutAsync` first commits BIO-VIS-001 checkout, then looks up and revokes the credential. If card lookup/deactivation, audit, or credential replacement fails afterward, the visitor remains checked out while the credential/card remains Granted/Active.

Retry is not a recovery path: BIO-VIS-001 checkout is one-way and its service rejects a second checkout because the visitor is no longer `checked-in`. The stranded active credential therefore cannot be completed through this orchestrator.

## Reproduction basis

- `bash tests/features/bio-vis-002/BIO-VIS-002.test.sh` reports 42/42 PASS, but its atomicity check only greps that audit precedes the local repository commit; it does not cover the earlier external card/visitor mutations.
- `VisitorCredentialServiceTests` contains no audit/card/repository failure injection for grant, revoke, or checkout orchestration.
- The operation ordering above is directly visible in `VisitorCredentialService.cs`.

## Required remediation

Use an explicit durable workflow/state machine with compensation and reconciliation, or a shared transactional/outbox boundary where supported. At minimum:

- grant must recover or deactivate any issued/activated card when later audit/local persistence fails, and retries must not issue duplicates;
- checkout must make credential/card revocation recoverable after visitor checkout succeeds, including retries when the visitor is already checked out;
- add failure-injection tests at every boundary and prove convergence to one consistent terminal state.

## Other categories

- Functional: FAIL (AC02/AC03).
- Audit/data integrity: FAIL (AC05).
- Security: FAIL due to stranded active physical-access credentials.
- Tenant isolation: PASS by inspection; repository reads are tenant filtered.
- Concurrency: per-visitor locking is present, but it does not solve partial failure across stores.
- UI/regression: structural harness passes; no UI redesign was found. These do not override the blocking workflow failures.
