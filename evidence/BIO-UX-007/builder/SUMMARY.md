# BIO-UX-007 — Builder evidence

**Story:** Build interactive cards, credentials and Card Designer journeys
**Result:** PASS

## Restated scope (per the story-stage prompt)

**User story.** As a credential operator, I need the full cards and credential UI working in
simulation so card workflows can be reviewed before issuance services and hardware are complete.

**Acceptance criteria:** AC01 (six interactive pages), AC02 (Card Designer front/back, already
shipped by BIO-CRD-002/003/004), AC03 (lost/recovered/replaced/revoked transitions), AC04
(simulated evidence + SIMULATED marking), AC05 (shared tokens only, no shell edits), AC06 (stable
service ports, including for Card Designer).

**Dependencies.** BIO-UX-004 (synthetic-data/scenario system - extended, see below), BIO-UX-005
(mock/live adapter framework - this story is the first to give a module more than one real,
parameterised port beyond ReportingService's own precedent).

**Owned write paths.** `web/features/cards/**` (new: `CardsRegistryApp.tsx`, `IssueCardApp.tsx`,
`ReplaceCardApp.tsx`, `CredentialsApp.tsx`, `NfcUhfApp.tsx`, `MobileCredentialsApp.tsx`,
`SaveDesignBar.tsx`, `data-store.ts`, `cards.module.css`, `components/*`; extended:
`ports.ts`/`mock.ts`/`live.ts`/`index.ts`), `web/app/cards/**`, `web/app/credentials/page.tsx`,
`web/app/nfc-uhf/page.tsx`, `web/app/mobile-credentials/page.tsx`, `tests/frontend/playwright/bio-ux-007-cards.spec.ts`,
`tests/frontend/unit/bio-ux-007-data-store.test.ts`, `docs/features/bio-ux-007/**`,
`evidence/BIO-UX-007/**`.

**Shared, unlisted entry points touched** (the same role `module-registry.ts`/`Program.cs` play for
every story in this epic - not exclusively owned by any one story):
`web/lib/module-registry.ts` (registers 9 new `CredentialService` port keys),
`web/lib/feature-mode.ts` (`MODULE_PORT_KEYS['CredentialService']` extended to match),
`web/lib/route-manifest.ts` (`nfc-uhf`/`mobile-credentials` resolved from `Unassigned` to
`CredentialService`, with notes explaining why),
`web/lib/synthetic-data/generators.ts` (`generateCredentials` - see "Design decisions" below),
`web/app/card-designer/page.tsx` (adds `SaveDesignBar` alongside BIO-CRD-002/003/004's own
components, does not touch them), `tests/frontend/unit/bio-ux-005-feature-mode.test.ts` (its own
"documents the current shape" guard test updated to include the new multi-port module, per that
test's own stated purpose).

**Read-only shared contracts respected.** `web/app/layout.tsx`, `web/components/*`,
`web/design-system/**`, `web/features/bio-crd-002/003/004/**` (BIO-CRD-002/003/004's own owned
canvas files) - none modified.

**Security/privacy risks identified and addressed.**
- Every synthetic person/tenant/card already carries the `(SYNTHETIC)` marker upstream
  (BIO-UX-004); no new real-looking data was introduced.
- Tenant isolation: every `data-store.ts` mutation takes `tenantId` explicitly and is verified not
  to cross tenants (`bio-ux-007-data-store.test.ts` "tenant isolation").
- No new client-side secret/credential handling - this is a pure UI-simulation story.

**Tests planned and written.** See "Test evidence" below.

## Design decisions

See `docs/features/bio-ux-007/README.md` for the full write-up. Summary: nine independently
resolvable, typed `CredentialService` ports (not one generic action); a feature-owned data-store
singleton rather than shell state; `recovered` as its own credential status (mirrors BIO-CRD-006's
`CardLockState.Recovered`); the NFC/UHF and Mobile Credentials routes resolved from `Unassigned`
per this story's own AC01/AC06; Card Designer gains only a `SaveDesignBar`, its canvas untouched.

## Issues found and fixed during development (not assumed correct, verified)

1. **Baseline dataset left nothing for Issue Card to demonstrate.** Every generated person started
   with an active card, so a fresh `/cards/issue` load had zero eligible people - caught by my own
   Playwright test timing out on a `selectOption` that could never resolve, not assumed working.
   Fixed in `generateCredentials` (leaves the last person per tenant uncredentialed; the RNG draw
   still happens for every person so no other generator's deterministic output shifts - verified by
   rerunning the full `bio-ux-004-*` suite, still green).
2. **Badge/banner colour contrast failed WCAG AA at small size.** `bio-ux-007-cards.spec.ts`'s own
   axe accessibility baseline caught this on the very first run (`colorSuccess`-on-`colorSuccessBg`:
   3.67:1; white-on-solid-`colorSuccess`: 4.14:1; both below 4.5:1 at this size/weight). Fixed by
   switching every status badge/marker/banner to the light tint + shared neutral dark text pairing,
   verified by rerunning axe against all 6 routes.
3. **Two Playwright locator bugs of my own, not production bugs**, both self-caught by fixing the
   test rather than declaring it flaky: a status-text regex incidentally matched the "Mark **lost**"
   button too (strict-mode violation); `getByLabel('Card')`'s default substring match collided with
   the shell's own global search box ("...cards, devices..."). Fixed by scoping to the specific
   table cell and switching to `getByRole('combobox', { name: 'Card', exact: true })`.

None of these were review-round findings - all three were caught and fixed during my own first
implementation pass, before any independent review.

## Post-submission retrofit (commit `7599925`, before merge)

`develop@2bcd997` consolidated the five independently-invented badge patterns from this wave
(BIO-UX-006/007/008/009/010/011) into a single canonical `web/components/status-badge.tsx` +
`.biolog-status-badge` (5 tones). My own neutral-text + tinted-bg contrast fix (see finding #2
above) was one of the two techniques that pattern converged on. Per god's request, retrofitted
`CredentialStatusBadge` to render `<StatusBadge tone="...">` (active→success, recovered→info,
lost→warning, revoked→error, replaced→neutral) instead of its own `.badge*` classes, and removed
the now-dead rules from `cards.module.css`. `simBadge`/`resultBanner*` and the absence of a table
pattern were both explicitly out of scope for this retrofit. Added `status-badge.tsx` and its
globals.css block directly (additively) rather than merging/cherry-picking from develop, since this
branch was 15 commits behind and the consolidation commit also touches BIO-UX-008/011 files not
present here; every CSS custom property the new block depends on already existed in this branch's
tokens. Re-verified: typecheck + build clean, unit 265/265, `bio-ux-007-cards.spec.ts` 17/17
(incl. axe-core on all 6 routes).

## Test evidence

- **`tsc --noEmit` + `next build`**: PASS, zero errors, all 34 routes prerendered statically. See
  `web-typecheck-build-output.txt`.
- **Frontend unit tests** (`tests/frontend`, vitest): 265/265 passing across 34 files, including
  11 new `bio-ux-007-data-store.test.ts` tests (AC01/AC03/AC04: issue/mark-lost/recover/replace/
  revoke transitions, invalid-transition refusals, tenant isolation, durable replace-chain history)
  and the updated `bio-ux-005-feature-mode.test.ts` multi-port guard. See
  `frontend-unit-test-output.txt`.
- **Frontend e2e tests** (`tests/frontend`, Playwright + axe-core): 127/127 passing across 8 spec
  files, including 17 new `bio-ux-007-cards.spec.ts` tests covering every AC (registry render,
  the preserved `/cards` related-links landmark, Issue Card eligibility filtering, mark-lost→
  recover round trip, revoke terminality, Replace Card, NFC/UHF encode+verify, Mobile Credentials
  view+revoke, Credentials detail+revoke, Card Designer's new Save design action alongside its
  unchanged canvas, and axe accessibility on all 6 new/changed routes with zero violations) - no
  regression across the 110 pre-existing tests (BIO-FND-007, BIO-UX-002, BIO-UX-005, BIO-CRD-002/
  003/004). See `frontend-e2e-test-output.txt`.
- **`scripts/quality.sh`**: not run in full for this round - this story's own test commands above
  (typecheck, build, unit, e2e) are the same targeted set BIO-UX-005's own builder evidence used;
  this is a frontend-only story with no backend/.NET files touched.
