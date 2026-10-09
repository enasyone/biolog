# BIO-UX-006 — Build interactive identity and enrolment journeys

As a BIOLOG operator, I need complete simulated identity and enrolment journeys so I can test the
intended workflow before live identity services are connected.

**Post-acceptance fix (2026-09-23, Damilola):** the People list only ever rendered a Suspend
action, never a Reinstate one — a person suspended directly from the list had no way back to
active without first navigating to their detail page (which does have both). See "Post-acceptance
fix: list-level Reinstate" below.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | People/Students/Staff/Visitors/Alumni/Contractors lists and detail views are interactive | `PersonListView` (one component, parameterised by `category`) for `/people`, `/students`, `/staff`, `/alumni`, `/contractors`; `PersonDetailView` at the single shared `/people/[personId]` route (see "One shared detail route" below). Visitors is BIO-UX-010's surface, not this story's — see "Scope: Visitors" | `bio-ux-006-identity-journeys.spec.ts` (AC01 block), `bio-ux-006-identity-data-store.test.ts` |
| **AC02** | New Enrolment: identity/profile/photo/biometric/credential/review stages via mock adapters | `EnrolmentWizard` at `/enrolment` — six-step client wizard; photo/biometric/credential stages call the already-scaffolded `PhotoService`/`BiometricService`/`CredentialService` mock ports' own generic `primaryAction()` (BIO-UX-005), review stage calls `identityPort.submitEnrolment` | `bio-ux-006-identity-journeys.spec.ts` (AC02 block), `bio-ux-006-identity-data-store.test.ts` "enrolment" |
| **AC03** | Identity lifecycle, emergency suspension and duplicate-review screens, synthetic scenarios | `PersonLifecycleStatus` (new field on `SyntheticPerson`, BIO-UX-004's own type — see "Lifecycle status" below); Suspend/Reinstate actions on the People list and detail view; `DuplicateReviewView` at `/duplicate-review`, reading the `duplicate-identity` synthetic scenario directly | `bio-ux-006-identity-journeys.spec.ts` (AC03 block), `bio-ux-006-identity-data-store.test.ts` "lifecycle"/"duplicate review" |
| **AC04** | Tenant/role changes update available actions without page reload errors | Every screen is a Client Component reading `useSession()`; the People list re-fetches on `session.tenant.id` change (tenant re-scopes the list) and re-evaluates Suspend's visibility on `session.role.id` on every render (no reload) | `bio-ux-006-identity-journeys.spec.ts` (AC04 block) |
| **AC05** | Every primary action produces a visible simulated result; no dead button | Every button (Suspend/Reinstate, each wizard stage, Submit enrolment, Resolve duplicate, Export list) either calls a mock port and renders its outcome, or (Export) performs a genuine client-side CSV download | `bio-ux-006-identity-journeys.spec.ts` (throughout) |
| **AC06** | Pages expose a stable integration seam for later live identity APIs | `IdentityPort` (extends the generic `ModulePort`) with seven typed methods; `mock.ts` implements them against synthetic data, `live.ts` implements the identical shape against real (not-yet-existing) HTTP endpoints, failing closed to `offline`/empty results | `bio-ux-006-identity-live-port.test.ts` |

## Scope: Visitors is BIO-UX-010's surface, not this story's

AC01 names "Visitors" among the interactive lists, but `governance/ui-integration-map.json` scopes
this story's port (`IdentityService`) to `/people`, `/students`, `/staff`, `/duplicate-review`
only — `/visitors` is mapped to `VisitorService`, which is BIO-UX-010's (Busola's) own declared
scope, and BIO-UX-010's own AC01 separately names "Visitor Management". Flagged to god before
writing any code (a genuine two-story AC overlap, not something to guess at); god confirmed the
same afternoon and recorded a "Scope notes" section in both stories' spec files, using the same
port-ownership precedent BIO-UX-003 used for an earlier "Visitors vs Visitor Management" naming
question. This story does not build `/visitors`.

## One shared detail route, not five

A Students/Staff/Alumni/Contractors row's "View profile" action, and People's own, all link to the
same `/people/[personId]` route rather than each list owning its own `/students/[id]`,
`/staff/[id]`, etc. — a person record has the identical shape regardless of which list surfaced
it, and `PersonDetailView` already resolves the record from the shared `identityStore` by id alone
(tenant-scoped, category-agnostic). Building four near-identical detail routes would only
duplicate this component with no behavioural difference.

## Lifecycle status: an additive field on BIO-UX-004's `SyntheticPerson`

Before this story, `SyntheticPerson` had no concept of lifecycle at all. `PersonLifecycleStatus`
(`'active' | 'suspended'`) and a required `lifecycleStatus` field were added to
`web/lib/synthetic-data/types.ts`, and `generators.ts` sets every generated person to `'active'` —
the deterministic baseline dataset is therefore unchanged in shape and still byte-for-byte
reproducible (BIO-UX-004-AC01/AC03, re-verified: `bio-ux-006-identity-data-store.test.ts`'s own
determinism check). `'suspended'` is reached only through an in-session mutation (the "Suspend
(emergency)" action), never part of the generated baseline — this is not a new `ScenarioId`
(BIO-UX-004-AC02's scenario list is a fixed, already-shipped enum this story does not touch), it
is a mutation applied the same way `store.ts`'s own header comment already anticipated ("e.g. a
simulated 'Issue card' primary action updating a credential's status").

## Duplicate Review reads its own dedicated scenario, not the People list's live store

`data-store.ts` keeps two independent `SyntheticDataStore` instances: `identityStore` (`normal`
scenario, backs People/Students/Staff/Alumni/Contractors/Enrolment) and `duplicateReviewStore`
(`duplicate-identity` scenario specifically, backs Duplicate Review only). There is no
scenario-switcher UI yet (BIO-UX-012, Human Test Console, not built), so a human tester loading
`/duplicate-review` needs a candidate to already be present — reading the dedicated scenario
directly, rather than depending on the main list ever being switched into it, guarantees that
without inventing new UI outside this story's scope.

## AC04: what "available actions react to tenant/role" concretely means here

- **Tenant**: switching tenant changes `session.tenant.id`; every list's `useEffect` depends on
  that value and re-fetches, so People/Students/Staff/Alumni/Contractors immediately show the new
  tenant's own synthetic records — no route change, no reload.
- **Role**: "Suspend (emergency)" is gated on `session.role.id === 'admin'`, re-evaluated on every
  render (a plain comparison, not state) — switching from System Administrator to Registration
  Staff immediately hides the action; switching back immediately restores it. This reuses
  `session.role.id` directly rather than adding new granular permission strings (e.g.
  `people.suspend`) to `session-context.tsx`'s shared `ROLES` array — a shared-shell file five
  other BIO-UX-006..011 builders' own domain stories may also need to touch this same wave; adding
  narrowly-scoped strings there risked a needless collision for a distinction `role.id` already
  expresses cleanly.
- Every list/detail/wizard/duplicate-review screen also gates its whole page behind
  `session.permissions.has(<its own nav:* permission>)`, rendering an explicit "Permission
  required" state rather than assuming the shell's nav-hiding is the only enforcement (a direct
  URL visit bypasses nav filtering entirely — `PlaceholderPage` itself has no such gate, so this
  is additive coverage for this story's own screens, not a claim about the wider shell).

## Regression fix: two BIO-UX-005/002 Playwright tests assumed `/people`/`/alumni` stayed placeholders

`tests/frontend/playwright/bio-ux-005-adapter-framework.spec.ts` and
`bio-ux-002-shell.spec.ts` had several tests that navigated to `/people` or `/alumni` specifically
to exercise the *generic* mock/live adapter framework (BIO-UX-005) or the *generic* placeholder
shell (BIO-UX-002) — using those two routes only because no domain story had replaced them yet.
Since this story gives both routes real content (removing the generic `PlaceholderPage` and its
"Simulate primary action" button entirely), those specific sub-tests were retargeted to
`/photographs` (PhotoService — confirmed via a direct `deterministicIndex` calculation that its
own mock scenario cycle happens to start success→failure→offline, the same order as Identity's,
so the existing assertions' wording didn't need rewriting beyond the module name) and `/nfc-uhf`
(still genuinely `Unassigned`, for the "no port mapped" honesty test). Nothing about the
*framework* itself changed — only which still-placeholder route demonstrates it. This is expected
to recur: as each of the other five BIO-UX-006..011 stories lands, it will likely need to retarget
whichever of these generic tests still happens to point at a route it just replaced, the same way
this story did for `/people`/`/alumni`.

## Known local-environment limitation: `next dev`'s HMR is unusable here, `next build` + `next start` used instead

In this session's sandboxed shell, `next dev`'s Turbopack HMR WebSocket handshake fails
(`ERR_INVALID_HTTP_RESPONSE`) on every route, including ones this story never touched (confirmed
directly on `/devices`, unmodified code) — clicking any mock-port button then hangs indefinitely,
because Fast Refresh's broken reconnect loop appears to starve the page's own timers. This is not
caused by this story: the same hang reproduces on a clean checkout with no BIO-UX-006 changes
present. All Playwright verification in this round therefore ran against `next build` + `next
start` (a real production server) instead of `next dev`, which resolved the hang entirely and
matches this pack's own precedent (BIO-UX-005's evidence also used a manual `next build`/`next
start` step to prove its AC03 production-exclusion). One consequence: `bio-ux-005-adapter-framework.spec.ts`'s two feature-mode-panel tests are dev-only by design (the panel is excluded from
production builds, BIO-UX-005-AC03) and could not be exercised in this environment at all — they
were already failing under `next dev` for the same HMR reason before this story touched anything,
so this is a disclosed, pre-existing environment gap, not a regression.

## Accessibility fixes made while writing this story's own axe coverage

- `.biolog-identity-badge--active/--suspended` (now retired — see "Design-system consolidation
  retrofit" below): the shared `--biolog-color-success`/`-error` tokens paired with their own
  light `-bg` variants measured 3.67:1 at this badge's 12px text — short of WCAG 1.4.3's 4.5:1.
  Originally fixed with solid `--biolog-color-success`/`-error` backgrounds plus `-on` (white)
  text (7.26:1/5.49:1); superseded by the canonical `<StatusBadge>` pattern, which solves the same
  contrast defect a different way (see below).
- The list table's actions column header used `aria-label` alone on an empty `<th>` (`empty-table-header`, axe best-practice rule); replaced with a `.biolog-visually-hidden` span carrying real
  (visually hidden) text.

## Design-system consolidation retrofit (2026-09-23, after Seyi's review PASSed)

Five of the six W01.5 domain stories (BIO-UX-006/007/008/009/010/011) each independently needed a
status badge, and four needed a data table; none of BIO-UX-001's frozen tokens or the two
`reference/ui/*.png` screenshots covered either pattern, so each story invented its own — this
story's own `.biolog-identity-badge`/`.biolog-identity-table`. Flagged to Damilola (design-system
owner) per god's standing "route anything not fully tokenised to Damilola" reminder; she landed a
single consolidated fix on `develop` (`2bcd997`, `docs/UI-DESIGN-SYSTEM-CONSOLIDATION.md`) rather
than each story reconciling in isolation. This story's own code was confirmed not wrong — Seyi's
functional review PASSed against it unchanged — but the instruction from god (relayed to all four
affected builders at once) was for each to do the mechanical swap to the canonical pattern before
merging. Done here:

- `.biolog-identity-badge`/`--active`/`--suspended` → `<StatusBadge tone="success">`/`<StatusBadge tone="error">` (`web/components/status-badge.tsx`) in `person-list-view.tsx` and
  `person-detail-view.tsx`. `error`, not `neutral`, for `suspended` — an emergency-suspension state
  is a negative status, the same category `audit/page.tsx`'s own `tone="error"` usage (a denied
  decision) already establishes, not `neutral` (reserved for offline/unknown-status badges
  elsewhere). The canonical badge's own tinted-background + neutral-text + left-border-accent
  technique resolves the WCAG contrast defect a different way than this story's original local fix
  (`#146341`), so that override is dropped entirely — no longer needed once text stopped being the
  tone colour.
- `.biolog-identity-table` → `.biolog-table` (canonical, gained `<caption>`/`aria-label` support)
  in `person-list-view.tsx`; added an `aria-label` of `"<title> directory (SYNTHETIC)"` matching
  `audit/page.tsx`'s own established usage of the class.
- `.biolog-identity-button-secondary` → `.biolog-button-secondary` (now the canonical global class,
  consolidated from two independently-conflicting BIO-UX-010/011 definitions) across
  `person-list-view.tsx`, `enrolment-wizard.tsx` and `duplicate-review-view.tsx`.
- Retired the now-dead `.biolog-identity-badge*`/`.biolog-identity-table*`/
  `.biolog-identity-button-secondary*` CSS rules from `globals.css` entirely, rather than leaving
  unused rules behind.
- Re-ran this story's own Playwright spec (`bio-ux-006-identity-journeys.spec.ts`), updating its
  own selectors from `.biolog-identity-table`/`.biolog-identity-badge` to the canonical
  `.biolog-table`/`.biolog-status-badge` — verified 156/158 (the 2 failures are the same disclosed
  dev-only feature-mode-panel gap noted above, unaffected by this retrofit).

## Post-acceptance fix: list-level Reinstate

Damilola found this incidentally while running Playwright for her own, unrelated BIO-UX-012 work
(confirmed via `git diff --stat` that none of her changes touch identity files) and flagged it to
god, who did a code read, couldn't rule it in or out statically, and passed it on. Reproduced with
a small Playwright probe: `person-list-view.tsx`'s row actions only ever rendered a Suspend button
(`canSuspend && person.lifecycleStatus === 'active'`) — there was no matching branch for
`lifecycleStatus === 'suspended'`, so once a row was suspended *from the list itself*, its action
cell went blank. The only way back to active was to open that person's detail page (which always
had both Suspend and Reinstate) and use the button there — the detail-page round trip itself
worked correctly the whole time, which is exactly why the story's own original test (which only
ever exercised the detail page) never caught this.

Fix: `handleSuspend` generalised to `handleLifecycleAction(personId, kind: 'suspend' |
'reinstate')`, and the row now renders a symmetric Reinstate button whenever
`lifecycleStatus === 'suspended'`, matching the detail page's own logic. Added
`bio-ux-006-identity-journeys.spec.ts`'s "admin can suspend then reinstate a person directly from
the People list" as regression coverage — asserts the Suspend/Reinstate buttons swap correctly in
both directions without ever navigating away from `/people`.

Also hardened, found while re-verifying: every `getByRole('button', { name: 'Next' })` locator in
this story's own Playwright spec (the Enrolment wizard's step navigation) now passes `exact:
true`. Next.js's dev-mode "Open Next.js Dev Tools" overlay button's accessible name contains the
substring "Next" (as in "Next.js"), and Playwright's default text matching is substring-based, so
an unscoped `{ name: 'Next' }` locator can match either button once that overlay is present —
Damilola hit exactly this collision in the same report (also affecting `/nfc-uhf`'s test in
`bio-ux-005-adapter-framework.spec.ts`, flagged separately to Emeka as shared dev-mode test-infra,
not this story's own bug).

Also discovered while re-verifying (a consequence of the wave's own pace, not of this fix):
`bio-ux-005-adapter-framework.spec.ts`'s "a route with no backend-port mapping says so honestly"
test, previously retargeted from `/alumni` to `/nfc-uhf` by this story, broke again — BIO-UX-007
(Ngozi) has since given `/nfc-uhf` its own real screen too. Checked `route-manifest.ts`: its only
two remaining `servicePort: 'Unassigned'` entries are `/alumni` and `/contractors`, both of which
*this same story* already gave real `IdentityService` screens — so there is currently no route in
the app that is both labelled `Unassigned` and still rendering the generic placeholder fallback.
Skipped that one test with an explanation rather than forcing it onto a route that would make its
own assertion inaccurate; not this story's file to otherwise rewrite.

Verified end to end: `tsc`/`next build` clean, 371/371 unit tests, 202/204 Playwright (1 newly
skipped as above, the same disclosed 2 dev-only-panel failures otherwise unaffected),
`scripts/quality.sh` PASS.

## Deliberately out of scope

- **`/visitors`** — see "Scope: Visitors" above.
- **Real capture hardware, real biometric templates, real document images** — the enrolment
  wizard's photo/biometric/credential stages call existing, still-generic mock ports
  (`PhotoService`/`BiometricService`/`CredentialService`); building those domains' own real
  screens is a different story's job.
- **A Human Test Console / scenario switcher UI** — BIO-UX-012, not built yet; Duplicate Review
  works around its absence by reading its own dedicated scenario directly (see above).
- **New granular action-level permission strings** (`people.suspend`, etc.) in
  `session-context.tsx` — see AC04 section above.

## Verification

```sh
corepack pnpm --dir web typecheck
corepack pnpm --dir web build
corepack pnpm --dir tests/frontend test:unit   # includes bio-ux-006-identity-data-store.test.ts,
                                                # bio-ux-006-identity-live-port.test.ts
# next dev's HMR is broken in this sandbox (see above) - verified instead against a real
# production server:
(cd web && pnpm build && PORT=3413 pnpm start) &
BIOLOG_BASE_URL=http://127.0.0.1:3413 corepack pnpm --dir tests/frontend exec playwright test
bash scripts/quality.sh
```
