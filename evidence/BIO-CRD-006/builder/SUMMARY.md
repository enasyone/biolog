# BIO-CRD-006 — Builder evidence

**Story:** Report lost/missing/stolen/damaged and card replacement
**Result:** PASS

## Round 4 — fix for Chioma's acceptance-review finding BIO-CRD-006-QA-03

**Finding (round 3, High, Security/Functional/Concurrency/Regression FAIL).** `evidence/BIO-CRD-006/acceptance/REPORT.md`
(round 3): QA-01 and QA-02 were both confirmed closed for non-concurrent calls, but the dry-run
precheck (`GetAsync`) only READS BIO-CRD-005's own authoritative state - it does not RESERVE it.
A concurrent mutation between that read and the real `ICardIssueGateway.ActivateAsync`
compare-and-set (e.g. BIO-CRD-005's own raw `/activate`/`/deactivate` endpoint called directly,
concurrently, bypassing this story entirely) could still make the authoritative activation fail
AFTER the earlier-card sweep already ran - reproducing QA-01's own outcome (cards permanently
revoked, no replacement ever active) through a narrower door. Reviewed head `a6b413b`, confirmed
via Chioma's own `activation_gateway_toctou_probe.py`, which also checks for a
`ReserveActivation`/`PrepareActivation` primitive on `ICardIssueGateway`.

**Why no gateway-level reservation was added.** A true fix at that level requires BIO-CRD-005's
own `CardIssueService` to expose a genuine two-phase reserve/commit primitive against its own
`CardIssue` aggregate - modifying that DIFFERENT, ALREADY-ACCEPTED story's own owned files
(`api/features/bio-crd-005/**`), outside this project's declared write scope. This was treated as
a real architectural boundary, not worked around: Chioma's own remediation text explicitly offers
a second, equally valid path ("or safely compensated"), which was chosen instead, kept entirely
within this project's own owned files.

**Fix.** Implemented a genuine compensating transaction in
`api/features/bio-crd-006/CardLifecycleService.cs`. `CardLockState` (`api/features/bio-crd-006/Domain/CardLockState.cs`)
gains two values: `RevocationPending` (non-terminal - still denied, still non-recoverable, so
QA-01's own invariant holds throughout the whole window) and `Unlocked` (the compensated outcome
for a card with no prior report - never a silent delete, matching this codebase's "no delete
operation, ever" convention). `CardLifecycleRecord` gains `PriorLockStateBeforeRevocationPending`,
recorded DURABLY on the row itself (not a local variable), so a later, separate retry of an
interrupted attempt can still compensate correctly. `ActivateReplacementAsync` now: (1)
`BeginRevocationAsync`s every earlier card to `RevocationPending`, recording its prior state; (2)
calls the real, authoritative `ActivateAsync`; then either (3a) `ConfirmRevocationAsync`s every
pending record to `PermanentlyRevoked` on success, or (3b) `CompensateRevocationAsync`s every
pending record back to its recorded prior state (or `Unlocked`) if that call fails. This story's
own Idempotency-Key completion is recorded only once BOTH steps fully succeed, so a
confirmation-only failure resumes confirming on retry rather than replaying stale state.
`CheckPresentationAsync`/`GetDenyListAsync` were updated to treat `RevocationPending` as denied.

Added 3 new deterministic concurrency/compensation tests to
`tests/features/bio-crd-006/CardLifecycleActivationAtomicityTests.cs`, using a gateway decorator
that forces the real `ActivateAsync` call to fail exactly once (simulating a raced compare-and-set)
- proving a previously-`Locked` earlier card compensates back to `Locked` (and is genuinely
recoverable and re-supersede-able by a subsequent, unraced retry), and a never-reported earlier
card compensates to `Unlocked` (presentation allowed again). Added 7 structural-harness checks.
Updated 2 existing tests whose assertions were superseded by the new, correct two-phase
Begin/Confirm behavior (a confirmed row now reaches version 2, not 1; a mid-sweep audit failure
now correctly leaves a processed record at `RevocationPending`, not prematurely `PermanentlyRevoked`).

**On Chioma's own probes.** All THREE of Chioma's own reproduction probes
(`activation_revocation_atomicity_probe.py`, `activation_precondition_side_effect_probe.py`,
`activation_gateway_toctou_probe.py`) now raise `ValueError: substring not found` rather than a
PASS/FAIL verdict, because each references the now-renamed `RevokePermanentlyAsync` (replaced by
`BeginRevocationAsync`/`ConfirmRevocationAsync`/`CompensateRevocationAsync`). This is the same
class of probe staleness disclosed after QA-01's and QA-02's own fixes, now affecting all three
probes simultaneously since this round's refactor touched the method every one of them referenced.
`activation_gateway_toctou_probe.py`'s own `gateway_reservation_present` check would also report
`False` regardless, since no `ReserveActivation`/`PrepareActivation` string was added - a
deliberate choice (see "Why no gateway-level reservation was added" above), not an oversight.
Disclosed explicitly here and in `docs/features/bio-crd-006/README.md`. The property every probe
was actually checking - no observable state contains an active replacement with an earlier card
destroyed for nothing - is instead verified directly by the new deterministic concurrency tests
and the structural-harness checks against the current code.

**Re-verification after the fix:**
- Backend unit/integration tests: 61/61 passing (58 prior + 3 new), 0 build warnings.
- Structural harness: 47/47 passing (41 prior + 6 net new).
- Full `.NET` integration suite: 115/115 passing, no regression.
- `scripts/quality.sh` (non-full): see `quality-round4-output.txt`.

## Round 3 — fix for Chioma's acceptance-review finding BIO-CRD-006-QA-02

**Finding (round 2, High, Security/Functional/Regression FAIL).** `evidence/BIO-CRD-006/acceptance/REPORT.md`
(round 2): QA-01's fix (sweep before activate) was still not failure-atomic against the
replacement's OWN activation preconditions. `RevokePermanentlyAsync` ran for every earlier card
before `cardIssueGateway.ActivateAsync` validated the replacement's own existence,
`expected_version`, lifecycle state and idempotency key. A stale/missing `expected_version`, an
already-`Deactivated` replacement, or an idempotency-key conflict therefore failed activation
only AFTER irreversibly revoking earlier cards - violating AC02 (recovery must remain possible
until an actual replacement activation) and allowing a denial-of-service: an invalid activation
request could destroy usable earlier cards with no replacement ever activated. Reviewed head
`48b3757`, confirmed via Chioma's own `activation_precondition_side_effect_probe.py`.

**Fix.** `ActivateReplacementAsync` in `api/features/bio-crd-006/CardLifecycleService.cs` now:
(1) reserves the request at this story's OWN idempotency layer first (`IIdempotencyStore`,
fingerprinted on person/card class/sequence/expected_version) - a genuine replay short-circuits
immediately, a conflicting reuse is rejected outright; (2) reads the replacement's current
snapshot and validates the same preconditions BIO-CRD-005's own `ActivateAsync` would eventually
validate (exists, `expected_version`, not already `Deactivated`) - a read-only dry-run, entirely
before any revocation side effect; (3) only once that dry-run passes does the full earlier-card
sweep run (preserving QA-01's own "sweep before activate" order); (4) only then is the real,
authoritative `cardIssueGateway.ActivateAsync` called. Added a new `CardLifecycleError.ReplacementNotActivatable`
for the already-`Deactivated` case. Added 5 new unit tests
(`CardLifecycleActivationAtomicityTests.cs`) and 1 real-HTTP-pipeline test
(`CardLifecycleEndpointsIntegrationTests.cs`) proving zero earlier-card lifecycle-record changes
when the precondition, existence, or idempotency reservation is rejected. Added two structural
harness checks (precondition-validation-before-sweep line order; presence of the new dry-run
Idempotency-Key claim).

**On Chioma's own probe.** `activation_precondition_side_effect_probe.py` does a literal text
search for `RevokePermanentlyAsync` preceding any occurrence of `cardIssueGateway.ActivateAsync`.
After this fix it still reports FAIL, because the real, authoritative commit call is - correctly,
per the remediation's own second clause ("while preserving the rule that activation is not
externally committed until the complete revocation sweep succeeds") - still positioned after the
sweep. The probe has no way to distinguish that real commit from this fix's own dry-run
validation (which calls `GetAsync`, not `ActivateAsync`, specifically so it is not itself a second
commit attempt against a service with no "validate without committing" mode). This is the same
class of probe staleness QA-01's own fix already produced against `activation_revocation_atomicity_probe.py`;
disclosed explicitly here and in `docs/features/bio-crd-006/README.md` rather than silently
omitted. The property the probe was actually checking is instead verified directly: a
structural-harness line-order check on precondition-validation vs. the sweep, and
`CardLifecycleActivationAtomicityTests.cs`'s own failure-injection tests.

**Re-verification after the fix:**
- Backend unit/integration tests: 58/58 passing (52 prior + 6 new), 0 build warnings.
- Structural harness: 41/41 passing (37 prior + 4 new).
- Full `.NET` integration suite: 115/115 passing, no regression.
- `scripts/quality.sh` (non-full): see `quality-round3-output.txt`.

## Round 2 — fix for Chioma's acceptance-review finding BIO-CRD-006-QA-01

**Finding (round 1, High, Security/Functional/Offline/Regression FAIL).** `evidence/BIO-CRD-006/acceptance/REPORT.md`:
`ActivateReplacementAsync` activated the replacement BEFORE completing the full permanent-revocation
sweep of earlier cards. If a later record's revoke failed after activation had already succeeded,
the replacement was left observably `Active` while an earlier card was still `Locked`/`Recovered`
(reachable by `RecoverAsync`) or absent from the deny list - violating AC04/AC05 for the whole
failure interval, not merely until a retry repaired it. Reviewed implementation `beb0d63`,
Chioma's own executable source-order probe (`activation_revocation_atomicity_probe.py`) confirmed
the production ordering.

**Fix.** Reordered `ActivateReplacementAsync` in `api/features/bio-crd-006/CardLifecycleService.cs`:
the full earlier-card revocation sweep (each record's own audit-before-commit) now runs to
completion FIRST, and only then is the gateway's own `ActivateAsync` called to actually activate
the replacement. This makes "the replacement is Active" a true invariant of every observable
state - it cannot be reached until every earlier card is already durably `PermanentlyRevoked`.
Added `tests/features/bio-crd-006/CardLifecycleActivationAtomicityTests.cs` (3 new tests) proving
this directly with an injected audit failure partway through the sweep: the replacement's own
status stays `Issued`, cards processed before the failure point are already revoked, and a
not-yet-reached `Locked` card remains legitimately recoverable because no replacement ever
activated. Also added two structural-harness checks (line-order comparison of the sweep call vs.
the activate call, plus a check for the new regression test's presence) so this exact class of
regression is caught structurally, not only at runtime - mirroring BIO-CRD-005-QA-01's own
precedent in this epic.

Chioma's own reproduction probe (`activation_revocation_atomicity_probe.py`) does a literal
string match on `"var activated = await cardIssueGateway.ActivateAsync"`, which no longer appears
verbatim after the fix (the call is now a direct `return await ...`); the probe therefore now
raises `ValueError: substring not found` rather than running to a PASS/FAIL verdict. The
underlying property it was checking - sweep strictly before activate - is what the harness's new
line-order check now verifies directly against the actual, current call sites, and is exercised
behaviourally by the new failure-injection tests above.

**Re-verification after the fix:**
- Backend unit/integration tests: 52/52 passing (49 prior + 3 new), 0 build warnings.
- Structural harness: 37/37 passing (35 prior + 2 new).
- Real-packaged-host tests (`CardLifecycleHostChannelTests.cs`): 6/6 passing.
- Full `.NET` integration suite: 115/115 passing (one unrelated, pre-existing flake in
  `ObservabilityTests.AC04_a_secret_in_a_request_header_does_not_appear_anywhere_in_captured_output`
  - a `StringBuilder` capacity race under parallel execution, nothing to do with this story -
  reproduced as passing standalone and on a clean full-suite rerun).
- `scripts/quality.sh` (non-full): PASS - see `quality-round2-output.txt`.

## Round 1 (commit `8838d47` implementation / `be6585d` evidence, superseded by round 2's fix above)

## Restated scope (per the story-stage prompt)

**User story.** As an authorised operator, I need to lock a reported card, optionally reactivate
it before replacement, and permanently revoke all prior cards when a replacement activates.

**Acceptance criteria**
- AC01: Reported active card immediately becomes locked/denied with configurable reason.
- AC02: Before replacement activation, permitted recovery may reactivate according to policy.
- AC03: Replacement uses next sequence.
- AC04: After replacement activation every earlier physical card is permanently non-reactivatable.
- AC05: Old-card presentation denies and logs even when offline once revocation is available locally.

**Dependencies.** BIO-CRD-005 (real, load-bearing runtime dependency - see below).

**Owned write paths.** `api/features/bio-crd-006/**`, `tests/features/bio-crd-006/**`,
`docs/features/bio-crd-006/**`, `evidence/BIO-CRD-006/**`. Also touched, as every prior story in
this epic does: `api/Biolog.Api/Program.cs` (module registration), `api/Biolog.Api/Biolog.Api.csproj`
(project reference), `api/Biolog.Api/Modules/BioCrd006Module.cs` (new, one-way module adapter),
`scripts/quality.sh` (new harness/unit-test/vulnerability-audit lines), and
`tests/dotnet/Biolog.IntegrationTests/CardLifecycleHostChannelTests.cs` (new, real-host proof of
the BIO-CRD-005 dependency).

**Read-only shared contracts respected.** `contracts/**`, `docs/adr/**`,
`governance/feature-path-ownership.json`, `governance/contract-freeze/**` - none modified.
`api/features/bio-crd-005/**` (a different, already-accepted story's own owned files) was never
touched - see docs/features/bio-crd-006/README.md's "A separate lock layer" section for why the
whole design deliberately avoids needing to.

**Security/privacy risks identified and addressed.**
- ADR-0003 Decision 7 (audit-atomicity, and this codebase's own BIO-CRD-005-QA-01 lesson): every
  mutating method holds a per-(tenant, person, card class) lock across read-validate-audit-commit,
  audits BEFORE committing, and verifies an ambiguous audit-write throw via `ExistsAsync` -
  applied to BOTH the ordinary audit sink and, newly, `IPresentationEventLog` (AC05's own "logs").
- ADR-0001 §3.2 (CAS versioning): every mutating write to an EXISTING `CardLifecycleRecord`
  requires `expected_version` (428 absent / 409 stale); a first-ever report needs none, mirroring
  BIO-CRD-005's own `IssueAsync` precedent.
- ADR-0004 §4.3 (Idempotency-Key): `activate-replacement` (a card activation) and the offline
  presentation-event intake (this story's own "offline sync intake") both require the header.
- ADR-0006 Decision 1: every actor is for audit ATTRIBUTION only - no authorization decision is
  gated on the asserted `X-Actor-Id`/`X-Actor-Type`, the same disclosed shape every sibling story
  uses pending BIO-INT-001.
- Tenant isolation: `ICardLifecycleRepository` and `IPresentationEventLog` are keyed by the
  resolved tenant id only, never a caller-supplied one; an unrecognised host fails closed
  (`tenant_context_missing`), proven through the real packaged host.
- Cross-story exception leakage: the `ICardIssueGateway` adapter translates BIO-CRD-005's own
  `CardIssueError` into this feature's own `CardLifecycleError` (same code/status/detail), so this
  story's error handling stays exhaustive over exactly one exception type.

**Tests planned and written.** See "Test evidence" below.

## Implementation notes

- `CardLockState` (`Locked`/`Recovered`/`PermanentlyRevoked`) is this story's OWN layer over
  BIO-CRD-005's own `CardIssue`, keyed by the same natural composite key - deliberately never
  written onto BIO-CRD-005's own `CardIssueStatus` (a one-way terminal state there that would
  make AC02's reversibility impossible). `CheckPresentationAsync`/`IntakePresentationEventAsync`
  consult ONLY this lock state; it is the sole, authoritative denial gate.
- `ActivateReplacementAsync` (AC04): best-effort deactivates whichever earlier card is still
  `Active` in BIO-CRD-005 (required there before a second card can activate), activates the
  replacement, then sweeps EVERY earlier sequence number - not only the one that was Active - to
  the terminal `PermanentlyRevoked`. The whole operation is idempotent end-to-end and safe to
  retry with the same `Idempotency-Key`.
- `ICardRecoveryPolicyStore` (AC02) is a real, tenant-scoped, per-reason-code setting, not a
  hardcoded always-true, exposed via `PUT /api/v1/card-lifecycle/recovery-policy`.
- `IPresentationEventLog` (AC05) is a durable, append-only store fed by two paths: a live
  synchronous `presentation-check` endpoint, and an idempotent offline `presentation-events`
  intake endpoint (ADR-0004 §4.3's own "offline sync intake"); `GetDenyListAsync` is the
  local-cache feed an offline reader pulls before losing connectivity.
- `ICardIssueGateway` (this project's own interface) is implemented by an adapter in
  `BioCrd006Module` wrapping BIO-CRD-005's own already-registered, `internal` `CardIssueService`
  directly - reachable only via that project's own `InternalsVisibleTo Biolog.Api` grant, the same
  seam BIO-CRD-005's own `PersonExistenceGatewayAdapter` established for its dependency on
  BIO-IDN-001.
- No frontend UI - `web/features/bio-crd-006/**` and `mobile/features/bio-crd-006/**` are
  deliberately empty; AC05's offline capability is the backend building blocks (deny-list feed +
  idempotent intake) a future offline reader would consume.

## Test evidence

- **Structural harness** (`tests/features/bio-crd-006/BIO-CRD-006.test.sh`): 35/35 passing -
  AC01-AC05 mapped checks, ADR-0001 identifier discipline, module-wiring/write-scope discipline,
  developer-test-wiring checks.
- **Backend unit/integration tests** (`tests/features/bio-crd-006/Biolog.Features.CardLifecycle.Tests.csproj`):
  49/49 passing - `CardLifecycleRepositoryTests.cs` (7), `CardLifecycleServiceTests.cs` (13,
  AC01/AC02), `CardLifecycleReplacementTests.cs` (9, AC03/AC04 incl. concurrent-activation race
  and idempotency-replay tests), `PresentationEventTests.cs` (11, AC05),
  `CardLifecycleEndpointsIntegrationTests.cs` (10, real HTTP pipeline via `TestServer`). Zero
  build warnings.
- **Real-packaged-host tests** (`tests/dotnet/Biolog.IntegrationTests/CardLifecycleHostChannelTests.cs`):
  6/6 passing - registers a real tenant domain and person, issues and activates a real BIO-CRD-005
  card, then reports/replaces/activates through this story's own endpoints; asserts BIO-CRD-005's
  own card row is genuinely `deactivated` afterward (not merely this story's own lock record),
  cross-tenant rejection, unknown-card 404, and unrecognised-host fail-closed.
- **Full `.NET` integration suite**: 115/115 passing (includes every prior story's own host-channel
  tests plus this story's own 6 new ones) - no regression.
- **`scripts/quality.sh --full`**: all backend/foundation/contract harnesses and unit/integration
  test suites PASS, `.NET` vulnerability audit PASS. Two failures, both pre-existing and
  out-of-scope for this backend-only story: `frontend unit tests` and `Playwright tests` fail with
  `command not found` (`vitest`/`playwright`) because this worktree's `tests/frontend/node_modules`
  was never installed - confirmed via `git status --short tests/frontend` (no tracked changes) and
  `ls tests/frontend/node_modules` (does not exist). This story touches zero frontend files and no
  AC names a frontend surface; see full log for the exact failure text.

## Real bugs found and fixed during development (disclosed)

1. **`ActivateReplacementAsync` originally called `gateway.ActivateAsync` before deactivating the
   blocking earlier-Active card.** BIO-CRD-005's own `ActivateAsync` refuses a second `Active` card
   outright (its own AC04), so every activate-replacement attempt failed with
   `another_card_already_active`. Fixed by best-effort deactivating whichever earlier card is
   still `Active` in BIO-CRD-005 BEFORE attempting the replacement's own activation - see
   `CardLifecycleService.ActivateReplacementAsync`'s own header comment. Caught by this story's own
   developer tests before any review round.
2. **`CheckPresentationAsync`/`IntakePresentationEventAsync` accepted `AuditActor actor` but never
   recorded it anywhere.** `PresentationEvent` had no actor field, breaking from this codebase's
   consistent audit-attribution discipline (ADR-0006 Decision 1) despite the endpoint requiring
   `X-Actor-Id`. Fixed by adding `PresentationEvent.RecordedBy` (structured, per ADR-0003), wired
   through both paths and included in the offline-intake idempotency fingerprint. Caught by
   self-review before any external review round.
