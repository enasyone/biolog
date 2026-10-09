# BIO-UX-014 acceptance audit — round 1

## Verdict: FAIL

Implementation `cf4def0c66c05f8047b869f7a21267e5304c1c38` and builder evidence
`1b14041d87b83a32095575761cae1932f2c8fb10` do not yet provide a valid theme baseline.

### QA-01 — High — the nominal dark baselines are light-theme captures

All three Dashboard dark PNGs are byte-for-byte identical to their corresponding light PNGs:

- mobile light/dark: SHA-256 `a57d3df78a2529387159166fe4412a2eeb661da2dde926b5b085bd06b155f444`
- tablet light/dark: SHA-256 `b27d8d599efea4378324939b4ef92e17d5f8c55017b6b431f305292fd640fa5f`
- desktop light/dark: SHA-256 `e6ccae2edbd8ab5c7c9949e6f4c76cf787bf6304149f84e871e94af90eae5d57`

Visual inspection confirms the file named `dashboard-desktop-dark-darwin.png` renders the light
surface palette. The screenshot helper sets local storage but never asserts the resulting
`html[data-theme]` value before capture, so the suite passes against mislabeled light images and
cannot detect dark-theme drift. This contradicts the documented viewport/theme matrix and the
builder's AC01/AC02 evidence.

Required remediation: make theme selection effective, assert `html[data-theme]` equals the
requested theme before every capture, regenerate the affected baselines, and provide a deliberate
dark-only visual change proof showing dark fails while light remains green. Fresh independent review
must bind to the remediated candidate.

### Traceability

- **AC01: FAIL** — representative page coverage exists, but the claimed shell theme matrix contains
  no effective dark capture.
- **AC02: FAIL** — the combinations are documented, but the executable baseline contradicts the
  documented dark combinations.
- **AC03: PASS** — `corepack pnpm --dir tests/frontend test:visual` is documented as one command.
- **AC04: PASS, subject to QA-01** — update is explicitly separated and the deliberate Dashboard
  colour-change transcript detects six affected files; it does not prove theme separation.
- **AC05: PASS** — local URLs, seeded synthetic data, UTC, and `en-GB` are pinned.

The BIO-UX-013 fixture extraction itself is acceptable: its existing viewport/page values and
assertions are unchanged, `/enrolment` is additive, and the committed full regression is 368/368.
Acceptance replay independently passed 51 files / 384 unit tests and TypeScript. Those green checks
do not detect QA-01 because no test asserts the applied theme or light/dark image distinctness.
