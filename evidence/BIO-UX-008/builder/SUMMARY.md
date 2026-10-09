# BIO-UX-008 builder summary

Implements interactive fingerprint and face biometric journeys on the real `/fingerprints` and
`/facial-recognition` routes, previously `PlaceholderPage` shells since BIO-UX-002.

## What changed

- `web/features/biometrics/ports.ts` — `BiometricsPort` extends `ModulePort` with seven real
  methods (`connectDevice`, `captureFingerprint`, `verifyFingerprint`, `captureFace`,
  `checkLiveness`, `verifyFace`, `identifyFace`), each taking an optional `hint` so every named
  simulated outcome is explicitly selectable (AC03), not just reachable by chance.
- `web/features/biometrics/mock.ts` / `live.ts` — deterministic, hint-aware mock implementations
  (every message says "SIMULATED", AC04) and honestly-failing live stubs against seven named
  `/api/v1/biometrics/*` endpoints (AC05), with `primaryAction` preserved unchanged for
  `/enrolment`'s still-shell `PlaceholderPage`.
- `web/features/bio-ux-008/` (new) — `FingerprintJourneyApp.tsx` (tenprint grid, single-finger
  capture, 1:1 verification — AC01), `FaceJourneyApp.tsx` (capture, liveness placeholder, 1:1
  verification, 1:N identification — AC02), `DeviceConnectionPanel.tsx` (shared simulated device
  connection — AC03), plus pure/testable `tenprintState.ts`, `scenarioOptions.ts`, `formatters.ts`.
- `web/app/fingerprints/page.tsx`, `web/app/facial-recognition/page.tsx` — now render the real
  journey apps instead of `PlaceholderPage`.

## Scope boundary

Only `/fingerprints` and `/facial-recognition` are touched. `/photographs` (no BIO-UX story
assigned yet) and `/enrolment` (BIO-UX-006's own scope) are untouched — see
`docs/features/bio-ux-008/README.md` for the full reasoning, including why this story's own five
ACs, not the broader domain description in the dispatch, define its scope.

## Test evidence

- `pnpm --dir web run typecheck && pnpm --dir web run build` — clean, 34 static routes generated —
  see `web-typecheck-build-output.txt`.
- `pnpm --dir tests/frontend test:unit` — 290/290 passing across 38 files (11 new: 5 BIO-UX-008
  files) — see `frontend-unit-test-output.txt`.
- `pnpm --dir tests/frontend test:e2e` (against a manually started `next dev` server — the
  sandbox's own network policy denies `listen()` on a fresh port from inside a sandboxed process,
  so the server was started with the sandbox disabled for that one command, the same category of
  documented exception `dotnet test`/vstest already needs on this floor) — 125/125 passing (15 new
  BIO-UX-008 tests, no regression in any other story's suite) — see `frontend-e2e-test-output.txt`.
- `python3 scripts/check-traceability.py --all` — PASS.

## Notes

- No HTTP endpoint exists yet on the backend for any of the seven live methods — consistent with
  every BIO-BIO-004..007 backend story's own "no HTTP endpoint is mapped" scope decision.
- Security requirement ("Simulation must not store or request real biometric data") holds
  structurally: no file input, no camera/`getUserMedia` call, and no persistence anywhere in this
  story's own code.
