# BIO-CRD-005 — Physical card issue transaction

As a card production operator, issuing the next per-person/card-class card sequence and
guaranteeing only one active physical card per (person, card class) after activation.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Sequence starts 001 and increments permanently per person/card class | `ICardIssueRepository.AppendAsync`'s optimistic-retry loop - the same `TryAdd`-and-retry-on-loss pattern BIO-CRD-004's own `SignatureAsset` versioning uses. `sequence_display` renders the "001, 002, 003…" form ADR-0001 §1.6 itself names verbatim | `CardIssueRepositoryTests.cs`, `CardIssueEndpointsIntegrationTests.cs` (AC01) |
| **AC02** | Sequence numbers are never reused or reset | No delete operation exists anywhere on `ICardIssueRepository` - the same "governed deletion, no delete operation" principle `Person`'s own AC04 established; retiring a card is `CardIssueStatus.Deactivated`, never a row removal | `CardIssueRepositoryTests.cs` (AC02) |
| **AC03** | Encode/print/verify steps may precede activation | `EncodedAt`/`PrintedAt`/`VerifiedAt` - three independent, optional, set-once timestamps, settable in any order at any status other than `Deactivated`; none is required before activation succeeds | `CardIssueEndpointsIntegrationTests.cs` (AC03) |
| **AC04** | Activation is transactional and prevents two active physical cards | `CardIssueService.ActivateAsync` - refuses activation when a sibling for the same (tenant, person, card class) already holds `Active`; the surrounding per-(tenant, person, card class) lock (`CardIssueLockRegistry`) makes the check-then-set a real transactional guarantee, not merely a race-prone check; `Idempotency-Key` is required (ADR-0001 §3.4/ADR-0004 §4.3 names card activation explicitly) | `CardIssueEndpointsIntegrationTests.cs` (including a real concurrent-activation race test and idempotency replay/reuse tests) (AC04) |
| **AC05** | Card provenance records template/photo/logo/signature/operator/printer/time | `CardIssueProvenance` - captured once at issuance, immutable thereafter; every named field present (`TemplateRef`/`PhotoRef`/`LogoRef`/`SignatureRef`/`Operator`/`PrinterRef`/`IssuedAt`) | `CardIssueEndpointsIntegrationTests.cs` (AC05) |

## Design decisions and scope boundaries

### No frontend UI - this story is backend-only

No acceptance criterion here describes a designer-facing canvas or tool the way BIO-CRD-002/003/
004 each do; this is an operator/production workflow (issue → encode/print/verify → activate/
deactivate), the same "backend only, no frontend" shape BIO-CRD-001's own `BrandAssets` feature
established within this epic. `web/features/bio-crd-005/**` is deliberately empty.

### A genuinely mutable, CAS-versioned aggregate - unlike every other card-designer sibling

Every prior story in this epic (`PhotoMaster`, `BrandAsset`, BIO-CRD-004's `SignatureAsset`) is
immutable/append-only per ADR-0001 §3.3. `CardIssue` is the first that is not: a physical card
issue genuinely changes state over its own life (encoded, printed, verified, activated,
deactivated), and AC04's "activation is transactional" requirement is exactly the compare-and-set
guarantee ADR-0001 Decision 3 defines for a mutable aggregate. `CardIssue.Version` starts at 1 and
every mutating endpoint requires `expected_version`: absent is `428 precondition_required`,
mismatched is `409 concurrency_conflict` - ADR-0001 §3.2's table, followed exactly (a stricter
reading than `PersonService`'s own `UpdateStatusAsync`, which maps an absent version to a `422`
validation error instead; this story follows the ADR's own table directly since it is the higher
authority).

### No canonical identifier type minted for a physical card issue

ADR-0001's own identifier registry table already anticipates this exact shape: "Credential | crd
| tenant plane | Logical credential. A physical card issue is a child record, not this id." No
story that mints a `crd_` Credential id is a declared dependency of this story (only BIO-IDN-001
and BIO-CRD-002 are), so rather than assuming access to an aggregate this story cannot reach, a
card issue is keyed by the natural composite `(TenantId, PersonId, CardClass, SequenceNumber)` -
the same "no canonical id of its own kind, keyed by a natural/composite key" choice `PhotoMaster`,
`BrandAsset` and `SignatureAsset` each made for an analogous reason. A later story that DOES own
real credential issuance can add a `CredentialId` reference field to `CardIssue` without breaking
anything, since ADR-0001 itself describes the physical card issue as the child record.

### `CardClass` is a plain business identifier, not a canonical enum

ADR-0001 §1.6: "business identifiers ... attributes of a record, never its primary key." No AC
enumerates an exact taxonomy of card classes, and tenant policy may need to add one this story has
no standing to anticipate - `CardClass` is a non-blank string, validated for presence only.

### AC04's "prevents two active physical cards" is scoped per (person, card class), not per person

A person may plausibly hold more than one simultaneously-active credential type (for example, a
staff badge and a separate visitor pass for a different purpose) - the "at most one active" claim
is checked against `(TenantId, PersonId, CardClass)`, the same key AC01's sequence is scoped to,
not against the person alone. Two DIFFERENT card classes for the same person may each have their
own independent active card.

### `DeactivateAsync` is a necessary, disclosed addition - not named by any AC directly

Without an explicit way to retire the currently-active card, AC04's own invariant could never be
exercised: a second card could never be activated at all, since the first would permanently
occupy the "active" slot. AC04's "prevents two active physical cards" implies an operator has a
real way to make room for the next one; `DeactivateAsync` is the minimal, necessary mechanism -
CAS-versioned and audited exactly like every other mutation, refusing to fire from any status
other than `Active`.

### `Idempotency-Key` on activation only, following ADR-0004 §4.3 exactly

ADR-0004 §4.3 names exactly four operations as requiring `Idempotency-Key`: "card activation,
production completion, offline sync intake and webhook intake." This story's own `ActivateAsync`
is literally the first of those four. No other mutation in this feature (issue/encode/print/
verify/deactivate) carries the header requirement, matching the ADR's own named scope precisely
rather than over-applying it.

### A full audit-atomicity apparatus, unlike BIO-CRD-004's leaner `SignatureAsset`

Unlike BIO-CRD-004's own `SignatureAsset` (which deliberately followed BIO-CRD-001's lighter
`BrandAsset` precedent - no audit sink, no lock registry, since no AC there asked for an audit
trail), `CardIssueService` follows `PersonService`'s full audit-atomicity pattern (ADR-0003
Decision 7): a per-(tenant, person, card class) lock across each mutation's whole
read-validate-audit-commit sequence, an audit write before every commit, and
`ICardIssueAuditSink.ExistsAsync` verification on an ambiguous audit-write throw. This is the
more proportionate precedent here because (a) `CardIssue` is genuinely mutable with a real
security-relevant invariant (AC04) the lock itself must enforce, not merely record, and (b) this
story records provenance including who performed each production step - exactly the kind of
operationally-sensitive action ADR-0003 expects an audit trail for.

### A genuine cross-story runtime dependency, verified through the real packaged host

Unlike BIO-CRD-002/003/004 (whose "Dependencies" lines are sequencing only), this story has a
real runtime dependency on BIO-IDN-001: a card cannot be issued for a person who does not exist
in the resolved tenant. `IPersonExistenceGateway` is this project's own narrow interface (no
`ProjectReference` to `api/features/bio-idn-001`); the real implementation is an adapter in
`BioCrd005Module` wrapping BIO-IDN-001's own already-registered `PersonService.GetAsync`, the same
shape BIO-PHO-001's identical dependency already established.
`tests/dotnet/Biolog.IntegrationTests/CardIssuanceHostChannelTests.cs` proves this wiring through
the REAL packaged `Biolog.Api` host - a real tenant domain, a real registered person, a real card
issued and activated - not merely an injected test double, plus the ADR-0001 §1.5 "no enumeration
oracle" property: referencing a real tenant-A person through tenant B's host fails identically to
referencing a person that was never registered anywhere.

### "Dependencies: BIO-CRD-002" is sequencing, not coupling

Confirmed by direct inspection: no BIO-CRD-002 file or type is referenced anywhere in this
feature's code. `CardIssueProvenance.TemplateRef`/`PhotoRef`/`LogoRef`/`SignatureRef` are opaque
strings, never resolved against BIO-CRD-002/003/004's own design-time objects - this story only
records WHICH references were used to produce a physical card, for later audit/dispute
resolution, matching that provenance type's own header.

## Deliberately out of scope for this story

- Any frontend UI (see "No frontend UI" above).
- Real barcode/QR/signature RENDERING onto a physical card - this story records which
  template/photo/logo/signature references were used and orchestrates the issue/encode/print/
  verify/activate/deactivate transaction; BIO-CRD-002/003/004 own the design-time canvas, and a
  later production-rendering story owns turning a design into a print-ready file.
- A real Credential (`crd_`) aggregate and its relationship to a card issue - no such story is a
  declared dependency; see "No canonical identifier type minted" above.
- Real per-tenant database persistence - `InMemoryCardIssueRepository` is the same "in-memory
  placeholder behind a real interface" seam ADR-0005 requires of every story in this codebase
  until a Kemi-approved migration lands.
- Revocation, expiry, or any lifecycle beyond issue/encode/print/verify/activate/deactivate - no
  AC names these; a later story can extend `CardIssueStatus` without breaking this one's own
  invariants, the same "conservative baseline table a later story narrows" precedent `Person`'s
  own lifecycle states already established.

## Post-acceptance fix: TD-CRD-005-IDEMPOTENCY-RACE (idempotency-claim ownership race)

**The gap.** `ActivateAsync` claimed the key, then decided whether to proceed by checking only
`claimed.State == IdempotencyRecordState.Completed`. A shared `InFlight` record from a CONCURRENT
caller racing for the SAME key looked identical to a claim this caller itself just won, so a
second caller fell through the check and activated a second time regardless, silently
overwriting the first caller's stored result. The realistic trigger is a client reconnect-storm
retry - exactly the scenario this story's own idempotency design claims to defend against.
Surfaced by Tunde's own BIO-INT-002-F01 finding (Seyi/Zainab, High), confirmed to also exist here
by Kemi reading this file directly (2026-10-06), ruled fix-now not backlog.

**The fix.** Ported Tunde's BIO-INT-002-F01 remediation verbatim, not redesigned.
`IIdempotencyStore.Claim` now returns an `IdempotencyClaim(IdempotencyRecord Record, bool
IsNewClaim)` - `IsNewClaim` is true ONLY for the caller whose own factory-created record actually
won `ConcurrentDictionary.GetOrAdd`'s race, detected via `ReferenceEquals` on the factory-created
object, never by inspecting the record's own content (identical either way for a genuine replay).
`ActivateAsync` now loops: `Completed` → replay the stored result; `IsNewClaim` → break out and
proceed as the sole owner; otherwise (someone else's claim is in flight) → re-`Claim` after a
short poll delay, bounded by a new `_idempotencyWaitTimeout` (5s production default, injectable
for tests), failing closed with the new `idempotency_wait_timeout` (503) error once the deadline
passes rather than waiting forever or falling through to execute anyway.

**Tests - round 1 (insufficient, Zainab CHANGES_REQUIRED).** The first pass gated the two
concurrent callers only by a shared `TaskCompletionSource` start signal. Zainab correctly
rejected this: a start gate controls when the two tasks are SCHEDULED, not when either actually
reaches `Claim()` - the scheduler could run the whole winning attempt to completion before the
loser ever calls `Claim()`, at which point the loser just takes the ordinary `Completed`-replay
path, which the OLD defective State-only check would ALSO pass. No real discriminating power.

**Tests - round 2 (the actual proof).** Fixed per Zainab's exact direction, porting the pattern
BIO-VIS-002 closed the identical gap with (rounds 9-10, Tunde/Seyi) rather than rediscovering it:

- **`ClaimBarrierIdempotencyStore`** (in `TdIdempotencyRaceTests.cs`) wraps the real
  `InMemoryIdempotencyStore` and holds the WINNING caller blocked immediately after its claim
  becomes durably `InFlight` - strictly before the real service proceeds to its side effect -
  until the test has independently confirmed the losing caller has itself called `Claim` and
  observed that SAME still-`InFlight` record. Only then is the winner released. This drives the
  real `ActivateAsync` call site through the exact contested sequence deterministically, proving:
  exactly one real execution (`Version` advances from 1 to 2, never to 3; exactly one
  `card_issue.activate` audit event) and both concurrent callers observing the identical, stable
  result.
- **`Mutant_discrimination_the_historical_State_only_check_lets_both_concurrent_claimants_execute`**
  proves the harness shape itself has real discriminating power (Zainab's explicit requirement,
  not optional): a minimal two-caller race forced via a plain `Barrier` so both callers complete
  their own `Claim` observation before EITHER acts on it (mirroring BIO-VIS-002-F09's own
  `checkBarrier` placement exactly). The real decision (`IsNewClaim`) converges to exactly one
  execution under this forced interleaving; a deliberately reinstated copy of the historical
  decision (`State != Completed` alone) does not - both callers execute.
- **`ActivateAsync_a_second_caller_times_out_if_the_owners_claim_never_completes`** is the ONE
  representative behavioral timeout test for the entire TD-IDEMPOTENCY-RACE-NGOZI remediation
  (Zainab's own concession: the timeout branch is identical across all five call sites, so one
  proof suffices, provided every project retains structural/error-mapping coverage - confirmed
  via the structural harness check above). Reuses `ClaimBarrierIdempotencyStore`: simply never
  calling `ReleaseOwner()` deterministically reproduces "the owner's attempt never completes."

**Verified:** `dotnet build` clean; feature tests 34/34 (31 pre-existing + 3 new - round 1's
single test was replaced, not kept alongside - zero regression); structural harness 40/40 (34
pre-existing + 6 new checks); full API host build clean. Re-ran the new tests 3x in a row with no
flakiness.
