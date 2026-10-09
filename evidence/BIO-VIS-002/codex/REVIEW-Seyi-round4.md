# BIO-VIS-002 independent re-review round 4 — Seyi

Date: 2026-10-06
Builder commit reviewed: `43d0636` (remediation `b49a46e`)

## Verdict

FAIL — F04 card mis-adoption is closed, but the new ledger only prevents reuse. It deliberately leaves the stranded physical credential Active with no reconciliation or deny path, so the original access-control hazard remains.

## BIO-VIS-002-F05 — ledgered stranded card remains active indefinitely

Severity: High / blocking

The new regression test explicitly proves the pathological result:

- visitor grant fails;
- credential row remains Pending with no card sequence;
- backing card remains `active` because compensation failed;
- ledger records the card and later grants skip it.

Skipping adoption prevents cross-visit aliasing, but it does not revoke, expire, quarantine at the access-decision boundary, or reconcile the active card. `IStrandedCardLedger` is consulted only by future grant selection. No access evaluator, checkout/revoke path, background recovery, or operator endpoint consumes it.

The result is an active physical-access credential that:

- has no Granted credential record/history;
- cannot be reached through visitor checkout/revoke because the Pending row has no sequence;
- is not automatically retried for deactivation;
- can remain usable even though the grant operation returned failure and its audit/final transition did not complete.

This violates AC02's governed temporary credential lifecycle, AC03's revocation guarantee, and AC05's auditable history. Preventing another visit from adopting the card does not neutralize it.

## What is closed

- Card sequence is normally attached early to Pending.
- Triple-failure cards are no longer mis-adopted by later visits.
- Normal and concurrent same-PERSON visits remain isolated.

## Required remediation

The stranded ledger must drive recovery or fail-closed access enforcement, not just grant filtering. Provide a reconciliation path that repeatedly deactivates ledgered cards and records terminal state/audit, or make the card/access-decision layer reject any ledgered card immediately. Prove that the forced attach+compensation+fallback failure scenario cannot leave usable access, and that recovery converges once dependencies resume.

## Evidence

The structural harness passes 60/60. The new unit test itself asserts the stranded card is still `active`; repository-wide search shows the ledger is read only in `GrantAsync` adoption filtering and has no reconciliation/access-deny consumer.
