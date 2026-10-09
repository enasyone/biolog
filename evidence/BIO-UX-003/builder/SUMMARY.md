# BIO-UX-003 — Builder evidence

**Story:** Create complete page inventory, route manifest and interaction map
**Owner:** Damilola — Product UI/UX & Frontend Lead
**Worktree:** `biolog-worktrees/BIO-UX-003`, branch `feature/BIO-UX-003` off `develop`
**Dependency:** BIO-UX-002 (DONE, merged `8417756`)
**Conflict group:** `ui-information-architecture`
**Commit:** `ce8ce0e5385bc490444384e5285de261abad3aea`

## AC to evidence map

See `docs/features/bio-ux-003/README.md` for the full AC table, the three resolved ambiguities and
the four journey diagrams. Summary:

| AC | Evidence |
|---|---|
| AC01 (route manifest covers named families) | `web/lib/route-manifest.ts` `ROUTE_MANIFEST` (31 entries); `bio-ux-003-route-manifest.test.ts` |
| AC02 (actions/visibility/port per route) | Every entry's `primaryActions`/`secondaryActions`/`requiredPermission`/`servicePort`; same test file |
| AC03 (page states) | `STANDARD_PAGE_STATES` + per-entry `states` override with mandatory note; same test file |
| AC04 (journeys diagrammed) | 4 Mermaid diagrams in the docs README (enrolment, card lifecycle, biometric verification, document verification) |
| AC05 (no dead primary nav item) | Filesystem existence check for every `PRIMARY_NAV_ITEMS` href's `page.tsx`; same test file |

## Developer tests (24 unit files / 183 tests, 5 Playwright spec files / 102 tests — all pass)

- New: `bio-ux-003-route-manifest.test.ts` (11 tests) — mechanically verifies AC01/AC02/AC03/AC05
  rather than relying on documentation review alone, including a real `existsSync` check against
  `web/app/` for every nav item's route file.
- **A real gap was caught and fixed before commit**, not just written and left unverified: the
  first test run failed — `settings-authentication` narrowed its `states` list but had no
  explanatory `note`, which the "a narrowed states list always carries an explanatory note" test
  correctly rejected. Fixed by adding the note; re-ran to confirm green.
- No route, component or shell file changed by this story, so BIO-UX-002's full Playwright suite
  (102 tests) was re-run unmodified for evidence completeness, not because anything user-visible
  changed — confirmed identical pass count.

## Decisions and assumptions

- **"Visitors" vs. "Visitor Management" resolved to one route.** This story's own AC01 — the
  authoritative source for this specific deliverable — names only "Visitors" once, and
  `ui-integration-map.json` defines exactly one route for the domain. Documented in
  `route-manifest.ts`'s own `visitors` entry note and the docs README, not just decided silently.
- **`/alumni`, `/contractors`, `/nfc-uhf` confirmed to have zero `ui-integration-map.json` entries**
  (re-read the full file, not just re-checked memory of it from BIO-UX-002). Marked `Unassigned`
  with a note naming the most plausible eventual owner — a suggestion for governance, not an
  assertion. Assigning the real port is Kemi's/governance's call.
- **A third discrepancy found while building the manifest**: `ReportingService`'s
  `ui-integration-map.json` entry names `/dashboard`, distinct from the shell's real `/` home page.
  Resolved to `/` (flagged, not silently duplicated into a second dashboard route).
- **Primary/secondary actions describe intended production behaviour**, sourced from the
  already-approved `BIO-UX-006`–`BIO-UX-011` story ACs (read in full this session), not today's
  literal "Simulate primary action" placeholder button every route currently renders — this is a
  forward-looking design record, matching the story's own framing ("before feature coding").
- **Journeys are Mermaid diagrams in Markdown**, the practical form of "diagrammed" available in a
  text-based repository with no diagramming tool; every route referenced was cross-checked against
  `ROUTE_MANIFEST_HREFS` to ensure no diagram points at a route this manifest doesn't also cover.

## Known gaps / local-session notes (not blocking)

- Full-repo `scripts/quality.sh` was not run (same precedent as BIO-UX-001/002: backend/.NET/infra
  suites unrelated to a frontend-only, no-route-changed story). Direct AC-relevant commands (`web/`
  typecheck+build, full `tests/frontend` unit and Playwright suites) all ran to completion and pass.
- The port-mapping gaps for `/alumni`/`/contractors`/`/nfc-uhf` and the `/dashboard` vs. `/` map
  discrepancy remain genuinely open — they are governance-file corrections this story flags but
  does not have the authority or the ownership to make itself.
