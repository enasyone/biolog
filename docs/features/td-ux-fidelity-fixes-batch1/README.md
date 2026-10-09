# TD-UX-FIDELITY-FIXES-BATCH1 — four fixes authorized from TD-UX-PROTOTYPE-FIDELITY-AUDIT

Four of the six findings from the prototype-fidelity audit
(`hive/agents/damilola-mudq9p2l/memory.md`'s 2026-09-24 entry, reported to god as
`td-ux-prototype-fidelity-audit-findings`), authorized for a direct fix. The other two
(`.biolog-button-primary`/`.biolog-user-avatar`'s solid-orange fill) are held pending a separate
human decision — **not touched here**.

## 1. Enrolment wizard's Identity/Profile steps had zero field styling (the highest-value fix)

`features/identity/components/enrolment-wizard.tsx`'s Identity step (First name/Last name/Date of
birth) and Profile step (Category) used bare `<label>Text<input/></label>` with no class at all —
labels ran directly into their inputs with zero spacing, unlike every other form in the app. This
was a real, visible defect independent of the prototype comparison — probably the concrete thing
behind the human's "doesn't look right" complaint.

Fixed by applying `.biolog-op-field`/`.biolog-op-form` — **not** `.biolog-field-row`, matching
Access Control's own form exactly as instructed, since `.biolog-field-row`'s input styling is
scoped to `input[type='text']`/`select` and would have silently missed the Date of birth field
(`type="date"`), while `.biolog-op-field input, select` targets every input type. Each field is now
`<label className="biolog-op-field"><span>Text</span><input/></label>`, grouped inside a
`<div className="biolog-op-form">`, exactly mirroring `features/access/access-page.tsx`'s own
structure.

## 2. Cards' "Issue Card"/"Replace Card" links were bare unstyled text

`features/cards/components/RelatedLinksNav.tsx` rendered its links with no class at all (its own
`cards.module.css` `.toolbar` class only laid out the `<nav>`/`<ul>`, never touched the `<a>`
elements) — plain underlined text, contradicting the component's own doc comment claiming it was
"styled consistently with the rest of this page." Fixed by switching to `.biolog-placeholder-related`
— the exact same class `PlaceholderPage`'s own related-links nav uses for this identical markup
shape (`nav > ul > li > a`), confirmed by reading `components/placeholder-page.tsx` directly rather
than guessing the right class. Removed `cards.module.css`'s now-dead `.toolbar` rule (its only
usage was this component) and the now-unused CSS module import.

## 3. Dashboard metric tiles: number above label, and orange instead of dark

`app/page.tsx`'s `MetricTile` rendered the large value before the small label (reference has the
label above); `.biolog-metric-value` used `--biolog-color-accent` (orange) for the number (reference
uses a plain dark colour, the same as body text). Fixed by swapping the JSX order and changing the
value's colour to `--biolog-color-text`. No test depended on either the order or the colour
(`bio-ux-011-admin-journeys.spec.ts` only reads `.biolog-metric-value`'s text content).

## 4. Tenant/Role selectors: box the label and dropdown together

`.biolog-context-select` previously rendered the "Tenant"/"Role" label as plain text above a
separately-bordered `<select>` — two visual units. The reference groups both inside one bordered
box. Fixed by moving the border/padding/radius onto the outer `.biolog-context-select` container
and making the inner `select` borderless/transparent/padding-free, so there's exactly one visible
box per selector.

**Real regression caught and fixed during verification, not shipped**: adding a border + padding to
the outer box without adjusting the select's own `max-width` (previously `11rem`) pushed the
Tenant+Role pair a few pixels past 375px at mobile width — caught by `bio-ux-013-breakpoints.spec.ts`
and several other existing no-horizontal-overflow tests (14 failures on the first full-suite run).
Fixed by reducing the select's `max-width` to `8rem` — the value was already truncating well before
`11rem` on real tenant/role names, so this loses no visible information, and it happens to match the
reference's own more compact selector proportions better anyway. Re-ran the full suite clean after
the fix (see Verification below) — this is the discipline this session has repeatedly needed:
re-verify the ENTIRE suite after every fix, not just the page you touched, since a shared-shell
change (this selector renders in the header on every single page) can regress far from where you're
looking.

## BIO-UX-014 visual-regression baselines

Regenerated per that story's documented procedure (`corepack pnpm --dir tests/frontend
test:visual:update`) — the Dashboard and shell chrome legitimately changed (metric tile order/colour,
selector box). All 14 baselines updated; spot-checked the dashboard baseline directly.

## Verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes, all prerendered
corepack pnpm --dir tests/frontend test:unit   # 384/384 pass, unchanged
corepack pnpm --dir tests/frontend test:e2e    # 360/360 pass, zero regressions (after the
                                                # max-width fix above — first run caught 14
                                                # real overflow failures, fixed, re-ran clean)
corepack pnpm --dir tests/frontend test:visual # 14/14 pass against regenerated baselines
```
