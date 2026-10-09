# BIO-UX-004 acceptance audit — round 1

## Verdict: PASS

All approved criteria map to implementation and evidence:

- AC01: a seeded generator deterministically produces tenant, people, credential, photo, device, access, attendance, visitor, transport, and document records for two tenants.
- AC02: all eight named scenarios are pure, deterministic transforms with asserted outcomes.
- AC03: `resetToSeed()` discards mutations and regenerates the exact seed-and-scenario state.
- AC04: records are machine-marked synthetic; no secret, national-identifier, biometric-template, or image-payload fields exist; photos contain only quality metadata and a placeholder colour.
- AC05: display-facing Nigerian people, visitor, institution, and device names carry `(SYNTHETIC)` markers, and every record also carries `isSynthetic: true`.

Acceptance replay:

```text
corepack pnpm --dir tests/frontend test:unit
PASS: 30 files, 220 tests

corepack pnpm --dir web typecheck
PASS

corepack pnpm --dir web build
PASS: 34 static pages generated
```

Builder evidence bound to implementation `9e5ffd564497e7dfd17285d154d6af3a7a1a175f`
also records 102/102 Playwright regression tests passing against the correct worktree server.
Seyi independently reviewed determinism, ten-domain/two-tenant coverage, all scenarios,
reset behavior, and privacy markers and replayed 30 files/220 tests at `f2ff294`.

Live UI integration is intentionally downstream in BIO-UX-005/BIO-UX-012; this story supplies
the deterministic data and scenario primitive and has no user-facing route or primary action.
