# BIO-PHO-002 — Photo Booth integration contract

As an external Photo Booth, a versioned API contract receives capture specifications and pushes
approved captures to BIOLOG.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Capture specification includes frame size, aspect ratio, minimum DPI, face-height/background rules | `CaptureSpecification.Current` (a fixed, versioned baseline), readable via `GET /api/v1/photo-booth/capture-specification` | `CaptureQualityGateTests` (AC01 tests) |
| **AC02** | Upload authenticates the booth/device | `HeaderDeviceAuthenticator` requires both `X-Biolog-Device-Id` and a matching `X-Biolog-Device-Secret` (constant-time, out-of-band provisioned), AND the device must be `DeviceStatus.Active` in BIO-DEV-001's real registry | `DeviceAuthenticatorTests` (incl. `BIOPHO002_QA01_...` forgery test), `PhotoBoothEndpointsIntegrationTests` (AC02 tests), `PhotoBoothHostChannelTests` (real device registration) |
| **AC03** | Retries are idempotent | `IIdempotencyStore` (claim/complete/release/get, ADR-0001 §3.4) on the submit endpoint | `IdempotencyStoreTests`, `CaptureServiceTests` (AC03 tests) |
| **AC04** | Unapproved/low-quality capture is not silently promoted to master | `CaptureQualityGate.Evaluate` runs before a `BoothCapture` record even exists; a failing submission is created already `Rejected`, never `Pending`; only `ApproveAsync` (a separate mutation) calls `IPhotoMasterPromotionGateway.PromoteAsync` | `CaptureServiceTests` (AC04 tests), `PhotoBoothHostChannelTests.AC04_approving_a_real_capture_creates_a_real_independently_readable_BIOPHO001_photo_master` |

## Design decisions and scope boundaries

### The one genuine cross-story runtime dependency in this epic so far

Every prior BIO-PHO/BIO-IDN/BIO-TEN sibling story's own "Dependencies" line in its story
metadata has been build-wave sequencing, not a runtime service dependency - documented
explicitly, story after story. This one is different: `ApproveAsync` really does call BIO-PHO-001's
own already-registered `Photography.PhotoMasterService.AppendAsync` through
`IPhotoMasterPromotionGateway`, creating a real, immutable `PhotoMaster`. Still no
`ProjectReference` between the two feature projects - the adapter lives in
`Biolog.Api.Modules.BioPho002Module`, the one place allowed to reference both.

### Applying BIO-CRD-001-QA-01/QA-02 from the start

BIO-CRD-001's own tenant authenticator shipped round 1 trusting a bare, unverified id header,
and round 1's fix then shipped with no real provisioning path, making the composed host
unusable. `HeaderDeviceAuthenticator`/`DeviceCredentialProvisioning` apply both lessons from day
one: a device must present a verified, out-of-band-provisioned secret (never through this
module's own HTTP surface), and that provisioning reads real `IConfiguration`
(`PhotoBooth:DeviceCredentials:<deviceId>`, i.e. `PhotoBooth__DeviceCredentials__<deviceId>=<secret>`
as an environment variable) wired into `BioPho002Module.RegisterServices` from the start. No
secret value is committed to this repository.

### Applying BIO-PHO-001-QA-01 from the start

BIO-PHO-001's own `PhotoMaster`/`PhotoDerivative` shipped with a get-only `byte[]` `Content`
property that leaked a mutable backing array - closed only after an independent review found
it. `BoothCapture.Content` uses the identical "defensive copy in, defensive copy out" treatment
from its first commit, with the same regression-test shape (`BoothCaptureTests.cs`).

### CaptureStatus: three states, one real promotion path

`Pending -> Rejected` (auto, at submission, if the quality gate fails; or explicit, via
`RejectAsync`) and `Pending -> Promoted` (explicit, via `ApproveAsync`, and ONLY there) are the
only transitions. `IBoothCaptureRepository.ReplaceAsync` itself refuses to replace anything
whose current `Status` is not `Pending` - "decided is terminal" is enforced at the repository
boundary, not merely by service-layer discipline, mirroring `ApprovalRequest`'s (BIO-IDN-006)
identical enforcement for a different aggregate.

### What is measured vs. what is booth-attested

Frame size, aspect ratio and DPI are measured directly from the capture's own bytes
(`CaptureRasterInspector`, a dependency-free PNG/JPEG header parser). Face-height and background
compliance cannot be measured this way - no computer-vision library exists anywhere in this
host - so a submission carries booth-supplied `face_height_compliant`/`background_compliant`
attestations; a `false` (or omitted) attestation is a quality failure with the same weight as a
measured one. This is a disclosed scope boundary, not a silent gap: a booth that never sends
these fields cannot promote a capture by omission.

### No new ADR-0001 canonical identifier type

`BoothCapture` mints no new type - its natural key is `(PersonId, SequenceNumber)`, the same
choice `PhotoMaster` (BIO-PHO-001) made for an identical reason.

### Actor identity: audit-attribution only on approve/reject (ADR-0006 Decision 1/2/4)

This feature defines no `RoleType`, permission check, or any gate that makes who may approve or
reject a capture depend on the asserted `X-Actor-Id` - every caller with API access may already
decide any pending capture, uniformly. This is the same disclosed-residual "audit-attribution"
category ADR-0006 already catalogues for BIO-IDN-001 through BIO-IDN-005 and BIO-PHO-001 itself
- distinct from device authentication (AC02), which DOES verify a real credential before a
capture may even be submitted.

### Persistence is in-memory

Same ADR-0005 rationale as every sibling story: `IBoothCaptureRepository`,
`IIdempotencyStore` and `ICaptureAuditSink` are the seams a real, migration-backed
implementation drops in behind.

## Deliberately out of scope for this story

- Real pixel resampling or face/background computer-vision analysis (no library exists anywhere
  in this host - a disclosed boundary, not a gap to be silently worked around).
- RBAC enforcement on who may approve/reject a capture (BIO-IDN-006's own catalogue exists, but
  nothing wires it into this feature - see "Actor identity" above).
- A tenant-configurable capture specification (the story's AC01 does not ask for one; `Current`
  is a fixed baseline, the same shape BIO-IDN-006's `RoleType` catalogue uses).
- A bytes-delivery/download endpoint for a capture's own content - responses are metadata only,
  matching `PhotoMasterResponse`'s (BIO-PHO-001) identical boundary.
- Real device PKI/mTLS - `HeaderDeviceAuthenticator` proves possession of a provisioned secret
  for a real, active, registered device, not a certificate-bound identity.

## Post-acceptance fix: TD-PHO-002-IDEMPOTENCY-RACE (idempotency-claim ownership race)

**The gap.** `SubmitAsync` claimed the key, then decided whether to proceed by checking only
`claimed.State == IdempotencyRecordState.Completed`. A shared `InFlight` record from a CONCURRENT
caller racing for the SAME key looked identical to a claim this caller itself just won, so a
second caller fell through the check and submitted a second time regardless, silently minting a
second sequence number for what should have been a single capture and overwriting the first
caller's stored result. The realistic trigger is a client reconnect-storm retry - exactly the
scenario this story's own idempotency design (AC03: "retries are idempotent") claims to defend
against. Surfaced by Tunde's own BIO-INT-002-F01 finding (Seyi/Zainab, High), confirmed to also
exist here by Kemi reading this file directly (2026-10-06), ruled fix-now not backlog.

**The fix.** Ported Tunde's BIO-INT-002-F01 remediation verbatim, not redesigned.
`IIdempotencyStore.Claim` now returns an `IdempotencyClaim(IdempotencyRecord Record, bool
IsNewClaim)` - `IsNewClaim` is true ONLY for the caller whose own factory-created record actually
won `ConcurrentDictionary.GetOrAdd`'s race, detected via `ReferenceEquals` on the factory-created
object, never by inspecting the record's own content (identical either way for a genuine replay).
`SubmitAsync` now loops: `Completed` → replay the stored result; `IsNewClaim` → break out and
proceed as the sole owner; otherwise (someone else's claim is in flight) → re-`Claim` after a
short poll delay, bounded by a new `_idempotencyWaitTimeout` (5s production default, injectable
for tests), failing closed with the new `idempotency_wait_timeout` (503) error once the deadline
passes rather than waiting forever or falling through to execute anyway. The pre-existing
`Get(tenantId, operation, idempotencyKey)` read method is untouched - it still returns a plain
`IdempotencyRecord?`, unrelated to this ownership gap.

**Tests - round 1 (insufficient, Zainab CHANGES_REQUIRED).** The first pass gated the two
concurrent callers only by a shared `TaskCompletionSource` start signal. Zainab correctly
rejected this: a start gate controls when the two tasks are SCHEDULED, not when either actually
reaches `Claim()` - the scheduler could run the whole winning attempt to completion before the
loser ever calls `Claim()`, at which point the loser just takes the ordinary `Completed`-replay
path, which the OLD defective State-only check would ALSO pass. No real discriminating power.
(`IdempotencyStoreTests.cs`'s own pre-existing tests were already updated for `Claim`'s new
`IdempotencyClaim` return shape and the two explicit `IsNewClaim` assertions - that part stands.)

**Tests - round 2 (the actual proof).** Fixed per Zainab's exact direction, porting the pattern
BIO-VIS-002 closed the identical gap with (rounds 9-10, Tunde/Seyi) rather than rediscovering it:

- **`ClaimBarrierIdempotencyStore`** (in `TdIdempotencyRaceTests.cs`) wraps the real
  `InMemoryIdempotencyStore` and holds the WINNING caller blocked immediately after its claim
  becomes durably `InFlight` - strictly before the real service proceeds to its side effect -
  until the test has independently confirmed the losing caller has itself called `Claim` and
  observed that SAME still-`InFlight` record. Only then is the winner released. This drives the
  real `SubmitAsync` call site through the exact contested sequence deterministically, proving:
  exactly one real execution (`ListForPersonAsync` returns exactly one capture, never two;
  exactly one `photo_booth.capture.submit` audit event) and both concurrent callers observing the
  identical, stable result.
- **`Mutant_discrimination_the_historical_State_only_check_lets_both_concurrent_claimants_execute`**
  proves the harness shape itself has real discriminating power (Zainab's explicit requirement,
  not optional): a minimal two-caller race forced via a plain `Barrier` so both callers complete
  their own `Claim` observation before EITHER acts on it (mirroring BIO-VIS-002-F09's own
  `checkBarrier` placement exactly). The real decision (`IsNewClaim`) converges to exactly one
  execution under this forced interleaving; a deliberately reinstated copy of the historical
  decision (`State != Completed` alone) does not - both callers execute.

The bounded-timeout path itself is not independently re-tested here - Zainab's own review
explicitly agreed one representative behavioral timeout test (in BIO-CRD-005, the simplest
single-call-site sibling) is sufficient, since the timeout branch is identical across all five
call sites and the timeout/poll settings are injectable everywhere.

**Verified:** `dotnet build` clean; feature tests 63/63 (round 1's single race test replaced by
2 new ones, zero regression); structural harness 40/40 (35 pre-existing + 5 new checks); full API
host build clean. Re-ran the new tests 3x in a row with no flakiness.
