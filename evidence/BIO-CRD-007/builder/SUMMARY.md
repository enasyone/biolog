# BIO-CRD-007 — Builder evidence

**Story:** Card production jobs and print/encode verification
**Commit:** `caad172`
**Result:** PASS

## Restated scope (per the story-stage prompt)

**User story.** As a card centre, I need authorised production jobs supporting connected,
site-offline and controlled standalone production.

**Acceptance criteria**
- AC01: Production job lifecycle is implemented.
- AC02: Print/encode/read-back/QC failures do not activate a credential.
- AC03: Signed time-limited production packages support permitted offline mode.
- AC04: Production report records printer/operator/workstation/card identifiers/result/rejection/reprint reason.
- AC05: Activation preserves one-active-card invariant.

**Dependencies.** BIO-CRD-006, BIO-CRD-004 (sequencing only, per direct inspection - no file or
type from either is referenced anywhere in this feature). BIO-CRD-005 (real, load-bearing
runtime dependency - see below).

**Owned write paths.** `api/features/bio-crd-007/**`, `tests/features/bio-crd-007/**`,
`docs/features/bio-crd-007/**`, `evidence/BIO-CRD-007/**`. Also touched, as every prior story in
this epic does: `api/Biolog.Api/Program.cs` (module registration), `api/Biolog.Api/Biolog.Api.csproj`
(project reference), `api/Biolog.Api/Modules/BioCrd007Module.cs` (new, one-way module adapter),
`scripts/quality.sh` (new harness/unit-test/vulnerability-audit lines), and
`tests/dotnet/Biolog.IntegrationTests/CardProductionHostChannelTests.cs` (new, real-host proof of
the BIO-CRD-005 dependency).

**Read-only shared contracts respected.** `contracts/**`, `docs/adr/**`,
`governance/feature-path-ownership.json`, `governance/contract-freeze/**` - none modified.
`api/features/bio-crd-005/**`, `api/features/bio-crd-006/**` (different, already-accepted
stories' own owned files) were never touched.

**Security/privacy risks identified and addressed.**
- ADR-0003 Decision 7 (audit-atomicity): every mutating method holds a per-(tenant, person, card
  class, sequence number) lock across read-validate-audit-commit, audits BEFORE committing, and
  verifies an ambiguous audit-write throw via `ExistsAsync`.
- ADR-0001 §3.2 (CAS versioning): every mutating write to an existing `ProductionJob`/
  `ProductionPackage` requires `expected_version` (428 absent / 409 stale).
- ADR-0004 §4.3 (Idempotency-Key): `activate` (a card activation) and `offline-complete` (this
  story's own "offline sync intake") both require the header.
- ADR-0006 Decision 1: every actor is for audit ATTRIBUTION only - no authorization decision is
  gated on the asserted `X-Actor-Id`/`X-Actor-Type`.
- Signed package integrity: HMAC-SHA256 with a tenant-scoped, randomly-generated signing key
  (never returned to a caller), constant-time signature comparison
  (`CryptographicOperations.FixedTimeEquals`), explicit expiry, single-use consumption enforced
  under the same per-card lock every other mutation on that card uses (see "Design decisions"
  below for the concurrent-double-consumption race this closes).
- Cross-story exception leakage: the `ICardIssueGateway` adapter translates BIO-CRD-005's own
  `CardIssueError` into this feature's own `ProductionError` (same code/status/detail).
- Tenant isolation: every store is keyed by the resolved tenant id only; an unrecognised host
  fails closed (`tenant_context_missing`), proven through the real packaged host; a token signed
  for a different tenant fails signature verification directly (never a distinguishable "wrong
  tenant" response), proven by a dedicated test.

**Tests planned and written.** See "Test evidence" below.

## Design decisions applying BIO-CRD-006's own hard-won lessons from the start

BIO-CRD-006 (the immediately preceding story in this epic, same conflict-group family) took
FOUR acceptance rounds to reach a correct activate-then-cascade design, because its own
activate-replacement performed an irreversible LOCAL mutation (permanently revoking earlier
cards) before an external, independently-locked authoritative call that could fail or race.
This story's own AC02/AC05 gate (`ActivateAfterProductionAsync`) was deliberately designed to
have NO local irreversible side effect of its own: it only reads the latest job's status and
either refuses outright or delegates UNCHANGED to BIO-CRD-005's own real `ActivateAsync`. There
is nothing here for BIO-CRD-006-QA-01 (audit-before-commit ordering), QA-02
(precondition-before-side-effect) or QA-03 (compensate-on-race) to apply to, because there is no
side effect to get wrong in the first place - disclosed explicitly in
`docs/features/bio-crd-007/README.md`.

A second, distinct race WAS identified and closed during design, before any code was written:
`CompleteOfflineJobAsync` (AC03) verifies a package's signature and parses its payload as a pure
computation, but only checks the package's own `Consumed` flag and creates the resulting job
AFTER acquiring the same per-card lock every other mutation on that card already serialises
against - preventing two concurrent submissions of the identical signed token from both
observing "not yet consumed" and both creating a job. This is exactly the class of TOCTOU
(time-of-check-to-time-of-use) race BIO-CRD-006's own three review rounds taught this epic to
watch for; it is caught here in the design phase, not through a review cycle.

## Implementation notes

- `ProductionJob` (AC01) is its own aggregate, keyed by BIO-CRD-005's own card-issue composite
  key plus this project's own `JobSequenceNumber` (never reused - a reprint after a
  failed/rejected/cancelled attempt gets the next number, mirroring BIO-CRD-005's own sequence
  discipline exactly). Its overall `Status` is DERIVED from three independent step results
  (print/encode/read-back-QC), never set directly by a caller - `Passed` only if all three
  passed, `Failed` if any one did not. `Rejected` is a separate, explicit operator action (a
  card that mechanically passed every check but is visually/physically flawed); `Cancelled` is a
  necessary, disclosed addition (mirrors BIO-CRD-005's own `DeactivateAsync` precedent) so a job
  queued in error has a real way to be closed out.
- `ProductionPackageSigner` (AC03) is a hand-rolled HMAC-SHA256 `payload.signature` token - no
  JWT dependency exists anywhere in this codebase, and adding one would be disproportionate for
  this story's own single, narrow need. Signing keys are tenant-scoped, randomly generated on
  first use, held in-memory (the same ADR-0005 placeholder seam every story in this codebase
  uses), never returned to a caller.
- `CompleteOfflineJobAsync` is ADR-0004 §4.3's own named "offline sync intake" -
  `Idempotency-Key` required, and does NOT re-derive or override what the offline reader already
  decided about whether print/encode/read-back-QC passed - the entire point of offline
  production is that the station recorded its own results without asking this server.
- `ICardIssueGateway` (this project's own interface) is deliberately narrower than BIO-CRD-006's
  own identically-named interface - this story only ever needs to check a card exists and,
  gated on a passed job, activate it; it neither lists siblings nor issues replacements nor
  deactivates anything.
- No frontend UI - `web/features/bio-crd-007/**` and `mobile/features/bio-crd-007/**` are
  deliberately empty, matching BIO-CRD-001/005's own precedent.

## Round 2 remediation (evidence/BIO-CRD-007/acceptance/REVIEW-Chioma-round1.md)

Chioma's round 1 acceptance audit failed on two grounds: (1) the independent reviewer's own
evidence (`evidence/BIO-CRD-007/codex/REVIEW-Seyi-round1.md`) was too thin to reproduce - no
commands, raw output, timestamp, or commit hash - and (2) no test exercised the offline-package
double-consumption race *concurrently*; the existing tests only replayed the same token
sequentially (`await`, then `await` again), which proves idempotency-key replay handling but not
the lock actually serialising two simultaneous submissions.

Ground (1) is Seyi's own remediation item - producing independent QA evidence is by definition not
something the builder can supply without undermining the independence Chioma is auditing for, so
it is out of this round's scope.

Ground (2) is addressed here: added
`ProductionPackageTests.CompleteOfflineJob_two_truly_concurrent_submissions_of_the_same_package_yield_exactly_one_success`,
which uses a `Barrier(2)` to hold two `CompleteOfflineJobAsync` attempts at the starting line until
both have finished their pure signature/payload parse and are genuinely racing for
`ProductionJobLockRegistry`'s per-card lock, then asserts exactly one succeeds, the package is
consumed, and exactly one job exists.

**A genuine bug was found and fixed while validating this new test**, unrelated to the underlying
story implementation: the first version called `Barrier.SignalAndWait()` (a blocking synchronous
call) as the first statement of a local `async` function invoked without `Task.Run`. An async
method runs synchronously up to its first `await`, so the first `AttemptAsync(...)` call ran
straight into the blocking wait and never returned control to the caller - the second attempt was
never invoked, so the barrier (needing 2 signals) deadlocked permanently. This looked identical to
a sandbox artifact (the managed sandbox separately and genuinely does deny `dotnet test`'s own
vstest loopback socket bind, per Chioma's own round 1 note and confirmed independently here) and
cost real time before being isolated by filtering to individual test classes
(`ProductionJobRepositoryTests` alone: 33ms; `ProductionPackageTests` alone: hung) with the sandbox
disabled. Fixed by wrapping the barrier-wait in `Task.Run` so both attempts are actually scheduled
onto separate threads before either blocks. Re-verified: `ProductionPackageTests` 15/15 in 128ms,
full suite 50/50 in 372ms.

## Test evidence

- **Structural harness** (`tests/features/bio-crd-007/BIO-CRD-007.test.sh`): 41/41 passing -
  AC01-AC05 mapped checks, ADR-0001 identifier discipline, module-wiring/write-scope discipline,
  developer-test-wiring checks.
- **Backend unit/integration tests** (`tests/features/bio-crd-007/Biolog.Features.CardProduction.Tests.csproj`):
  50/50 passing - `ProductionJobRepositoryTests.cs` (8), `ProductionJobLifecycleTests.cs` (13,
  AC01/AC04), `ProductionActivationGateTests.cs` (7, AC02/AC05), `ProductionPackageTests.cs` (15,
  AC03, including the new round-2 true-concurrency test), `ProductionEndpointsIntegrationTests.cs`
  (7, real HTTP pipeline via `TestServer`). Every test outside the round-2 addition passed on the
  first implementation attempt - no implementation defects found during development. Zero build
  warnings. Run outside the managed sandbox (`dangerouslyDisableSandbox`) because vstest's own
  loopback socket bind is denied inside it - the same block Chioma's round 1 evidence hit; this is
  a sandbox artifact of the test host's IPC channel, not a property of the code under test.
- **Real-packaged-host tests** (`tests/dotnet/Biolog.IntegrationTests/CardProductionHostChannelTests.cs`):
  5/5 passing - registers a real tenant domain and person, issues a real BIO-CRD-005 card,
  produces it (all three steps passing) and activates it through this story's own endpoints,
  including the full signed-offline-package round trip end to end; asserts the real card issue
  is genuinely `active` afterward, and genuinely still `issued` when a gated activation attempt
  is correctly refused; cross-tenant rejection; unrecognised-host fail-closed.
- **Full `.NET` integration suite**: 120/120 passing at round 1 (includes every prior story's own
  host-channel tests plus this story's own 5); the `CardProductionHostChannelTests` filter above
  reconfirms this story's own 5 individually for round 2 - no regression.
- **`scripts/quality.sh --full`**: all backend/foundation/contract harnesses and unit/integration
  test suites PASS (including this story's own structural harness and unit tests, confirmed
  individually), `.NET` vulnerability audit PASS. Two failures, both pre-existing and
  out-of-scope for this backend-only story: `frontend unit tests` and `Playwright tests` fail
  with `command not found` (`vitest`/`playwright`) because this worktree's
  `tests/frontend/node_modules` was never installed - the same gap disclosed in BIO-CRD-005's
  and BIO-CRD-006's own builder evidence for the identical reason. This story touches zero
  frontend files and no AC names a frontend surface; see `quality-full-output.txt` for the exact
  failure text.
- **`scripts/quality.sh` (non-full)**: see `quality-output.txt`.
