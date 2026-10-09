# BIO-UX-007 — Build interactive cards, credentials and Card Designer journeys

As a credential operator, the full cards and credential UI works in simulation so card workflows
can be reviewed before issuance services and hardware are complete.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Cards registry, Issue Card, Replace Card, NFC/UHF and Mobile Credential pages are interactive | Six real pages replace BIO-UX-002's `PlaceholderPage` shells: `CardsRegistryApp`, `IssueCardApp`, `ReplaceCardApp`, `CredentialsApp`, `NfcUhfApp`, `MobileCredentialsApp` (`web/features/cards/*.tsx`) | `bio-ux-007-cards.spec.ts`; `bio-ux-007-data-store.test.ts` |
| **AC02** | Card Designer supports front/back canvas interactions consistent with the approved card-designer specification | Already shipped by BIO-CRD-002/003/004 (`CardDesignerApp`'s own front/back tabs) - untouched by this story; reconfirmed still passing | `bio-crd-002-card-designer.spec.ts` (unchanged, rerun as regression) |
| **AC03** | Lost/recovered/replaced/revoked card scenarios can be exercised with correct simulated status transitions | `CredentialStatus` extended with `recovered` (mirrors BIO-CRD-006's own `CardLockState.Recovered`); `data-store.ts`'s `markCardLost`/`recoverCard`/`replaceCard`/`revokeCard` enforce the real transition graph (active/recovered→lost→recovered\|replaced\|revoked, active/recovered→revoked directly) | `bio-ux-007-data-store.test.ts` (11 tests); `bio-ux-007-cards.spec.ts` |
| **AC04** | Primary issue/replace/print/encode actions return simulated evidence and are visibly marked when not live | `CardActionResult.evidenceRef` (deterministic `SIM-EVD-<action>-<n>` reference) on every success; `ActionResultBanner` renders a `SIMULATED` badge whenever the resolved port's `mode === 'mock'` | `bio-ux-007-cards.spec.ts` ("shows a success result with simulated evidence") |
| **AC05** | All card pages use shared design tokens and do not modify application-shell code | `cards.module.css` uses only `--biolog-*` custom properties already defined in `globals.css`/`design-system/tokens.ts`; `web/app/layout.tsx` and `components/*` are untouched | `bio-ux-007-cards.spec.ts` accessibility baseline (6 routes, axe, 0 violations) |
| **AC06** | Stable service ports exist for later card lifecycle, production and encoding integrations | `web/features/cards/ports.ts` defines 9 independently-resolvable, typed ports (`issueCard`, `replaceCard`, `markLost`, `recoverCard`, `revokeCard`, `encodeTag`, `verifyRead`, `revokeMobileCredential`, `saveDesign`), each with its own mock/live pair; Card Designer gets a real `saveDesign` port via a new `SaveDesignBar` composed alongside (not inside) BIO-CRD-002/003/004's own components | `bio-ux-005-module-registry.test.ts` (multi-port registry parity, updated); `bio-ux-007-cards.spec.ts` ("Save design is a real, working primary action") |

## Design decisions

### Nine real ports, not one generic `primaryAction`

BIO-UX-005 shipped `web/features/cards/{ports,mock,live,index}.ts` as an intentionally empty
scaffold (`CardsPort = ModulePort`) because no Cards screen shipped real behaviour yet. This story
is the one BIO-UX-005's own header comment named as the reason that scaffold exists. Every real
action here needs typed arguments (which card, which reason) that the zero-argument
`ModulePort.primaryAction()` cannot carry, so each action is its own independently-resolvable port
instance, following the exact precedent `features/reporting/ports.ts` already established for a
module with more than one real action (`primaryAction`/`viewAuditTrail`). The original
`primaryAction` port is kept and still registered (two existing BIO-UX-005 tests require every real
domain module keep one), just no longer what any real Cards page actually calls.

Every richer port interface declares its input parameter *optional* (`input?: X`), not because it
is ever meaningfully optional, but because `ModulePort.primaryAction` itself takes zero arguments
and TypeScript checks parameter count/required-ness strictly even for method-shorthand interface
extension - a method requiring an argument cannot extend one that takes none. A missing `input` is
therefore a genuine programmer error, handled as an explicit `failure` result rather than a thrown
exception.

### A tenant-scoped, module-singleton data store - not shell state

`synthetic-data/store.ts`'s own header comment names exactly this story's use case ("a simulated
'Issue card' primary action updating a credential's status") as the reason `SyntheticDataStore`
exists, but explicitly defers *where* it gets instantiated to "BIO-UX-005/012's integration point...
shared-shell changes need Damilola's approval." `web/features/cards/data-store.ts` is a
module-scoped singleton owned entirely by this feature directory, never imported by shell/layout
code - not a shared-shell change (AC05), just this domain's own data layer, the same seam every
other feature's `mock.ts` already is.

### Mock `failure` always means a genuine, reproducible business-rule rejection

Unlike the generic placeholder ports (`createMockAdapter`'s scripted success/failure/offline
cycle, unrelated to input), every Cards action here has a real, checkable precondition. `failure`
always reflects `data-store.ts`'s own validation (a card already replaced, a person who already
holds a card, etc.) - the same invalid input always fails, the same valid input always succeeds
modulo the deterministic offline gate. `offline` is on its own deterministic schedule (every 5th
call to a given action instance), simulating a dropped connection *before* the request would have
reached the "server" - nothing is mutated when it fires.

### `recovered` is its own status, not a silent reset to `active`

A card reported lost and later found keeps that history visible in the registry, mirroring the
already-shipped backend's own `CardLockState.Recovered` (BIO-CRD-006) rather than losing the fact
it was ever lost. A recovered card is usable again in every way `active` is.

### The baseline dataset leaves one person per tenant without a card

`generateCredentials` (`synthetic-data/generators.ts`, BIO-UX-004-owned, extended here per the same
"shared, unlisted entry point every conflict-group story updates" precedent as
`module-registry.ts`/`route-manifest.ts`/`feature-mode.ts`) previously issued every single
generated person an active card. A fresh `normal` dataset where nobody is eligible for Issue Card
left that screen with nothing to ever demonstrate on first load - not a realistic card-centre
baseline either (a new hire/student always exists). The RNG draw for a card number still happens
for every person, including the one skipped, so this does not shift any other generator's own
deterministic output for this or any later tenant - verified by rerunning the full existing
`bio-ux-004-*` test suite (still green) rather than assumed.

### `nfc-uhf`/`mobile-credentials` routes resolved from `Unassigned`

BIO-UX-002 left both routes explicitly `servicePort: 'Unassigned'` with a note reading "not assumed
here." This story's own AC01 (interactive NFC/UHF and Mobile Credential pages) and AC06 (stable
ports "for later card lifecycle, production and encoding integrations") make that assumption:
NFC/UHF encoding is the same `CredentialService` domain, and Mobile Credentials' status/revoke
surface (provisioning itself stays on the mobile app, per BIO-UX-002's own documented boundary) is
a real `CredentialService` action too. `route-manifest.ts`'s own notes on both entries record this
resolution and its reasoning.

### Card Designer's canvas is untouched; it only gains a real "Save design" port

Per Kemi's ruling (communicated in this story's dispatch), Card Designer stayed outside the
mock/live adapter framework in BIO-UX-005 because it has zero I/O - pure client-side canvas, no
port to retrofit. `SaveDesignBar` (`web/features/cards/SaveDesignBar.tsx`) is composed into
`web/app/card-designer/page.tsx` (the same shared, unlisted entry point BIO-CRD-002/003/004 already
each add their own section to) as a fourth, independent section. It does not read or touch any of
the three canvas apps' own internal design state - only a real, working "Save design" action for
the route's own primary action name (`route-manifest.ts`).

### Badge/banner colour contrast: solid token colours are not always AA-sufficient at small sizes

The first implementation paired `colorSuccess`/`colorWarning`/`colorError`/`colorOffline`/`colorInfo`
as either coloured text on a tinted background, or white text on a solid fill, following
`tokens.ts`'s own documented "-Bg for badges" / "-On for solid fill" guidance. Both combinations
measured under the WCAG AA 4.5:1 threshold at this badge's small size (0.75rem/600, below the "large
text" exception) - caught by `bio-ux-007-cards.spec.ts`'s own axe accessibility baseline, not
assumed passing. Every status badge, the `SIMULATED` marker, and every result banner now use the
light tinted background paired with the shared neutral dark body text colour (`--biolog-color-text`)
instead - comfortably clears 4.5:1 regardless of hue, while the tint still gives each status its own
visual identity at a glance.

### The `/cards` related-links landmark is preserved exactly

`bio-ux-002-shell.spec.ts`'s own "related-links from a placeholder page navigate to a real
sub-route" test asserts a `nav[aria-label="Cards related pages"]` landmark with an "Issue Card" link
against `/cards` specifically. Replacing `/cards`'s `PlaceholderPage` with this story's own real
registry reproduces that exact landmark (`RelatedLinksNav.tsx`) rather than silently dropping the
navigation path - verified by rerunning that existing test, not just avoided.

## Deliberately out of scope

- Real backend connectivity for any of the nine new ports - every `live.ts` adapter genuinely calls
  its own endpoint and genuinely fails closed to `offline` until a backend story connects it,
  exactly like every other domain's own `live.ts` in this codebase.
- Redesigning the Card Designer canvas itself (BIO-CRD-002/003/004's own owned files) - untouched.
- Any change to `governance/ui-integration-map.json` - `route-manifest.ts`'s own notes record the
  NFC/UHF and Mobile Credentials resolution for governance review, per the same disclosed pattern
  the Dashboard/ReportingService entry already established (BIO-UX-003), not decided unilaterally
  in the governance source file itself.
