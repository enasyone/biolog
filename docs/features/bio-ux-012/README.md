# BIO-UX-012 — Build local Human Test Console and scenario switcher

As the human product owner, a local test console changes roles, tenants, connectivity and
synthetic scenarios from one place so the whole product can be repeatedly clicked through while
agents keep building.

**Round 1 CHANGES_REQUIRED (Chioma):** one finding — AC03 says scenario changes "update the
application deterministically" for all 8 dimensions; credential state and document result were
(by a documented, deliberate Round 1 choice) pure navigation shortcuts with no observable effect
on the destination page. A fair reading of the AC text as literally written. Fixed in Round 2 —
see "Round 2 remediation" below. Identity type stays a pure navigation shortcut and was not
challenged: choosing a distinct route (People/Students/Staff/Alumni/Contractors) IS itself a
distinct, deterministic, observable effect, unlike credential state/document result where the
destination page was identical regardless of the choice.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Console can select tenant, role, connectivity, identity type, credential state, biometric result, document result, device state | `web/dev-tools/human-test-console.tsx` — 8 selects, one per dimension | `bio-ux-012-human-test-console.spec.ts` AC01 block |
| **AC02** | Exposes module SIMULATION/HYBRID/LIVE status without production-only controls | Reuses `auditFeatureModes()` (BIO-UX-005) for a read-only counts summary on the toggle itself | AC02/AC05 block |
| **AC03** | Scenario changes update the application deterministically and can be reset | See "Architecture" below; `Reset scenario` restores every dimension to its fixed default | AC03/AC06 block |
| **AC04** | Excluded from production builds or requires a dev-only flag | `process.env.NODE_ENV === 'production'` early-return, dead-code-eliminated at build time — same proven technique as BIO-UX-005's `FeatureModePanel` | AC04 block + a production-build proof (see "Proving AC04") |
| **AC05** | Visible development indicator naming LOCAL and current feature modes | The toggle button itself always reads e.g. "LOCAL test console — 13 SIMULATION", visible before expanding | AC02/AC05 block |
| **AC06** | Navigate from console to the affected workflow without editing code | Every dimension has a `<Link>` to its target route, carrying the chosen value as a query param where wireable | AC03/AC06 block |

## Architecture

Tenant, role and feature flags (BIO-UX-002/011) are already global session state and apply
immediately — the console is a second, more complete place to set them, not a new mechanism.

The other six dimensions (connectivity, identity type, credential state, biometric result,
document result, device state) are new. This console is their only source of truth, held as
local component state — reset to fixed literal defaults is therefore exact, no store/server
round-trip involved (AC03's "can be reset").

Where a target page already has a matching control, the console's chosen value travels as a
query param that page reads once, on mount, via `useTestScenarioPreset`
(`web/dev-tools/use-test-scenario-preset.ts`) — additive: a human arriving at any of these pages
without the console sees completely unchanged default behaviour. This is the same pattern
`session-context.tsx` already uses for `?tenant=`/`?as=`.

**A real bug found while building this, not just asserted away:** the first implementation put
the query-param read inside each page's `useState` *lazy initializer*
(`useState(() => readTestScenarioParam(...))`). This looked correct and passed a unit test, but
failed every real-browser check — because a lazy initializer also runs during SSR, where
`window` is undefined, so it always computed the fallback there; React then preserves that
server-computed initial state through hydration rather than re-running the initializer
client-side, so the URL preset silently never applied. This is exactly the hazard
`session-context.tsx`'s own header comment already documents for `?tenant=`/`?as=` — it uses a
post-mount `useEffect`, not an initializer, for the same reason. Fixed by extracting a
`useTestScenarioPreset` hook that does the same: `useState(fallback)` then a one-time
`useEffect` that applies the preset. Caught by writing real Playwright tests against a real
dev server, not by the unit test (which stubs `window` directly and never exercises SSR/hydration
timing) or by typecheck/build (both were clean the whole time — this was a runtime-only bug).

Wired dimensions and their target(s):

| Dimension | Query param | Target page(s) | What it seeds |
|---|---|---|---|
| Connectivity | `?scenario=` | Access Control, Attendance, Devices, Transport, Visitors | Each page's own `ScenarioPicker` initial value (BIO-UX-010) |
| Biometric result | `?hint=` | Fingerprints, Facial Recognition | Only the *primary* capture control (tenprint hint / face capture hint) — see "Scope boundary" below |
| Device state | `?deviceHint=` | Fingerprints, Facial Recognition | `DeviceConnectionPanel`'s own connection hint (BIO-UX-008), one shared component for both pages |
| Credential state (Round 2) | `?credentialState=` | Cards | Ensures a credential in that exact status exists for the tenant's first person — see "Round 2 remediation" |
| Document result (Round 2) | `?documentResult=` | Document Verify | Sets the Authenticity step's result deterministically — see "Round 2 remediation" |

Not-wired dimensions (pure navigation, still genuinely "selectable" per AC01 and "navigable" per
AC06 — the only remaining one after Round 2):

| Dimension | Why not wired |
|---|---|
| Identity type | Doesn't correspond to one settable value on one page — it selects *which* of six separate routes (People/Students/Staff/Alumni/Contractors) to view, itself a distinct, deterministic, observable effect. The console navigates directly to that route. |

## Scope boundary: only the primary control, not every sub-section

Fingerprints and Facial Recognition each have several independent "Simulate outcome" controls
(tenprint capture, single-finger capture, 1:1 verification, liveness, 1:N identification — up to
five per page, each with its own hint type). AC01 names "biometric result" as one dimension, not
five. Wiring the console's one value into every sub-section would require inventing semantic
mappings between unrelated hint types (e.g. is "failed" a capture concept or a verify concept?)
that aren't obviously correct and risk mis-seeding a control this story wasn't asked to own.
Deliberate, documented choice: the console pre-seeds only the *primary/first* control on each
page (tenprint capture hint; face capture hint) — the rest stay manual, exactly as they already
were before this story, which is the whole point of a *human* test console: a starting point, not
total automation. `readTestScenarioParam`'s validation means an inapplicable value (e.g.
`?hint=exception` arriving at Facial Recognition, whose `FaceCaptureScenarioHint` has no
`'exception'`) safely falls back to manual rather than crashing or coercing.

## Round 2 remediation

**Credential state** (`web/features/cards/CardsRegistryApp.tsx`, `web/dev-tools/
credential-state-preset.ts`): `?credentialState=` deterministically ensures a credential in that
exact status exists for the tenant's first person, via the *same* pure mutation functions
(`issueCard`/`markCardLost`/`recoverCard`/`revokeCard`/`replaceCard`, all already exported from
BIO-UX-007's `data-store.ts`) the registry's own "Mark lost"/"Recover"/"Revoke card" buttons
already call — not the async ports, which wrap those in a success/failure/offline scenario cycle
that would make the preset itself non-deterministic. Idempotent (a repeat visit with the same
target does nothing further); every other target issues one fresh card and transitions it, always
reachable regardless of the person's existing credential history. Zero changes to `data-store.ts`
itself — every function it calls already existed and is exported; the only new code is the
orchestration, in a new dev-tools file.

**Document result** (`web/features/documents/DocumentVerifyApp.tsx`): `?documentResult=`
deterministically sets the Authenticity step's result on arrival, using the *same* pure,
already-exported `runEvidenceScenario(step, callCount)` function
(`web/features/documents/evidence.ts`) the step's own "Run" button calls via
`documentsPort.checkAuthenticity()` — computed synchronously (no port call, no `setTimeout`, no
network path) by searching for the first call count that lands on the requested state. This adds
no new mechanism to the mock/port layer and does not touch `ports.ts`, `mock.ts`, or `evidence.ts`
at all — everything needed was already exported. Console option list scoped to exactly the
Authenticity step's own 3 non-default outcomes (`consistent`/`inconclusive`/`failed`) — dropped
`authentic` from Round 1's list, since that value belongs to the `nfc`/`selfie-comparison` steps'
own outcome sets, not Authenticity's; seeding a step with a value it can never actually produce
would have been worse than the Round 1 gap.

**Why not a `DocumentsPort` interface change instead:** the alternative — an optional hint
parameter on each of the 8 step methods, mirroring BIO-UX-008's own `hint?` pattern — was
considered and rejected for this pass. It would touch the shared `DocumentsPort` interface
(`ports.ts`), which `live.ts` also implements — a later story's actual integration surface, closer
to "shared contract" territory than a single feature's own component. The chosen approach reaches
the same observable outcome without going anywhere near that interface.

**A build-time regression caught and fixed before this even reached review:** the first version of
both seams called their preset-orchestration functions unconditionally from inside the `useEffect`
(the `NODE_ENV` check lived only inside `readTestScenarioParam`, one level removed from the call
site). Typecheck and the full test suite were clean, but grepping the production bundle found the
preset functions' code — including a literal `"SIMULATED preset (Human Test Console)"` string —
still present in `.next/server`/`.next/static`, just runtime-unreachable rather than actually
absent. That is a *weaker* guarantee than the bar this codebase already holds (BIO-UX-005's
`FeatureModePanel`, and this story's own console UI): "dead-code-eliminated entirely," not merely
"never called." Fixed by adding the identical literal `if (process.env.NODE_ENV === 'production')
return;` check directly inside each `useEffect`, at the top, before anything else — verified again
by the same grep, now finding nothing.

Full regression after remediation: 376/376 unit tests (unchanged from Round 1), and all
directly-affected Playwright specs (console: 14/14 incl. 2 new AC03 tests per fixed dimension;
BIO-UX-007 cards: 17/17; BIO-UX-009 document verify: 13/13; BIO-UX-008 biometric journeys: 17/17;
BIO-UX-010 operational journeys: 16/16) pass — 77 tests total, zero regressions in either domain
this round touched.

## Proving AC04

Static analysis and a real production build, not just the `NODE_ENV` check read in isolation:

```sh
corepack pnpm --dir web build
grep -rl "biolog-human-test-console" web/.next/static web/.next/server   # no matches — the
                                                                          # component's markers
                                                                          # are absent from the
                                                                          # production bundle,
                                                                          # dead-code-eliminated
                                                                          # entirely, not just
                                                                          # hidden at runtime
```

Also verified end-to-end against a running production server (`next start`): visiting
`/access-control?scenario=offline` there renders the Scenario select at its normal default
(`normal`), proving the query-param read path is equally inert in production, not just the
console's own UI — the same dead-code-elimination covers both.

## Accessibility

A real WCAG AA contrast bug found via axe-core in this story's own new CSS (not assumed away):
`.biolog-test-console-links a` initially used `--biolog-color-accent` (the brand orange) as link
text colour, which measured 3.48:1 on white at this row's small size — under AA's 4.5:1. Fixed by
switching to `--biolog-color-text` (near-black, always clears AA on a light surface) with the
underline kept for the link affordance; orange is meant for solid-fill buttons/accents, not small
link text.

Also fixed a second, purely accessibility issue (not caught by axe, found by reasoning about the
shell): the console's own "Tenant"/"Role" selects would otherwise share an accessible name with
the shell's *existing* top-bar Tenant/Role selectors (`app-shell.tsx`) — ambiguous for assistive
tech reading two same-named controls on one page. Every console select has an explicit
`aria-label="Test console <field>"`, and every "Go" link has a distinguishing accessible name
(e.g. "Go to Fingerprints (biometric result)" vs "Go to Fingerprints (device state)", since both
rows link to the same two pages) — same reasoning `scenario-picker.tsx` already documents for
itself.

## Regression testing note

While running the full suite for a final check, 4 pre-existing failures surfaced in files this
story never touches: `bio-ux-005-adapter-framework.spec.ts`'s `/nfc-uhf` test, and three
`bio-ux-006-identity-journeys.spec.ts` tests (an enrolment-wizard "Next" button match ambiguous
with Next.js 16's own floating dev-tools button, whose accessible name "Open Next.js Dev Tools"
contains the substring "Next"; and a suspend/reinstate status-badge assertion that appears to be
a genuine pre-existing bug in BIO-UX-006's own identity code). Confirmed via `git diff --stat`
that this story's changes touch none of the files involved. Flagged to god/Tunde rather than
fixed here — out of this story's ownership.

## Local development / verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes, all prerendered
corepack pnpm --dir tests/frontend test:unit   # 376 tests (50 files) pass
corepack pnpm --dir tests/frontend test:e2e    # full suite passes except the 4 pre-existing,
                                                # unrelated failures noted above
```
