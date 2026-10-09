# BIO-UX-014 acceptance audit — round 2

## Verdict: PASS

QA-01 is closed at remediation `5955bfdf5e36f33af66f25c9690190168adb8581`, builder evidence
`68847f0`, and Seyi round-two review `07073d7`.

- Viewport setup now precedes theme init-script installation.
- Every capture asserts the requested `html[data-theme]` and has only one bounded reload fallback.
- All three Dashboard light/dark PNG pairs have distinct SHA-256 hashes; direct inspection confirms
  that the dark desktop baseline renders the approved dark palette.
- The bidirectional selectivity proof shows a dark-only change fails exactly the three dark
  Dashboard tests, while a light-only change fails the three light Dashboard tests and eight
  light-only representative-page tests.
- Representative routes, explicit baseline updates, local-only synthetic data, UTC, and `en-GB`
  remain intact. The BIO-UX-013 shared-fixture extraction remains additive and unchanged.

Acceptance replay passed 51 files / 384 unit tests and TypeScript. A clean archived checkout also
passed TypeScript, including the generated `next-env.d.ts` path recorded in the candidate. Local
Playwright replay could not start its server because the managed sandbox denied binding
`127.0.0.1:3000` with `EPERM`; this is an environment limitation, not a test failure. Builder evidence
contains clean 14/14 runs, and governance independently ran the remediated visual suite four times at
14/14. The disclosed stale `/cards` result at the end of the mutation transcript did not reproduce in
those later independent runs and does not undermine the fail-closed theme assertion.

AC01–AC05 pass. The separately tracked Dashboard timezone hydration defect remains outside this
test-infrastructure story and is not silently treated as fixed here.
