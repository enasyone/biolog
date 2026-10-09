# web/

BIOLOG web application: Next.js (App Router) + TypeScript + React, per
`02-ARCHITECTURE/MASTER-TECHNICAL-SPECIFICATION.md` section 1 (Stack).

**BIO-FND-007** scaffolded the application shell and design-system foundation
(owner: Ngozi, Wave W02): navigation, a tenant/role context area, and
light/dark theme tokens. **BIO-UX-002** (owner: Damilola) extended it into the
full production shell: grouped sidebar navigation covering every approved
product area, a working global search, independent tenant/role selectors, a
user menu, and a deliberate page shell for every one of the 34 routes those
areas resolve to. Feature pages replace a placeholder page's content as the
stories that own each area ship them, without touching this shell.

## Layout

- `app/` — App Router routes: `layout.tsx` mounts the shell; `page.tsx` is a
  placeholder dashboard; `not-found.tsx` renders inside the shell for any
  route that doesn't exist yet; every other `app/<route>/page.tsx` renders the
  shared `PlaceholderPage` until its owning story ships real content
  (BIO-UX-002-AC04).
- `components/` — shell chrome: `app-shell`, `brand-mark`, `theme-toggle`,
  `theme-script`, `skip-link`, `search-bar`, `user-menu` (BIO-UX-002), and
  `placeholder-page` (BIO-UX-002, shared by every not-yet-built route). No
  feature-specific logic.
- `context/session-context.tsx` — placeholder tenant/role/permission session,
  now with independent tenant and role selection (BIO-UX-002). See its header
  comment: this is a stand-in for a real identity/RBAC story, not
  authentication.
- `lib/tokens.ts` — the original colour-token single source of truth (brand
  palette, light/dark theme values), from BIO-FND-007. `app/globals.css`
  mirrors these as CSS custom properties; the two are pinned equal by
  `tests/frontend/unit/bio-fnd-007-tokens.test.ts`.
- `lib/permissions.ts` / `lib/nav-items.ts` — generic, framework-agnostic
  permission-based nav filtering and grouping (`groupNavItems`, BIO-UX-002),
  and the full nav sitemap covering every approved product area.
- `lib/search-fixtures.ts` — deterministic synthetic global-search fixture
  (BIO-UX-002); replaced by a live search port once one exists.
- `lib/module-port.ts`, `lib/feature-mode.ts`, `lib/module-registry.ts` —
  **BIO-UX-005**: the mock/live adapter framework and feature-mode registry.
  `route-manifest.ts`'s `ServicePort` names every module; `module-registry.ts`
  maps each to its resolved port. `PlaceholderPage` calls through this, not
  local state — see `docs/features/bio-ux-005/README.md`.
- `features/<domain>/{ports,mock,live,index}.ts` — **BIO-UX-005**: one
  scaffold per backend-mapped module (13: identity, visitors, cards,
  biometrics, photos, documents, access, attendance, transport, devices,
  reporting, tenants, integrations), matching
  `02-ARCHITECTURE/UI-ADAPTER-ARCHITECTURE.md`'s reference structure. A
  domain story extends its own `ports.ts`/`mock.ts`/`live.ts` with real
  methods; `bio-crd-002/003/004` (Card Designer) are pre-existing,
  story-specific directories, unrelated to this convention and explicitly
  excluded from it (Kemi's ruling, see the BIO-UX-005 docs).
- `dev-tools/feature-mode-panel.tsx` — **BIO-UX-005**: development-only
  read-only audit of every module's current SIMULATION/HYBRID/LIVE status.
  Verified absent from a real production build (not just asserted) — see the
  BIO-UX-005 docs' "Proving AC03".
- `design-system/` — **BIO-UX-001**, owned by Damilola: the frozen design
  system (status colours, typography, spacing, radii, shadows, iconography,
  breakpoints, component states). Re-exports and extends `lib/tokens.ts` rather than
  replacing it. See its own `README.md` for the visual-reference provenance
  and ownership rule.
- `public/brand/` — approved BIOLOG brand assets (see that directory's
  `README.md` for provenance).

## Local development

```sh
corepack pnpm --dir web install
corepack pnpm --dir web dev      # http://127.0.0.1:3000
corepack pnpm --dir web typecheck
corepack pnpm --dir web build
```

Demo role: append `?as=admin`, `?as=staff` (default), or `?as=viewer` to the
URL, or use the header's Role selector, to preview permission-based nav
visibility (BIO-FND-007-AC03, BIO-UX-002-AC01). Demo tenant: `?tenant=alpha`
(default) or `?tenant=beta`, or the header's Tenant selector. All demo
data is `SYNTHETIC`.

Tests live in `tests/frontend/` (`unit/bio-fnd-007-*.test.ts`, `unit/bio-ux-001-*.test.ts`,
`unit/bio-ux-002-*.test.ts`, `unit/bio-ux-003-*.test.ts`, `unit/bio-ux-005-*.test.ts`,
`playwright/bio-fnd-007-shell.spec.ts`, `playwright/bio-ux-002-shell.spec.ts`,
`playwright/bio-ux-005-adapter-framework.spec.ts`) and `tests/foundation/BIO-FND-007.test.sh`
— see `docs/TESTING.md`.

To flip a module from SIMULATION to LIVE locally: set
`NEXT_PUBLIC_FEATURE_MODE_<MODULE>=LIVE` (e.g.
`NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE=LIVE`) in `web/.env.local` and restart
`pnpm dev`. View current state via the "DEV — feature modes" panel
(bottom-right, development only).
