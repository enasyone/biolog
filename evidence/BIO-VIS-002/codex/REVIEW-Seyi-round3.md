# BIO-VIS-002 independent re-review round 3 — Seyi

Date: 2026-10-06
Builder commit reviewed: `9c8b56f` (remediation `0e66987`)

## Verdict

FAIL — normal sequential/concurrent grants are now isolated, and cancellation recovery is improved, but stranded-card adoption still cannot identify the visitor attempt that created an unowned card. The exact recovery path can therefore still cross-link visits.

## BIO-VIS-002-F04 — “unowned” stranded cards remain ambiguous

Severity: High / blocking

The new ownership check calls `FindByCardAsync`, which can find an owner only when a credential row already contains `CardSequenceNumber`. A Pending row is created with `CardSequenceNumber = null`; the sequence is written only when Pending transitions to Granted or Failed.

If a card becomes issued/active and recovery cannot write the Failed transition (the implementation explicitly catches and suppresses this), the system contains:

- Pending credential for visitor V1 with no card sequence;
- issued/active card S for the same PERSON/card class;
- no row discoverable by `FindByCardAsync(..., S)`.

A later grant for visitor V2 of the same PERSON sees S as `owner is null` and adopts it. The PERSON-scoped lock serializes the grants but cannot resolve ownership; it does not correlate S to V1. V2 can therefore take V1's stranded card, and a later V1 recovery can no longer safely identify its own artifact.

This is also possible at the process boundary between card activation and local transition whenever the backing card store outlives the local credential write boundary. The source documentation itself acknowledges that the Pending/active double-failure state may remain.

## What is closed

- Normal same-PERSON/different-visitor sequential and concurrent grants no longer share an already-owned card.
- Caller cancellation no longer skips the compensation path because recovery uses `CancellationToken.None`.
- Checkout ordering remains fixed.

## Required remediation

Persist a durable issuance/adoption correlation before or atomically with card creation, and expose that correlation from the card side so an unowned-looking card can be matched only to its exact Pending attempt. Do not adopt merely because `FindByCardAsync` returns null. Add a test that leaves V1 Pending with null sequence plus active card S (compensation and Failed-write both fail), then grants V2 and proves V2 cannot adopt S.

## Evidence

The structural harness passes 56/56 but covers only normal owned-card sequential/concurrent cases and cancellation. `VisitorCredential.CreatePending` still stores a null sequence, `FindByCardAsync` searches that nullable field, and the adoption condition explicitly accepts `owner is null`.
