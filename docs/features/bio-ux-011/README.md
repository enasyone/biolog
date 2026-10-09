# BIO-UX-011 — Build interactive dashboard, administration, settings, reports and audit journeys

As a tenant administrator, the dashboard and administration experience is fully clickable so
day-to-day management can be reviewed before live services are connected.

**Round 1 CHANGES_REQUIRED (Chioma, evidence `2c343fc`):** two findings, both fixed in round 2 (see
"Round 2 remediation" below): (1) AC04's Feature Flags section had no real effect on navigation —
its own description literally said so; (2) AC05's Reset handlers only reset local React state, never
calling the mock adapter, so only Save actually went "through a mock adapter."

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Dashboard, Reports, Audit, Administration, Settings are interactive | Dashboard/Settings/Reports/Audit rebuilt as real pages (not `PlaceholderPage`); Administration unchanged from BIO-UX-002/005 (already interactive) | `bio-ux-011-admin-journeys.spec.ts` AC01 block |
| **AC02** | Settings has all six approved sections | `app/settings/page.tsx`: General, Notification, Retention & Privacy, Security & Access, Feature Flags, Offline & Device Sync | `bio-ux-011-admin-journeys.spec.ts` AC02 block |
| **AC03** | Dashboard metrics/activity are deterministic synthetic data | `lib/dashboard-metrics.ts` computed from BIO-UX-004's `generateDataset()` (fixed seed) — no `Math.random()`/`Date.now()` anywhere | `bio-ux-011-dashboard-metrics.test.ts` |
| **AC04** | Tenant/role/feature changes visibly alter navigation and controls | Dashboard/Audit/Reports filter BIO-UX-004's dataset by `session.tenant.id`; Settings' Security & Access MFA control is disabled for non-admin roles; **Feature Flags toggles now genuinely show/hide their nav item** (round 2) | `bio-ux-011-admin-journeys.spec.ts` AC04 block; `bio-ux-011-feature-flag-nav.test.ts` |
| **AC05** | Settings save/reset work through mock adapters, marked SIMULATION | Every section's Save **and Reset** button calls `TenantService`'s port via `useModuleAction` (round 2: Reset now included) | `bio-ux-011-admin-journeys.spec.ts` AC05 block |
| **AC06** | Shared design-system and shell rules remain intact | `app-shell.tsx`/`nav-items.ts`/`permissions.ts` extended additively (new optional fields/functions, no existing behaviour changed — see "Round 2" below); brand tokens re-verified on every new page | `bio-ux-011-admin-journeys.spec.ts` AC06 block |

## Round 2 remediation

**AC04 — Feature Flags now control real navigation.** `session-context.tsx` gained shared
`featureFlags`/`setFeatureFlag` state (alongside the existing tenant/role state); `nav-items.ts`
gained an optional `requiredFeatureFlag` on the three items whose flag Settings already named
(Card Designer, Document Verify, Mobile Credentials); `permissions.ts` gained
`filterNavByFeatureFlags`, composed with the existing `filterNavByPermission` in `app-shell.tsx`.
Toggling a flag in Settings applies immediately (the same way the tenant/role selectors already do,
not gated behind a separate save step) — verified in a real browser: unchecking "Mobile
Credentials" (its own default is off) removes it from the sidebar instantly, and the change
survives client-side navigation to another page (`SessionProvider` is mounted once in `layout.tsx`
and isn't remounted by `<Link>` navigation).

**AC05 — Reset now calls the mock adapter too.** Every section's `onReset` still restores its own
fields to their defaults synchronously (an unconditional, real UI action — not contingent on a mock
outcome), then calls the same `useModuleAction` the Save button uses, so Reset's result is equally
visible and equally adapter-backed. The remediation notice offered a second reading ("mark reset as
explicitly local if that's correct instead") — not taken, since AC05's own text names "save/reset
actions" together as both needing to go through mock adapters.

**AC06 check:** `nav-items.ts`/`permissions.ts`/`app-shell.tsx` changes are additive only —
`requiredFeatureFlag` is a new optional field (existing items are unaffected), `filterNavByFeatureFlags`
is a new function (the existing `filterNavByPermission` is untouched), and `app-shell.tsx` composes
the new filter after the existing one rather than replacing it. Confirmed via the full regression
suite (37 unit files/277 tests, 127 Playwright tests) — zero pre-existing test needed updating.

## Reused, not reinvented

- **BIO-UX-004's synthetic dataset** (`generateDataset()`) backs Dashboard, Reports and Audit
  directly — its tenant ids (`alpha`/`beta`) exactly match `session-context.tsx`'s tenant catalog,
  so filtering by `session.tenant.id` was a drop-in integration, not new fixture work.
- **BIO-UX-005's port framework** (`useModuleAction`, a new thin wrapper around `MODULE_PORTS`)
  backs every Save/Reset/Run/Sync action — no new adapter pattern invented.
- **BIO-UX-005's `viewAuditTrail` port** (added during its own CHANGES_REQUIRED remediation) is
  reused as-is for the Audit page's primary action.

## `nav-items.ts` placeholder permissions — resolved: left as-is (this story's call)

God's dispatch asked me to resolve whether `nav:people`/`nav:settings`/etc. survive as the real
permission catalog or get replaced, since this story owns admin/settings. Decision: **left
untouched.** No identity/RBAC story has run yet (Tunde's domain), and inventing a "more real"
permission catalog now — before that story defines actual roles/permissions — would just replace
one set of guesses with another, still not the real thing. Where this story needed a role check
(Security & Access's MFA control), it reads `session.role.id === 'admin'` directly rather than
adding a new placeholder permission string like `settings:security:admin-only` to the catalog —
narrower, and doesn't expand a list that's already flagged as provisional.

## A real WCAG AA contrast bug found via axe (not assumed away)

`.biolog-decision-badge--granted` (Audit page) — green text on a light-green background at 12px —
measured 3.67:1 via axe-core, under AA's 4.5:1. This is the exact same *token pairing*
(`--biolog-color-success` on `--biolog-color-success-bg`) BIO-UX-001 froze and BIO-UX-005's
`feature-mode-badge` also uses, but neither of those ever actually rendered this pairing in their
own axe runs (BIO-UX-005's badge only showed `SIMULATION`/grey during its own tests). Fixed by
adding two new theme-aware custom properties, `--biolog-decision-success-fg`/
`-error-fg` (darker in light mode — 5.7:1/6.2:1; equal to the existing tokens in dark mode, where
they already pass at 6.6:1/5.3:1) — the frozen BIO-UX-001 tokens themselves are unchanged. Flagged
here since the same underlying gap could resurface wherever `colorSuccess`/`colorError` render as
small badge text on their own `-bg` tint; not fixed pre-emptively elsewhere since that's outside
this story's own new surface.

## Local development / verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes, all prerendered
corepack pnpm --dir tests/frontend test:unit   # 277 tests (37 files) pass
corepack pnpm --dir tests/frontend test:e2e    # 127 Playwright tests pass, zero regressions
```
