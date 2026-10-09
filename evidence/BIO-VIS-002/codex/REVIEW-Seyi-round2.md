# BIO-VIS-002 independent re-review — Seyi

Date: 2026-10-06
Builder commit reviewed: `c9e84b2` (remediation `48e43c4`)

## Verdict

FAIL — the original failure ordering is improved, but the new adoption mechanism can attach one active card to two distinct visitor records for the same PERSON. This creates a new cross-visit integrity and access-control defect.

## BIO-VIS-002-F03 — adoption is not correlated to the visitor/grant attempt

Severity: High / blocking

`GrantAsync` searches BIO-CRD-005 using only `(PersonId, CardClass)` and adopts the first `issued` or `active` card. The card carries no visitor-record or pending-credential correlation. The lock is keyed by `(TenantId, VisitorId)`, not PERSON, so it cannot serialize distinct visitor records for the same PERSON.

Reproduction scenario:

1. PERSON P has checked-in visitor record V1. Grant creates credential C1 backed by active card sequence S.
2. The same PERSON P has another checked-in visitor record V2 (sequential or concurrent visits are not forbidden by this story).
3. Grant for V2 finds no active credential for V2, creates a Pending C2, then `ListAsync(P, visitor_temporary)` returns S.
4. The service adopts S and marks C2 Granted. C1 and C2 now claim the same card.
5. Checkout/revoke of either visit deactivates S, silently invalidating the other still-Granted credential.

The same race can occur concurrently because V1 and V2 use different per-visitor locks.

The new tests prove adoption after a partial failure, but they do not distinguish a stranded card from a legitimately active card belonging to another visit. Without a durable attempt/visitor correlation on the card or an ownership lookup that excludes already-linked cards, safe adoption is impossible.

## Review of prior findings

- F01 original ordering: materially improved by Pending-before-card and compensation.
- F02 checkout ordering: closed; revoke occurs before checkout and an already-checked-out retry can finish a stranded revoke.
- F03 blocks acceptance because the F01 recovery path introduces shared-card aliasing across visits.

## Required remediation

Correlate issuance/adoption to the exact visitor credential attempt using a durable idempotency/correlation key stored on both sides, or maintain an authoritative mapping that proves the card is unowned/owned by this same Pending record before adoption. Serialize at the PERSON/card-class scope where necessary. Add sequential and concurrent tests with two visitor records for one PERSON and prove they cannot share a card or deactivate one another's credential.

## Evidence

`bash tests/features/bio-vis-002/BIO-VIS-002.test.sh` passes 50/50, but contains no same-PERSON/different-visitor adoption test. Source inspection confirms `ListAsync(visitor.PersonId, CardClass)` and a per-visitor lock are the only selection/serialization criteria.
