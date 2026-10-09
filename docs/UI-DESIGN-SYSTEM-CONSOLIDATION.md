# UI design-system consolidation — 2026-09-23

This records a direct-patch consolidation to `develop`, agreed with god per the W01.5 wave's
design-system-owner process (BIO-UX-001's constraints name Damilola as owner; process for this
specific patch — direct commit with evidence logged here rather than a formal story — was
explicitly approved, not self-authorized). No AC or functional behaviour changed; this retires
duplicate/conflicting CSS and one shipped accessibility defect.

## What happened

Six independent stories in the W01.5 wave (BIO-UX-006, 007, 008, 009, 010, 011) each needed a
status badge, and five needed a data table. None of the frozen BIO-UX-001 tokens or reference
screenshots covered either pattern, so each story invented its own:

- Badge: `.biolog-decision-badge` (011), `.biolog-op-badge` (010), `.biolog-identity-badge` (006),
  a CSS-module `.badge`/`data-tone` (007, 009, and — found only once merged — 008).
- Table: `.biolog-table` (011), `.biolog-op-table` (010), `.biolog-identity-table` (006),
  `.comparisonTable` (009).
- `.biolog-button-secondary`: independently defined as a new global class by both 010 and 011,
  same selector, different rules (padding, background, and 011 was missing a `:disabled` rule 010
  had) — a silent cascade-order conflict, not just inconsistency, since CSS doesn't flag two
  files appending conflicting rules for the same selector as a merge conflict.

BIO-UX-008's badge had already merged (`70b899e`) with the colour-on-its-own-tint pairing
(`color: var(--biolog-color-success)` on `background: var(--biolog-color-success-bg)`, 12px/600
weight) that four of the wave's other five badges independently found and fixed as a WCAG AA
contrast failure (~3.67–4.4:1 in light mode, under the 4.5:1 minimum for text this size) — and it
had shipped with no axe-core coverage, so nothing had caught it.

## Decision

- **Badge:** canonical technique is the tinted `-bg` per tone (unchanged — preserves the existing
  approved visual language) with **neutral body text** (`--biolog-color-text`, not the tone
  colour) plus a solid 3px left-border accent in the tone's colour. Two of three
  independently-built fixes (BIO-UX-007, BIO-UX-010) converged on this without seeing each
  other's work, which is why it was chosen over the alternative already in `develop`
  (BIO-UX-011's own darker-token approach, which is retired by this patch). Five tones:
  neutral/info/success/warning/error, mapped from BIO-UX-009's naming (the most complete set of
  the five). New shared component: `web/components/status-badge.tsx` (`<StatusBadge tone="...">`),
  backed by canonical classes `.biolog-status-badge`/`.biolog-status-badge--<tone>` in
  `web/app/globals.css`.
- **Table:** canonical is `.biolog-table` (already in `develop`), gaining `<caption>` support
  backported from BIO-UX-010's `.biolog-op-table`.
- **`.biolog-button-secondary`:** kept BIO-UX-011's padding (`0.6rem 1.25rem`, matches
  `.biolog-button-primary` — the two are paired side by side in every real usage) and background
  (`transparent` — correct regardless of the parent surface colour, unlike a hardcoded
  `var(--biolog-color-surface)`), added BIO-UX-010's missing `:disabled` rule (a real completeness
  gap, not a style choice).
- **BIO-UX-005's `.biolog-feature-mode-badge--live`** had the identical contrast defect (it's the
  actual shell precedent the wave's badges were extending) — migrated to the same neutral-text +
  border-accent technique in the same pass, since it was cheap and left live otherwise.

## Fix

**Principled/root-cause**, not a per-story patch: one shared component + canonical CSS classes,
consumers migrated to it, the superseded classes/tokens removed rather than left dead.

- Removed `--biolog-decision-success-fg`/`-error-fg` custom properties and `.biolog-decision-badge`
  (BIO-UX-011); `web/app/audit/page.tsx` now uses `<StatusBadge tone="success"|"error">`.
- BIO-UX-008's `web/features/bio-ux-008/Badge.tsx` now renders `.biolog-status-badge` classes
  directly instead of its own CSS-module badge (removed from `biometric-journeys.module.css`);
  `BadgeTone` (`'success' | 'warning' | 'error'`) is a structural subset of `StatusBadgeTone`, so
  no formatter changes were needed. Added axe-core coverage to
  `bio-ux-008-biometric-journeys.spec.ts` (previously none existed for this page — the actual gap
  that let the defect ship unnoticed).
- `.biolog-feature-mode-badge` variants: text switched from tone colour to
  `--biolog-color-text` + added `border-left` accent (`web/dev-tools/feature-mode-panel.tsx`, the
  only consumer, needed no changes — pure CSS fix).
- `.biolog-table` gained `caption` styling.
- `.biolog-button-secondary` gained the combined `:disabled` rule with `.biolog-button-primary`.

**Not yet done, intentionally out of scope for this patch:** BIO-UX-006/007/009/010's own local
badge/table CSS were left as submitted — Seyi's functional review proceeds on their current code
(nothing in it is wrong), and each builder does the mechanical swap to `<StatusBadge>`/
`.biolog-table` before their own branch merges, per the instructions god is relaying to all four
at once. The field-row/op-field radius inconsistency (`--biolog-radius-sm` vs `-md`) is unresolved,
flagged for Kemi.

## Evidence

- `web/typecheck` and `web/build` (production): clean, 0 errors.
- `tests/frontend` unit suite: 313/313 pass (includes `bio-ux-001-css-sync.test.ts`, which pins
  `globals.css` against the token source files — unaffected, since no frozen BIO-UX-001 token was
  touched, only the new `--biolog-decision-*-fg` tokens this same session added on top).
- `tests/frontend` Playwright, full suite against a production build: 142/144 pass. The 2 failures
  (`bio-ux-005-adapter-framework.spec.ts`'s dev-only feature-mode-panel tests) are an environment
  mismatch, not a regression: `web/dev-tools/feature-mode-panel.tsx` explicitly returns `null` when
  `process.env.NODE_ENV === 'production'`, by design, unrelated to this patch — confirmed by
  reading that gate directly.
- `bio-ux-011-admin-journeys.spec.ts` (32/32, including 4 axe scans) and
  `bio-ux-008-biometric-journeys.spec.ts` (17/17, including 2 new axe scans proving the
  previously-uncovered contrast defect is now fixed) both pass in full against the patched code.
