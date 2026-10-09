# BIO-UX-003 acceptance audit — round 1

## Verdict: PASS

All approved criteria map to implementation and evidence:

- AC01: the typed manifest covers all 25 named page families across 31 routes.
- AC02: every route declares primary/secondary actions, visibility permission, and a target service port; unresolved mappings are explicit `Unassigned` values with notes.
- AC03: every route resolves to the standard loading, empty, success, warning, error, permission-denied, and offline contract, or a documented subset.
- AC04: enrolment, card lifecycle, biometric verification, and document verification are diagrammed as cross-page journeys.
- AC05: every primary navigation target and manifest route has a real `page.tsx`, and navigation is fully covered by the manifest.

Acceptance replay:

```text
corepack pnpm --dir tests/frontend test:unit
PASS: 24 files, 183 tests

corepack pnpm --dir web typecheck
PASS

corepack pnpm --dir web build
PASS: 34 static pages generated
```

Builder evidence bound to implementation `ce8ce0e5385bc490444384e5285de261abad3aea`
also records 102/102 Playwright tests passing. Seyi independently reviewed the route/action/
permission/port/state contracts and journeys and replayed 24 files/183 tests at `e130163`.

The current authoritative `governance/ui-integration-map.json` has been corrected outside the
feature commit so BIO-RPT-001 through BIO-RPT-004 map the dashboard to `/`, with a dated note.
This matches the manifest and removes the earlier `/dashboard` discrepancy. Alumni, Contractors,
NFC/UHF, and the web-facing Mobile Credentials status page remain explicitly `Unassigned`; the
story does not invent backend ownership.
