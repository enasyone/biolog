# BIO-UX-009 — Builder evidence

**Story:** Build interactive Document Verify journeys
**Owner:** Ifeanyi — Document Intelligence Engineer
**Worktree:** `biolog-worktrees/BIO-UX-009`, branch `feature/BIO-UX-009` off `develop`
**Dependencies:** BIO-UX-004 (DONE), BIO-UX-005 (DONE)
**Conflict group:** `ui-documents`

## What was built

Replaced `/document-verify`'s BIO-UX-002 `PlaceholderPage` shell with the real interactive journey
(`web/features/documents/DocumentVerifyApp.tsx`): eight independently-runnable evidence-check steps
(capture/upload, MRZ, barcode, OCR, document identification, NFC, authenticity/evidence-comparison,
portrait-vs-selfie), an evidence-comparison table rendering every step's current state side by
side, a simulated human-review queue that automatic and manual routing both feed, and a selfie
comparison section that explicitly never touches a camera or real biometric data.

`web/features/documents/evidence.ts` is the domain model: a seven-state `EvidenceCheckState`
vocabulary matching AC02's own wording, and a deterministic per-step scenario table (the same
seeded-hash cycling `module-port.ts`'s `createMockAdapter` uses, reimplemented locally since this
story needed richer per-step scenario tables than the shared framework's generic three-outcome
shape). `DocumentsPort` (`ports.ts`) extends the shared `ModulePort` with eight named methods, each
resolving through the SAME `NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE` mock/live switch every
other module already uses (BIO-UX-005-AC04) — no change was needed to `module-port.ts`,
`feature-mode.ts`, or `module-registry.ts`; `ports.ts`'s own header comment (written by BIO-UX-005)
explicitly anticipated exactly this extension shape.

Full AC-to-evidence mapping, the "why not touch the shared framework files" reasoning, and the
determinism note below are in `docs/features/bio-ux-009/README.md`.

## A real bug found by actually running the app, not by trusting the code

My first Playwright draft assumed clicking "Capture/upload document" always succeeds on the first
click. It doesn't — `capture`'s own deterministic first scenario is `failed` (by design; the same
"a module's first call can legitimately be non-success" precedent BIO-UX-005's own spec already
documents for `/devices`' first call landing on `offline`). Running the spec against a real browser
caught this immediately: every test that clicked capture once and expected the rest of the journey
to unlock timed out. Fixed by writing a `clickUntilBadge` helper that retries until the target state
is reached (verified against the actual deterministic sequence via a standalone Node script, not
assumed) rather than hardcoding "click N times" — and added a NEW test asserting the failed-first-
capture-keeps-everything-gated behaviour explicitly, since it is itself a real, intended AC05
behaviour ("state genuinely matters, not just any click") worth locking in as a regression.

## Environment issue found and worked around, not a code defect

This sandboxed machine has an unrelated process (`BIOLOG_Interactive_Prototype_v0.3`, a separate
static prototype, not this repository) permanently bound to port 3000 — the exact port
`tests/frontend/playwright.config.mjs`'s default `webServer` targets, with `reuseExistingServer:
!process.env.CI` meaning a default `pnpm --dir tests/frontend test:e2e` run silently reuses that
UNRELATED server rather than this repo's app. Worked around with `BIOLOG_BASE_URL` pointing at a
`web/` server started separately, on a free port — the SAME technique BIO-UX-005's own builder
evidence (`evidence/BIO-UX-005/builder/metadata.json`) already used for its manual verification
runs, confirming this is a known, precedented pattern for this sandbox, not something new.

Separately, and independent of the port conflict: `next dev` (Turbopack) was observed to be
unstable under Playwright automation in THIS sandbox specifically — button clicks silently no-op.
Verified this is a sandbox/dev-server issue, not a defect in this story's code, by reproducing the
exact same failure against BIO-UX-005's own theme-toggle button (`bio-ux-005-adapter-framework.spec.ts`
is unrelated, already-accepted code) under the same dev server. Switching to a `next build && next
start` PRODUCTION server made every click-interaction test (mine and BIO-UX-005's) pass reliably.
All evidence in this directory was captured against a production server.

## Test/quality evidence

- `pnpm --dir web typecheck && pnpm --dir web build` — clean, `/document-verify` statically
  prerendered among all 34 routes (`web-typecheck-build-output.txt`).
- `pnpm --dir tests/frontend test:unit` — 35 files / 269 tests pass, including this story's own 2
  new files (9 + 6 = 15 tests) (`frontend-unit-test-output.txt`).
- `pnpm --dir tests/frontend exec playwright test bio-ux-009-document-verify.spec.ts` (against a
  production server via `BIOLOG_BASE_URL`) — 12/12 pass, including zero axe accessibility
  violations (`frontend-e2e-test-output.txt`).
- Full repo `scripts/quality.sh --full` (also run against the `BIOLOG_BASE_URL` production server,
  for the same port-3000-conflict reason above): every .NET, contract-harness, Flutter, and
  frontend-unit check passes; 120/122 Playwright tests pass — the only 2 failures are
  `bio-ux-005-adapter-framework.spec.ts`'s dev-only feature-mode-panel tests, which correctly do
  NOT render in a production build (BIO-UX-005-AC03's own requirement, not a regression). Full
  output captured separately (not committed inline — see `test_command` in `metadata.json`; ~56k
  lines dominated by unrelated .NET/Flutter suites already covered by their own stories' evidence).

## Design-system retrofit (post-review, before merge)

Seyi (round 1) and Zainab both passed this story as originally submitted (commit `57e925f`). Before
merge, god directed a mechanical retrofit onto Damilola's cross-story consolidation patch
(`2bcd997` on `develop`, `docs/UI-DESIGN-SYSTEM-CONSOLIDATION.md`), which replaced six independently
invented badge patterns — including this story's own — with one canonical `<StatusBadge tone=...>`
component (`web/components/status-badge.tsx`) and a shared `.biolog-table` class. Merged `develop`
into `feature/BIO-UX-009` (clean, no conflicts — the consolidation patch never touched this story's
own files) and:

- Replaced `document-verify.module.css`'s local `.badge`/`.badgeNeutral`/`.badgeInfo`/
  `.badgeSuccess`/`.badgeWarning`/`.badgeError` classes, and this story's own light-mode contrast
  stopgap (`--document-verify-success-fg`/`-warning-fg`), with `<StatusBadge tone="neutral|info|
  success|warning|error">` — the canonical component avoids the contrast defect by construction
  (neutral text + tinted background + solid left-border accent, not tone-coloured text on its own
  tint), so the stopgap this story added for Seyi's review is no longer needed at all, not just
  superseded.
- Renamed `.comparisonTable` to the shared `.biolog-table` and added a `<caption>` (the table didn't
  have one before — `.biolog-table` gained caption support in the same consolidation patch).
- `StateBadge`/the review-queue-count badge keep their existing `data-testid` attributes by wrapping
  `<StatusBadge>` in a thin `data-testid`-carrying `<span>` (same shape BIO-UX-008's own migrated
  `Badge.tsx` uses for its `role="status"`/`data-testid` — `<StatusBadge>` itself takes no extra
  props by design).
- Re-ran the full evidence set after the retrofit: typecheck/build clean, 328/328 vitest (up from
  269 — the jump is `develop`'s BIO-UX-008/011 test files arriving via the merge, not new tests of
  my own), and all 13 of this story's own Playwright tests pass against a fresh production server,
  including both axe accessibility scans (the multi-tone-badge contrast scan in particular, which
  now exercises the canonical component's contrast approach rather than the retired stopgap).

## Scope notes (deliberately not built)

- No HTTP endpoint (`/api/v1/documents/*`) — `live.ts` genuinely calls the paths a later backend-
  wiring story creates and fails closed to `offline` until then, matching every other module's own
  live-adapter stub (BIO-UX-005-AC04).
- No per-step independent HYBRID mode switching (unlike `ReportingService`'s 2-port precedent) —
  this story's ACs did not require it; all eight step methods resolve together as one module, via
  the existing single `DocumentVerifyService` `primaryAction` mode key.
- No real camera/file-upload API — every step is a simulated port call (AC04's own explicit
  requirement for the selfie step, applied consistently to every step for the same "synthetic,
  reference-safe" security requirement).
