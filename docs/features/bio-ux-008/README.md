# BIO-UX-008 — Build interactive biometric journeys

Implements the BIO-UX epic's eighth story (conflict group `ui-biometrics`): the real `/fingerprints`
and `/facial-recognition` routes, previously `PlaceholderPage` shells since BIO-UX-002 (user story,
verbatim: "As an enrolment or verification operator, I need fingerprint and face workflows
simulated end-to-end so capture, quality, match and exception handling can be reviewed early.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Fingerprint tenprint, single-finger, quality, exception and verification screens are interactive | `web/features/bio-ux-008/FingerprintJourneyApp.tsx` — three independent sections (`TenprintSection`, `SingleFingerSection`, `VerificationSection`) on `/fingerprints`, all calling `biometricsPort` | `bio-ux-008-tenprint-state.test.ts`, `bio-ux-008-biometrics-mock.test.ts`, Playwright `BIO-UX-008-AC01` |
| **AC02** | Face capture, quality, 1:1 verification, 1:N identification and liveness placeholder states are interactive | `web/features/bio-ux-008/FaceJourneyApp.tsx` — four independent sections on `/facial-recognition` | `bio-ux-008-biometrics-mock.test.ts`, Playwright `BIO-UX-008-AC02` |
| **AC03** | Simulated device connection, capture progress, quality scores, match/no-match and low-quality outcomes are selectable | `web/features/bio-ux-008/DeviceConnectionPanel.tsx` (device connect/offline); every capture/verify/identify/liveness control in both journey apps carries its own "Simulate outcome" `<select>` sourced from `scenarioOptions.ts`, plus a loading state observable while the (simulated-latency) call is in flight | `bio-ux-008-scenario-options.test.ts`, Playwright `BIO-UX-008-AC03` |
| **AC04** | Biometric simulation is clearly identified as synthetic and never implies real processing occurred | A `role="note"` synthetic banner at the top of both routes; every mock result's own `message` text contains the literal word "SIMULATED" (`mock.ts`); liveness is rendered under a section literally titled "Liveness (placeholder)" | `bio-ux-008-formatters.test.ts`, `bio-ux-008-biometrics-mock.test.ts`, Playwright `BIO-UX-008-AC04` |
| **AC05** | All primary biometric actions work and expose stable ports for later live engines | `web/features/biometrics/ports.ts` extends `BiometricsPort` with seven real methods (`connectDevice`, `captureFingerprint`, `verifyFingerprint`, `captureFace`, `checkLiveness`, `verifyFace`, `identifyFace`); `live.ts` genuinely POSTs to a named `/api/v1/biometrics/*` endpoint per method and honestly throws until a domain story connects one | `bio-ux-008-biometrics-live.test.ts` |

## Design decisions and scope boundaries

**Scope is exactly this story's five ACs, not the dispatch's broader domain description.** The
dispatch that assigned this story described the author's overall domain as "BiometricService
(BIO-BIO-001..007) and PhotoService (BIO-PHO-001..002)", but the approved story's own five ACs
(the higher-authority source per the source hierarchy — approved story/AC outranks a dispatch
message) name only fingerprint and face workflows. `/photographs` and `/enrolment` are therefore
untouched by this story: `/photographs` has no BIO-UX story assigned yet, and `/enrolment` is
BIO-UX-006's own scope (Tunde, "interactive identity and enrolment journeys") — this story does not
redesign or reopen either route.

**`/enrolment`'s existing generic `BiometricService` port usage must keep working.** `/enrolment` is
still a `PlaceholderPage` shell that resolves `MODULE_PORTS.BiometricService.primaryAction` and
calls it generically. `web/features/biometrics/ports.ts`'s `BiometricsPort` interface therefore
*extends* `ModulePort` rather than replacing it — `primaryAction` is preserved byte-for-byte on both
`biometricsMockPort` and `biometricsLivePort`, proven by a dedicated regression test in each of
`bio-ux-008-biometrics-mock.test.ts`/`-live.test.ts`.

**One extended port object, not seven independently-switchable ports.** Reporting (BIO-UX-005) set
the precedent for a module with more than one genuinely distinct action by registering a *second*
generic `ModulePort` instance under a new key in the shared `MODULE_PORT_KEYS`/`MODULE_PORTS`
registries (`web/lib/feature-mode.ts`, `web/lib/module-registry.ts`) — both BIO-UX-005-owned shared
contracts this story's own constraints forbid changing without Kemi's approval. `ports.ts`'s own
prior header comment explicitly invited a domain story to add real, parameterised methods here
instead ("mock.ts/live.ts... are the only files it needs to touch... not any page component") —
capture/verify need a `FingerPosition` argument tenprint's ten independent slots make unavoidable,
which the zero-argument `ModulePort.primaryAction()` shape cannot express at all. All seven methods
therefore resolve together as one object through the single existing
`NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE` switch, without touching either shared registry file. A
later story is free to split one method onto its own independently-switchable port the way
`viewAuditTrail` did, if a real need for partial-HYBRID `BiometricService` state ever arises; nothing
in this story's five ACs asks for that.

**Outcomes are "selectable" via an explicit hint, not only reachable by chance.** The base
`createMockAdapter` (BIO-UX-005) cycles scenarios deterministically by call count with no caller
control. AC03's own wording — "quality scores, match/no-match and low-quality outcomes are
selectable" — asks for more: every named branch must be producible on demand, so a reviewer
verifying "does the exception screen work" does not have to keep clicking and hoping. Every one of
the seven `BiometricsPort` methods therefore takes an optional final `hint` parameter
(`CaptureScenarioHint`, `VerificationScenarioHint`, etc., all defined in `ports.ts`); every screen
exposes it as a `<select>` next to the action button, defaulting to `'Auto (cycles)'` (`hint`
omitted), which still uses the standard deterministic-cycle default every other BIOLOG mock adapter
uses (BIO-UX-005-AC05's own "reproducible, not flaky" property is unchanged, just no longer the only
option).

**Fingerprint capture has an EXCEPTION branch; face capture does not.** AC01 names "quality,
exception and verification"; AC02 names only "capture, quality, 1:1 verification, 1:N
identification and liveness" — no exception wording. `CaptureScenarioHint` (`'good' | 'low' |
'exception'`) backs `captureFingerprint`; the narrower `FaceCaptureScenarioHint` (`'good' | 'low'`)
backs `captureFace` — a real fingerprint capture exception (e.g. a bandaged or missing finger) has
no equivalent scope in this story's own face AC, so it is not invented for symmetry.

**Tenprint and single-finger are two independent capture surfaces, not one shared grid.** AC01 names
them separately ("tenprint, single-finger ... screens"). `TenprintSection` tracks per-finger state
across all ten positions (`web/features/bio-ux-008/tenprintState.ts`, pure and unit-tested
independent of React so it runs under `tests/frontend/vitest.config.mjs`'s `environment: "node"`);
`SingleFingerSection` is a standalone one-shot capture against a caller-chosen finger with its own
local state, matching a real single-finger verification/attendance workflow rather than tenprint
enrolment.

**An exception counts as "attempted", not "incomplete".** `isTenprintComplete` in `tenprintState.ts`
treats any non-`'pending'` slot — captured or exception — as resolved. A real tenprint enrolment
that hits a genuine exception (bandaged finger) is still a completed attempt for that finger, not a
stuck state blocking the rest of the grid.

**Liveness is rendered as a named placeholder, not a hidden simulation.** The story's own user story
names it "liveness placeholder states" explicitly. `LivenessSection`'s heading literally reads
"Liveness (placeholder)" with an explanatory line ("no real liveness detection engine is connected
yet") directly above the control — AC04's "never implies real processing occurred" applied most
strictly to the one operation in this story with no real counterpart shipped anywhere yet (no
BIO-BIO liveness-detection story exists in the backend epic today).

**1:N identification mirrors BIO-BIO-007's own candidate/score shape**, ranked descending, capped at
three synthetic candidates when found — this story is UI-only and does not call the real backend
`FaceSearchService` (no HTTP endpoint exists there yet, see that story's own README), but the shape
(`candidateId`, `displayName`, `score`) is chosen to need no remapping when a future domain story
connects `identifyFace`'s LIVE adapter to it.

**Live adapters throw on failure rather than returning a fabricated domain result.** The generic
`createLiveAdapter` (BIO-UX-005) can report `'offline'` because `PrimaryActionOutcome` has an
`offline` member. This story's own richer result types (`VerificationResult.outcome: 'match' |
'no_match'`, `CaptureQualityResult.outcome: 'good' | 'low'`, etc.) have no such member — smuggling a
network failure into `'no_match'` would let a caller mistake "the live service isn't connected yet"
for a genuine biometric non-match. `live.ts`'s `callLive` helper throws a clear, endpoint-naming
`Error` instead on any network failure or non-OK response — the same typed-outcome-vs-exception
discipline this author's own BIO-BIO-004..007 backend stories use for a genuine caller/integration
error, applied here to the browser side of the same seam.

**No production PII, secrets or tenant data anywhere in this story.** Every displayed name
(`CANDIDATE_NAMES` in `mock.ts`) and every quality/match score is synthetic filler, and the security
requirement ("Simulation must not store or request real biometric data") holds structurally: no
`<input type="file">`, no camera/`getUserMedia` call, and no persistence of any kind exists anywhere
in this story's own code — every result lives only in transient `useState`.

**Human Test Console/mode-switch controls are out of this story's scope.** BIO-UX-012 owns the
dedicated "Human Test Console and scenario switcher"; this story's own per-screen "Simulate outcome"
selectors are ordinary, always-visible product UI (present in every build, not a dev-only overlay),
answering AC03's "selectable" requirement without duplicating or pre-empting BIO-UX-012's own scope.

**No HTTP endpoint exists yet on the backend for any of the seven live methods.** Consistent with
every BIO-BIO-004..007 backend story's own documented "no HTTP endpoint is mapped" scope decision —
this story's LIVE adapters are stable, honestly-failing stubs a future domain/backend story connects
without any change to this story's own page components (BIO-UX-008-AC05).
