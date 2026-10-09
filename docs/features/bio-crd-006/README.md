# BIO-CRD-006 — Report lost/missing/stolen/damaged and card replacement

As an authorised operator, locking a reported card, optionally reactivating it before
replacement, and permanently revoking all prior cards once a replacement activates.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Reported active card immediately becomes locked/denied with configurable reason | `CardLifecycleService.ReportAsync` - creates/transitions a `CardLifecycleRecord` to `CardLockState.Locked` with a plain, non-blank, tenant-configurable `reason_code`; refuses a second report while already `Locked` | `CardLifecycleServiceTests.cs`, `PresentationEventTests.cs`, `CardLifecycleEndpointsIntegrationTests.cs` (AC01) |
| **AC02** | Before replacement activation, permitted recovery may reactivate according to policy | `CardLifecycleService.RecoverAsync` - transitions `Locked` → `CardLockState.Recovered`, gated by `ICardRecoveryPolicyStore.IsRecoveryPermitted` (a real, tenant-scoped, per-reason-code setting, not a hardcoded always-true); refused once a later replacement's activation has already moved the record to the terminal `PermanentlyRevoked` state | `CardLifecycleServiceTests.cs`, `CardLifecycleReplacementTests.cs` (AC02) |
| **AC03** | Replacement uses next sequence | `CardLifecycleService.ReplaceAsync` - a pure passthrough to BIO-CRD-005's own `IssueAsync` via `ICardIssueGateway`, whose own repository already guarantees the next sequence number; nothing extra to enforce here | `CardLifecycleReplacementTests.cs`, `CardLifecycleHostChannelTests.cs` (AC03) |
| **AC04** | After replacement activation every earlier physical card is permanently non-reactivatable | `CardLifecycleService.ActivateReplacementAsync` - reserves the request at this story's own idempotency layer, validates the replacement's own activation preconditions (BIO-CRD-006-QA-02), moves EVERY earlier sequence number to the non-terminal `CardLockState.RevocationPending` (deactivating whichever earlier card was still `Active` in BIO-CRD-005 first), calls the gateway's own authoritative `ActivateAsync`, then either CONFIRMS every pending record to the terminal `CardLockState.PermanentlyRevoked` on success or COMPENSATES each one back to its recorded prior state (or `Unlocked`) if that authoritative call fails - a real compensating transaction closing the residual time-of-check-to-time-of-use race a pure reordering alone could not (BIO-CRD-006-QA-01/QA-02/QA-03, see below); `Idempotency-Key` required and the whole operation is safe to retry end-to-end | `CardLifecycleReplacementTests.cs` (including a concurrent-activation race test and an idempotency-replay test), `CardLifecycleActivationAtomicityTests.cs` (confirmed audit failure mid-sweep, stale/missing precondition, invalid state, unknown replacement and idempotency-reuse regressions, plus a deterministic concurrent-mutation compensation test), `CardLifecycleHostChannelTests.cs` (AC04) |
| **AC05** | Old-card presentation denies and logs even when offline once revocation is available locally | `CardLifecycleService.CheckPresentationAsync` (live/online check) and `IntakePresentationEventAsync` (ADR-0004 §4.3's own "offline sync intake", `Idempotency-Key` required) both durably log to `IPresentationEventLog`; `GetDenyListAsync` is the local deny-list feed (`Locked` + `PermanentlyRevoked` records, optionally delta-filtered by `since`) an offline reader pulls before going offline | `PresentationEventTests.cs`, `CardLifecycleEndpointsIntegrationTests.cs` (AC05) |

## Design decisions and scope boundaries

### No frontend UI - this story is backend-only

No acceptance criterion here describes a designer-facing canvas or tool; this is an operator
report/recover/replace/activate workflow plus a reader-facing presentation-check/offline-sync
surface, the same "backend only, no frontend" shape BIO-CRD-001's `BrandAssets` and BIO-CRD-005's
own `CardIssuance` already established within this epic. `web/features/bio-crd-006/**` and
`mobile/features/bio-crd-006/**` are deliberately empty - AC05's "offline" requirement is
satisfied by the backend building blocks (the deny-list feed and the idempotent offline-sync
intake endpoint) a future offline reader app would consume, not by this story building that
reader itself.

### A separate lock layer, deliberately NOT a mutation of BIO-CRD-005's own `CardIssueStatus`

`CardLockState` (`Locked`/`Recovered`/`PermanentlyRevoked`) is this story's own layer, keyed by
the same natural composite `(TenantId, PersonId, CardClass, SequenceNumber)` BIO-CRD-005's own
`CardIssue` uses, but stored in this feature's own `CardLifecycleRecord` - never written onto
BIO-CRD-005's own `Status` field. This is a deliberate design choice, not an oversight:
BIO-CRD-005's own `CardIssueStatus.Deactivated` is a ONE-WAY terminal state there (no operation
in that already-shipped, already-accepted story ever moves a card back out of it). Reusing it for
AC01's "report" would make AC02's "recovery may reactivate" impossible without reopening
BIO-CRD-005's own state machine - which this story, per its declared write scope
(`api/features/bio-crd-005/**` is owned by a different, already-accepted story), has no standing
to do. Layering an independent lock state that stays freely reversible
(`Locked` ↔ `Recovered`) right up until AC04's replacement activation makes it genuinely permanent
(`PermanentlyRevoked`) is what makes both AC02's reversibility and AC04's permanence
simultaneously satisfiable.

`CheckPresentationAsync`/`IntakePresentationEventAsync` consult ONLY this story's own
`CardLockState` - it is the sole, authoritative denial gate. `ICardIssueGateway` is used only to
validate a card issue exists, to issue/activate a replacement (AC03/AC04), and for a best-effort
secondary sync of BIO-CRD-005's own record during AC04's PERMANENT sweep (where, unlike
report/recover, there is no tension: both this story's own `PermanentlyRevoked` and BIO-CRD-005's
own `Deactivated` are terminal, so keeping both in sync there is safe and disclosed as
best-effort - a failure of that secondary sync never blocks or undoes this story's own
authoritative revocation).

### AC04's full revocation sweep must complete BEFORE the replacement activates, not after (BIO-CRD-006-QA-01)

**Finding (Chioma's acceptance review round 1, High).** The original `ActivateReplacementAsync`
activated the replacement first and swept earlier cards to `PermanentlyRevoked` afterward. If a
later record's revoke failed (e.g. this story's own audit sink briefly unavailable), the
replacement was left observably `Active` while an earlier card was still `Locked`/`Recovered` -
reachable by `RecoverAsync` - or simply absent from `GetDenyListAsync`. That is a real AC04/AC05
violation for the whole failure interval, not merely until a caller happened to retry; an
executable source-order probe confirmed the production ordering and a retry-dependent repair does
not satisfy the invariant during the gap.

**Fix.** `ActivateReplacementAsync` now: (1) best-effort deactivates whichever earlier card is
currently `Active` in BIO-CRD-005 (its own `ActivateAsync` refuses a second `Active` card
outright, its own AC04), (2) sweeps EVERY earlier sequence number - including ones that were
never `Active` at all, e.g. an issued-but-never-activated stale card - to this story's own
terminal `PermanentlyRevoked`, each record's own audit written and confirmed durable before that
record's own commit, and only once that WHOLE sweep has fully completed does it (3) call the
gateway's own `ActivateAsync` to actually activate the replacement. This makes "the replacement
is Active" a true invariant of every OBSERVABLE state: it cannot be reached until every earlier
card is already permanently revoked. If any one revoke throws, the replacement is never activated
at all that attempt - the caller sees the failure and may safely retry the whole operation with
the same `Idempotency-Key`.
`CardLifecycleActivationAtomicityTests.cs` proves this directly with an injected audit failure
partway through the sweep: the replacement's own status stays `Issued`, cards processed before
the failure are already durably `PermanentlyRevoked`, and a not-yet-reached `Locked` card remains
legitimately recoverable (because no replacement has actually activated yet).

### The whole activate-replacement operation is idempotent end-to-end

The deactivate-the-blocker step and the permanent-revocation sweep are each individually safe to
re-run (a card already `Deactivated`/`PermanentlyRevoked` is a no-op, not an error), and the
gateway's own `ActivateAsync` is itself replay-safe via BIO-CRD-005's own Idempotency-Key
handling. A retry after a partial sweep failure only re-attempts whichever earlier cards were not
yet revoked, then proceeds to activation once the sweep is complete.

### The replacement's own activation preconditions are validated and reserved BEFORE any revocation (BIO-CRD-006-QA-02)

**Finding (Chioma's acceptance review round 2, High).** QA-01's fix alone still let a genuinely
INVALID activation request - a stale or missing `expected_version`, or a replacement that was not
actually `Issued` - run the whole earlier-card revocation sweep before BIO-CRD-005's own
`ActivateAsync` finally rejected it. An operator's typo could permanently destroy earlier cards'
recoverability with no replacement ever activated: a real AC02 violation (recovery is supposed to
remain possible right up until an ACTUAL replacement activation) and a denial-of-service surface.

**Fix.** `ActivateReplacementAsync` now: (1) reserves the caller's Idempotency-Key at THIS
story's own layer via `IIdempotencyStore`, fingerprinted on
`(person, card class, sequence, expected_version)`, before anything else - a genuine replay
short-circuits immediately with the cached result, and a conflicting reuse is rejected outright;
(2) reads the replacement's current snapshot and validates the same preconditions BIO-CRD-005's
own `ActivateAsync` would eventually validate at commit time (exists, `expected_version`
precondition, not already `Deactivated`) - all before any revocation side effect; only once that
dry-run passes does it (3) run the full sweep (still strictly before activation, preserving
QA-01's own fix), then (4) call the real `ActivateAsync`, which remains the final, authoritative
commit including its own independent Idempotency-Key handling as a second safety net.

This dry-run is deliberately NOT a second call to BIO-CRD-005's own `ActivateAsync` - that story
exposes no "validate without committing" mode, and calling the real one twice would either
double-activate or contradict QA-01's own "not externally committed until the sweep succeeds"
rule. It is this project's own narrow re-validation of the same read-only facts, current as of
the moment it is read under this story's own per-(tenant, person, card class) lock (held for the
whole operation). The one disclosed residual: a caller invoking BIO-CRD-005's own raw endpoint
directly and concurrently, bypassing this story's endpoints entirely, could still race the
dry-run - an existing, already-disclosed limitation of composing two independently-locked
services (see "A genuine cross-story runtime dependency" below), not something this finding
introduced or that this story has standing to close from its own side.

**Chioma's own reproduction probe** (`activation_precondition_side_effect_probe.py`) does a
literal text search for `RevokePermanentlyAsync` preceding `cardIssueGateway.ActivateAsync`; it
still reports FAIL after this fix, because the real, authoritative activation call is - correctly,
per the remediation's own second clause - still textually positioned after the sweep. The probe
cannot distinguish that real commit call from the dry-run validation above (which reads the
snapshot via `GetAsync`, not `ActivateAsync`, precisely so it is not itself a second commit
attempt). The property the probe was actually checking - a bad precondition rejected before any
revocation - is instead verified directly: a structural-harness line-order check on the
precondition validation vs. the sweep, and `CardLifecycleActivationAtomicityTests.cs`'s own
failure-injection tests, which assert zero earlier-card lifecycle-record changes when the
replacement's own precondition, existence, or idempotency reservation is rejected.
(Note: after BIO-CRD-006-QA-03's own fix below renamed `RevokePermanentlyAsync` to
`BeginRevocationAsync`/`ConfirmRevocationAsync`/`CompensateRevocationAsync`, this probe - and
QA-01's own `activation_revocation_atomicity_probe.py` - now raise `ValueError: substring not
found` rather than a PASS/FAIL verdict, for the identical reason.)

### A compensating transaction closes the gateway-level time-of-check-to-time-of-use race (BIO-CRD-006-QA-03)

**Finding (Chioma's acceptance review round 3, High).** QA-01 and QA-02 together still left a
genuine race: the dry-run precheck reads a snapshot of the replacement via `GetAsync`, the sweep
runs, and only THEN does the real, authoritative `ICardIssueGateway.ActivateAsync` compare-and-set
run against BIO-CRD-005's own, independently-locked store. A concurrent mutation there between
that read and the final call - for example, someone calling BIO-CRD-005's own raw `/activate` or
`/deactivate` endpoint directly, bypassing this story's endpoints entirely - can still make that
final call fail AFTER the sweep already ran, reproducing QA-01's own "revoked for nothing, no
replacement ever active" outcome through a narrower door. Confirmed via Chioma's own
`activation_gateway_toctou_probe.py`, which also checks whether `ICardIssueGateway.cs` exposes a
`ReserveActivation`/`PrepareActivation` primitive.

**Why this project does not add a true gateway-level reservation.** A genuine fix at that level
would require BIO-CRD-005's own `CardIssueService` to expose a real two-phase reserve/commit
primitive against its own `CardIssue` aggregate - which means modifying that different,
already-accepted story's own owned files (`api/features/bio-crd-005/**`), outside this project's
declared write scope and standing (see this project's own csproj remarks on why no
`ProjectReference` to that project exists at all). Chioma's own remediation text explicitly offers
a second, equally valid path: "or safely compensated" - taken here instead.

**Fix.** `CardLockState` gains two new values: `RevocationPending` (a non-terminal state a card
enters while its own earlier-card sweep is in flight - still denied, still non-recoverable, so
QA-01's own invariant holds throughout, but not yet the terminal `PermanentlyRevoked`) and
`Unlocked` (the compensated outcome for a card that had no prior report). `CardLifecycleRecord`
gains `PriorLockStateBeforeRevocationPending`, recorded durably on the row itself (not a local
variable) so a later, separate retry of an interrupted attempt can still compensate correctly.
`ActivateReplacementAsync` now: (1) `BeginRevocationAsync`s every earlier card to
`RevocationPending` (recording its prior state), (2) calls the real, authoritative
`ActivateAsync`, then either (3a) `ConfirmRevocationAsync`s every pending record to
`PermanentlyRevoked` on success, or (3b) `CompensateRevocationAsync`s every pending record back to
its recorded prior state (or `Unlocked`) if that authoritative call fails. This story's own
Idempotency-Key completion is recorded only once BOTH the activation and the confirmation sweep
have fully succeeded, so a retry after a confirmation-only failure resumes confirming rather than
replaying a stale result. `CheckPresentationAsync`/`GetDenyListAsync` treat `RevocationPending` as
denied, conservatively, throughout the window.

**Chioma's own `activation_gateway_toctou_probe.py`** also cannot report a clean PASS/FAIL after
this fix, for the same reason as the two probes above (it too references
`RevokePermanentlyAsync`), and its `gateway_reservation_present` check would report `False`
regardless, since this project deliberately did not add a `ReserveActivation`/`PrepareActivation`
primitive - see "Why this project does not add a true gateway-level reservation" above for why
that alternative was chosen deliberately, not omitted. The actual required property - no
observable state contains an active replacement with an earlier card destroyed for nothing, under
a genuine concurrent mutation - is verified directly by
`CardLifecycleActivationAtomicityTests.cs`'s own deterministic concurrency tests, which force the
real `ActivateAsync` call to fail exactly once (simulating a raced compare-and-set) and assert the
earlier card is compensated back to its prior state (or `Unlocked`) and remains genuinely usable
afterward - recoverable, presentable, and successfully supersede-able by a subsequent, unraced
retry.

### A real, tenant-scoped recovery policy - not a hardcoded always-true

AC02 says recovery "may reactivate ACCORDING TO POLICY." `ICardRecoveryPolicyStore` is a real,
tenant-scoped, per-reason-code setting (default permitted, explicitly overridable per tenant per
reason code, e.g. an operator may decide a "stolen" report is never recoverable while "lost" is) -
own, right-sized store; no BIO-TEN story is a declared dependency, and this setting is entirely
this feature's own runtime concern.

### A full audit-atomicity apparatus, matching BIO-CRD-005's own `CardIssueService`

Every mutating method holds a per-(tenant, person, card class) lock across its own
read-validate-audit-commit sequence, audits BEFORE committing (BIO-CRD-005-QA-01's own lesson,
applied here from the start), and resolves an ambiguous audit-write throw via
`ICardLifecycleAuditSink.ExistsAsync` rather than assuming failure - the same pattern applied a
second time, now also to `IPresentationEventLog` writes, since AC05 explicitly requires
presentation events to be durably "logged."

### A genuine cross-story runtime dependency, verified through the real packaged host

Unlike BIO-CRD-002/003/004's own "Dependencies is sequencing, not coupling" precedent, this
story's declared dependency on BIO-CRD-005 IS a real, load-bearing runtime call: reporting,
replacing and activate-replacement all read and mutate BIO-CRD-005's own card issue rows.
`ICardIssueGateway` is this project's own narrow interface (no `ProjectReference` to
`api/features/bio-crd-005`); the real implementation is an adapter in `BioCrd006Module` wrapping
BIO-CRD-005's own already-registered, `internal` `CardIssueService` directly - reachable only
because that project's own csproj grants `InternalsVisibleTo Biolog.Api`, the same seam
BIO-CRD-005's own `PersonExistenceGatewayAdapter` already established for its dependency on
BIO-IDN-001. Every gateway call is wrapped in a translation of BIO-CRD-005's own `CardIssueError`
into this feature's own `CardLifecycleError` (same code/status/detail), so this story's own error
handling stays exhaustive over exactly one exception type.
`tests/dotnet/Biolog.IntegrationTests/CardLifecycleHostChannelTests.cs` proves this wiring through
the REAL packaged `Biolog.Api` host - a real tenant domain, a real registered person, a real card
issued and activated through BIO-CRD-005's own endpoints, then reported/replaced/activated through
this story's own endpoints, with BIO-CRD-005's own card issue row genuinely observed
`deactivated` afterward, not merely this story's own lock record.

## Post-acceptance fix: TD-CRD-006-DENY-LIST-DEVICE-AUTH (device-secret auth on the deny-list feed)

**The gap.** `GET /api/v1/card-lifecycle/deny-list` shipped with no auth of any kind - not even
the baseline `X-Actor-Id` assertion every other endpoint on this route table requires. It resolves
the tenant purely from the ambient host-based `ICurrentTenantProvider`, so any caller who could
reach the tenant's resolved host context (the same host every other endpoint on this route table
already trusts for ordinary reads) could pull every `Locked`/`RevocationPending`/`PermanentlyRevoked`
card record for that tenant - the offline-reader deny-list feed AC05 exists to serve. Surfaced by
Busola while scoping BIO-ACC-003, confirmed High severity by Zainab (confidentiality/integrity).
Not currently exploitable beyond this local/synthetic-data environment, but a real gap on an
already-accepted story.

**Why this is a device-secret gate, not an `ActorResolver`/`X-Actor-Id` gate like every other
endpoint here.** This feed exists for an offline READER to pull before going offline (AC05) - a
device, not a logged-in operator presenting an asserted actor id. Re-derived from this story's own
data, per BIO-ACC-001-QA-01's lesson (a sibling story's convention must be re-justified against
what THIS endpoint actually discloses, never pattern-matched on endpoint shape): the caller here
IS the device, so device-secret authentication (ADR-0002 Decision 7) is the correct gate, the same
one BIO-PHO-002/BIO-MOB-003 already established for their own device-authenticated, no-ambient-
actor surfaces.

**The fix.** `HeaderDeviceAuthenticator` (`api/features/bio-crd-006/Api/DeviceAuthenticator.cs`)
mirrors BIO-PHO-002's own `HeaderDeviceAuthenticator` exactly: a request must present both a
well-formed `X-Biolog-Device-Id` and the matching `X-Biolog-Device-Secret` an
`IDeviceCredentialStore` was seeded with out of band (`DeviceCredentialProvisioning`, its own
independent `CardLifecycle:DeviceCredentials:<deviceId>` configuration section - deliberately not
shared with BIO-PHO-002's own section, so the same physical device presenting to two different
feature surfaces is registered, and can be rotated, separately under each). The device must ALSO
exist and be `DeviceStatus.Active` in the resolved tenant (`IDeviceExistenceGateway`, an adapter in
`BioCrd006Module` wrapping BIO-DEV-001's own already-registered `DeviceService.GetAsync`). Every
failure mode (no id, no secret, malformed id, unregistered device, inactive device, wrong secret)
returns the identical generic `401 device_authentication_failed`, so a caller cannot use this
endpoint to enumerate device/credential state.

**Tenant binding, preserved for free rather than reimplemented.** `DeviceService.GetAsync` resolves
the ambient tenant itself (the same `ICurrentTenantProvider` mechanism this endpoint's own
`RequireTenant()` already uses) and looks the device up only within that tenant's own device set -
a caller who authenticates with a real device id/secret pair registered to a DIFFERENT tenant gets
a device that does not exist in the resolved tenant, and therefore does not authenticate. This is
the remediation's "preserving tenant binding" requirement, satisfied by reusing the same
tenant-scoped lookup every other endpoint on this route table already trusts, not a new mechanism -
proven by a real, packaged-host test (`CardLifecycleHostChannelTests.TDCRD006_a_device_genuinely_registered_in_tenant_A_cannot_authenticate_tenant_Bs_deny_list`)
that registers a REAL device through BIO-DEV-001's own endpoints under tenant A's host and proves
its genuinely correct secret still fails against tenant B's deny-list.

**Scope discipline.** Only `GET /deny-list` was ungated; every other endpoint on this route table
already required `ActorResolver`'s asserted-actor check, which this fix does not touch or weaken.
No canonical identifier type minted (`dev_` already exists in the registry); `CanonicalId.IsDeviceId`
added feature-locally, mirroring BIO-PHO-002's own pattern exactly.

**Verified:** `dotnet build` clean; `Biolog.Features.CardLifecycle.Tests` 76/76 (12 new: 8
`DeviceAuthenticatorTests` + 4 new `CardLifecycleEndpointsIntegrationTests` covering no-headers,
wrong-secret, unregistered-device and cross-tenant-device rejection, plus the existing deny-list
positive-path test updated to present real device headers); full `Biolog.IntegrationTests` 159/160
(the one failure, `ObservabilityTests.AC04_...`, is the same pre-existing global-`Console.Out`-
redirection parallel-execution flake already tracked as `TD-FND009-OBSERVABILITYTESTS-FLAKE` -
confirmed by re-running it alone, 9/9 clean, unrelated to this change); structural harness 62/62
(was 46, +16 new checks); `scripts/quality.sh` re-run clean.

## Deliberately out of scope for this story

- Any frontend UI or mobile reader app (see "No frontend UI" above).
- Reopening or extending BIO-CRD-005's own `CardIssueStatus` enum or `CardIssueService` - see "A
  separate lock layer" above; this story owns `api/features/bio-crd-006/**` only.
- A real device/reader offline sync client - this story provides the backend building blocks
  (deny-list feed, idempotent offline-sync intake) a future offline reader story would consume;
  mirrors BIO-IDN-004's own disclosed "no live downstream consumer yet" shape for its credential
  revocation event stream.
- Real per-tenant database persistence - `InMemoryCardLifecycleRepository` and
  `InMemoryPresentationEventLog` are the same "in-memory placeholder behind a real interface" seam
  ADR-0005 requires of every story in this codebase until a Kemi-approved migration lands.
- A configurable recovery-policy MANAGEMENT UI or RBAC gate on `PUT /api/v1/card-lifecycle/recovery-policy` -
  no AC asks for one; the endpoint exists so AC02's "according to policy" is a real, testable
  setting rather than a hardcoded constant, following the same ADR-0006 Decision 1
  audit-attribution-only shape every mutation in this story already uses.

## Post-acceptance fix: TD-CRD-006-IDEMPOTENCY-RACE (idempotency-claim ownership race, two call sites)

**The gap.** Both idempotency-guarded methods - `ActivateReplacementAsync` and
`IntakePresentationEventAsync` - claimed the key, then decided whether to proceed by checking only
`claimed.State == IdempotencyRecordState.Completed`. A shared `InFlight` record from a CONCURRENT
caller racing for the SAME key looked identical to a claim this caller itself just won, so a
second caller fell through the check and executed the side effect a second time regardless -
re-running the whole earlier-card revocation sweep (`ActivateReplacementAsync`) or double-appending
a presentation event (`IntakePresentationEventAsync`), silently overwriting the first caller's
stored result. The realistic trigger is a client reconnect-storm retry - exactly the scenario this
story's own idempotency design claims to defend against. Surfaced by Tunde's own BIO-INT-002-F01
finding (Seyi/Zainab, High), confirmed to also exist here by Kemi reading this file directly
(2026-10-06), ruled fix-now not backlog.

**The fix.** Ported Tunde's BIO-INT-002-F01 remediation verbatim, not redesigned.
`IIdempotencyStore.Claim` now returns an `IdempotencyClaim(IdempotencyRecord Record, bool
IsNewClaim)` - `IsNewClaim` is true ONLY for the caller whose own factory-created record actually
won `ConcurrentDictionary.GetOrAdd`'s race, detected via `ReferenceEquals` on the factory-created
object, never by inspecting the record's own content (identical either way for a genuine replay).
Both call sites now loop: `Completed` → replay the stored result; `IsNewClaim` → break out and
proceed as the sole owner; otherwise (someone else's claim is in flight) → re-`Claim` after a short
poll delay, bounded by a new `_idempotencyWaitTimeout` (5s production default, injectable for
tests), failing closed with the new `idempotency_wait_timeout` (503) error once the deadline
passes rather than waiting forever or falling through to execute anyway.

**Tests - round 1 (insufficient, Zainab CHANGES_REQUIRED).** The first pass added one test per
call site gated only by a shared `TaskCompletionSource` start signal. Zainab correctly rejected
this: a start gate controls when the two tasks are SCHEDULED, not when either actually reaches
`Claim()` - the scheduler could run the whole winning attempt to completion before the loser ever
calls `Claim()`, at which point the loser just takes the ordinary `Completed`-replay path, which
the OLD defective State-only check would ALSO pass. No real discriminating power.

**Tests - round 2 (the actual proof).** Fixed per Zainab's exact direction, porting the pattern
BIO-VIS-002 closed the identical gap with (rounds 9-10, Tunde/Seyi) rather than rediscovering it:

- **`ClaimBarrierIdempotencyStore`** (in `TdIdempotencyRaceTests.cs`) wraps the real
  `InMemoryIdempotencyStore` and holds the WINNING caller blocked immediately after its claim
  becomes durably `InFlight` - strictly before the real service proceeds to its side effect -
  until the test has independently confirmed the losing caller has itself called `Claim` and
  observed that SAME still-`InFlight` record. Only then is the winner released. This drives the
  REAL production call sites (`ActivateReplacementAsync`, `IntakePresentationEventAsync`) through
  the exact contested sequence Zainab specified, deterministically, and proves: exactly one
  execution of the real side effect (the sweep's own `begin_revoke`/`confirm_revoke` audit events
  appear exactly once; exactly one presentation event is appended), both concurrent callers
  observing the identical, stable result.
- **`Mutant_discrimination_the_historical_State_only_check_lets_both_concurrent_claimants_execute`**
  proves the harness shape itself has real discriminating power (Zainab's explicit requirement,
  not optional): a minimal two-caller race forced via a plain `Barrier` so both callers complete
  their own `Claim` observation before EITHER acts on it (mirroring BIO-VIS-002-F09's own
  `checkBarrier` placement exactly - after the observation, before the decision). The real
  decision (`IsNewClaim`) converges to exactly one execution under this forced interleaving; a
  deliberately reinstated copy of the historical decision (`State != Completed` alone) does not -
  both callers execute, proving the mutant violates the exact invariant the real fix satisfies.

The bounded-timeout path itself is not independently re-tested per call site here - Zainab's own
review explicitly agreed one representative behavioral timeout test (in BIO-CRD-005, the simplest
single-call-site sibling) is sufficient, since the timeout branch is identical across all five
call sites and the timeout/poll settings are injectable everywhere.

**Verified:** `dotnet build` clean; feature tests 79/79 (76 pre-existing + 3 new - the 2 round-1
tests were replaced, not kept alongside - zero regression); structural harness 67/67 (62
pre-existing + 5 new checks); full API host build clean. Re-ran the new tests 5x in a row with no
flakiness.

## Post-acceptance fix: BIO-OPS-003-AC04 (production-passed/revocation composition gap)

**Finding (Seyi's BIO-OPS-003 acceptance run, Kemi's architectural ruling, 2026-10-07).** No
endpoint in this codebase composed "production passed" AND "earlier cards revoked" into one
check before activating a replacement card. BIO-CRD-007's own `ActivateAfterProductionAsync`
checks production status but never sweeps/revokes earlier cards (never this story's job). This
story's own `ActivateReplacementAsync` ran the full revocation sweep and activated the
replacement without ever consulting production status at all - a replacement card with a
production job that had genuinely Failed (or no production job at all) could still activate, and
every earlier card would already be permanently revoked by the time anyone noticed.

Kemi verified both services' actual code directly before ruling: the fix belongs entirely here,
in BIO-CRD-006, since this story already owns the hard, compensation-protected revocation
transaction (QA-01 through QA-03 above) - it is the only endpoint that performs the real sweep,
so it is also the only place that can enforce both preconditions together. BIO-CRD-007 needed
zero changes - its own `ProductionService.ListJobsAsync` was already public.

**The fix.** A new, narrow, read-only `IProductionStatusGateway` (`Domain/IProductionStatusGateway.cs`)
- the same shape `ICardIssueGateway` already established for BIO-CRD-005 - carries a decoupled
enum mirror of BIO-CRD-007's own `ProductionJobStatus` (`ProductionJobLifecycleStatus`, the same
pattern `CardIssueLifecycleStatus` already uses) and one method, `HasPassedAsync`, returning
whether the latest production job (by `JobSequenceNumber`) for this card issue has Passed. The
real implementation, `ProductionStatusGatewayAdapter` in `Biolog.Api.Modules.BioCrd006Module`,
wraps BIO-CRD-007's own already-registered `ProductionService.ListJobsAsync` directly - reachable
only because that project's own csproj already grants `InternalsVisibleTo Biolog.Api`, the same
seam every other cross-feature gateway adapter in this module uses. The module's own `DependsOn`
gained `"CardProduction"` (BIO-CRD-007's registered module name) alongside `"Tenant"`,
`"CardIssuance"` and `"DeviceRegistry"`.

`ActivateReplacementAsync` now consults this gateway right after the lock is acquired and BEFORE
the revocation sweep begins - see the class header's own new section. A card whose latest
production job has not Passed throws `CardLifecycleError.ProductionNotPassed` (409, carrying the
latest job sequence number and status for diagnosis) and releases the idempotency claim through
the SAME catch-all every other precondition failure in this method already uses - a retry with
the identical `Idempotency-Key`, once production genuinely passes, is not stuck waiting out a
stale claim. No earlier card is ever touched until this precondition passes.

**Verified by:**
- `CardLifecycleActivationAtomicityTests.cs`: a unit test proving activation is refused when
  production has not passed and the earlier (genuinely Active) card is never swept at all (still
  Active, no lifecycle record created), followed by a same-key retry that succeeds once
  production is seeded as Passed (proving the idempotency claim really was released); a second
  test proving activation still succeeds and sweeps normally once production has passed.
- `CardLifecycleHostChannelTests.cs`, through the REAL packaged host (a real
  `ProductionStatusGatewayAdapter` wrapping BIO-CRD-007's own real `ProductionService`, not a
  fake): a new test proving activation of a replacement with NO real production job at all is
  refused (`production_not_passed`, 409) and the earlier real BIO-CRD-005 card stays genuinely
  Active, never swept; the pre-existing `AC03_AC04_...` test now creates and passes a real
  production job for the replacement card first, through BIO-CRD-007's own real endpoints
  (`/production/jobs` + print/encode/read-back-QC steps), before activating.

**Verified:** `dotnet build` clean; feature tests 81/81 (79 pre-existing + 2 new); full dotnet
integration suite 161/161 (14 CardLifecycle/CardProduction host-channel tests, including the new
negative, +1 over pre-existing); dotnet unit suite 200/200 (unaffected); structural harness 77/77
(67 pre-existing + 10 new checks, 1 pre-existing check's literal `DependsOn` string updated to
match the new array shape). Zero changes to `api/features/bio-crd-007` - confirmed by inspection
(`ListJobsAsync` was already public, no new `InternalsVisibleTo` needed).

## Post-acceptance fix round 2: BIO-OPS-003-AC04 (SY-OPS003-02, cross-feature lock race, Kemi's ruling)

**Finding.** Seyi's round-2 re-review confirmed round 1's sequential gap closed, but found a
genuine time-of-check-to-time-of-use race round 1 did not: `ActivateReplacementAsync` reads
BIO-CRD-007's latest production status via `IProductionStatusGateway.HasPassedAsync` under only
this story's own `CardLifecycleLockRegistry`, while BIO-CRD-007's own mutating methods
(create/print/encode/readback/reject/cancel/offline-complete) each acquire a fully independent
`ProductionJobLockRegistry` for the identical key. A concurrent reprint or failure on BIO-CRD-007's
side can land between the status read above and the real activation call further down,
activating the replacement against a now-stale production snapshot.

Kemi verified both lock registries directly (confirmed genuinely independent
`ConcurrentDictionary<string, SemaphoreSlim>` singletons, zero shared state) before ruling: shared
per-card serialization, not a BIO-CRD-007-owned coordinator (that would invert round 1's
"complexity stays where it lives" reasoning). A new, shared, host-layer
`Biolog.Api.Cards.CardActivationBarrierLockRegistry` (Emeka) - neither feature project may own it
outright (neither may reference the other, or the host project directly -
`scripts/check-module-project-references.sh`) - keyed at `(tenantId, personId, cardClass,
sequenceNumber)`, the same granularity as BIO-CRD-007's own `ProductionJobLockRegistry` (a strict
refinement of this story's own coarser `(tenantId, personId, cardClass)` key). Deadlock safety
checked explicitly: neither feature ever acquires the other's own lock, only this one shared
resource, which cannot form a cycle on its own.

**The fix.** A new, decoupled `ICardActivationBarrier` interface (`Domain/ICardActivationBarrier.cs`)
- the same per-feature split `IProductionStatusGateway` already established - with a real adapter
in `BioCrd006Module` wrapping that SAME shared singleton instance BIO-CRD-007's own module also
wraps (registered with a plain `AddSingleton` in BOTH modules deliberately, not `TryAddSingleton`
- see the module's own header for why that is safe here). `ActivateReplacementAsync` now acquires
it immediately after its own `CardLifecycleLockRegistry` acquisition, held through the real
activation call completing - keyed by the REPLACEMENT's own sequence number only, never needing
to span every earlier sequence number the revocation sweep also touches (that sweep has no
production check of its own to protect).

**Verified by:**
- Two new unit tests proving activation refused-and-never-swept when production has not passed
  (with a same-key retry succeeding once it has), and activation succeeding normally when
  production has passed from the start - unchanged shape from round 1, now exercising the new
  constructor parameter.
- A new, dedicated real-packaged-host test file,
  `tests/dotnet/Biolog.IntegrationTests/CardActivationBarrierHostChannelTests.cs`: the test
  itself acquires the shared lock directly from the real, resolved
  `CardActivationBarrierLockRegistry` singleton for a real replacement card's key, then asserts a
  concurrent real HTTP call to `activate-replacement` genuinely blocks (does not complete within
  400ms) until that held lock is released, then succeeds. A deterministic proof - a real dual-
  service race would be nondeterministic and would not prove WHICH lock either side actually
  acquired. **Confirmed via mutant check**: temporarily removing the new acquisition from
  `ActivateReplacementAsync` made this exact test fail (`Assert.NotSame` failure - the call
  completed immediately rather than blocking), then restoring it made it pass again, 3x
  consecutive clean runs.

**Verified:** `dotnet build` clean; feature tests 81/81 (unchanged count, existing tests extended
with the new constructor parameter, not replaced); full dotnet integration suite 295/295 (+2 new
barrier tests, zero regression elsewhere); dotnet unit suite 211/211 (unaffected); structural
harness 82/82 (77 pre-existing + 5 new checks); `BIO-OPS-003.acceptance.sh`'s own SY-OPS003-02
check (rewritten from a placeholder probe to check for the real `activationBarrier.AcquireAsync`
mechanism in both features) now passes. Mutant-discrimination confirmed directly (see above). No
new ADR - same tier as BIO-DEV-001's own `ICrossTenantDeviceLookup` placement decision.
