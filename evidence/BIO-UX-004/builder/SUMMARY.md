# BIO-UX-004 — Builder evidence

**Story:** Build deterministic synthetic data and scenario system
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-UX-004`, branch `feature/BIO-UX-004` off `develop`
**Dependencies:** BIO-UX-003 (DONE — route manifest, referenced for the domains each page family needs)
**Commit:** `9e5ffd564497e7dfd17285d154d6af3a7a1a175f`

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — seeded synthetic records generated deterministically across all named domains | `web/lib/synthetic-data/rng.ts` (seeded `mulberry32`), `generators.ts` (tenant/people/credentials/photos/devices/access/attendance/visitors/transport/documents), `dataset.ts`'s `generateDataset(seed, scenario)` | `bio-ux-004-generators.test.ts`, `bio-ux-004-rng.test.ts` (14 tests) |
| AC02 — the eight named scenario presets | `scenarios.ts`'s `applyScenario`, one pure transform per `ScenarioId` | `bio-ux-004-scenarios.test.ts` (10 tests) |
| AC03 — reset-to-seed restores a known state | `store.ts`'s `SyntheticDataStore.resetToSeed()` | `bio-ux-004-store.test.ts` (4 tests) |
| AC04 — no production PII/biometric templates/secrets/tenant data | `isSynthetic: true` on every record; photo records hold only `qualityScore`/`placeholderColor`, never image bytes or a template | `bio-ux-004-privacy-markers.test.ts` (7 tests) |
| AC05 — synthetic Nigerian names/institutions clearly marked | `names.ts`'s `withMarker()` appends `(SYNTHETIC)`, matching the existing `search-fixtures.ts`/`session-context.tsx` convention | `bio-ux-004-privacy-markers.test.ts`, `bio-ux-004-tenant-parity.test.ts` (2 tests) |

## Developer tests (37 new, 220 total frontend unit tests pass)

- `tests/frontend/unit/bio-ux-004-rng.test.ts` — seeded PRNG determinism and helper bounds.
- `tests/frontend/unit/bio-ux-004-generators.test.ts` — every AC01 domain populated and
  cross-referenced correctly; two independent `generateDataset()` calls with the same seed are
  deep-equal; different seeds differ.
- `tests/frontend/unit/bio-ux-004-scenarios.test.ts` — `SCENARIO_IDS` matches AC02 verbatim; each
  scenario's documented effect is asserted directly (e.g. `offline` flips every device to
  `connectivity: 'offline'`; `duplicate-identity` produces two people sharing a
  `duplicateGroupId`); scenario application is itself deterministic.
- `tests/frontend/unit/bio-ux-004-store.test.ts` — a store mutated in three different ways still
  returns to the exact original dataset (`JSON`-snapshot deep equality) after `resetToSeed()`.
- `tests/frontend/unit/bio-ux-004-privacy-markers.test.ts` — every record across every domain
  carries `isSynthetic: true`; no field name in the whole dataset resembles a password/secret/NIN/
  BVN/biometric-template field; photo records have exactly the documented shape (no image bytes).
- `tests/frontend/unit/bio-ux-004-tenant-parity.test.ts` — this story's synthetic tenant/person
  identities stay consistent with `session-context.tsx`'s already-shipped demo session, to the
  extent that module's internals are actually exported (documented as a partial guarantee, not
  overclaimed).

Reproduce with:
```sh
corepack pnpm --dir web typecheck
corepack pnpm --dir web build
corepack pnpm --dir tests/frontend test:unit
BIOLOG_BASE_URL=http://127.0.0.1:3299 corepack pnpm --dir tests/frontend test:e2e
```

## A testing mistake caught before it became a false regression report

Capturing Playwright evidence, port 3000 was held by a previously-flagged unrelated stale
process (not this story's to touch) and a first retry on port 3214 turned out to collide with
`biolog-worktrees/BIO-UX-005/web`'s own dev server — a different story dispatched in parallel
this wave. That server's `next dev` process had actually bound the port from an earlier attempt
whose CLI wrapper misreported failure; running Playwright against it produced one failure in
`bio-ux-002-shell.spec.ts` (a "Simulate primary action" status-text assertion) that would have
read as a real regression on `develop`. Verified via `lsof`/`ps` that the responding process's
`cwd` was `BIO-UX-005/web`, not this worktree, before trusting the result — restarted on a
confirmed-free port (3299), re-verified the new process's `cwd` matched this worktree, and the
full suite passed 102/102. No regression exists; full detail in
`docs/features/bio-ux-004/README.md`'s own section on this. Not reported to god as a finding
since it resolved to "no bug" once traced — recorded here and in the docs as a reproducible
gotcha for any story capturing e2e evidence while sibling worktrees are also running dev servers
in this wave.

## Scope boundaries, documented rather than silently assumed

1. **No live UI wiring.** `SyntheticDataStore` is a library primitive; nothing in this story
   calls it from a real button or wires it into `web/app/layout.tsx`. That integration belongs to
   BIO-UX-005 (mock/live adapter framework) and BIO-UX-012 (Human Test Console) — shared-shell
   changes need Damilola's approval, not decided unilaterally here.
2. **No real biometric/document content**, ever — photo records are a quality score plus a
   deterministic CSS colour, never image bytes or a template of any kind.
3. **No `governance/stories/BIO-UX-004.json`.** Matches BIO-UX-001/002/003's own precedent:
   `scripts/check-traceability.py`'s inventory is keyed off `tests/foundation/<KEY>.test.sh`,
   which no pure-frontend UX story uses. This evidence directory (`manifest.json` + `builder/`)
   is the traceability record, following that established pattern exactly.
4. **Partial, not total, tenant/person-name parity with `session-context.tsx`.** That module
   doesn't export its `TENANTS`/`DEMO_USER_BY_ROLE` consts, so the cross-check test can only
   verify what is actually exported (`DEFAULT_TENANT_ID`) plus the three literal demo names —
   documented as a known, partial guarantee in both the test file and
   `docs/features/bio-ux-004/README.md`, not overclaimed as full referential equality.

Full suite green: `tsc --noEmit` clean, `next build` 34 static pages (unchanged from
BIO-UX-003 — no routes added/removed), unit 220/220 (37 new), Playwright 102/102 (regression,
no new specs — this story adds no UI surface).
