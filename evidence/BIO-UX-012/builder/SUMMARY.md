# BIO-UX-012 — Builder evidence

**Story:** Build local Human Test Console and scenario switcher
**Owner:** Damilola — Product UI/UX & Frontend Lead
**Worktree:** `biolog-worktrees/BIO-UX-012`, branch `feature/BIO-UX-012` off `develop`
**Dependencies:** BIO-UX-006/007/008/009/010/011 (all merged; develop was at 371/371 unit tests
before this story)
**Conflict group:** `ui-test-console`
**Commit:** `7c33dd66e309917f431d15541a0cae3618adef26` (round 2; round 1 was `d0902a2d`)

## Round 2 — CHANGES_REQUIRED remediation (Chioma)

Round 1 rejected on one finding: AC03 says scenario changes "update the application
deterministically" for all 8 named dimensions; credential state and document result were, by a
documented Round 1 choice, pure navigation shortcuts with zero observable effect on the
destination page. Fair reading of the AC text. Fixed:

1. **Credential state** — `web/dev-tools/credential-state-preset.ts` orchestrates the same pure,
   already-exported `data-store.ts` mutation functions (`issueCard`/`markCardLost`/`recoverCard`/
   `revokeCard`/`replaceCard`) BIO-UX-007's own registry buttons call — not the async ports,
   which wrap them in a success/failure/offline cycle that would make the preset itself
   non-deterministic. Zero changes to `data-store.ts`. Wired via a new `useEffect` in
   `CardsRegistryApp.tsx`.
2. **Document result** — `DocumentVerifyApp.tsx` computes the Authenticity step's result
   synchronously using the same pure, already-exported `runEvidenceScenario(step, callCount)` its
   own Run button calls — no port call, no new mock/hint mechanism, zero touch to `ports.ts`/
   `mock.ts`/`evidence.ts`. Considered and rejected adding an optional hint parameter to
   `DocumentsPort` (mirroring BIO-UX-008's own pattern) — that would touch a shared interface
   `live.ts` also implements, closer to contract territory than one component's own state.

A real build-time regression caught before this reached review: the first pass gated only inside
`readTestScenarioParam`, one level removed from the `useEffect` call site — typecheck/tests were
clean, but grepping the production bundle found the preset code (including a literal string)
still present, just runtime-unreachable rather than dead-code-eliminated — a weaker guarantee
than this codebase's own established bar (BIO-UX-005's `FeatureModePanel`, this story's own
console UI). Fixed by adding the identical literal `NODE_ENV` check directly at the top of each
`useEffect`; reverified by the same grep, now finding nothing (`round2-web-build-output.txt`).

Full regression: 376/376 unit tests (unchanged), 77/77 Playwright across all directly-affected
specs (console 14/14, BIO-UX-007 cards 17/17, BIO-UX-009 document verify 13/13, BIO-UX-008
biometric journeys 17/17, BIO-UX-010 operational journeys 16/16) — zero regressions in either
domain this round touched. See `round2-*-output.txt` files.

## AC to evidence map

Full design/reasoning in `docs/features/bio-ux-012/README.md`. Summary:

| AC | Evidence |
|---|---|
| AC01 (select tenant/role/connectivity/identity type/credential state/biometric result/document result/device state) | `web/dev-tools/human-test-console.tsx` — 8 selects | `bio-ux-012-human-test-console.spec.ts` AC01 block |
| AC02 (exposes module status, no production-only controls) | Toggle button shows a live SIMULATION/HYBRID/LIVE count via `auditFeatureModes()` | AC02/AC05 block |
| AC03 (deterministic, resettable) | Local console state with fixed defaults; `Reset scenario` restores all 6 new dimensions + tenant/role; round 2 - credential state/document result now also produce a real, distinct, observable state on arrival, not just navigation | AC03/AC06 block, incl. 2 new round-2 tests per fixed dimension |
| AC04 (excluded from production) | `NODE_ENV === 'production'` early-return, dead-code-eliminated | Build-artifact grep (0 matches) + a production-server check (console absent, stray `?scenario=` inert) |
| AC05 (visible LOCAL/feature-mode indicator) | Same toggle text as AC02, always visible before expanding | AC02/AC05 block |
| AC06 (navigate to affected workflow) | Every dimension has a `Link` to its target route(s), carrying the value as a query param where wireable | AC03/AC06 block |

## Developer tests (5 unit tests / 1 file, 12 Playwright tests / 1 file — all pass)

- `bio-ux-012-test-scenario-params.test.ts`: `readTestScenarioParam` in isolation — valid value
  accepted, invalid/typo/absent falls back, and the AC04 safety property itself (production
  always returns the fallback even with a valid window+param present) is directly unit-tested,
  not just proven at the build-artifact level.
- `bio-ux-012-human-test-console.spec.ts`: all 6 ACs, including an axe-core scan of the expanded
  panel, real navigation + query-param pre-seeding checks against the actual target pages
  (Access Control, Fingerprints, Facial Recognition, Students, Cards, Document Verify), and a
  full Reset round-trip.
- Full suite run against a real Chromium browser and a real `next dev`/`next build` in this
  session.

## A real runtime bug found and fixed (not caught by typecheck/build/unit tests)

The first implementation read the query param inside each target page's `useState` lazy
initializer. Typecheck, production build, and the unit test (which stubs `window` directly) were
all clean — but every real-browser Playwright check of the actual wiring failed, because a lazy
initializer also runs during SSR (`window` undefined there), and React preserves that
server-computed initial state through hydration instead of re-running the initializer
client-side. `session-context.tsx`'s own header comment already documents this exact hazard for
`?tenant=`/`?as=` and uses a post-mount `useEffect` instead — I'd missed replicating that when
writing the first pass. Fixed by extracting `useTestScenarioPreset`
(`web/dev-tools/use-test-scenario-preset.ts`): `useState(fallback)` + a one-time `useEffect`.
Full details and the exact reasoning in `docs/features/bio-ux-012/README.md`.

## A real WCAG AA contrast bug found via axe-core (not assumed away)

`.biolog-test-console-links a` initially used `--biolog-color-accent` (brand orange) as link text
colour — 3.48:1 on white at this row's small size, under AA's 4.5:1. Fixed by switching to
`--biolog-color-text` (near-black), keeping the underline for the link affordance. Orange is
meant for solid-fill buttons/accents in this design system, not small link text on a light
surface.

## Accessible-name collision found and fixed (not caught by axe, found by reasoning about the shell)

The console's own "Tenant"/"Role" selects would otherwise share an accessible name with the
shell's *existing* top-bar Tenant/Role selectors (`app-shell.tsx`) — two same-named controls on
one page, ambiguous for assistive tech. Every console select got an explicit
`aria-label="Test console <field>"`; every "Go" link (several of which point at the same target
page for different dimensions) got a distinguishing accessible name.

## Decisions and assumptions

- **Not every dimension is wired into a target page's control.** Identity type, credential
  state, and document result are pure navigation shortcuts, not query-param-seeded — each has a
  documented, specific reason in `docs/features/bio-ux-012/README.md` (none of the three
  corresponds to a single settable value on one page). Still genuinely "selectable" (AC01) and
  "navigable" (AC06) per the AC text.
- **Biometric result seeds only the primary/first control per page**, not every "Simulate
  outcome" sub-section (Fingerprints/Facial Recognition each have up to 5). Deliberate scope
  boundary — inventing semantic mappings across unrelated hint types risked mis-seeding controls
  this story wasn't asked to own. Documented in README.md's "Scope boundary" section.
- **Reused, not reinvented:** `OPERATIONAL_SCENARIO_OPTIONS`/`ScenarioPicker` (BIO-UX-010),
  `auditFeatureModes()` (BIO-UX-005), `PersonCategory`/`CredentialStatus` (BIO-UX-004/006/007's
  synthetic-data types), `CaptureScenarioHint`/`DeviceScenarioHint`/`EvidenceCheckState`
  (BIO-UX-008/009's own port/evidence types) — no parallel type or fixture system invented.
- **`DeviceConnectionPanel.tsx` is a single shared component** used by both `/fingerprints` and
  `/facial-recognition` (kind='fingerprint_scanner'/'face_camera') — one file patch covers both
  pages' device-state wiring.

## Known gaps / local-session notes (not blocking)

- Full-repo `scripts/quality.sh` was not run (same precedent as every prior BIO-UX-00x story this
  session: unrelated backend/.NET/infra suites, no bearing on a frontend-only story). Direct
  AC-relevant commands (`web/` typecheck+build, full unit suite, and the directly-affected
  Playwright specs) all ran to completion and pass — see `*-output.txt` files in this directory.
- 4 pre-existing Playwright failures surfaced in files this story never touches
  (`bio-ux-005-adapter-framework.spec.ts`'s `/nfc-uhf` test; 3 `bio-ux-006-identity-journeys.spec.ts`
  tests — a "Next" button ambiguous with Next.js 16's own dev-tools overlay button in dev mode,
  and an apparent pre-existing suspend/reinstate badge bug). Confirmed via `git diff --stat` this
  story touches none of the involved files. Flagged in README.md and to god, not fixed — outside
  this story's ownership (identity domain is Tunde's).
- `next-env.d.ts` (auto-regenerated by `next dev`, "should not be edited" per its own header) was
  intentionally left out of the commit.
