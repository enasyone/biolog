# BIO-UX-004 — Build deterministic synthetic data and scenario system

As a human tester, I need deterministic synthetic BIOLOG data and scenarios so the same UI
journey can be repeated reliably while services are still being built.

This story adds `web/lib/synthetic-data/`, a pure, dependency-free TypeScript library — no new
routes, no shell/component changes, no `governance/ui-integration-map.json` entry (it has no
user-facing surface of its own). It is a data layer other stories build on: BIO-UX-005 (mock/live
adapter framework) is the intended first consumer, and BIO-UX-012 (Human Test Console) the
intended place a human switches scenarios interactively. Neither integration is wired here —
see "Scope boundaries" below.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Seeded synthetic tenant, people, credentials, photos, devices, access, attendance, visitor, transport and document records are generated deterministically | `generators.ts`'s per-domain generators, composed by `dataset.ts`'s `generateDataset(seed, scenario)`, driven by `rng.ts`'s seeded `mulberry32` PRNG | `bio-ux-004-generators.test.ts`, `bio-ux-004-rng.test.ts` |
| **AC02** | Scenario presets: normal, empty, error, offline, low-quality biometric, duplicate identity, lost card, document mismatch | `scenarios.ts`'s `applyScenario`, one pure transform per `ScenarioId` | `bio-ux-004-scenarios.test.ts` |
| **AC03** | Reset-to-seed restores a known state | `store.ts`'s `SyntheticDataStore.resetToSeed()` — regenerates from the held seed+scenario, discarding any `mutate()` applied in between | `bio-ux-004-store.test.ts` |
| **AC04** | No production PII, biometric templates, secrets or tenant data are included | Every record carries `isSynthetic: true`; photo records hold only a `qualityScore`/`placeholderColor`, never image bytes or a biometric template — see `types.ts`'s header note | `bio-ux-004-privacy-markers.test.ts` |
| **AC05** | Synthetic Nigerian names and institutions are clearly marked as test data | `names.ts`'s `withMarker()` appends `(SYNTHETIC)` to every display-facing label, matching the convention already shipped in `search-fixtures.ts`/`session-context.tsx` | `bio-ux-004-privacy-markers.test.ts`, `bio-ux-004-tenant-parity.test.ts` |

## Design

- **Determinism**: `mulberry32`, a small well-known seeded PRNG (`rng.ts`). No generator reads
  the system clock, `Math.random()`, or module-level mutable state — `generateDataset(seed,
  scenario)` is a pure function, verified directly by `bio-ux-004-generators.test.ts` and
  `bio-ux-004-scenarios.test.ts` calling it twice and asserting deep equality.
- **Two tenants, ten domains per tenant**: `alpha`/`beta`, matching the two tenants
  `session-context.tsx` (BIO-FND-007/BIO-UX-002) already ships in its tenant selector — see
  "Consistency with the shipped demo session" below. Each tenant gets 8 people (4 categories:
  student/staff/alumni/contractor), 3 devices, 1 credential + 1 photo per person, 2 access
  events per device, attendance for student/staff, 2 visitors, 2 transport events and 2
  documents.
- **Scenarios are transforms, not separate generators**: every `ScenarioId` other than `normal`
  takes the one `normal` baseline dataset and applies a small, documented, testable mutation
  (e.g. `offline` flips every device's `connectivity` and sets `flags.simulateOffline`). This
  keeps the eight presets from drifting out of sync with each other or with AC01's domain list.
- **`ScenarioFlags`** (`simulateError`, `simulateOffline`) exist for a future mock adapter to
  read and decide whether to simulate a failed/degraded call, rather than needing its own
  per-scenario interpretation logic.

## Consistency with the shipped demo session

`session-context.tsx`'s `TENANTS` (`alpha` → "Alpha State University", `beta` → "Beta
Polytechnic") and `DEMO_USER_BY_ROLE` (Amara Adeleke, Kelechi Okafor, Ovie Eferakeya) are not
exported — shell-internal. Rather than importing that shell-owned module into this library (or
exporting those consts, a shared-shell edit that needs Damilola's approval, out of this story's
scope), `names.ts` keeps its own literal copy of the same two tenant ids/names, and its first
three generated people reuse the same three demo names at the same indices.
`bio-ux-004-tenant-parity.test.ts` is a regression tripwire on the parts that are actually
exported today (`DEFAULT_TENANT_ID`) plus the three literal demo names — a partial, not total,
consistency guarantee, documented as such rather than overclaimed.

## Scope boundaries

1. **No live UI wiring.** `SyntheticDataStore` exists so a future mock adapter has something to
   mutate and reset, but nothing in this story calls `mutate()` from a real button, and the
   store is not wired into `web/app/layout.tsx` or any page. That integration is BIO-UX-005's
   (adapter framework) and BIO-UX-012's (Human Test Console) to do, per the UI-first rule's
   "shared shell edits require Damilola's approval" — not decided unilaterally here.
2. **No real biometric/document content.** Photos are a quality score plus a deterministic CSS
   colour, never image bytes; there is no fingerprint/face template field of any kind. A future
   story that adds real capture storage must not repurpose these fields as if they already held
   real biometric data.
3. **Governance traceability**: this story has no `governance/stories/BIO-UX-004.json` spec, by
   the same precedent as BIO-UX-001/002/003 — `scripts/check-traceability.py`'s inventory is
   keyed off `tests/foundation/<KEY>.test.sh`, which no pure-frontend UX story has used so far.
   Evidence lives under `evidence/BIO-UX-004/` (`manifest.json` + `builder/`), matching those
   three stories' own pattern exactly.

## A testing mistake caught before it became a false report

While capturing this story's Playwright regression evidence, the default port (3000) was held
by an unrelated stale process (previously flagged, not this story's to touch), and a first
retry on port 3214 collided with a **different, concurrently-running worktree's** dev server
(`biolog-worktrees/BIO-UX-005/web`, running in parallel per this wave's dispatch) — confirmed by
checking that process's `cwd` directly. The Playwright run against that wrong server showed one
failure (`bio-ux-002-shell.spec.ts`'s "Simulate primary action" status-text assertion) that would
have looked like a real regression on `develop`. Verified `cwd` before trusting the result,
restarted on a confirmed-free port (3299) against this story's own build, and the full suite
passed (102/102) — no regression exists. Recorded here as a reminder: a passing/failing
Playwright run only means something once the server it hit is confirmed to be the right one,
especially when other stories are dispatched in parallel on shared, guessable ports.

## Local development / verification

```sh
corepack pnpm --dir web typecheck
corepack pnpm --dir web build
corepack pnpm --dir tests/frontend test:unit   # includes 5 new bio-ux-004-*.test.ts files, 37 tests
corepack pnpm --dir tests/frontend test:e2e    # unchanged (no new UI surface); re-run for regression evidence
```
