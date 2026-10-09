# BIO-VIS-002 — Implement visitor check-in/out and temporary credentials

**Owner:** Tunde — Identity & Tenant Engineer
**Dependencies:** BIO-VIS-001 (visitor preregistration and host approval), BIO-CRD-005 (physical card issue transaction), both DONE.

## User story

As reception/security, I need visitor arrival, identity evidence, temporary credential issuance and automatic expiry.

## A note on real scope: AC01 is already satisfied by BIO-VIS-001

BIO-VIS-001 already fully implements check-in/out: `VisitorRecordService.CheckInAsync` enforces
`HostApprovalStatus == Approved`, the preregistration has not expired, the watchlist screening
result is not `Flagged`, and every one of `VisitorTenantConfig`'s four configurable `Require*`
fields — this already satisfies AC01's wording ("check-in requires configured
approvals/evidence") exactly. `docs/features/bio-vis-001/README.md`'s own "Deliberately out of
scope" section explicitly names this story as the one that would own real `BIO-CRD-00x`
integration for `TemporaryCredentialIssued`. This story's real, new work is therefore AC02
(a genuine temporary-credential object with its own expiry and scope, backed by a real BIO-CRD-005
card), AC03 (check-out must actually revoke it), AC04 (a proper Overstay-vs-Expired split BIO-VIS-001's
own evaluator collapses into one bare `Expired` value) and AC05 (a combined history read).

## AC → evidence map

| AC | Statement | Where it lives |
| --- | --- | --- |
| AC01 | Check-in requires configured approvals/evidence. | Already satisfied by BIO-VIS-001's own `CheckInAsync` — see note above. |
| AC02 | Temporary credential has expiry and scope. | `Domain/VisitorCredential.cs`, `VisitorCredentialService.GrantAsync` |
| AC03 | Check-out revokes/ends credential. | `VisitorCredentialService.CheckOutAsync` (this story's own orchestrator) |
| AC04 | Overstay/expired status is visible. | `Domain/VisitorOverstayEvaluator.cs` |
| AC05 | Visitor history is auditable. | `VisitorCredentialService.GetHistoryAsync` |
| AC06 | `/visitors` connected through `VisitorService`. | Takes over `POST /{visitorId}/check-out` from BIO-VIS-001's own (narrowed) mapping — see below. |

## Design decisions and scope boundaries

### Why a credential requires a linked PERSON

BIO-VIS-001 deliberately supports a "lead" visitor with a null `PersonId` — no PERSON needs
minting just to be preregistered. BIO-CRD-005's own `CardIssueService.IssueAsync` REQUIRES a real,
existing `PersonId` — a physical card has nowhere to attach without one. Rather than silently
create a credential with no backing card, or relax BIO-CRD-005's own requirement, `GrantAsync`
treats a null `PersonId` as a hard precondition failure (`VisitorHasNoLinkedPerson`) before it ever
calls BIO-CRD-005's own gateway. A visitor must be linked to a PERSON (at preregistration or later,
out of this story's own scope) before a temporary credential can be granted.

### AC02: a genuinely richer object than either dependency's own fields

BIO-VIS-001's `TemporaryCredentialRequested`/`TemporaryCredentialIssued` are bare booleans with no
expiry or scope of their own. BIO-CRD-005's own `CardIssue` has NO expiry or scope field at all —
just a lifecycle status and provenance. `VisitorCredential` is this story's own aggregate, carrying
both: `ExpiresAt` (validated at grant time to be no later than the visit's own `ExpiresAt` — a
credential cannot outlive the visit it was granted for) and `Scope` (a small, synthetic taxonomy —
`general`/`escorted`/`restricted_area` — no AC enumerates an exact list, so this story defines one
rather than leave the field a bare string). Granting calls BIO-CRD-005's own `IssueAsync` then
`ActivateAsync` to obtain a REAL, Active backing card before the `VisitorCredential` row is ever
created — never a credential record with no genuine card behind it.

### AC03: this story's own orchestrating checkout, and why it edits BIO-VIS-001's own endpoint mapping

BIO-VIS-001's own `CheckOutAsync` has no knowledge of this story's own `VisitorCredential`
aggregate, and this story cannot reach back into BIO-VIS-001's own internals to make it credential-
aware (wrong dependency direction). The correct shape: this story owns a NEW orchestrating
`VisitorCredentialService.CheckOutAsync` that (1) calls through to BIO-VIS-001's own real
`CheckOutAsync` via this story's own `IVisitorRecordGateway` — never reimplementing its
validation — then (2) revokes whichever credential is currently `Granted` for that visitor, if
any, deactivating its backing BIO-CRD-005 card too. Both steps are audited under this story's own
audit sink.

A visitor must never be checked out through two different code paths that disagree about
credential revocation, so this story takes over the real tenant-scoped
`POST /api/v1/visitors/{visitorId}/check-out` route mapping from BIO-VIS-001's own `VisitorEndpoints.cs`
— the identical disclosed-handoff shape BIO-TRN-001→BIO-TRN-002 and BIO-ATT-001→BIO-ATT-002 already
established this session, narrowed to the smallest possible diff:

- `api/features/bio-vis-001/Api/VisitorEndpoints.cs` — the `/{visitorId}/check-out` route mapping
  and its handler method were removed, not left duplicated. Every other route (preregister/read/
  approve/deny/check-in/evidence-capture/config/the three headerless demo bridge routes) is
  untouched, including the application-service method `VisitorRecordService.CheckOutAsync` itself
  — this story calls through to it (via the gateway adapter), it is never reimplemented or deleted.

**The headerless demo bridge's own separate `/check-out-visitor` route is deliberately
untouched.** `VisitorDemoBridgeService.CheckOutNextDemoVisitorAsync` manipulates
`VisitorRecord.CheckOut()` directly against the Singleton repository (it never goes through the
Scoped `VisitorRecordService`, since a headerless UI POST resolves no tenant context) — a
completely separate code path from the real endpoint this story takes over. The demo UI has no
action that grants a temporary credential in the first place, so there is nothing for that path to
revoke; fixing it would require either giving the headerless bridge knowledge of this story's own
credential repository too, or a far larger handoff, for a capability the demo UI never exposes.
Documented here, not silently glossed over.

`web/features/visitors/live.ts` is UNCHANGED — its three ports already call exactly these routes
(BIO-UX-005/010's own scaffolding); this story just re-points which module answers ONE of them, the
same "no redesign, just re-pointed" precedent BIO-TRN-002/BIO-ATT-002 established.
`web/next.config.mjs`'s own `/api/v1/visitors/*` proxy rule (added by BIO-VIS-001) already covers
the re-pointed route unchanged.

### AC04: Overstay vs Expired, computed on top of BIO-VIS-001's own fields

BIO-VIS-001's own `VisitorStatusEvaluator.EffectiveStatus` collapses both "checked in, now past
`ExpiresAt`" and "never arrived, preregistration lapsed" into the same bare `Expired` value — the
real gap this story closes. `VisitorOverstayEvaluator.EffectiveStatus` is a pure, read-time-only
function (same discipline as every evaluator this session) operating on this story's own
`VisitorRecordView` projection (never mutating or re-deriving BIO-VIS-001's own stored record):
`Overstay` when the visitor was checked in and is now past expiry (they arrived; something needs
to happen about them still being here), `Expired` when they never arrived at all, and `CheckedOut`
unconditionally once departed (a departed visitor's record never "expires" retroactively, the same
rule BIO-VIS-001's own evaluator already applies).

### AC05: history is a projection, not a new persisted aggregate

`GetHistoryAsync` combines BIO-VIS-001's own lifecycle facts (read via `IVisitorRecordGateway`) with
every `VisitorCredential` ever granted for that visitor record (this story's own append-only
read), each still carrying its own grant/revoke metadata (who, when, why). Kept deliberately
simple — a DTO projection, not a new event-sourced timeline.

### Cross-story gateway pattern

This project has zero `ProjectReference`s (`scripts/check-module-project-references.sh`). Two
narrow interfaces — `IVisitorRecordGateway`, `ICardIssuanceGateway` — are implemented as private
nested adapter classes in `api/Biolog.Api/Modules/BioVis002Module.cs`, resolving BIO-VIS-001's own
`VisitorRecordService` and BIO-CRD-005's own `CardIssueService` (both `internal`, reachable via
each project's own `InternalsVisibleTo Biolog.Api`) directly — the same pattern every story this
session uses. Each adapter translates the wrapped feature's own domain error type
(`Biolog.Features.Visitors.Domain.VisitorError` / `Biolog.Features.CardIssuance.Domain.CardIssueError`)
into this feature's own `VisitorCredentialError` (identical Code/Status/Message/Detail) — without
this translation, a sibling feature's own validation failure (e.g. "not checked in", "another card
already active") would surface as an unhandled 500 instead of the correct 4xx problem response.

## Post-review remediation (BIO-VIS-002-F01/F02)

Seyi's independent review FAILed commit `45f4736` (`evidence/BIO-VIS-002/codex/REVIEW-Seyi.md`)
with two High/blocking cross-feature atomicity defects. Both are fixed on top of that commit; the
design below is a deliberate, stronger fix than Seyi's own stated minimum in both cases, not
merely the smallest change that satisfies the literal wording.

### F01 — grant failure strands an activated card

The original `GrantAsync` called BIO-CRD-005 `IssueAsync`+`ActivateAsync` BEFORE writing anything
local. A failure in the final local write (the audit write, or the repository create) therefore
left a genuinely Active physical-access card with no local row to find or revoke it, and a retry's
duplicate check — consulting only that now-missing local row — would blindly issue a SECOND card.

Fixed by giving `VisitorCredential` a third status, `Pending` (`Domain/VisitorCredentialEnums.cs`),
sitting before `Granted` in its lifecycle:

1. `GrantAsync` now persists a `Pending` anchor (`VisitorCredential.CreatePending`) BEFORE calling
   BIO-CRD-005 at all, still under the same per-visitor lock held for the whole method.
2. Before calling `IssueAsync`, it now checks BIO-CRD-005's OWN state for this `(person, card
   class)` via a new `ICardIssuanceGateway.ListAsync` (wraps BIO-CRD-005's own already-existing
   `CardIssueService.ListAsync` — no change needed on that side) and adopts an already-issued or
   already-active card from a prior partial failure instead of minting a new one. This is the
   concrete fix for "retries must not issue duplicates" — the old duplicate check's blind spot was
   exactly the local row this failure mode deletes.
3. On a CONFIRMED final-commit failure (verified via the same `ExistsAsync`-on-throw trick this
   codebase already uses for an ambiguous audit write — re-reading the row before assuming the
   write failed), `GrantAsync` attempts a best-effort compensating `DeactivateAsync` on the card and
   moves the local anchor to a terminal `Failed` status carrying the card's identifiers
   (`VisitorCredentialService.MarkFailedBestEffortAsync`) — never left dangling `Pending`.

**Disclosed residual risk.** True two-phase-commit across two independent in-memory stores is out
of scope. If BOTH the compensating deactivate AND the `Failed`-mark write themselves fail (a double
failure), the local anchor can be left stuck `Pending` while the card is genuinely Active and
un-recorded. Even then, the NEXT grant attempt's own pre-issue `ListAsync` check (step 2) still
finds and adopts that card rather than minting a third one — the guarantee Seyi actually asked for
holds even in this double-failure case, though full local bookkeeping may not converge for it.
Proven by `VisitorCredentialServiceTests`: `GrantAsync_adopts_an_already_active_card_from_a_prior_partial_failure_instead_of_issuing_a_second_one`,
`GrantAsync_adopts_and_activates_a_stranded_issued_but_not_yet_active_card`,
`GrantAsync_when_the_final_local_commit_fails_after_the_card_is_active_the_card_is_compensated_and_the_record_converges_to_failed`,
`GrantAsync_a_fresh_attempt_after_a_compensated_failure_succeeds_with_a_new_card`.

### F02 — checkout failure can leave active access after the visit ends

The original `CheckOutAsync` committed BIO-VIS-001's own checkout FIRST, then revoked the
credential. A failure in the revoke step left a visitor checked-out with still-active physical
access, and BIO-VIS-001's own checkout being one-way meant no retry could ever reach the revoke at
all.

Fixed by REORDERING rather than merely making the old order "recoverable" (Seyi's own stated
minimum) — a deliberately stronger fix: revoke now happens FIRST, while the visitor is still
checked-in (a safe, retryable state), and BIO-VIS-001's own checkout is the final,
point-of-no-return step. A visitor is never checked-out while still holding active access, not even
transiently — if the revoke throws, BIO-VIS-001's checkout is never even attempted, and the visitor
simply stays checked-in with access intact (fail-closed, trivially retryable — the opposite of the
original bug's fail-open mode). The whole orchestrator is also now idempotent on retry: if the
visitor is already checked out (a prior run of this same orchestrator, or BIO-VIS-001's own direct
endpoint with no credential ever granted), it is a no-op success, never a second call into
BIO-VIS-001's one-way checkout.

Proven by `VisitorCredentialServiceTests`:
`CheckOutAsync_when_revoke_fails_the_underlying_visitor_checkout_is_never_called_and_the_visitor_stays_checked_in`,
`CheckOutAsync_retried_after_the_revoke_failure_is_fixed_succeeds_and_reaches_a_consistent_terminal_state`,
`CheckOutAsync_called_again_after_the_visitor_is_already_checked_out_is_an_idempotent_no_op`.

## Post-review remediation round 3 (BIO-VIS-002-F03)

Commit `c9e84b2` (the F01/F02 remediation above) was independently re-reviewed and got TWO NEW
High/blocking findings, both labelled "F03" by their respective reviewers — different defects, same
label. Both are fixed on top of that commit.

### F03 (Seyi, `REVIEW-Seyi-round2.md`) — card adoption was not correlated to the visitor/grant attempt

The F01 fix's "adopt a stranded card" step searched BIO-CRD-005 by `(PersonId, CardClass)` only, with
no visitor-record or pending-credential correlation at all, and the grant lock was keyed by
`(TenantId, VisitorId)` — which cannot serialize two *different* visitor records for the *same*
person. Person P with two checked-in visits (V1, V2 — entirely legitimate; nothing in this story
forbids a person having more than one open visit) could end up with V1's grant creating credential
C1 against card S, and V2's own later (or concurrent) grant finding that same still-active card S via
`ListAsync` and blindly adopting it too — C1 and C2 would then both claim card S, and
revoking/checking-out either visit would silently deactivate the *other* visit's still-Granted
credential.

Fixed two ways:

1. **Ownership correlation.** A new `IVisitorCredentialRepository.FindByCardAsync(tenantId,
   personId, cardClass, sequenceNumber)` returns whichever existing credential row (in ANY status)
   already references that exact card, across every visitor record for that person. `GrantAsync`'s
   adoption loop now only adopts a candidate card if `FindByCardAsync` returns nothing (genuinely
   unowned/stranded) or returns a row belonging to THIS SAME visitor (its own earlier
   Pending/Failed/Granted attempt) — never a row belonging to a *different* visitor record. If no
   candidate is adoptable, `GrantAsync` falls through to issuing a genuinely new card, exactly as it
   already does when `ListAsync` finds nothing at all. Two visits for the same person ending up with
   two separate cards is the correct, expected outcome, not a bug to prevent.
2. **Person-scoped locking.** The sequential case above needs no lock change at all — the ownership
   check alone closes it. But Seyi also flagged a genuine *concurrent* variant: two simultaneous
   `GrantAsync` calls for V1 and V2 could both pass the ownership check before either has committed
   anything, specifically because they hold different per-visitor locks that never contend. Fixed by
   resolving the grant lock's key from `visitor.PersonId ?? visitorId` (an unlocked pre-read, then
   re-read under the lock for freshness — the same "stale read, re-read under the lock" shape
   `RevokeAsync` already used) instead of the visitor id alone — two visitor records for the same
   person now genuinely serialize their own grant attempts against each other. `RevokeAsync`/
   `CheckOutAsync` keep their existing per-visitor lock key unchanged; they operate on a specific,
   already-known credential/visitor, not a cross-visitor *search*, so widening their lock scope
   would add contention without closing any additional race.

Proven by `VisitorCredentialServiceTests`:
`GrantAsync_two_visitor_records_for_the_same_person_sequentially_never_share_a_card_and_revoking_one_never_touches_the_other`
(sequential — two different cards issued, revoking one leaves the other `active`) and
`GrantAsync_concurrent_grants_for_two_visitor_records_of_the_same_person_never_race_into_sharing_a_card`
(concurrent — a deterministic coordination hook pauses V1 mid-grant while it still holds the
person-scoped lock, and asserts V2's own grant task is still incomplete at that point; without the
fix, V2 would race ahead uncontended since "v1"/"v2" are different keys).

### F03 (Zainab, `SECURITY-REVIEW-ZAINAB.md`) — request cancellation could bypass F01's own compensation

`GrantAsync`'s `catch` block reused the *caller's* `CancellationToken` for its own recovery
work — the `reread` that checks whether the final commit actually landed despite the throw, and
`MarkFailedBestEffortAsync`'s card lookup/deactivate calls. The real in-memory repository calls
`ThrowIfCancellationRequested()`; if the original request were cancelled after `ActivateAsync`
succeeded but before the local commit completed, that `reread` would throw
`OperationCanceledException` from *inside* the catch block, escaping before
`MarkFailedBestEffortAsync` ever ran — the card could be left genuinely `Active` forever with the
local row stuck `Pending` forever.

Fixed by removing the `CancellationToken` parameter from `MarkFailedBestEffortAsync` entirely — every
external call inside it, and the `reread` in `GrantAsync`'s own catch block, now unconditionally uses
`CancellationToken.None`, matching the convention the final local commits already followed. Once a
card may genuinely exist, the recovery path must never be abortable by the same token that may have
just been cancelled.

Proven by `VisitorCredentialServiceTests.GrantAsync_when_cancelled_right_after_activation_still_compensates_and_converges_to_failed`
— cancels the token from inside the fake card gateway's own `IssueAsync` hook (i.e. genuinely after
the lock is held and a card is being issued, not before — an already-cancelled token passed in from
the very start would just fail at the unrelated lock-acquire check and never reach this scenario at
all), combined with the same final-commit fault injection as the F01 test above, and asserts
compensation still runs (card deactivated, record converges to `Failed`) despite the cancellation.

## Post-review remediation round 4 (BIO-VIS-002-F04)

Commit `0e66987` (the F03 remediation above) was independently re-reviewed and got one more
High/blocking finding from Seyi (`REVIEW-Seyi-round3.md`): normal sequential/concurrent grants were
now correctly isolated, and cancellation recovery was sound, but stranded-card adoption still could
not identify *which* visitor attempt created an "unowned-looking" card.

### F04 — "unowned" stranded cards remained ambiguous

`FindByCardAsync` can only attribute a card to a visitor once a credential row durably records that
card's sequence number — but the round-3 fix only ever persisted that number at the *end* of a grant
attempt (on the final `Granted` or `Failed` transition). If a card became genuinely `issued`/`active`
and BOTH the compensating deactivate AND that final local write failed, the system was left with: a
`Pending` row for V1 with `CardSequenceNumber = null`; a genuinely `issued`/`active` card S for the
same person/card class; and no row anywhere `FindByCardAsync(..., S)` could find. A *later* grant for
a different visitor V2 of the same person would see S as `owner is null` and legitimately adopt it —
exactly the cross-visit card-sharing F03 had just closed, reopened through the one failure path F03's
own fix didn't yet cover. This is a sequential scenario (V1's attempt fails completely; time passes;
V2 grants later), so the F03 person-scoped lock does nothing for it.

Fixed two ways:

1. **Persist the correlation as soon as the card's identity is known, not at the end of the
   attempt.** `GrantAsync` now calls a new `AttachCardSequenceAsync` helper immediately once the
   card is confirmed `Active` (covering all three paths: adopt-already-active, adopt-issued-then-
   activate, issue-then-activate) — a dedicated `Pending -> Pending` write (`CardSequenceNumber`
   `null -> value`, nothing else about the row changes) committed *before* the audit write and
   *before* the final `Granted`/`Failed` transition. `InMemoryVisitorCredentialRepository.ReplaceAsync`
   now allows this one additional transition, narrowly scoped to this exact use (see its own code
   comment). If this write itself throws, the same ambiguous-failure verification this codebase
   already uses elsewhere (reread, check whether it actually landed) resolves it before concluding
   it genuinely failed. This alone closes the overwhelming majority of real failures: even if
   everything after this point throws, the row is already correctly attributed.
2. **A last-resort stranded-card ledger for the genuinely pathological remainder.** If *that* write
   is itself confirmed to have failed, AND the compensating deactivate also fails, AND the final
   `Pending -> Failed` fallback write *also* fails — the point at which no local credential row
   anywhere references the card — `MarkFailedBestEffortAsync` records
   `(TenantId, PersonId, CardClass, SequenceNumber)` in a new, dedicated `IStrandedCardLedger`
   (`InMemoryStrandedCardLedger`, Singleton, the same simplicity as every other in-memory store in
   this codebase). `GrantAsync`'s adoption loop consults this ledger *before* `FindByCardAsync` for
   every candidate — once a card is recorded there, it is permanently excluded from adoption by ANY
   future grant for ANY visitor, independent of whatever the credential repository ends up
   containing. See `IStrandedCardLedger`'s own header for exactly where this story stops hardening
   further (it does not chase a backstop for the backstop — an infinite regress is not proportionate
   for a synthetic, in-memory system with no real persistence tier).

Proven by `VisitorCredentialServiceTests.GrantAsync_when_both_local_persistence_and_compensation_are_exhausted_the_card_is_recorded_stranded_and_a_later_visitor_cannot_adopt_it`
— forces both the early attach write and the final `Failed` fallback write to fail persistently
(`FaultInjectingVisitorCredentialRepository.AlwaysThrowOnPendingCardAttach`) and the compensating
deactivate to fail persistently too (`FakeCardIssuanceGateway.AlwaysThrowOnDeactivate`), then asserts:
V1 is left with a genuinely `active`, unreferenced card and a `Pending` row with `CardSequenceNumber`
still `null`; `FindByCardAsync` for that card returns `null` (confirming the exact ambiguity this
round closes); the stranded-card ledger contains it; and a subsequent grant for a different visitor
V2 of the same person issues a genuinely new card rather than adopting it.

## Post-review remediation round 5 (BIO-VIS-002-F05)

Commit `43d0636` (the F04 remediation above) was independently re-reviewed by BOTH Seyi
(`REVIEW-Seyi-round4.md`) and Zainab (`SECURITY-REVIEW-ZAINAB-ROUND3.md`), and both FAILed with the
same High/blocking finding - the round-4 fix's own regression test proves it itself.

### F05 — a ledgered stranded card remained active indefinitely

`IStrandedCardLedger` (F04) only ever stopped a stranded card being ADOPTED by a future grant. It did
nothing about the card's own genuinely-`active` state. Nothing else consumed the ledger — no
checkout/revoke path, access-decision gate, recovery worker, or operator endpoint read it. A failed,
unaudited grant could therefore leave real, usable physical access indefinitely: no `Granted`
credential history exists for it, and no visitor-lifecycle path can ever reach it again (by
construction, the `Pending` row in this exact scenario has no card sequence number attached).

Both reviewers offered the same two acceptable remediation shapes, verbatim: *"Provide a
reconciliation path that repeatedly deactivates ledgered cards and records terminal state/audit, or
make the card/access-decision layer reject any ledgered card immediately."*

**Reconciliation was chosen over reaching into another story's access-decision layer.** The second
option would mean this feature's own code reaching into BIO-ACC-002's or BIO-CRD-006's own domain — a
cross-story change needing Kemi's recorded approval, and a weaker fix besides: the card would still
show genuinely `active` in BIO-CRD-005's own system of record, and only a consumer that remembered to
ALSO check this ledger would ever treat it as denied. Reconciliation instead uses a capability this
feature already legitimately owns (`ICardIssuanceGateway.DeactivateAsync`) to make the card's own
stored status true to reality — strictly more honest, and stays entirely within this feature's own
ownership (nothing outside `api/features/bio-vis-002/` changed for this round).

Fixed with `VisitorCredentialService.ReconcileStrandedCardsAsync`:

1. `IStrandedCardLedger` gained `ListUnresolvedAsync`/`MarkResolvedAsync` — "stranded" now has two
   states, not one: recorded-and-unresolved (still excluded from adoption AND awaiting
   reconciliation) and resolved (card confirmed genuinely no longer `active` — `IsStrandedAsync`
   only reports the former; adoption's own `ListAsync` filter already excludes a non-issued/active
   card on its own once reconciled).
2. `ReconcileStrandedCardsAsync` retries the SAME compensating `DeactivateAsync`
   `MarkFailedBestEffortAsync` already attempted once, for every still-unresolved ledger entry for a
   `(tenantId, personId, cardClass)`, rereading the card's current version immediately before each
   attempt (never reusing a stale one). On success: audited
   (`visitor_credential.stranded_card_reconciled`) and marked resolved. On failure: left unresolved
   for the next opportunity — "repeatedly deactivates" and "recovery converges once dependencies
   resume" (both reviewers' own words), without inventing any kind of retry loop or background
   polling.
3. **Trigger point**: called opportunistically at the START of every `GrantAsync` for a person,
   under the same person-scoped lock already held there (F03's own lock), wrapped in its own
   best-effort `try`/`catch` so a reconciliation hiccup can never block or fail an otherwise-valid
   grant attempt that has nothing to do with it. This codebase deliberately has no background-job
   infrastructure (every other derived-status evaluator in it is read-time-only for the same reason)
   — "the next time anyone touches this person's temporary-credential flow" is therefore the
   correct, consistent place to retry, not a cron job this synthetic system has no way to run. Also
   `internal` (not `private`), so a test — or, disclosed here, a future operator/reconciliation tool
   — can invoke it directly without a full grant attempt.
4. Uses `CancellationToken.None` throughout and takes no `CancellationToken` parameter at all (the
   same F03 discipline `MarkFailedBestEffortAsync` applies) — a card may already genuinely be
   `active`, so this recovery path must never be abortable by any caller's token.

**Disclosed limit, not chased further**: if BIO-CRD-005 itself stays unavailable long enough that NO
grant attempt for the affected person ever happens again, reconciliation never runs. This is an
accepted consequence of a trigger model with no background jobs, consistent with this codebase's own
established discipline elsewhere, not something this story's own scope can close further without
inventing infrastructure no other feature here has either.

Proven by three new tests:
`ReconcileStrandedCardsAsync_deactivates_a_ledgered_card_once_the_card_service_recovers_and_the_ledger_entry_converges_to_resolved`
(the backing card is genuinely `deactivated`, not merely excluded from adoption, and the ledger entry
converges to resolved),
`GrantAsync_opportunistically_reconciles_this_persons_own_stranded_cards_before_proceeding` (the real
wired trigger — a second visitor's own `GrantAsync` call self-heals the first visitor's stranded card
with no explicit reconciliation call), and
`ReconcileStrandedCardsAsync_leaves_an_entry_unresolved_and_retries_next_time_while_deactivation_keeps_failing`
(while the dependency is still down, reconciliation does not throw and the entry stays retryable).

## Post-review remediation round 6 (BIO-VIS-002-F06)

Commit `7e1bfc4` (the F05 remediation above) was independently re-reviewed by both Seyi
(`REVIEW-Seyi-round5.md`) and Zainab (`SECURITY-REVIEW-ZAINAB-ROUND4.md`), and both FAILed again —
two distinct High/blocking findings, one from each reviewer, both labeled `F06`.

### F06 (Zainab) — reconciliation failure was ignored and a second card was issued

`GrantAsync` wrapped its own opportunistic `ReconcileStrandedCardsAsync` call in `try`/`catch` and
proceeded regardless of outcome. If reconciliation failed to resolve a stranded card, the F04
adoption-skip logic correctly refused to adopt that still-unresolved candidate — but the method then
fell through to issuing a BRAND NEW card for the current grant anyway, leaving the SAME person
holding TWO simultaneously `Active` physical-access cards (the old stranded one, plus the new one).

**Fixed by failing closed.** After the opportunistic reconciliation attempt, `GrantAsync` now checks
`IStrandedCardLedger.ListUnresolvedAsync` for this exact `(person, card class)` again; if ANY entry is
still unresolved, it throws `VisitorCredentialError.StrandedCardUnresolved` instead of proceeding to
adopt/issue — no new card is ever minted while a prior attempt's card may still be genuinely active
and unaccounted for. The caller can retry once reconciliation succeeds (another grant, a
checkout/revoke for this person, or the new reconcile endpoint below).

Proven by `GrantAsync_blocks_issuing_a_new_card_while_an_unresolved_stranded_card_remains_for_the_same_person`:
reproduces F05's exact triple-failure state for visitor V1, keeps the card gateway's `DeactivateAsync`
failing, then grants for a SECOND visitor V2 of the same person and proves `IssueCallCount`/
`ActivateCallCount` never increase — the call fails with `stranded_card_unresolved` instead.

### F06 (Seyi) — opportunistic reconciliation is not eventual recovery

`ReconcileStrandedCardsAsync` was only ever invoked at the start of `GrantAsync` for the same person —
`internal`, with no endpoint, startup recovery, timer/job, operator workflow, checkout hook, or
access-decision integration. A person who never requests another grant could keep a stranded card
active indefinitely, with no path to ever neutralize it.

Seyi's own review named an acceptable fix explicitly: *"a scheduled/background worker, an
operator-visible recovery command/endpoint, or a fail-closed access/card expiry mechanism."* This
codebase has no background-job infrastructure (deliberately, consistent with every other read-time-
only derived-status evaluator in it) — so the guaranteed-availability answer is the operator endpoint:

1. `VisitorCredentialService.ReconcileStrandedCardsForPersonAsync(personId, actor, correlationId)` —
   a new, directly callable, permission-gated entry point. Unlike the opportunistic
   `ReconcileStrandedCardsAsync`, it does not depend on a grant/checkout/revoke ever happening again
   for this person — it can be invoked on demand, any time. Acquires this person's own lock for the
   duration, same as every other mutation this service makes against a person's cards.
2. Mapped as a real HTTP endpoint, `POST /api/v1/visitor-credentials/reconcile`, gated identically to
   `GrantAsync`/`RevokeAsync` (permission grant + a verified ADR-0006 provenance proof) — this is a
   privileged operator action against physical access, not a read.
3. **Bonus, not the primary fix**: the opportunistic trigger is also widened into `CheckOutAsync` and
   `RevokeAsync` (best-effort, same discipline as `GrantAsync`'s own call) — any interaction with this
   person's visitor-credential flow is now a chance to self-heal. This increases real-world coverage
   but still depends on *some* interaction happening; the endpoint in point 2 is what actually
   guarantees availability, and is the real answer to Seyi's "guaranteed recovery trigger" ask.

Proven by `ReconcileStrandedCardsForPersonAsync_neutralizes_a_stranded_card_without_any_grant_checkout_or_revoke`
(the operator endpoint neutralizes a stranded card with zero grant/checkout/revoke activity for that
person — the exact "person who never returns" gap Seyi's review named),
`ReconcileStrandedCardsForPersonAsync_requires_the_manage_permission` (the gate is real), a new real-
host test `VISCRED005_the_real_reconcile_endpoint_is_mapped_and_gated_identically_to_grant` (the route
is genuinely mapped, not dead), and
`CheckOutAsync_opportunistically_reconciles_this_persons_own_stranded_cards_too` (the widened trigger).

## Post-review remediation round 7 (BIO-VIS-002-F07)

Commit `5aea251` (the F06 remediation above) was independently re-reviewed and both reviewers
converged on the exact same finding — Seyi round 7 (`REVIEW-Seyi-round6.md`) and Zainab round 5
(`SECURITY-REVIEW-ZAINAB-ROUND5.md`): High on "two active cards for one person" was closed, but
`ReconcileStrandedCardsAsync` itself could resolve a ledger entry without ever durably auditing the
deactivation.

**The gap.** `ReconcileStrandedCardsAsync` deactivated the card, then wrote the audit event, then
marked the ledger entry resolved. If deactivation succeeded but the audit write then failed, the
entry correctly stayed unresolved — but on the NEXT attempt, the card was already inactive, so the
"nothing left to neutralize" branch fired and marked the ledger resolved immediately, without ever
writing or re-verifying the audit record. The physical-access deactivation — a privileged mutation —
permanently vanished from audit history with no trace, violating AC05.

**The fix.** `IStrandedCardLedger.GetOrCreateReconciliationEventIdAsync` mints a stable `evt_` id the
first time a given ledger entry asks for one and returns the SAME id on every later call for that
exact entry — one more dictionary field on the same in-memory ledger, no new infrastructure.
`ReconcileStrandedCardsAsync` now always runs the audit write under that stable id — whether
deactivation just happened in this attempt or already happened on a prior one — before marking the
entry resolved; it never again takes the "already inactive, skip straight to resolved" shortcut.
`AuditAsync`'s own existing ambiguous-write-failure handling (ADR-0003 Decision 7's
`ExistsAsync`-on-throw check) now also accepts this stable id up front: if a prior attempt's write
already durably succeeded, the id is recognised and the write is skipped entirely — deterministically
idempotent across however many separate top-level attempts (a grant, a checkout, a revoke, or the
operator endpoint) it takes to converge. Every existing `AuditAsync` call site is unaffected — the new
parameter is optional and defaults to minting a fresh id exactly as before.

Proven by
`ReconcileStrandedCardsAsync_when_the_audit_write_fails_after_deactivation_succeeds_a_retry_writes_exactly_one_audit_record_and_the_ledger_converges_to_resolved`:
attempt 1 deactivates successfully but the audit write is forced to fail — the ledger stays
unresolved and zero audit records exist; attempt 2 (card already inactive) writes exactly one audit
record, the ledger converges to resolved, and the card is never deactivated a second time.

Also corrected this round: `ReconcileStrandedCardsAsync`'s own XML doc comment had gone stale after
round 6 — it still claimed no operator-independent recovery trigger existed, when
`ReconcileStrandedCardsForPersonAsync`'s endpoint already closed exactly that gap (Seyi's round-6
review flagged this as a non-blocking documentation note).

## Post-review remediation round 8 (BIO-VIS-002-F08)

Commit `ac00f58` (the F07 remediation above) was independently re-reviewed and both reviewers again
converged on the exact same finding — Seyi round 8 (`REVIEW-Seyi-round7.md`) and Zainab round 6
(`SECURITY-REVIEW-ZAINAB-ROUND6.md`): the sequential retry case was fixed, but the stable-id
deduplication itself was not atomic.

**The gap.** `ReconcileStrandedCardsAsync` is triggered from several different call paths that do
NOT all share one lock — grant and the dedicated operator endpoint hold the tenant/person lock,
while checkout and revoke hold a visitor-record lock instead. `AuditAsync`'s stable-id path
(round 7's fix) still did `ExistsAsync(eventId)` then, if absent, built and `WriteAsync`'d the event
— a classic check-then-act race. Two concurrent reconciliation attempts for the same stranded card
could both observe "absent" before either wrote, then both call the production in-memory sink's
`WriteAsync`, which is plain dictionary assignment (`_byEventId[eventId] = event`) — the second
write silently clobbered the first. The record count stayed one, but an append-only audit event had
become mutable, and the original actor/correlation attribution could be lost.

**The fix.** Deliberately NOT a lock-unification redesign — unifying reconciliation's trigger paths
under one common lock would widen the blast radius of locks this service already uses correctly for
their own, narrower purposes (grant/operator vs. checkout/revoke), per an explicit scope decision.
Instead, `IVisitorCredentialAuditSink` gains `CreateIfAbsentAsync`, implemented in the in-memory sink
as a single `ConcurrentDictionary.GetOrAdd(auditEvent.EventId, auditEvent)` call —
`GetOrAdd` IS the atomic primitive: when two threads race on the same key, exactly one value wins
and every caller observes that same winning value, with no additional lock needed. `AuditAsync`'s
stable-id path now routes through this single atomic call instead of the racy check-then-write
sequence; every other call site (a fresh, collision-free id) is untouched and still uses plain
`WriteAsync` exactly as before.

Proven by two tests: `CreateIfAbsentAsync_two_genuinely_concurrent_writers_for_the_same_event_id_converge_to_exactly_one_immutable_payload`
races two `Task.Run` callers (released together via `Task.WhenAll`, not one-after-another) against
the raw sink with the same event id and different payloads, and asserts an invariant that holds for
every possible interleaving: both callers observe the identical winning payload, and exactly one
record is ever durably stored. `ReconcileStrandedCardsAsync_two_concurrent_callers_for_the_same_stranded_card_converge_to_exactly_one_audit_record`
reproduces the real shape — one caller standing in for the operator-endpoint trigger, one for
checkout/revoke's, both calling `ReconcileStrandedCardsAsync` directly with no shared lock, same as
production — and proves the ledger still converges to resolved with exactly one audit record, never
two, never clobbered.

## Post-review remediation round 9 (BIO-VIS-002-F09)

Commit `c264927` (the F08 remediation above) was independently re-reviewed. Zainab round 9 PASSed
outright; Seyi round 9 (`REVIEW-Seyi-round8.md`) confirmed the F08 **production** fix is correct —
`CreateIfAbsentAsync`'s `ConcurrentDictionary.GetOrAdd` genuinely is atomic — but found the two new
concurrency tests' own proof was too weak. This round is test-only; no production code moved.

**The gap.** Both round-8 tests gated their two racing callers on a single `TaskCompletionSource`
signalled once at task start. That only makes both callers *eligible* to run together — it does not
force them to actually reach the contested call at the same moment. The scheduler could still run
caller A fully through `CreateIfAbsentAsync` before caller B's task even began executing, in which
case the test would pass even against the *old*, defective `ExistsAsync`-then-`WriteAsync` sequence
(A observes absent and writes, then B observes present and no-ops) — proving nothing about the race
the fix actually closes.

**The fix.** A new test-only decorator, `BarrierGatedVisitorCredentialAuditSink`
(`tests/features/bio-vis-002/Fakes.cs`), wraps the real sink and calls `Barrier.SignalAndWait()`
(a `System.Threading.Barrier` with exactly 2 participants) as the very first thing inside
`CreateIfAbsentAsync`, before delegating to the inner call. Both racing callers now block right at
the real contested boundary until BOTH have arrived, then are released together — no scheduler slack
for them to serialize. Both strengthened tests wrap their sink in this decorator and drop the old
single-signal gate entirely:

- `CreateIfAbsentAsync_two_genuinely_concurrent_writers_for_the_same_event_id_converge_to_exactly_one_immutable_payload`
  now additionally asserts the two callers' returned objects are the exact same reference
  (`Assert.Same`, not just value-equal), and that the losing caller's own correlation id is never
  subsequently observable in the sink at all — not merely that the record count stayed at one.
- `ReconcileStrandedCardsAsync_two_concurrent_callers_for_the_same_stranded_card_converge_to_exactly_one_audit_record`
  now races a second `VisitorCredentialService` instance (sharing the same repository, gateways,
  and stranded-card ledger, but with its audit sink wrapped in the barrier decorator) and,
  critically, deactivates the stranded card once, single-threaded, *before* racing: letting the two
  callers also race the card gateway's own deactivate step would depend on an unrelated, non-atomic
  test-fake behavior (`FakeCardIssuanceGateway`'s backing `Dictionary` is not thread-safe and
  `DeactivateAsync` is version-checked) and could leave one caller short-circuited by a
  concurrency-conflict exception before it ever reaches the barrier — hanging the other caller on
  it forever. Confining the controlled race to the one boundary this round is actually about (the
  audit write) keeps the test both safe and a faithful reproduction of the documented scenario: a
  prior attempt already neutralized the card but never durably audited it, and two independent
  triggers now contend only over who gets to record that fact.

## Post-review remediation round 10 (BIO-VIS-002-F09 continued)

Commit `7904d39` (round 9 above) was independently re-reviewed again. Zainab round 10
(`SECURITY-REVIEW-ZAINAB-ROUND8.md`) PASSed outright. Seyi round 10 (`REVIEW-Seyi-round9.md`)
FAILed on a further-refined version of the same F09 concern. This round is test-only again; no
production code moved — Seyi has now confirmed `CreateIfAbsentAsync`'s `ConcurrentDictionary.GetOrAdd`
is correct for three rounds running.

**The gap.** Round 9's `BarrierGatedVisitorCredentialAuditSink` places `Barrier.SignalAndWait()`
immediately *before* delegating to the inner sink's `CreateIfAbsentAsync`. That guarantees both
callers reach the wrapper before *either* enters the inner call — but it does not control what
happens after the barrier releases. The scheduler is still free to run caller A's entire inner
create operation to completion before caller B's even starts. The old defective
`ExistsAsync`-then-`WriteAsync` sequence would pass under that exact serialized-after-barrier
interleaving too (A observes absent and writes, B later observes present and no-ops) — so the test
still didn't have the power to fail against the bug it was meant to catch.

**The fix — a mutation/differential proof, not a stronger barrier placement.** Per the explicit
direction given for this round: rather than chase an ever-more-precise barrier position against the
*real* production sink (which has no observable mid-operation hook to barrier against at all, since
it uses the single-step `GetOrAdd(TKey, TValue)` value overload), prove the test *methodology*
itself has discriminating power by running the identical contested sequence against two small,
self-contained, test-only reference implementations side by side (`tests/features/bio-vis-002/Fakes.cs`):

- **`AtomicFactoryRaceSink`** drives its own `ConcurrentDictionary<string, VisitorCredentialAuditEvent>`
  through the **factory-delegate** overload of `GetOrAdd`, with the barrier placed *inside* the
  factory itself: `_store.GetOrAdd(eventId, _ => { barrier.SignalAndWait(); return candidate; })`.
  .NET's own documented contract for this overload is that under genuine contention the factory may
  be invoked on *both* racing threads before one result is kept and the other discarded — it does
  not serialize factory invocation. Placing the barrier inside therefore forces exactly the sequence
  Seyi asked for: neither factory can return — so neither candidate can be stored *or* discarded —
  until *both* have been invoked (both have "observed absent" by virtue of being asked to produce a
  value at all). This is the real atomic primitive the production sink is built on, driven through
  its documented worst case instead of its common case.
- **`RacyCheckThenWriteSink`** deliberately *reimplements* the historical bug this whole F07/F08/F09
  chain fixed: an explicit absence check, a shared barrier both callers must clear before either may
  write, then a plain unconditional overwrite. A semaphore pair forces caller B's write to land
  strictly after caller A's already-completed write — deterministic ordering, not a second
  uncontrolled race — reproducing "both legitimately observed absent, then A writes, then B writes"
  without smuggling a separate race into the proof itself.

Two new tests run the identical assertions against both:
`CreateIfAbsent_mutation_proof_the_atomic_GetOrAdd_factory_based_sink_survives_forced_concurrent_factory_invocation`
asserts both callers converge on the exact same winning object even though both factories were
forced to run; `CreateIfAbsent_mutation_proof_the_historical_check_then_write_algorithm_violates_immutability_once_both_callers_observe_absent`
asserts the *opposite* against the racy reference — the second write silently discards the first,
even though both legitimately observed "absent" first. The second test existing and failing its own
final assertion if pointed at the atomic sink (it isn't) is the proof that this methodology can
actually catch the historical bug, not merely that the real implementation happens to behave. The
two round-9 tests (`CreateIfAbsentAsync_two_genuinely_concurrent_writers_...` and
`ReconcileStrandedCardsAsync_two_concurrent_callers_...`) are kept as-is — they still exercise the
real production sink and service end-to-end, just with a weaker proof than the mutation pair above.

**What this does and does not claim.** This proves the test *methodology* discriminates between an
atomic and a non-atomic design under a genuinely forced concurrent sequence. It does not — cannot —
force a mid-operation interleaving inside the real production `InMemoryVisitorCredentialAuditSink`,
because `GetOrAdd(TKey, TValue)` is a single atomic step with no observable "half-way" point to
barrier against. Combined with Seyi's own repeated source-inspection confirmation (three rounds
running) that production `CreateIfAbsentAsync` is in fact the atomic design, this closes the loop
without inventing non-deterministic timing-based tests against code that has no seam to hook.

## Deliberately out of scope

- **Manual badge reporting / lost-card workflows beyond a bare reason string.** `RevokeAsync`
  takes a free-text `reason`; no AC asks for a structured incident taxonomy.
- **Multiple simultaneous active credentials per visitor.** `GrantAsync` refuses a second grant
  while one is already `Granted` — a visitor has at most one active temporary credential at a
  time, the same simplification every CAS-versioned aggregate in this codebase draws when an AC
  does not explicitly ask for concurrency beyond one.
- **New UI elements for scope/expiry/overstay.** `web/features/visitors/visitors-page.tsx`/
  `mock.ts` have no existing element referencing any of these concepts; this story does not invent
  one (no AC asks for a UI redesign, and the existing three ports already answer their actions for
  real).

## Verification

```
dotnet build api/Biolog.Api/Biolog.Api.csproj                                                   0 errors
dotnet test tests/features/bio-vis-002/Biolog.Features.VisitorCredentials.Tests.csproj           43/43
dotnet test tests/features/bio-vis-001/Biolog.Features.Visitors.Tests.csproj                     25/25 (no regression)
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj                  192/192
bash tests/features/bio-vis-002/BIO-VIS-002.test.sh                                              79/79
bash tests/features/bio-vis-001/BIO-VIS-001.test.sh                                              31/31 (no regression)
```

`scripts/quality.sh` currently reports an overall `FAIL` for an unrelated, floor-wide, pre-existing
reason: `tests/features/bio-int-001/BIO-INT-001.test.sh` (Emeka's, already DONE) has a
self-referential `git diff ... develop` scope check that was only ever valid while running inside
BIO-INT-001's own build round; now that story is merged, it fails for every subsequent story's
`quality.sh` sweep floor-wide. Already reported to god, not this story's to fix. Every check this
story owns (both structural harnesses, both unit suites, the full integration suite) passes
individually.
