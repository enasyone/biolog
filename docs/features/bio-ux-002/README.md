# BIO-UX-002 — Build production application shell and permission-aware navigation

As a BIOLOG operator, the complete production application shell exists so the whole product can
be navigated before every backend service is live.

This story **extends** BIO-FND-007's shell (`app-shell.tsx`, `nav-items.ts`, `permissions.ts`,
`session-context.tsx`) rather than rebuilding it — every existing export keeps working
(`filterNavByPermission`, `isNavItemVisible`, `NavItem`), and `web/lib/tokens.ts`/BIO-UX-001's
`design-system/` are untouched.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Sidebar, top bar, global search, tenant selector, role selector, user menu, theme control | `app-shell.tsx` composes `SearchBar`, two `<select>` context switchers bound to `session-context.tsx`'s new independent tenant/role axes, `UserMenu`, and the existing `ThemeToggle`/sidebar | `bio-ux-002-shell.spec.ts` (search, tenant/role selectors, user menu sign-out) |
| **AC02** | Navigation contains the approved product areas, permission/feature-flag visibility | `nav-items.ts`'s `PRIMARY_NAV_ITEMS` covers every `04-FEATURE-SPECS/PRODUCT-EXPERIENCE.md` page family, grouped to match `reference/ui/INITIAL_PROTOTYPE_REFERENCE.png`'s sidebar sections; `groupNavItems` (new, in `permissions.ts`) renders only groups with at least one permission-visible item | `bio-ux-002-nav-groups.test.ts`, `bio-fnd-007-permissions.test.ts` (unchanged, still passing) |
| **AC03** | Responsive, keyboard navigable, visible focus states | Existing mobile-drawer/desktop-sidebar breakpoint behaviour (BIO-FND-007) unchanged; every new header control (search input, two selects, user-menu button) is a native, keyboard-operable HTML control | `bio-ux-002-shell.spec.ts` "keyboard navigable" describe block |
| **AC04** | Every route renders a deliberate page shell, not blank/dead | 34 routes now build (was 2: `/` and `/card-designer`) — every `PRIMARY_NAV_ITEMS` href plus 5 related-link sub-routes (`/cards/issue`, `/cards/replace`, `/administration/tenants`, `/administration/integrations`, `/settings/authentication`) render the shared `PlaceholderPage` component | `bio-ux-002-shell.spec.ts`'s route-inventory loop (30 routes × render check), `next build`'s route manifest |
| **AC05** | Shared shell has no feature-specific business logic | `PlaceholderPage` takes only `title`/`description`/`primaryActionLabel`/`notes`/`relatedLinks` — plain strings, no computed domain rules; `groupNavItems` is purely structural | `bio-ux-002-shell.spec.ts` "no feature-specific business logic" (asserts no `/api/` calls from a placeholder page) |
| **AC06** | Branding matches approved baseline, no unapproved redesign | No BIO-UX-001 token changed; the one visual addition (grouped section headers) matches the reference screenshot directly; one new colour decision (see "On-accent text contrast" below) is documented, not silent | `bio-ux-002-shell.spec.ts` brand-token pin test (unchanged from BIO-FND-007's) |

## Route inventory and its sources

Every route below resolves from `governance/ui-integration-map.json` wherever it defines one for
the owning backend port. The following did **not** have a map entry and were a judgement call,
not a silent invention:

| Route | Reasoning |
|---|---|
| `/alumni`, `/contractors` | Shown as their own sidebar rows in the approved reference screenshot (IDENTITY group) but have no dedicated backend port/route in `ui-integration-map.json` — likely intended to be served by `IdentityService` eventually. Flagged here, not decided, since assigning them a port is an architecture/governance question, not a shell one. |
| `/nfc-uhf` | Named as its own required page family in `PRODUCT-EXPERIENCE.md` and shown as its own sidebar row in the reference screenshot, but `CredentialService`'s `ui-integration-map.json` route list doesn't include it separately. Given a flat top-level path (`/nfc-uhf`), matching the sibling flat-path style `/card-designer`/`/credentials` already use next to the nested `/cards/issue`/`/cards/replace`. |
| `/mobile-credentials` | `MobileService`'s only mapped route is `"Flutter application"` — mobile-only, matching my own W01.5 plan's finding that BIO-MOB stories are Busola's mobile track, not `web/`. This web route is a **status view only**; provisioning stays on the mobile app. Documented directly on the page and here so a later story doesn't wire a provisioning action onto it by mistake. |
| `/administration` (landing) | `TenantService`/`IntegrationService` map `/administration/tenants` and `/administration/integrations` separately, with no landing page. Added one, linking to both via `relatedLinks`, matching "Administration" being its own required page family. |

**Card Designer's position**: `PRODUCT-EXPERIENCE.md` lists "Card Designer" as its own required
page family, but the approved reference screenshot's visible CREDENTIALS section (Cards, Issue
Card, Replace Card, NFC/UHF, Mobile Credentials) doesn't show it as a row. Since BIO-CRD-002/003/004
already shipped a real, tested `/card-designer` page before this story ran, and the master spec
(a higher source than the reference screenshot per the reading order) explicitly names it, it's
kept as its own nav entry rather than removed or hidden.

**"Visitor Management" vs. "Visitors"**: `PRODUCT-EXPERIENCE.md` lists both "Visitors" (as part of
the People/Students/Staff/Visitors/Alumni/Contractors identity-type list) and, later, "Visitor
Management" as a distinct required page family (operational check-in/out). `ui-integration-map.json`
defines exactly one route for this domain — `VisitorService` → `/visitors`. Rather than inventing a
second, unmapped route to represent "Visitor Management" separately, a single "Visitors" nav item
(placed in the Identity group, matching the approved reference screenshot) points at `/visitors`.
Flagged for BIO-UX-003 (the full route-manifest/journey-mapping story) to resolve explicitly if a
second route is actually intended — not decided unilaterally here.

## On-accent text contrast (AC06 — a documented colour decision, not a redesign)

Running the Playwright axe accessibility scan against the extended shell surfaced a real WCAG AA
failure: `--biolog-color-accent-contrast` (white, frozen by BIO-UX-001 for the *original* palette)
measures ~3.5:1 against `--biolog-color-accent` (the brand orange) — under the 4.5:1 AA minimum
for normal-size text, confirmed by hand (relative-luminance calculation) and by axe-core
independently. This affects any new normal-sized text placed directly on solid brand-orange, which
BIO-UX-001 never did (its only accent+text usages were icon-sized/large chrome).

This story introduces the first two such uses (`.biolog-user-avatar` initials, `.biolog-button-primary`
label) and gives both `var(--biolog-color-brand-navy)` as their foreground instead of
`--biolog-color-accent-contrast` — navy-on-orange measures ~4.65:1, comfortably AA. This is a new
colour *decision* for new components this story owns, not an edit to BIO-UX-001's frozen token
(`--biolog-color-accent-contrast` itself is untouched, still white, still used unmodified wherever
BIO-FND-007 already placed it). Flagged for Damilola/BIO-UX-001's own future revision if a
different fix (e.g. a second, purpose-built "on-accent-normal-text" token) is preferred — this
story's fix is scoped to unblock its own two new components, not to relitigate the frozen palette.

## Design decisions and scope boundaries

### Nav data shape stays backward compatible

`NavItem` gained one new optional field (`group?: string`) rather than a new grouped data
structure; `PRIMARY_NAV_ITEMS` is still the same flat, exported array `bio-fnd-007-permissions.test.ts`
already asserts against (its "every item is a plain string/href/permission tuple" check still
passes unmodified — `group` is just another string). `groupNavItems` (new, in `permissions.ts`) is
a pure, generic partitioning function applied at render time, after permission filtering, so an
empty group is never rendered.

### Tenant/role selectors replace the `?as=`-only demo switcher, without removing it

BIO-FND-007's session was one atomic "profile" (tenant+role+user bundled) switchable only via a
`?as=` query parameter — not a real selector, and only one tenant existed, so AC01's "tenant
selector" couldn't have been a working control against it. `session-context.tsx` now models tenant
and role as two independent axes with their own small catalogs (`TENANTS`, `ROLES`) and
`setTenantId`/`setRoleId`, with a second synthetic tenant added so the selector has something real
to select between. `?as=` still works (now selecting role only); a new `?tenant=` param mirrors it.
This is a placeholder session extension, not real multi-tenancy or RBAC — same disclaimer BIO-FND-007
recorded, now covering the wider catalog.

### Global search and the user-menu sign-out are real, not decorative

The UI-first no-dead-button rule applies to every visible primary action, and a search box / a
"Sign out" menu item are both primary actions once present. Neither has a live backend yet, so both
are deterministic SIMULATION: search matches a tiny fixed fixture list
(`web/lib/search-fixtures.ts`) and clearly labels a no-match result as simulated; sign-out flips a
real, visible signed-out state with a working "sign back in" recovery path. Search deliberately
does **not** use the full ARIA combobox/listbox/option pattern (which requires arrow-key selection
this story doesn't implement) — an incomplete version of that pattern is worse than a plain
`<input type="search">` plus an `aria-live` results region, which axe-core confirmed has no
violations.

### Placeholder pages are one shared, generic component

34 routes exist without exploding into 34 bespoke files: `PlaceholderPage` (new, in
`web/components/`) renders the reference screenshot's title/description/primary-action/notes-card
pattern from plain props. Every `web/app/<route>/page.tsx` is a 6-line file passing only strings.
Domain stories replace the *page*, not this shared component, when they ship real content — per
the UI-adapter architecture's "later domain stories plug live adapters into the approved experience
rather than rebuilding the page" rule.

## Local development / verification

```sh
corepack pnpm --dir web typecheck   # passes
corepack pnpm --dir web build       # passes — 34 routes, all prerendered
corepack pnpm --dir tests/frontend test:unit   # 23 files / 172 tests pass
corepack pnpm --dir tests/frontend test:e2e    # 102 Playwright tests pass (incl. every pre-existing
                                                # BIO-FND-007/BIO-CRD-002/003/004 spec, unmodified in
                                                # behaviour — 2 stale nav-content assertions in
                                                # bio-fnd-007-shell.spec.ts were updated to the new
                                                # nav catalog's item names, same permission mechanism)
```

Playwright was run against a real Chromium browser and a real `next dev` server in this session
(port 3211 — 3000/3100 were already bound by other local processes on this shared machine).
