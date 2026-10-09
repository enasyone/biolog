# BIO-VIS-002 independent re-review — Seyi round 5

Date: 2026-10-06
Builder commit reviewed: `7e1bfc4` (remediation `70cbf3d`)

## Verdict

FAIL — reconciliation works when explicitly invoked or when the same PERSON later receives another grant, but there is no guaranteed trigger. A person who never returns can retain the stranded active card indefinitely.

## BIO-VIS-002-F06 — opportunistic reconciliation is not eventual recovery

Severity: High / blocking

`ReconcileStrandedCardsAsync` can successfully deactivate and resolve ledger entries. However, production wiring invokes it only at the start of `GrantAsync` for that same PERSON. The method is internal and has no endpoint, startup recovery, timer/job, operator workflow, checkout hook, or access-decision integration.

Therefore the exact F05 state still has this valid execution:

1. grant fails after activating card S;
2. compensation fails and S is ledgered;
3. no later grant occurs for that PERSON;
4. S remains Active indefinitely, with no Granted credential record, expiry enforcement, audit-complete lifecycle, or automatic retry.

The builder's disclosed limitation confirms this behavior. A future unrelated grant is not a reliable recovery mechanism for a failed physical-access mutation. The story's user requirement includes automatic expiry, which cannot be applied to this orphan because the Pending row has no card sequence and no consumer periodically processes the ledger.

## What is closed

- The reconciliation routine itself correctly retries deactivation and retains unresolved entries on repeated failure.
- A later grant for the same PERSON triggers reconciliation before proceeding.
- No later visit can adopt the stranded card.

## Required remediation

Provide a guaranteed recovery trigger within the supported runtime: startup reconciliation, a scheduled/background worker, an operator-visible recovery command/endpoint, or a fail-closed access/card expiry mechanism. Prove that a stranded card converges to non-active without requiring another grant for the same PERSON. If infrastructure is intentionally unavailable, the story remains blocked rather than acceptance-ready because active access is not eventually neutralized.

## Evidence

The structural harness passes, and the new tests prove explicit/opportunistic calls. Repository-wide search confirms the only production call site is `GrantAsync`; no autonomous or operator-triggered path exists.
