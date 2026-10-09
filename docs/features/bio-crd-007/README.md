# BIO-CRD-007 — Card production jobs and print/encode verification

As a card centre, authorised production jobs support connected, site-offline and controlled
standalone production, with print/encode/read-back-QC failures never activating a credential.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Production job lifecycle is implemented | `ProductionJob` - own aggregate keyed by BIO-CRD-005's own natural card-issue key plus this project's own `JobSequenceNumber` (never reused, mirrors BIO-CRD-005's own sequence discipline); `ProductionJobStatus` (`InProgress`/`Passed`/`Failed`/`Rejected`/`Cancelled`) is DERIVED from its three step results, never set directly | `ProductionJobRepositoryTests.cs`, `ProductionJobLifecycleTests.cs` (AC01) |
| **AC02** | Print/encode/read-back/QC failures do not activate a credential | `ProductionService.ActivateAfterProductionAsync` refuses to even attempt activation unless the LATEST production job for this card has resolved to `Passed` - `InProgress`/`Failed`/`Rejected`/`Cancelled` all refuse outright, before BIO-CRD-005's own gateway is ever called | `ProductionActivationGateTests.cs`, `CardProductionHostChannelTests.cs` (AC02) |
| **AC03** | Signed time-limited production packages support permitted offline mode | `ProductionPackageSigner` - a hand-rolled HMAC-SHA256 payload.signature token, tenant-scoped random signing key, explicit `expires_at`; `CompleteOfflineJobAsync` is ADR-0004 §4.3's own named "offline sync intake" (`Idempotency-Key` required), verifying signature, expiry, tenant match and single-use consumption before creating the job | `ProductionPackageTests.cs`, `CardProductionHostChannelTests.cs` (AC03) |
| **AC04** | Production report records printer/operator/workstation/card identifiers/result/rejection/reprint reason | `ProductionJob` carries `PrinterRef`, a structured `AuditActor Operator` (never a bare string, ADR-0003), `WorkstationRef`, its own card-issue identifiers as its natural key, `Status` as the result, `RejectionReason` (set only when explicitly `Rejected`) and `ReprintReason` (required whenever `JobSequenceNumber` &gt; 1, forbidden on the first attempt) - `ListJobsAsync` IS the production report, every attempt in order | `ProductionJobLifecycleTests.cs`, `ProductionPackageTests.cs` (AC04) |
| **AC05** | Activation preserves one-active-card invariant | `ActivateAfterProductionAsync` never re-implements "at most one Active card" - it delegates to BIO-CRD-005's own real, authoritative `ActivateAsync`, which already enforces it | `ProductionActivationGateTests.cs` (AC05) |

## Design decisions and scope boundaries

### No frontend UI - this story is backend-only

No acceptance criterion here describes an operator-facing screen; this is a production-station
API surface (connected job steps, signed offline packages, gated activation), the same "backend
only, no frontend" shape BIO-CRD-001/005's own precedents already established within this epic.
`web/features/bio-crd-007/**` and `mobile/features/bio-crd-007/**` are deliberately empty.

### A separate `ProductionJob` aggregate, never a mutation of BIO-CRD-005's own `CardIssue`

Mirrors BIO-CRD-006's own precedent exactly: BIO-CRD-005 is a different, already-shipped,
already-accepted story this project has no standing to reopen. `ProductionJob` is keyed by that
story's own natural composite `(TenantId, PersonId, CardClass, SequenceNumber)` plus this
project's own `JobSequenceNumber` - a card may be produced more than once (a reprint after a
failed/rejected attempt), and each attempt is its own durable, audited row, never overwritten.

### AC02's gate has no local irreversible side effect, so there is nothing here for BIO-CRD-006-QA-01/02/03's own lessons to apply to

BIO-CRD-006's own activate-replacement needed a full compensating-transaction redesign across
three review rounds because it performed an IRREVERSIBLE local mutation (permanently revoking
earlier cards) before an external, independently-locked authoritative call that could still
fail or race. `ActivateAfterProductionAsync` has no equivalent: it only READS the latest
production job's own status and either refuses outright or delegates unchanged to BIO-CRD-005's
own real `ActivateAsync`. A bad or stale request is simply refused before anything happens; a
concurrent gateway-level race is caught by that real endpoint's own compare-and-set, exactly as
it already is for every other caller of that same endpoint - there is no local state this
project could destroy while getting it wrong, so no compensation is needed.

### AC03's signed package - a hand-rolled HMAC token, not a JWT library

No JWT (or any signing) dependency exists anywhere in this codebase yet, and no ADR mandates a
specific algorithm - `ProductionPackageSigner` is a small, project-local, right-sized primitive:
a base64url JSON payload, a period, and a base64url HMAC-SHA256 signature over the exact payload
bytes, verified with `CryptographicOperations.FixedTimeEquals` (constant-time, avoiding a timing
oracle). The signing key is tenant-scoped and randomly generated on first use, held in-memory -
the same "in-memory placeholder behind a real interface" seam ADR-0005 requires of every story
in this codebase, standing in for the master specification's own named production target
(Key Vault/Managed HSM). Verification always uses the CALLER's own resolved tenant's key, never
a key derived from the token's own embedded `tenant_id` - a token forged for or replayed against
the wrong tenant fails signature verification directly, rather than being distinguished by a
separate, earlier tenant check (avoiding a needless oracle, ADR-0001 §1.5's own spirit).

### The offline-complete race: consumed-check and job creation share one lock

A signed package always names exactly one specific card. `CompleteOfflineJobAsync` verifies the
signature and parses the payload BEFORE acquiring any lock (a pure computation), but only checks
the package's own `Consumed` flag and creates the resulting job AFTER acquiring the SAME
per-(tenant, person, card class, sequence number) lock every other mutation on that card already
serialises against. This is what prevents two concurrent submissions of the identical token from
both observing "not yet consumed" and both creating a job - the exact class of race BIO-CRD-006's
own three review rounds taught this epic to check for explicitly, caught here in design before
any code was written rather than through a review cycle.

### Package id is an opaque bearer token, not a new canonical identifier type

ADR-0001 registers exactly six identifier types; a production package is an ephemeral
authorisation artifact, not a durable business entity, so its id is a plain GUID - the same
"unregistered type" treatment an idempotency key already receives in this codebase, never a
locally-invented prefix.

### A genuine cross-story runtime dependency, verified through the real packaged host

Unlike this story's own declared "Dependencies: BIO-CRD-006, BIO-CRD-004" (sequencing only - no
AC here names report/recover/replace or a card-designer canvas object, and neither sibling's
files or types are referenced anywhere in this project), the dependency on BIO-CRD-005 IS real
and load-bearing: gating and then activating a card reads and finally mutates a real BIO-CRD-005
`CardIssue` row. `ICardIssueGateway` is this project's own narrow interface (no `ProjectReference`
to `api/features/bio-crd-005`); the real implementation is an adapter in `BioCrd007Module`
wrapping BIO-CRD-005's own already-registered, `internal` `CardIssueService` directly, the same
seam every prior gateway adapter in this epic already established.
`tests/dotnet/Biolog.IntegrationTests/CardProductionHostChannelTests.cs` proves this wiring
through the REAL packaged `Biolog.Api` host - a real tenant domain, a real registered person, a
real card issued through BIO-CRD-005's own endpoint, produced and activated through this story's
own endpoints (including the full signed-offline-package round trip), with the real card issue
genuinely observed `active` afterward, and genuinely still `issued` when a gated activation
attempt is correctly refused.

## Deliberately out of scope for this story

- Any frontend UI or physical printer/encoder driver integration - this story records production
  RESULTS and gates activation on them; it does not itself drive hardware.
- A real Key Vault/Managed HSM-backed signing key - `InMemoryProductionSigningKeyStore` is the
  same in-memory placeholder every story in this codebase uses until a Kemi-approved migration
  lands (ADR-0005).
- Real per-tenant database persistence - `InMemoryProductionJobRepository` and
  `InMemoryProductionPackageStore` are the same placeholder seam.
- A gateway-level reservation/prepare primitive on BIO-CRD-005's own `CardIssueService` - not
  needed here, since (unlike BIO-CRD-006's own activate-replacement) this story's own activation
  gate has no local irreversible side effect to protect; see "AC02's gate has no local
  irreversible side effect" above.

## Post-acceptance fix: TD-CRD-007-IDEMPOTENCY-RACE (idempotency-claim ownership race)

**The gap.** `CompleteOfflineJobAsync` claimed the key, then decided whether to proceed by
checking only `claimed.State == IdempotencyRecordState.Completed`. A shared `InFlight` record
from a CONCURRENT caller racing for the SAME key looked identical to a claim this caller itself
just won, so a second caller fell through the check and executed a second time regardless,
silently overwriting the first caller's stored result. The realistic trigger is a client
reconnect-storm retry - exactly the scenario this story's own idempotency design claims to defend
against. This is distinct from `SeyiRound3ConcurrencyProbe.cs` (Chioma round 2's own remediation
above), which races two DIFFERENT idempotency keys against the same bearer token to prove the
package-single-use invariant via the lock + `Consumed` check - that probe never exercises the
claim-ownership path at all, since different keys never share a claim. Surfaced by Tunde's own
BIO-INT-002-F01 finding (Seyi/Zainab, High), confirmed to also exist here by Kemi reading this
file directly (2026-10-06), ruled fix-now not backlog.

**The fix.** Ported Tunde's BIO-INT-002-F01 remediation verbatim, not redesigned.
`IIdempotencyStore.Claim` now returns an `IdempotencyClaim(IdempotencyRecord Record, bool
IsNewClaim)` - `IsNewClaim` is true ONLY for the caller whose own factory-created record actually
won `ConcurrentDictionary.GetOrAdd`'s race, detected via `ReferenceEquals` on the factory-created
object, never by inspecting the record's own content (identical either way for a genuine replay).
`CompleteOfflineJobAsync` now loops: `Completed` → replay the stored result; `IsNewClaim` → break
out and proceed as the sole owner; otherwise (someone else's claim is in flight) → re-`Claim`
after a short poll delay, bounded by a new `_idempotencyWaitTimeout` (5s production default,
injectable for tests), failing closed with the new `idempotency_wait_timeout` (503) error once
the deadline passes rather than waiting forever or falling through to execute anyway.

**Tests - round 1 (insufficient, Zainab CHANGES_REQUIRED).** The first pass gated the two
concurrent callers (using the SAME idempotency key, unlike `SeyiRound3ConcurrencyProbe.cs`'s
different-key probe above) only by a shared `TaskCompletionSource` start signal. Zainab correctly
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
  real `CompleteOfflineJobAsync` call site through the exact contested sequence deterministically,
  proving: exactly one job created (`jobRepository.List` has exactly one row, never two), exactly
  one `production.complete_offline_job` audit event, and both concurrent callers observing the
  identical, stable job.
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

**Verified:** `dotnet build` clean; feature tests 53/53 (51 pre-existing + 2 new - round 1's
single test was replaced, not kept alongside - zero regression); structural harness 46/46 (41
pre-existing + 5 new checks); full API host build clean. Re-ran the new tests 3x in a row with
no flakiness.

## Post-acceptance fix: BIO-OPS-003-AC04 (SY-OPS003-02, cross-feature lock race, Kemi's ruling)

**Finding (full detail in `docs/features/bio-crd-006/README.md`'s own matching section).**
Seyi's BIO-OPS-003 round-2 review found that this story's own `ProductionJobLockRegistry` and
BIO-CRD-006's own `CardLifecycleLockRegistry` are fully independent locks - BIO-CRD-006's
`ActivateReplacementAsync` reads this story's latest production status, then later activates the
replacement, all under only its own lock, so a concurrent mutation here (create, any of the
print/encode/readback steps, reject, cancel, offline-complete) could land in between and
invalidate the read. This project itself needed no behavioral change - every mutating method
already correctly serialises against its own lock - only an ADDITIONAL, parallel acquisition so
BIO-CRD-006's own activation genuinely excludes concurrent mutation here too.

**The fix.** A new, decoupled `ICardActivationBarrier` interface (`Domain/ICardActivationBarrier.cs`)
- the same per-feature split this story's own `ICardIssueGateway` already uses for BIO-CRD-005 -
wraps the SAME shared, host-layer `Biolog.Api.Cards.CardActivationBarrierLockRegistry` singleton
BIO-CRD-006's own module also wraps. Stated and applied as an INVARIANT, not an enumerated list:
every method in `ProductionService` that acquires `ProductionJobLockRegistry` for a given key
also acquires this, immediately after, nested the same way, for that same key -
`CreateJobAsync`, the shared `RecordStepAsync` (serving print/encode/readback-QC), `RejectJobAsync`,
`CancelJobAsync`, `CompleteOfflineJobAsync` and `ActivateAfterProductionAsync` - all six current
acquisition sites, confirmed by a structural harness check comparing the two call counts rather
than naming them, so a future 7th site cannot silently fall outside the fix.

**Verified by:** a new, dedicated real-packaged-host test,
`tests/dotnet/Biolog.IntegrationTests/CardActivationBarrierHostChannelTests.cs` -
`Holding_the_shared_barrier_blocks_a_real_CRD007_create_job_call_until_released` proves a real
`POST .../production/jobs` call genuinely blocks while the test itself holds the shared lock for
that exact key, then succeeds once released (mutant-discrimination confirmed by a matching check
on the CRD-006 side - see that story's own README for the exact mutant run).

**Verified:** `dotnet build` clean; feature tests 56/56 (unchanged count, existing constructors
extended with the new parameter, not replaced); full dotnet integration suite 295/295 (+2 new
barrier tests shared with BIO-CRD-006, zero regression); dotnet unit suite 211/211 (unaffected);
structural harness 51/51 (46 pre-existing + 5 new checks). No new ADR - same tier as BIO-DEV-001's
own `ICrossTenantDeviceLookup` placement decision.
