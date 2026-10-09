# BIO-BIO-009 — Implement biometric retention and deletion workflows

Implements the BIO-BIO epic's ninth story: governed biometric retention, deletion and legal-hold
behaviour (user story, verbatim: "As privacy administrators, we need governed biometric
retention, deletion and legal-hold behavior.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Retention is configurable by tenant/category subject to policy | `Domain/IRetentionPolicy.cs`/`InMemoryRetentionPolicy.cs` - tenant+category keyed, defaults to `RetentionDefaults`. Gated behind the real `biometric.retention.policy.manage` RBAC grant (see "ADR-0006" below) | `InMemoryRetentionPolicyTests`, `RetentionPolicyServiceTests`, `BiometricRetentionEndpointsTests` |
| **AC02** | Template deletion is audited | `Service/BiometricDeletionService.cs` writes a `biometric_retention.deletion_requested` event BEFORE calling the gateway and a `..._completed`/`..._not_found` event afterward, through `Audit/IBiometricRetentionAuditSink.cs` (ADR-0003 Decision 7 pattern) | `BiometricDeletionServiceTests`, `BiometricRetentionEndpointsTests` |
| **AC03** | Raw image retention defaults off unless authorised | `Domain/IRawImageRetentionPolicy.cs`/`InMemoryRawImageRetentionPolicy.cs` - a tenant absent from the map is unauthorised by construction. Round 3 closed the enforcement gap: BIO-BIO-002's own `FingerprintEnrolmentService.CaptureFingerAsync` now consults this tenant policy (via the new `IRawImageRetentionAuthority` seam) at the write boundary itself, failing closed - see "Round 3" below | `InMemoryRawImageRetentionPolicyTests`, `RetentionPolicyServiceTests`, `FingerprintEnrolmentServiceTests` |
| **AC04** | Deletion propagates to permitted caches/providers without deleting required immutable audit metadata | `Domain/ITemplateDeletionGateway.cs` genuinely deletes the record and evicts its gallery entry (Kemi-approved cross-story extension, round 2); `Audit/IBiometricRetentionAuditSink.cs` has no delete/remove member of any kind, proven structurally. Round 3 closed the durability gap: a failed/throwing eviction no longer reports plain success, and is durably recoverable - see "Round 3" below | `BiometricDeletionServiceTests`, `BiometricRetentionGovernanceTests` |
| **AC05** | The approved web surface (`/fingerprints`, `/facial-recognition`, `/enrolment`) is connected through `BiometricService` to this story's live implementation or an explicitly documented HYBRID state, and the mapped end-to-end human workflow passes without an unapproved UI redesign | Round 3 closed this: `web/features/bio-bio-009/RetentionSection.tsx`, mounted on `/fingerprints`, reaches this story's real endpoints through two new independently-switchable `BiometricService` ports - see "Round 3" below | `bio-bio-009-retention-port.test.ts`, `bio-bio-009-retention-privacy.spec.ts` |

## Round 4: convergent findings from both reviewers, and one genuine disclosed boundary

Both Zainab and Seyi FAILed round 3 (`302c735`), converging on largely the same three points.
Good news from both reviews: AC03 enforcement, the null-key provenance fix, and the typed
pending-cleanup/reconciler design, and the UI surface itself, were all confirmed sound.

### EASY FIX - the provenance secret had no canonical minimum length

`HmacActorProvenanceStore.Configure()` accepted a secret of any length, including trivially
short/guessable ones. Fixed to match BIO-INT-003's own `ActorProvenanceGate.cs` exactly (now
Kemi-ruled canonical floor-wide): a `MinimumSecretLength` of 32, below which `Configure` treats
the secret as unconfigured (never retains a short key). Proven by a new
`ActorProvenanceGateTests.cs` unit-test file (round 4 is the first time this mechanism got its
own dedicated unit tests, not only integration-level coverage).

### REAL GAP - the pending-cleanup obligation was an in-memory singleton, and an ambiguous delete-throw discarded it either way

Two compounding defects in round 3's own durability fix:

1. `InMemoryPendingGalleryCleanupStore` erased its only recovery record on every process
   restart - defeating the entire point of calling the obligation "durable." Fixed with a new
   `FileBackedPendingGalleryCleanupStore` (write-to-temp-then-replace, so a crash mid-write
   cannot corrupt the file), registered as the real production implementation via
   `PendingGalleryCleanupStoreProvisioning` (configurable path,
   `BiometricRetention:PendingGalleryCleanupStorePath`, defaulting to a file next to the running
   assembly). `InMemoryPendingGalleryCleanupStore` is kept for this feature's own pure unit
   tests, which must not touch the filesystem. No full external database exists anywhere in this
   codebase (every store in this system is an in-memory singleton); this is the minimal
   mechanism that genuinely satisfies "survives restart," the documented "at minimum" bar for
   this fix. Proven by `FileBackedPendingGalleryCleanupStoreTests.cs`: two separate instances
   over the same file path see each other's writes, the same shape a real restart has.
2. Any exception from the delete-gateway call removed the pending-cleanup obligation
   unconditionally - but an ambiguous delete-then-throw is NOT the same as "confirmed never
   deleted": if the delete had actually committed before throwing, discarding the obligation
   would strand that gallery entry with no recovery path at all. Fixed by verifying via the
   same read-only `Resolve*OwnerAsync` peek used to resolve the owner in the first place (ADR-0003
   Decision 7 element 4's "ambiguous write throw → verify, never assume" discipline, applied to
   deletion instead of an audit write): only a CONFIRMED "the record still exists" result
   justifies discarding the speculative pending record; a confirmed-gone result, or a failure of
   the verification itself, both leave it in place. Proven by two new
   `BiometricDeletionServiceTests`: a throw whose own verification shows the record is gone keeps
   the pending record; a throw whose own verification ALSO fails keeps it too (never treated as
   confirmed either way).

### NOT THIS STORY'S BUG - the live browser adapter cannot satisfy this story's own identity gate yet

Both reviewers independently found that `web/features/biometrics/live.ts`'s calls send no actor
identity at all - no `X-Actor-Id`, let alone an `X-Actor-Provenance` proof - so round 3's 4/4
Playwright pass only proved the SIMULATED path, never a real end-to-end LIVE workflow. This is
the exact same cross-cutting gap Tunde hit on BIO-TEN-003 (that story's own README "Round-2
disclosed HYBRID boundary" has the full account) and god escalated as
TD-WEB-PROVENANCE-BRIDGE: no browser anywhere in this codebase can satisfy an identity-gated
endpoint yet, because asserting a real actor identity and computing a valid provenance proof
client-side would mean shipping a shared secret in the JS bundle, defeating its purpose. That
bridge is foundational, cross-cutting infrastructure (Kemi/Emeka/Damilola), not something this
story invents alone. Per god's explicit instruction, this is disclosed as an honest HYBRID
boundary, not fixed unilaterally - see "Round 4 - AC05" below for the mechanism and what the new
LIVE-mode Playwright spec actually proves.

**A second, separate, genuinely pre-existing fact found while proving this honestly**: a fresh
`Biolog.Api` host starts with no tenant domain registered at all (BIO-TEN-002's own
control-plane registry starts empty), so before EVEN REACHING this story's own `ActorResolver`,
every tenant-scoped endpoint - not just this story's - 400s with `tenant_context_missing`. This
is not this story's gap either (BIO-TEN-002's own control-plane API is real and fully wired;
nothing here is *broken*, there is simply no tenant registered by default in a fresh
environment) - `bio-bio-009-retention-privacy-live.spec.ts`'s own `beforeAll` works around it by
registering and verifying one real tenant domain through BIO-TEN-002's own real API, the same
one-time setup any fresh deployment would need. Confirmed directly: `curl`-ing the real backend
with no actor header, after registering a tenant domain, returns 400 `actor_required` - the
ACTUAL honest claim this story's own LIVE spec proves, corrected from an earlier draft that
assumed 401 `actor_provenance_required` would be reached first.

## Round 3: both reviewers' required fixes (no waivable disclosures left)

god's dispatch after round 2 carried a floor-wide critical finding plus three non-waivable
findings from Zainab/Seyi. All four are closed in this round; none is a disclosed boundary.

### CRITICAL (floor-wide) - the provenance gate's "unconfigured" fallback was a public, forgeable key

`Api/ActorProvenanceGate.cs`'s `HmacActorProvenanceStore` fell back to a key derived from a
hardcoded literal (`SHA256.HashData("\0no-provenance-secret-configured\0")`) whenever
`BiometricRetention:ActorProvenanceSecret` was unconfigured - anyone reading the source could
compute the same HMAC and forge a valid provenance proof for any actor id, then inherit that
actor's real RBAC grant. The same bug was copy-pasted across 12 other already-merged stories
(Kemi is handling those floor-wide, separately). Fixed here by removing the fallback key entirely:
`Verify` now returns `false` immediately whenever the secret is unconfigured - unconfigured means
"reject everything," never "fall back to a key an attacker can compute." Proven by
`BiometricRetentionEndpointsTests.ZB_BIO009_05_an_unconfigured_secret_rejects_even_the_former_public_dummy_key_proof`,
which builds a second host with the secret genuinely absent and shows that even a proof computed
with the FORMER public fallback key is rejected (401), not accepted.

### HIGH (ZB-BIO009-03/BIO-BIO-009-F01) - AC03 is now enforced at the raw-image write boundary

Round 2's disclosure said BIO-BIO-002's own `retentionAuthorised` session flag controlled raw-image
persistence with no reference to this feature's tenant policy at all. Closed by adding
`api/features/bio-bio-002/.../Domain/IRawImageRetentionAuthority.cs` - BIO-BIO-002's OWN seam
(it cannot reference this later story directly; the dependency points the same direction as
`ITemplateDeletionGateway`, just inverted). `FingerprintEnrolmentService`'s constructor now
REQUIRES this authority (no default), and `CaptureFingerAsync` only retains a raw image when BOTH
the session's own flag AND the authority agree - any exception from the authority (unreachable,
unconfigured) fails CLOSED, never falls back to trusting the caller. The concrete adapter,
`RawImageRetentionAuthorityAdapter` in `BioBio009Module.cs`, wraps this feature's own real
`IRawImageRetentionPolicy` - the one place in the host with references to both projects. Proven by
two new tests in `FingerprintEnrolmentServiceTests.cs`: the tenant authority saying "no" overrides
a session that requested retention, and an authority that throws is treated the same way.

### HIGH (ZB-BIO009-06/BIO-BIO-009-F02) - AC04's gallery eviction is now durably recoverable

Round 2 resolved the deleted record's `PersonId` from the gateway's OWN delete call - if gallery
eviction then threw, that person id lived only on the stack and was lost; if eviction returned
`false`, the service still reported `Deleted`, leaving a biometric gallery entry searchable after
a "successful" deletion with no way to find it again. Closed by:

- `ITemplateDeletionGateway` gained two READ-ONLY peeks, `ResolveFingerprintSessionOwnerAsync`/
  `ResolveFaceReferenceOwnerAsync` (BIO-BIO-003 gained a new, equally plain `ResolveOwnerAsync`;
  BIO-BIO-002 needed no new method since `GetSessionAsync` already existed) - `BiometricDeletionService`
  now resolves the owner and records a durable `PendingGalleryCleanup` entry (`Domain/IPendingGalleryCleanupStore.cs`)
  BEFORE calling delete at all, not after.
- Gallery eviction gets a few bounded in-process retries (`TryEvictGalleryAsync`). If it still has
  not converged, the pending record is left in place (never removed) and the outcome is the new
  `DeletionOutcome.DeletedPendingGalleryCleanup` - deliberately distinct from `Deleted`, so a
  caller can tell "fully converged" apart from "deleted, cleanup still pending."
- `BiometricDeletionService.ReconcilePendingGalleryCleanupAsync` (exposed as
  `POST /api/v1/biometrics/retention/reconcile-gallery-cleanup`) retries every tenant's still-
  pending obligations to convergence - the recovery path for a `DeletedPendingGalleryCleanup`
  result, never a silent drop.

Proven by new `BiometricDeletionServiceTests`: a throw from gallery removal leaves the person id
in the pending store (not lost); a later `ReconcilePendingGalleryCleanupAsync` call converges it
once the gateway recovers; a still-unhealthy gateway correctly reports `stillPending`; a throw from
the delete call itself removes the speculative pending record rather than leaving a false one.

### HIGH (BIO-BIO-009-F03) - AC05 now has a real, connected control surface

Round 2's "no `web/**` file touched" disclosure was explicitly rejected by Seyi - AC05 requires
real connection or a real HYBRID state, not a documented absence. Closed: see "Round 3 - AC05" below.

## Round 2: Zainab's security review (ZA-BIO009-01) and its fixes

Round 1 shipped two real defects, found by Zainab's required security review and fixed before
resubmission:

### CRITICAL - the asserted-actor accommodation was used for an authorization decision, unsafely

Round 1 gated `SetRetentionPolicyAsync`/`SetRawImageRetentionAuthorisedAsync`/`BiometricDeletionService.DeleteAsync`
by comparing the caller-asserted `X-Actor-Permission` header against a required string - exactly
`ZA-IDN006-01`'s shape (ADR-0006 Decision 2): any caller could assert any permission, since the
header is unauthenticated. Fixed by adopting the identical sanctioned pattern BIO-ACC-001/
BIO-ACC-003/BIO-MOB-005 already use for their own authorization-decision endpoints:

- `Api/ActorProvenanceGate.cs` (a fresh project-local copy of the ADR-0006 Decision 3 HMAC-bound
  provenance mechanism, keyed by `BiometricRetention:ActorProvenanceSecret`) verifies the asserted
  actor id BEFORE anything else.
- `Domain/IPermissionGateway.cs`, implemented in `BioBio009Module.cs` by wrapping BIO-IDN-006's own
  already-registered `PermissionService.ActorHasPermissionAsync` directly - a REAL grant lookup
  against that tenant's actual role assignments, never a bare header compare.
- Both checks live entirely in the three mutating endpoint handlers, never inside
  `RetentionPolicyService`/`BiometricDeletionService` themselves - those services now take no
  permission parameter at all (the identical "service takes zero permission parameter, the
  endpoint gates" shape BIO-MOB-005's own `EnrolmentPackageService.SetCapturePolicyAsync` already
  uses). `Api/PermissionResolver.cs` (the naive header-compare helper) is deleted outright, not
  merely unused.
- Input-shape validation (which retention category, which deletion kind) was moved to AFTER
  authorization in all three handlers - round 1 validated the body before checking authority,
  which would have let an unauthorized caller learn whether their request shape was even valid.

**Same disclosed limitation as the rest of this codebase under ADR-0006 Decision 3**: no public
endpoint anywhere can create a tenant's first SUPER_ADMIN or grant, so an integration test cannot
drive a genuine SUCCESSFUL mutation through the real host - only prove the gate correctly REJECTS
an unprivileged or unproven caller (`BiometricRetentionEndpointsTests.cs`'s own two `[Theory]`
tests, mirroring BIO-ACC-001's own `AccessPolicyHostChannelTests.cs`). The positive-path business
logic (policy actually gets set, deletion actually happens) is proven at the unit level instead,
where the services are exercised directly without needing a real grant.

### HIGH - gallery eviction trusted a caller-supplied `PersonId` independent of the record being deleted

Round 1's `TemplateDeletionRequest` carried both `RecordId` (what to delete) and a separately
caller-supplied `PersonId` (whose gallery entry to evict) - nothing checked they actually belonged
together, so a caller deleting person A's record could supply person B's id and evict B's gallery
entry instead. Fixed by construction: `TemplateDeletionRequest` no longer carries a `PersonId` at
all. `ITemplateDeletionGateway.DeleteFingerprintSessionAsync`/`DeleteFaceReferenceAsync` now
return the DELETED record's own `PersonId`, resolved by the sibling story's own service from the
record itself (`FingerprintEnrolmentService.DeleteSessionAsync` already loaded the session before
deleting it to audit it; `FaceEnrolmentService.DeleteReferenceAsync` now loads the reference first
for this same reason) - gallery eviction uses only that verified value.
`BiometricDeletionServiceTests.AC04_gallery_eviction_uses_only_the_identity_the_gateway_resolved_from_the_deleted_record`
proves this directly.

### HIGH - "the real adapter deletes nothing" no longer applies

Round 1's gateway was an honest stub (no sibling store had a delete capability yet). The
Kemi-approved cross-story extension (see "Round 2: the cross-story extension" below) landed in
this same submission, so the gateway now genuinely deletes and genuinely evicts - the
`DeletionOutcome.NotYetAvailable` member this concern was about has been removed from the enum
entirely (dead code once the gap it named was closed), not merely left unreachable.

### AC03 - the disconnected-authority disclosure (round 2; closed in round 3)

Round 2's finding: `IRawImageRetentionPolicy` was never consulted by BIO-BIO-002's own
`retentionAuthorised` parameter (the thing that actually controls whether a raw image is kept on
`FingerRecord.RetainedRawImage`) - a caller of `FingerprintEnrolmentService.StartSessionAsync`
could set `retentionAuthorised: true` regardless of what this feature's own policy said. Round 2
disclosed this as a scope boundary rather than fixing it, reasoning that wiring BIO-BIO-002 to call
INTO this feature would point the dependency backwards.

**That reasoning missed the dependency-inversion option.** The actual closure (round 3) does not
point the dependency either direction described above: BIO-BIO-002 now owns its OWN seam,
`Domain/IRawImageRetentionAuthority.cs`, which it calls through locally; `BioBio009Module.cs` (the
one place in the host with references to both projects) supplies the concrete implementation. No
existing method's decision logic changed shape - `CaptureFingerAsync` gained one additional gate
alongside its existing session-flag check, exactly the "new seam, resolved by the composition
root" pattern `ITemplateDeletionGateway` already used for the opposite direction. See "Round 3"
above for the full mechanism and tests.

### Retention expiry and legal hold (not built, scope boundary named explicitly)

The user story's own prose mentions "legal-hold behavior," but none of the five approved
acceptance criteria operationalise it - no AC asks for an expiry sweep, a hold flag, or a
hold-blocks-deletion rule. Per this repository's own source hierarchy (approved AC text outranks
user-story prose), this story does not build either. Raised explicitly here, not silently dropped,
so a future story that DOES need legal-hold/expiry behaviour knows it inherits a clean slate
rather than an undocumented partial implementation to work around.

## Design decisions and scope boundaries

### Zero ProjectReferences to any sibling BIO-BIO story

This project takes **no** `ProjectReference` at all - not to BIO-BIO-001, and not to BIO-BIO-002/
003/006/007, the four stories whose stores this feature's deletion workflow targets. This mirrors
BIO-IDN-004's own precedent exactly (a write-capable gateway onto a sibling story's aggregate,
with zero runtime coupling to that sibling's actual project). `Domain/ITemplateDeletionGateway.cs`
is a narrow, Result-shaped interface this project owns; `api/Biolog.Api/Modules/
BioBio009Module.cs` (which already references all four sibling projects for its own module
registration) implements it by wrapping their existing services directly. This feature defines
its own local `Domain/BiometricRecordKind.cs` rather than reusing BIO-BIO-001's `BiometricModality`,
so this project's dependency footprint stays at zero sibling ProjectReferences.

### Round 2: the cross-story extension, and why each sibling got a different shape

None of BIO-BIO-002/003/006/007's own stores exposed a delete/remove capability when this story
was first submitted (verified directly). A minimal, additive extension of all four (one new
method each) was requested from and approved by Kemi (hive query/reply, 2026-10-06, same shape as
BIO-IDN-004's own "write-capable extension of an existing gateway seam" precedent):

- **BIO-BIO-002's `FingerprintEnrolmentService.DeleteSessionAsync`** reuses that story's EXISTING
  shared `FingerprintEnrolmentLockRegistry`/`IFingerprintEnrolmentAuditSink`/`AuditAsync` exactly
  like every other mutating method there, recording a new `fingerprint_enrolment.session_deleted`
  event. Kemi's own reasoning: this is genuinely different from the other three, because that
  lock/audit pair is already shared across EVERY existing method (start/capture/complete/cancel) -
  reusing it means a concurrent capture-vs-delete race on the same session is actually serialized,
  a real correctness win only available because the infrastructure already exists and is shared.
- **BIO-BIO-003's `FaceEnrolmentService.DeleteReferenceAsync`, BIO-BIO-006's
  `FingerprintSearchService.RemoveFromGalleryAsync`, BIO-BIO-007's
  `FaceSearchService.RemoveFromGalleryAsync`** are deliberately plain, unaudited, unlocked
  persistence wrappers - matching their own siblings' existing directness exactly
  (`GetLatestReferenceAsync`, `IndexAsync`). Kemi's own corrected reasoning (her first reply
  proposed reusing BIO-BIO-002's shape for these three too; her follow-up caught that none of
  them lock or audit their own EXISTING writes today - `FaceEnrolmentService.CaptureAsync` calls
  `repository.SaveAsync` directly, no lock registry anywhere in that file, same for both gallery
  stores' `IndexAsync`): adding a brand-new lock ONLY for the new method would not close the
  capture-vs-delete/index-vs-remove race either, since the EXISTING methods would never
  participate in a lock only the new method acquires - and retrofitting them to do so would be a
  behaviour change to an already-accepted method, which neither story wants. The only thing a new
  per-store lock would actually serialize (two concurrent deletes of the same record) is already
  free, since these wrappers are reachable only through this feature's own gateway.

For those three plain wrappers, `Service/BiometricDeletionService.cs` - not any of the three
sibling stores - is the one place the full ADR-0003 Decision 7 discipline (lock, audit-before-
commit, ambiguous-throw verification via this feature's OWN `IBiometricRetentionAuditSink`,
`CancellationToken.None` past the confirmed audit point) exists for this operation, recorded once
as this feature's own coherent retention-deletion event rather than four fragmented, inconsistent
partial records - less code than auditing/locking all four separately, and the actual discipline
lives in the one place (this feature's own gateway/service) that needed to invent it.

### AC04 - why a cache-propagation failure never fails the deletion itself

`ITemplateDeletionGateway`'s two gallery-removal methods return a plain `bool`
("propagated/not"), never an outcome the caller must branch on as an error.
`Service/BiometricDeletionService.cs` only attempts gallery propagation AFTER the record itself
has been confirmed deleted, using the gateway-resolved `PersonId` (never a caller assertion - see
"Round 2" above), and records whichever result it gets in the completion audit event's own detail
- a cache that cannot yet be cleared is a disclosed limitation of a secondary, best-effort cleanup
step, never a reason to report a successful, fully audited primary deletion as having failed.

### AC02/AC04 - why audit metadata cannot be deleted by this feature, structurally

`Audit/IBiometricRetentionAuditSink.cs` has exactly two members, `WriteAsync` and `ExistsAsync` -
no delete, no remove, no clear, on either the interface or `InMemoryBiometricRetentionAuditSink`.
Nothing reachable from this feature's own deletion/retention code could delete an audit event even
by a future, careless edit; `BiometricRetentionGovernanceTests` pins this structurally (reflection
over both types' member names), the same structural-not-behavioural proof technique BIO-BIO-008-
AC02 already established for its own distinctness guarantee.

### Every mutation follows ADR-0003 Decision 7's full pattern

`RetentionPolicyService` and `BiometricDeletionService` both implement the same five-element
pattern every prior BIO-BIO mutating service uses: a per-record/per-policy in-process lock
(`BiometricRetentionLockRegistry`), audit-before-commit, an `IBiometricRetentionAuditSink.ExistsAsync`
check resolving an ambiguous write-throw rather than assuming failure, and `CancellationToken.None`
past the point a write is confirmed durable. `BiometricDeletionService` writes two audit events
per request (requested, then the resolved outcome) - both past the same point-of-no-return
discipline, since a caller cancellation arriving after the "requested" event is confirmed durable
must never leave a genuinely attempted (and possibly already-completed) deletion with no
completion record.

### Round 3 - AC05: a real, connected control surface on `/fingerprints`

Round 2 disclosed "no `web/**` file touched" as a boundary, reasoning that inventing a UI
affordance on an already-shipped page would be an unapproved redesign. Seyi explicitly rejected
that reasoning for this story: AC05 requires real connection or a real HYBRID state, and a
documented absence satisfies neither.

The round-3 closure adds a genuinely new, additive section to the existing `/fingerprints` page -
`web/features/bio-bio-009/RetentionSection.tsx`, mounted as one more `<section>` inside
`FingerprintJourneyApp.tsx` after its existing verification section - rather than altering any
existing control. It reaches two new, independently-switchable `BiometricService` port keys,
`manageRetentionPolicy`/`deleteBiometricRecord` (`web/lib/feature-mode.ts`'s `MODULE_PORT_KEYS`),
the same independent-per-port mechanism BIO-BIO-008's own `checkLiveness` established:

- `web/features/biometrics/ports.ts` gained the two method signatures plus their wire types.
- `live.ts` genuinely `POST`s to `/api/v1/biometrics/retention/policy` and `/delete` (this story's
  own real `Biolog.Api` endpoints); `mock.ts` returns a deterministic SIMULATED result, the default
  for every other `BiometricService` action.
- `web/next.config.mjs` gained two scoped rewrite entries (not a `:path*` wildcard, the same
  deliberate narrowing `checkLiveness`'s own entry uses) so the LIVE path is genuinely reachable
  from the browser once switched.
- `BiometricService` genuinely reaches `HYBRID` (BIO-UX-005-AC02) through these two ports alone -
  the other three, still-unconnected actions (fingerprint/face capture, 1:1 verify, 1:N identify)
  are unaffected.

The mapped end-to-end workflow is proven by `tests/frontend/playwright/bio-bio-009-retention-privacy.spec.ts`,
exercising the real `/fingerprints` route in SIMULATION (the module's untouched default): the
section is reachable, updating the policy shows the applied category/day count, and requesting
deletion shows a typed outcome badge.

### Round 4 - AC05: the LIVE-mode proof, and the honest claim it actually makes

`RetentionSection.tsx` gained real error handling in round 4 (`policyError`/`deletionError`
state, surfaced via `role="alert"`, the same pattern BIO-TEN-003's own
`feature-configuration-console.tsx` established) - a genuine, pre-existing correctness gap in
the component itself (an uncaught live-adapter failure left the button stuck in its loading
state forever), not something introduced by this round's disclosure.

`tests/frontend/playwright/bio-bio-009-retention-privacy-live.spec.ts` (new, opt-in via
`BIOLOG_BIO009_LIVE_TESTS=1`, same shape as `bio-ten-003-feature-configuration-live.spec.ts`/
`bio-doc-009-authenticity-live.spec.ts`) proves the one honest AC05 claim available today: a
real browser, talking to a real `Biolog.Api` host through `web/next.config.mjs`'s own rewrites,
is correctly rejected - 400 `actor_required`, because `live.ts` sends no actor identity at all -
and the UI surfaces that failure visibly rather than rendering a false-success state. This file
was run for real against two actual servers (a LIVE-built Next dev server and a real
`Biolog.Api` host) while building this round, not merely authored and left unexecuted; its own
`beforeAll` registers and verifies a real tenant domain through BIO-TEN-002's own control-plane
API first, the one-time setup step any fresh environment needs before any tenant-scoped endpoint
is reachable at all - see "Round 4" above for why that step exists and why it is not a shortcut
around this story's own security gate.

### Every sample/identifier in this suite is synthetic filler

This story's own top-level constraint: "Use synthetic data only." No real biometric payload,
template or raw image exists anywhere in this feature's own code or tests - `BiometricRetentionAuditEvent`
is structurally incapable of carrying one (every property is a plain string -
`BiometricRetentionGovernanceTests.BiometricRetentionAuditEvent_carries_no_byte_array_or_binary_shaped_field`
pins this).

## API surface

| Method | Path | AC | Authorization |
|---|---|---|---|
| `GET` | `/api/v1/biometrics/retention/policy?category={fingerprint_template\|face_template\|raw_image}` | AC01 | none (read) |
| `POST` | `/api/v1/biometrics/retention/policy` `{ category, retentionDays }` | AC01 | `biometric.retention.policy.manage` |
| `GET` | `/api/v1/biometrics/retention/raw-image-authorization` | AC03 | none (read) |
| `POST` | `/api/v1/biometrics/retention/raw-image-authorization` `{ authorised }` | AC03 | `biometric.retention.policy.manage` |
| `POST` | `/api/v1/biometrics/retention/delete` `{ kind: "fingerprint_session"\|"face_reference", recordId, reason }` - outcome is one of `Deleted`/`NotFound`/`DeletedPendingGalleryCleanup` (round 3) | AC02, AC04 | `biometric.retention.delete` |
| `POST` | `/api/v1/biometrics/retention/reconcile-gallery-cleanup` (round 3) - retries every tenant's still-pending gallery-cleanup obligations to convergence | AC04 | `biometric.retention.delete` |

Every mutating endpoint requires `X-Actor-Id`, a valid `X-Actor-Provenance` proof bound to that
actor id (ADR-0006 Decision 3, with no derivable fallback key when unconfigured - round 3,
ZB-BIO009-05), and a real BIO-IDN-006 grant of the listed permission key.
