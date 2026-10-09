# BIO-UX-009 — Interactive Document Verify journeys

**Story:** As a BIOLOG operator, I need the entire Document Verify experience simulated so MRZ,
barcode, OCR, classification, NFC and authenticity evidence can be reviewed before all engines are
live.

## Acceptance criteria and where each is met

- **AC01** — Document capture/upload, MRZ, barcode, OCR, document identification, NFC and
  authenticity pages are interactive. `DocumentVerifyApp.tsx` renders eight independently-runnable
  step cards (`web/features/documents/evidence.ts`'s `EVIDENCE_STEPS`) at the single
  `/document-verify` route — capture/upload, MRZ, barcode, OCR, document identification, NFC,
  authenticity/evidence-comparison, and portrait-vs-selfie (AC04). Every card has its own "Run"/
  "Retry" button calling its own `documentsPort` method.
- **AC02** — Evidence comparison clearly distinguishes read, structurally valid, consistent,
  authentic, failed, not-checked and inconclusive states. `EvidenceCheckState` is exactly that
  seven-value vocabulary (`evidence.ts`); every step's deterministic scenario table reaches at
  least `failed` and `not-checked`, and the "Evidence Comparison" table on the page (`data-testid=
  "evidence-comparison-table"`) renders every step's current state as a coloured badge, side by
  side, not just per-card.
- **AC03** — Low-confidence OCR and conflicting-source scenarios route to simulated human review.
  Modelled uniformly as `requiresHumanReview(result) = result.state === 'inconclusive'` — OCR,
  document identification, NFC, authenticity and selfie-comparison each have a reachable
  `inconclusive` scenario, not just OCR, since "conflicting sources" (AC03's own wording) most
  naturally applies to the cross-source `authenticity` step. A step landing in `inconclusive` adds
  an entry to the on-page "Human Review Queue" (`data-testid="human-review-queue"`), which a
  reviewer can "Resolve" (marked resolved, not silently removed — an auditable trail). A retry that
  resolves the underlying issue drops the earlier entry for that step. The page-level secondary
  action named in `route-manifest.ts` ("Route to human review") lets a user manually escalate,
  independent of the automatic routing.
- **AC04** — Document portrait-to-live-selfie comparison can be simulated without real biometric
  data. The `selfie-comparison` step never accesses a camera or any real image — it is a labelled
  placeholder plus explicit on-page text ("No camera or real biometric data is used"), matching this
  story's own security requirement.
- **AC05** — No visible primary action may be dead and each page exposes stable ports for later
  live engines. Every button drives a real `documentsPort` method call (never local-only `useState`
  flips with no port behind them); `capture`'s deterministic first call is itself a `failed`
  scenario (see "A note on determinism" below) — proof the buttons reflect a REAL simulated
  service, not a rigged always-succeeds demo. `DocumentsPort` (extends `ModulePort`) is the stable
  seam a later story swaps to `LIVE` via `NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE=LIVE`
  without touching any page component (BIO-UX-005-AC04).

## A note on determinism (read before writing more tests against this page)

Every step's scenario table (`evidence.ts`'s `SCENARIOS`) cycles deterministically by call count,
the same mechanism `module-port.ts`'s `createMockAdapter` uses — but each step's FIRST call is not
guaranteed to be a success. `capture`'s own first deterministic call is `failed`; its first `read`
(success) lands on the third call. This mirrors BIO-UX-005's own precedent
(`bio-ux-005-adapter-framework.spec.ts`: "Devices' own deterministic first call lands on a
different scenario (offline) than Identity's (success)") — a module's first call being non-success
is expected, reproducible behaviour, not a bug. My own first Playwright draft assumed capture always
succeeds on the first click and failed against a live server for exactly this reason; the fixed spec
(`clickUntilBadge`) retries until the target state is reached instead of assuming call #1.

## Dependencies and owned paths

- **Declared dependencies:** BIO-UX-004 (synthetic data — `generateDataset` for applicant/document
  context), BIO-UX-005 (adapter framework — `ModulePort`/`createMockAdapter`/`createLiveAdapter`/
  `resolvePort`).
- **Owned/written paths:** `web/features/documents/{evidence.ts,ports.ts,mock.ts,live.ts,index.ts,
  DocumentVerifyApp.tsx,document-verify.module.css}`, `web/app/document-verify/page.tsx` (replacing
  its BIO-UX-002 `PlaceholderPage` shell), `tests/frontend/unit/bio-ux-009-*.test.ts`,
  `tests/frontend/playwright/bio-ux-009-document-verify.spec.ts`.
- **Read-only shared contracts:** `web/lib/route-manifest.ts` (the `/document-verify` entry already
  named `DocumentVerifyService`/primary+secondary actions — not modified), `web/lib/module-port.ts`,
  `web/lib/feature-mode.ts`, `web/lib/module-registry.ts`, `web/lib/synthetic-data/*` — none of
  these shared framework files needed a change; `ports.ts`'s own header comment explicitly
  anticipates a domain story extending `DocumentsPort` with real methods without touching
  `module-port.ts`/`feature-mode.ts`/`module-registry.ts`, which this story follows exactly (all
  eight step methods resolve together as ONE module-level mode, matching
  `MODULE_PORT_KEYS.DocumentVerifyService`'s existing single `primaryAction` key — no new per-step
  HYBRID granularity was needed to satisfy this story's ACs, unlike `ReportingService`'s own
  two-port precedent).
- **No shared-shell/design-system changes:** reuses existing `.biolog-card`/`.biolog-button-primary`
  classes and `--biolog-color-*` tokens from `web/app/globals.css`; only new CSS is this feature's
  own colocated `document-verify.module.css` (mirrors `bio-crd-002`'s own colocated module.css
  precedent) — no change requiring Damilola's sign-off.

## Security/privacy

- The security requirement ("Uploaded simulation fixtures must be synthetic/reference-safe and
  treated as untrusted input") is satisfied by never accepting a real file upload or camera stream
  at all in this story — every step is a simulated port call against deterministic, synthetic
  scenario data (`web/lib/synthetic-data`), never a live file/camera API.
- No PII beyond the existing SYNTHETIC dataset (`(SYNTHETIC)`-suffixed applicant names, already
  established by BIO-UX-004) is introduced.

## Tests

- `tests/frontend/unit/bio-ux-009-evidence.test.ts` — every AC02 state is reachable by at least one
  step; `runEvidenceScenario` is deterministic; every step reaches `failed`/`not-checked`;
  `requiresHumanReview` is true exactly for `inconclusive`; capture gates every other step.
- `tests/frontend/unit/bio-ux-009-documents-port.test.ts` — every `DocumentsPort` method exists and
  is step-tagged; each step's call counter is independent; the live adapter calls a distinct
  endpoint per step and fails closed to `offline`; an unrecognised live-response `state` value is
  not trusted.
- `tests/frontend/playwright/bio-ux-009-document-verify.spec.ts` — the real route (not a
  placeholder); every AC01 area renders; capture gates and unlocks the rest; a failed capture keeps
  the rest gated; the evidence-comparison table updates; an inconclusive OCR result queues for
  review and a resolve/retry both work correctly; the manual "Route to human review" action; the
  selfie section's no-real-biometric-data disclosure and its own runnable action; zero axe
  accessibility violations.

## Contrast fix (Damilola's review, before Seyi's round 1)

Damilola reviewed the four new component shapes (badges, step-card grid, comparison table, review
queue) against `reference/ui/README.md`'s approved visual family and approved them as compositions
of existing tokens/classes — no new colours, no new style system. She separately flagged a real
risk: the frozen `--biolog-color-success`/`--biolog-color-warning` tokens, used as badge text
directly on their own `-bg` tint at this badge's size/weight (13px/600), were the exact token
pairing she'd found failing WCAG AA contrast (4.5:1 required) in BIO-UX-011's own decision-badge.

Verified directly (not assumed) by computing the actual relative-luminance contrast ratio for
every tone pair `document-verify.module.css` uses:

| Tone | Light mode | Dark mode |
|---|---|---|
| success on success-bg | **3.67:1 — FAILS** | 6.64:1 — passes |
| warning on warning-bg | **3.23:1 — FAILS** | 7.20:1 — passes |
| error on error-bg | 4.58:1 — passes | 5.26:1 — passes |
| info on info-bg | 5.15:1 — passes | 5.52:1 — passes |
| neutral (offline) on offline-bg | 5.01:1 — passes | 5.96:1 — passes |

Both `success` and `warning` fail in light mode only (`error`, unlike in Damilola's BIO-UX-011
case, already passes here at this badge's size — checked, not assumed). Fixed with the SAME shape
as her BIO-UX-011 stopgap (not yet merged, so the shared token can't be imported yet): two new
custom properties scoped to this feature's own CSS module (`--document-verify-success-fg: #146c43`
5.72:1, `--document-verify-warning-fg: #8f5c13` 5.02:1, both computed fresh for this badge's own
background rather than copied from her BIO-UX-011 numbers), overridden back to the real
`--biolog-color-success`/`-warning` tokens in dark mode where they already pass. The frozen
BIO-UX-001 tokens themselves are untouched — reconcile to BIO-UX-011's shared token once that
branch merges.

A new Playwright test (`bio-ux-009-document-verify.spec.ts`, "has no axe violations once success,
warning and error badges are all visible") drives the page into a state with all three tones
rendered simultaneously and re-runs the axe scan — the ORIGINAL "initial load" axe test never
caught this, since every badge starts in the neutral `not-checked` tone before any step runs.

**Superseded by the retrofit below** — kept here as the record of how the defect was found and
fixed the first time; the actual badge markup no longer uses this stopgap.

## Retrofit onto the canonical StatusBadge/.biolog-table (post-review, before merge)

Damilola's cross-story consolidation patch (`2bcd997` on `develop`) picked this story's own five-tone
naming (neutral/info/success/warning/error) as the base for a shared `<StatusBadge tone=...>`
component, since it was the most complete set found across the six stories that had each invented
their own badge pattern. After Seyi's and Zainab's passes on the original submission, `develop` was
merged into this branch and `DocumentVerifyApp.tsx`/`document-verify.module.css` were retrofitted:
the local `.badge*` classes and this story's own light-mode contrast stopgap are gone, replaced by
`<StatusBadge>` (`web/components/status-badge.tsx`), which avoids the contrast defect by
construction (tinted background + left-border accent, not tone-coloured text on its own tint) —
so no stopgap is needed at all now, not just a shared one. `.comparisonTable` was renamed to the
shared `.biolog-table` and gained a `<caption>`. See `evidence/BIO-UX-009/builder/SUMMARY.md`'s
"Design-system retrofit" section for the full evidence re-run.

## Environment note (not a code defect)

This sandboxed development machine has an unrelated process permanently bound to port 3000 (a
separate `BIOLOG_Interactive_Prototype_v0.3` static prototype, not this repository's Next.js app),
which the default `tests/frontend/playwright.config.mjs` `webServer` config would otherwise silently
reuse (`reuseExistingServer: !process.env.CI`), running these tests against the WRONG application.
Manual verification for this story used a `next start` **production** build on a free port via
`BIOLOG_BASE_URL`, and confirmed the SAME instability does not reproduce against a production
server — the `next dev` (Turbopack) dev server was independently observed to be unstable under
Playwright automation in this specific sandbox (button clicks silently no-op even for an
already-accepted, unrelated feature — BIO-UX-005's own theme toggle — confirming it is an
environment issue, not a defect in this story's code). All 15 of this story's own developer tests
(9 vitest + 6 vitest + 12 Playwright, see `quality-output.txt`) pass; the only two Playwright
failures anywhere in the suite are `bio-ux-005-adapter-framework.spec.ts`'s dev-only
feature-mode-panel tests, which correctly do NOT render in a production build
(BIO-UX-005-AC03's own requirement) — expected, not a regression.
