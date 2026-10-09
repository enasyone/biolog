# BIO-PHO-003 — Photo quality and biometric suitability evaluation

As enrolment staff, I need separate print-quality and biometric-suitability assessments.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | A photo can pass print and fail biometric quality, or vice versa | `PrintQualityGate.Evaluate` (frame size/aspect ratio/DPI, measured from the photo's own bytes) and `IBiometricSuitabilityGateway.AssessAsync` (wraps BIO-BIO-003's real `IFaceQualityChecker`/`ILivenessDetector`) run independently; `PhotoQualityCapture` carries both verdicts separately and ALWAYS starts `Pending` regardless of either outcome — no automatic combined gate | `PhotoQualityServiceTests.AC01_*`, `PhotoQualityHostChannelTests.AC01_*` |
| **AC02** | Reasons are visible | `QualityVerdictResponse`/`PhotoQualityCapture` carry `reasons[]` for both verdicts, always, not only when a verdict was the reason for an eventual decision | `PhotoQualityServiceTests.AC02_*`, `PhotoQualityEndpointsIntegrationTests.AC02_*` |
| **AC03** | Approved operator decision is audited | `PhotoQualityService.ApproveAsync`/`RejectAsync` are the only two transitions out of `Pending`, both audited before commit (ADR-0003 Decision 7); `ApproveAsync` promotes a real BIO-PHO-001 `PhotoMaster` via `IPhotoMasterPromotionGateway`, resumably (ZB-PHO003-02 — see "Security review round 1" below) | `PhotoQualityServiceTests.AC03_*`, `PhotoQualityHostChannelTests.AC03_*` |
| **AC04** | No beauty/facial reshaping filters are permitted | No image-processing or computer-vision library is referenced anywhere in this feature — `PhotoQualityRasterInspector` reads header metadata only (format, dimensions, DPI), never decodes or re-encodes a pixel. Categorically, nothing in this project can apply any image filter at all | structural checks in `BIO-PHO-003.test.sh` (no library dependency, no beautify/retouch/reshape source) |
| **AC05** | `/photographs` connected through `PhotoService` to a real implementation; `/enrolment`'s own photo step stays an explicitly documented HYBRID boundary | `/photographs` is a full real screen (submit/list/approve/reject against the real backend); `/enrolment`'s wizard "Photo" step keeps calling the generic `primaryAction()` — see "Design decisions" below for why | `PhotoQualityEndpointsIntegrationTests`, SIMULATION Playwright spec, `BIO-PHO-003.test.sh` |

## Design decisions and scope boundaries

### AC01 is enrolment-staff discretion, not an automated gate — the key difference from BIO-PHO-002

BIO-PHO-002's own `CaptureQualityGate` auto-rejects a failing booth submission before a
`BoothCapture` record even exists — correct for a fully automated device pipeline with no human
reviewer at submission time. This story's own user story is explicitly "As enrolment staff, I
need separate ... assessments" — the staff member IS the reviewer. `PhotoQualityService.SubmitAsync`
therefore never auto-rejects on either verdict: a capture always starts `Pending`, carrying both
verdicts purely as *information* for a human decision. An operator may even approve despite a
failing verdict (proven directly: `AC03_an_operator_may_approve_despite_a_failing_verdict_discretion_is_the_point`)
— that discretion is the literal point of a human-reviewed workflow, not a bug.

### The biometric-suitability gateway wraps BIO-BIO-003's real checkers, never a re-implementation

`IBiometricSuitabilityGateway` (feature-local, no `ProjectReference`) is implemented in
`BioPho003Module` by resolving BIO-BIO-003's own already-registered, PUBLIC
`IFaceQualityChecker`/`ILivenessDetector` directly and building a `BiometricSample` from the same
bytes the staff member uploaded. This is read-only: it never calls
`FaceEnrolmentService.CaptureAsync`, so no `FaceBiometricReference` is ever committed from this
story's own write path — see "Deliberately out of scope" below.

### A real photo deterministically fails BIO-BIO-003's own synthetic face-count check — confirmed, not assumed

`SyntheticFaceQualityChecker` (BIO-BIO-003's own offline-safe stand-in, no real vision model)
reads a sample's `payload[0]` directly as "detected face count" — its own documented test-only
encoding. A real PNG's magic byte at offset 0 is `0x89` (137); a real JPEG's is `0xFF` (255).
Neither format can ever encode the value `1` at that offset, so every real photo this story
submits is deterministically assessed `multiple_faces_detected`, confirmed directly in
`PhotoQualityHostChannelTests.AC01_a_real_submission_shows_real_print_quality_pass_and_real_biometric_suitability_fail_independently`
by actually running it against the real packaged host, not assumed from reading the checker's
own source alone. This is an honest, disclosed consequence of feeding a genuine raster file into
a synthetic checker whose own encoding expects face count at byte offset 0 — not a defect in
this story's own integration, and exactly why AC03 gives the human operator, not an automatic
gate, the final decision: a synthetic checker that can never genuinely pass a real image is a
sound reason an enrolment system needs a human in the loop until a real vision model exists.

### Print-quality baseline reuses BIO-PHO-002's exact specification

`PrintQualitySpecification.Current` reuses BIO-PHO-002's own `CaptureSpecification.Current`
values (1050×1350px, 7:9, 300 DPI — a 35mm×45mm ICAO-style passport-photo frame) rather than
independently re-deriving them: both stories target the same physical card-photo slot, and a
second, differently-tuned specification for the same slot would be an undocumented, confusing
divergence, not a deliberate design choice. No master-spec section or ADR names concrete values
for this, the same disclosed gap `CaptureSpecification`'s own header names.

### `PrintQualityGate` is the print-side-only half of BIO-PHO-002's `CaptureQualityGate`

Face-height/background compliance (BIO-PHO-002's other two capture-specification rules) belong
to the *booth capture* context (a device-attested property of a specific frame grab) and have
no analogue here — this story's biometric side is a real face-count/seven-check/liveness
assessment via BIO-BIO-003, a strictly stronger, real signal for the same underlying concern.
`PrintQualityGate` therefore checks only frame size, aspect ratio and DPI — the properties
genuinely measurable from a photo file's own header, regardless of how it was captured.

### No new image-processing library — AC04's structural proof

No image-processing or computer-vision library (`SixLabors.ImageSharp`, `SkiaSharp`,
`System.Drawing`, OpenCV, etc.) is referenced anywhere in this project, the same disclosed
boundary BIO-PHO-001/BIO-PHO-002 already established. This is not merely a design choice
documented in prose — it is the literal reason AC04 ("no beauty/facial reshaping filters") holds
structurally: nothing in this codebase can decode, resample or otherwise manipulate a pixel at
all, so there is categorically no mechanism through which a beautifying filter could be applied,
accidentally or otherwise.

### Promotion mirrors BIO-PHO-002's own `IPhotoMasterPromotionGateway` exactly

`ApproveAsync` promotes the approved capture's own bytes as a new BIO-PHO-001 `PhotoMaster`
version via a narrow gateway, the identical adapter shape (including the same cross-feature
`ActorType` wire-string round-trip, never a raw numeric cast) BIO-PHO-002's own
`PhotoMasterPromotionGatewayAdapter` already established for its own approval action.

### Actor identity: provenance-verified and permission-gated (ADR-0006 Decision 1/2/3/4) — fixed in security round 1

Round 1 shipped with the BIO-PHO-001/BIO-PHO-002-style "audit-attribution only" shape — wrong for
this story once approval reaches a real `PhotoMasterService.AppendAsync` call. See "Security
review round 1" below for the full ZB-PHO003-01 fix: every route now requires a real
`ActorProvenanceGate` proof plus a tenant-scoped `IPermissionGateway` grant.

### No idempotency key required

Unlike BIO-PHO-002 (whose own AC03 explicitly requires idempotent retries for an unattended
booth's own network-retry behaviour), none of this story's five ACs ask for idempotent retries,
and ADR-0004 §4.3's own fixed list of the four operations that require an `Idempotency-Key`
(card activation, production completion, offline sync intake, webhook intake) does not include
this story's submit/approve/reject — checked explicitly against that list rather than added by
habit.

### No pagination, no delivery/download endpoint

Same boundary BIO-PHO-001/BIO-PHO-002 already established: responses describe the record
(metadata, verdicts, status), never raw bytes; no AC here asks for a bytes-delivery endpoint or
list pagination on a per-person capture history expected to stay small.

### Persistence is in-memory

Same ADR-0005 rationale as every sibling story: `IPhotoQualityCaptureRepository` and
`IPhotoQualityAuditSink` are the seams a real, migration-backed implementation drops in behind.

## AC05 — the UI surface

### `/photographs` is the real, connected screen

A full review screen: load a person's capture history, submit a new candidate photo (a real
file upload, unmodified), see both verdicts with their own reasons, and approve (promotes a real
`PhotoMaster`) or reject (requires a reason) — both through the real backend in LIVE mode. This
is the genuine "connected to live implementation" proof AC05 asks for.

### `/enrolment`'s own "Photo" step stays a disclosed HYBRID boundary — not a gap, a structural fact

BIO-UX-006's enrolment wizard creates its real `Person` only at its FINAL "review" step (via
`identityPort.submitEnrolment`) — every earlier step, including "Photo," operates on purely
local draft state, with no real `personId` in existence yet. This story's real
`submitCapture(personId, file)` genuinely requires an existing person in the resolved tenant
(the same `IPersonExistenceGateway` check every sibling story applies) — there is no sensible
"real" call this step could make without one. Connecting this specific step to a real
per-person submission would require restructuring BIO-UX-006's own wizard sequencing (creating
the person before the photo step runs) — that is BIO-UX-006's own shell, not this story's to
redesign unilaterally, and AC05's own wording explicitly allows "an explicitly documented HYBRID
state" as a valid alternative to a fully live connection. The wizard's "Photo" step therefore
keeps calling `photosPort.primaryAction()` (the original generic shape, additive — this story's
real methods sit alongside it on the same port object, not replacing it).

### Shared-route retargeting (BIO-UX-002/BIO-UX-005's own generic coverage)

`/photographs` was, until this story, BIO-UX-005's chosen example of a still-placeholder route
for its own generic mock-adapter-framework demonstration (itself inherited from `/people`, before
BIO-UX-006 claimed that route). Replacing `/photographs` with a real screen moves that
demonstration again — retargeted to `/administration` (`TenantService`), the one remaining route
in the whole app still rendering the bare `PlaceholderPage` shell (confirmed directly: every
other candidate route already renders a real domain screen). Verified the retargeted assertions
by actually running the spec against `/administration`, not assumed from module-name similarity.

## Deliberately out of scope for this story

- Real pixel resampling, face detection, or any other genuine computer-vision analysis (no
  library exists anywhere in this host — the same disclosed boundary BIO-PHO-001/BIO-PHO-002
  already established).
- Creating a BIO-BIO-003 `FaceBiometricReference`. Approving a photo here only promotes a new
  `PhotoMaster`; it never calls `FaceEnrolmentService.CaptureAsync`. None of this story's five
  ACs ask for a biometric reference to be created — only that a biometric SUITABILITY signal
  exists and is visible. A later story that wants "approving a photo here also enrols the
  biometric reference" is a new, disclosed decision for whoever owns that workflow.
- Restructuring BIO-UX-006's own enrolment wizard sequencing (see "AC05" above).
- A bytes-delivery/download endpoint or CDN integration for a capture's own content.
- Real MySQL-backed persistence (ADR-0005).

## Security review round 1 (Zainab, CHANGES_REQUIRED, fixed)

### ZB-PHO003-01 (Critical) — no provenance or RBAC gate at all

Round 1's `ActorResolver` carried BIO-PHO-001/BIO-PHO-002's own disclosed "audit-attribution
only" shape — stale by the time this story's write path reaches
`PhotoMasterService.AppendAsync`, which genuinely replaces a person's authoritative photo. Any
caller who could reach a tenant host and knew a valid person id could upload/approve an
arbitrary photo for anyone, and list biometric-suitability verdicts/decision reasons/photo
metadata for anyone, with zero proof of identity and zero authorization check.

**Fixed**: a fresh, feature-local `ActorProvenanceGate` (the same canonical HMAC-over-actor-id
pattern every current tenant-plane story uses — ported from and verified against
`api/features/bio-int-004/.../Api/ActorProvenanceGate.cs`, never a dummy-key fallback) plus a
feature-local `IPermissionGateway` wrapping BIO-IDN-006's own real, internal
`PermissionService.ActorHasPermissionAsync` (the same adapter shape `BioBio009Module`/
`BioAtt003Module` already established) are now required on **every one of this feature's five
routes, reads included** — unlike BIO-ATT-003's own capture/read split, biometric-suitability
verdicts and operator decision reasons are sensitive enough that a read needs the same real
grant check as a write, just a lesser-privileged key. Three distinct, least-privilege permission
keys: `photo_quality.capture.submit`, `.decide`, `.view`. `BioPho003Module.Configure` seeds the
demo web bridge's grant for `RoleType.Enrolment` (the "staff" role — this user story is literally
"As enrolment staff"), the same "seed data, not provisioning logic" shape `BioAtt003Module`/
`BioTen003Module` already established. `web/lib/actor-provenance-routes.ts` carries this
feature's own `PhotoQuality:ActorProvenanceSecret` entry so TD-WEB-PROVENANCE-BRIDGE's edge
(`web/proxy.ts`) actually injects a proof for `/api/v1/photo-quality/*`.

Proven via real-host negatives (missing provenance, forged provenance under the wrong secret,
and a real registered person genuinely holding no grant — all three real, un-substituted) plus
an authorized-positive path (this project has no `InternalsVisibleTo` access to seed a real
`RolePermissionGrant` into BIO-IDN-006's internals, the same disclosed limitation
`ApiSecurityEndpointsTests` already establishes, so the host-channel positive/business-logic
tests substitute only this feature's own public `IPermissionGateway` with an always-granted fake
— never Rbac's internals, never the real `ActorProvenanceGate`). The real, un-substituted RBAC
check is proven end-to-end by the unprivileged-real-actor negative above and, with a real seeded
grant, by `PhotoQualityEndpointsIntegrationTests`' own feature-hosted `TestServer` (a real,
controllable `FakePermissionGateway`).

### ZB-PHO003-02/03/04 and SY-PHO003-07 (High → Medium → Medium) — approval atomicity, resolved by an architectural ruling, not a sixth patch

**Round 1's fix (closed, superseded).** `ApproveAsync` called `IPhotoMasterPromotionGateway.PromoteAsync`
before writing the `photo_quality.capture.approve` audit event and before committing the
capture's own `Approved` transition. If the audit write was genuinely unavailable (or the
process crashed) in that window, the external `PhotoMaster` had already been appended and
independently committed, the capture remained `Pending`, and a retry would promote a **second**
master version — "disclosed, not fixed" was not good enough once a reviewer actually injected
the failure. Fixed by writing a marker the instant `PromoteAsync` returned, before
auditing/finalizing.

**Round 2 (Zainab): that marker was still written AFTER `PromoteAsync`**, leaving the window
between the external call succeeding and the marker's own commit completely unprotected, plus
omitting the initiating actor (a different operator's retry could misattribute the audit).
Fixed by moving the marker BEFORE the call and re-querying BIO-PHO-001's own "latest
version"/"version content" fresh each attempt, treating a version beyond a recorded baseline
whose CONTENT matched this capture's own bytes as this attempt's own earlier promotion.

**Round 3 (Zainab): that content/position heuristic is not a stable identity** — an unrelated
append sharing identical content could be falsely claimed, and a LATER unrelated append could
permanently bury this attempt's own earlier promotion. Fixed by carrying the stable attempt id
into BIO-PHO-001's own promotion-boundary audit event (`photo.master.append`, written
audit-before-commit) and matching on that exact `CorrelationId`.

**Round 4 (Seyi, SY-PHO003-07): that audit event is itself written BEFORE the repository commit
it describes** — its mere existence is not proof the commit succeeded. Fixed by independently
confirming the claimed version against BIO-PHO-001's own repository before trusting it.

**Round 5 (Zainab, ZB-PHO003-04): confirming a version EXISTS is not confirming THIS attempt
produced it** — an unrelated attempt legitimately committing the identical version number (the
exact same "failed-then-retried" scenario round 4's own fix relies on) could let this attempt's
retry misattribute that unrelated master as its own. This was the **third consecutive round**
narrowing the identical underlying mistake rather than closing it.

**Kemi's architectural ruling, not another adapter patch.** Every one of rounds 3–5 was trying
to RECONSTRUCT "did this exact attempt already promote" from BIO-PHO-001's own post-hoc state
(content, then an exact audit lookup, then a repository-existence check) — each fix closed one
failure shape and immediately exposed an adjacent one, because `PhotoMaster` genuinely has no
caller-supplied identity field, and inferring ownership of an external side effect from that
system's own observable state can never be fully disambiguated from outside it. **The real fix
is to stop reconstructing and start REMEMBERING** — the exact same category of fix as BIO-PHO-002's
own `TD-PHO-002-IDEMPOTENCY-RACE` remediation (`IIdempotencyStore`/`IsNewClaim`), applied to an
unrelated operation. `PhotoQualityCapture.PendingApprovalAttemptId` was already the right
crash-survivable key — no new field anywhere, on either side.

**The final shape.** `ApproveAsync` still records the durable INTENT marker (attempt id,
immutable initiating actor) BEFORE ever calling `PromoteAsync` (unchanged ordering from round
2). It then `Claim`s that SAME attempt id as a LOCAL idempotency key, via a fresh,
project-local `IIdempotencyStore` (`api/features/bio-pho-003/Domain/IdempotencyStore.cs`, the
same copy-per-feature convention BIO-CRD-001/BIO-PHO-002 already established), BEFORE calling
`PromoteAsync`: a genuine first attempt (`IsNewClaim`) executes the real promotion once and
`Complete`s the claim with the resulting master version; any resumed retry (same OR a different
authorized actor) finds the claim already `Completed` and returns ITS stored result directly —
**zero further calls to BIO-PHO-001 at all**, no reconstruction, no inference, nothing to get
wrong. A genuinely failed promotion `Release`s its claim so a real retry can actually try again
rather than waiting out a pointless bounded wait. `IPhotoMasterPromotionGateway` shrinks back to
exactly BIO-PHO-002's own shape — just `PromoteAsync`, nothing else; BIO-PHO-001's own module
registration reverts to its original single-line form (the round-3/4 concrete-type registration
this feature no longer needs is gone). Zero change to `PhotoMaster`, its repository, or any
shared contract — no ADR needed.

**Named residual (Kemi's own ruling, disclosed and deliberately not chased further).** A crash
strictly between `PromoteAsync` succeeding and `ApproveAsync`'s own `Complete()` call leaves that
claim stuck `InFlight` forever in the volatile, process-lifetime idempotency store. A retry's
own bounded poll-wait then times out and throws `idempotency_wait_timeout` (503) rather than
silently reusing wrong data — the same "crash residual inert with volatile singletons" boundary
every in-memory placeholder in this codebase carries pending ADR-0005's real persistence. This
fails LOUD instead of today's SILENT wrong-photo risk — strictly better, and explicitly not a
new gap to fix in this story.

Proven by `PhotoQualityServiceTests`: a crash before the marker is ever written (`PromoteAsync`
never attempted); a genuinely failed promotion whose claim is released so a real retry actually
re-promotes; a crash after promotion but before the audit write (a different operator's retry
finds the claim already `Completed` and returns its stored result, crediting the original
actor); a crash after the audit write but before the final commit (never re-promoted, never
re-audited); and Kemi's own named residual, reproduced directly (claim the key, let the real
promotion succeed, then simulate the crash by never calling `Complete` — a retry under a
different operator times out loud rather than reusing anything).

## Verified

- `dotnet build api/Biolog.Api/Biolog.Api.csproj` clean.
- `dotnet test tests/features/bio-pho-003/Biolog.Features.PhotoQuality.Tests.csproj`: 39/39
  passing (up from 23 — ZB-PHO003-01's `ActorProvenanceGateTests` plus six idempotency-store
  tests covering every crash window, a genuine-retry-after-failure path, and Kemi's own named
  residual).
- `dotnet test tests/dotnet/Biolog.IntegrationTests` (full suite): 250/250 passing (up from
  247 — the 3 new `PhotoQualityHostChannelTests` provenance/RBAC negatives, replacing round 1's
  ungated `ActorRequest` helper with a real HMAC-proof one throughout).
- `dotnet test tests/dotnet/Biolog.UnitTests`: 210/210 passing (unaffected).
- `bash scripts/check-module-project-references.sh`: PASS.
- `python3 scripts/check-traceability.py --all`: PASS (36 stories).
- `bash scripts/secret-scan.sh`: clean.
- `bash tests/features/bio-pho-003/BIO-PHO-003.test.sh`: 55/55 passing (up from 24 — 20 new
  ZB-PHO003-01/02/03/04 and SY-PHO003-07 checks).
- `web`: `tsc --noEmit` clean; `next build` clean (34 routes + Proxy middleware, `/photographs`
  now server-rendered as static content like every other real screen).
- `tests/frontend`: `vitest run` — 548/550 passing (the same 2 pre-existing, unrelated
  `bio-ux-005-*` `CredentialService`/`IntegrationService` port-registry fixture failures every
  other story's own session has independently confirmed on a clean `develop` checkout);
  `unit/td-web-provenance-bridge-route-table.test.ts` 39/39 (up from 37 — this story's new
  `PhotoQuality:ActorProvenanceSecret` route-table entry).
- Targeted SIMULATION Playwright: `bio-ux-005-adapter-framework.spec.ts` (retargeted to
  `/administration`) 7/7; `bio-ux-002-shell.spec.ts` and `bio-ux-013-axe-baseline.spec.ts`
  re-checked against the real `/photographs` screen.
