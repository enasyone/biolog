# BIO-ARC-001 — builder evidence

- **Story:** BIO-ARC-001 — Define canonical identifiers, clocks and versioning
- **Owner:** Kemi — Chief Architect & Integrator (claude-opus-5)
- **Wave / batch:** W01 / W01.1 · conflict group `shared-contract`
- **Branch:** `feature/BIO-ARC-001`, worktree `biolog-worktrees/BIO-ARC-001`, branched from `develop` at `2224d4d`
- **Date:** 2026-09-20
- **Remote:** none. Nothing pushed. `governance/release-state.yaml` untouched.

## What was built

The contract itself is `docs/adr/ADR-0001-canonical-identifiers-clocks-and-versioning.md`.
It is the normative decision; the schemas and code below are its machine-readable and
executable forms.

| Artifact | Purpose |
|---|---|
| `docs/adr/ADR-0001-…md` | The accepted decision: identifier strategy, clock rules, concurrency rules, rejected alternatives |
| `docs/adr/README.md` | ADR index and the rule that an ADR is superseded, never edited |
| `contracts/schemas/identifier-registry.json` | The six registered identifier types and their prefixes |
| `contracts/schemas/canonical-id.schema.json` | Wire form of an identifier |
| `contracts/schemas/utc-instant.schema.json` | Wire form of a timestamp |
| `contracts/schemas/calendar-date.schema.json` | A date with no instant (`*_on`) |
| `contracts/schemas/versioned-resource.schema.json` | Envelope for a mutable aggregate |
| `contracts/schemas/event-envelope.schema.json` | Envelope for a domain event |
| `contracts/schemas/offline-mutation.schema.json` | Envelope for a queued offline mutation |
| `contracts/js/identifiers.mjs` | Mint / parse / type-check / store identifiers |
| `contracts/js/clock.mjs` | UTC instants, explicit local rendering, device clock-skew assessment |
| `contracts/js/concurrency.mjs` | Compare-and-set, idempotency, sync conflict classification |
| `contracts/js/contracts.test.mjs` | 45 behavioural tests, each named with the AC it proves |
| `contracts/README.md` | Consumer rules: consume don't copy, how to add a type, freeze policy |
| `tests/foundation/BIO-ARC-001.test.sh` | 20 artifact/consistency checks; delegates behaviour to the node suite |

## Decisions of record

1. **Identifier** = `<type-prefix>_<UUIDv7, lowercase, dashed>`, e.g.
   `per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d`. Six registered types: `ten`, `per`, `crd`,
   `dev`, `dcs`, `evt`. Stored as `BINARY(16)` / `BLOB` without the prefix.
   UUIDv7 because offline devices must mint final identifiers with no round trip, and
   because time-ordered keys keep the MySQL primary-key index local at the specified
   event volume. The UUID's embedded time is ordering metadata, never a business time.
2. **Timestamps** are UTC everywhere internally, RFC 3339 with literal `Z` and exactly
   three fractional digits; MySQL `DATETIME(3)`, never `TIMESTAMP`. Local time exists
   only at render, always with an explicit IANA zone, and carries its zone and offset.
   Edge events carry both `occurred_at` (device claim) and `received_at` (authoritative).
3. **Concurrency** is optimistic: integer `version` from 1, +1 per commit, compare-and-set
   on every mutating write. Mismatch → `409 concurrency_conflict`; missing precondition →
   `428 precondition_required`, deliberately *not* last-write-wins. Append-only records
   carry no version and reject updates. Idempotency keys replay the stored result on an
   identical body and `409 idempotency_key_reuse` on a different one. Security-sensitive
   sync conflicts go to the human Sync Resolution Queue; without a written merge rule the
   default for everything else is also CONFLICT.

## AC → evidence map

| AC | Proven by |
|---|---|
| **AC01** canonical ID strategy for tenant, person, credential, device, document session, events | ADR §1; `identifier-registry.json`; tests 1–13 in `contracts.test.mjs` ("AC01: …"), incl. type-mismatch rejection, malformed/foreign/injection-shaped input, 5,000-id collision check, creation-order sort, 16-byte storage round trip; shell checks AC01 ×6 |
| **AC02** UTC internally with explicit local rendering | ADR §2; `utc-instant.schema.json`; tests 14–23 ("AC02: …"), incl. rejection of naive/offset/low-precision values, three-zone rendering of one instant, a British Summer Time transition, calendar-date separation, device clock-skew assessment; shell checks AC02 ×5 |
| **AC03** optimistic concurrency / version rules | ADR §3; `versioned-resource` + `offline-mutation` schemas; tests 24–38 ("AC03: …"), incl. two concurrent writers where exactly one wins, 428 on a missing precondition, append-only refusal, idempotent replay vs key reuse, security-sensitive sync conflict that a merge rule cannot override; shell checks AC03 ×4 |
| **AC04** contracts represented in shared code/schema and tests | `contracts/` (schemas + reference implementation + README); tests 39–45 ("AC04: …") assert schema and code agree on the same definitions and that a built envelope satisfies its schema; shell checks AC04 ×5 |

## Test results

```
tests/foundation/BIO-ARC-001.test.sh   20/20 checks passed   (exit 0)
node --test contracts/js/              45 pass, 0 fail       (exit 0)
scripts/secret-scan.sh --all           clean                 (exit 0)
```

Full output: `developer-test-output.txt`, `contract-suite-output.txt`,
`secret-scan-output.txt`. Repository state: `git-state.txt`.

Both suites are dependency-free and run offline (`node --test` is built into the pinned
Node 20 runtime). This is deliberate: the shared test harness from BIO-FND-006 is not on
`develop` yet (see Notes), so the contract tests must stand alone.

## Constraints observed

- Synthetic data only. Every identifier, name and timestamp in the schemas, tests and
  docs is fabricated; no production PII, biometrics, secrets or tenant data.
- No GitHub remote added, nothing pushed, `github_push_enabled` untouched.
- Work confined to the `BIO-ARC-001` worktree and to paths this story owns
  (`contracts/`, `docs/adr/`, `tests/foundation/BIO-ARC-001.test.sh`, plus two README
  index lines). No other story's files were modified.

## Notes for the reviewer and for Chinedu

1. **`develop` carries only BIO-FND-001.** `feature/BIO-FND-002` … `BIO-FND-006` are
   committed on their own branches but not merged, so the toolchain pins (FND-002) and
   the test harness (FND-006 — this story's stated dependency) are not on the branch I
   was told to build from. I did not merge them: per `LOCAL-GIT-WORKFLOW.md` step 8 that
   is Chinedu's to do. It is why my tests use only the Node runtime and bash.
   **When those branches merge, `scripts/quality.sh` should gain
   `bash tests/foundation/BIO-ARC-001.test.sh` alongside the FND-006 line.** Flagging it
   so it is a scheduled follow-up rather than a surprise at wave exit.
2. **Scope held deliberately narrow.** Only the six identifier types the story names are
   registered. Visitor, attendance, transport and the rest belong to the stories that own
   those entities; the ADR states the procedure for adding one instead of pre-empting it.
3. **.NET and Flutter bindings are not in this story.** The JSON Schemas are the
   language-neutral source of truth and the JS module is the reference implementation.
   The stories that stand up `api/` (BIO-FND-008) and `mobile/` generate their bindings
   from these schemas. Recorded in `contracts/README.md` so it is not mistaken for an
   omission.

## Round 2 — BIO-ARC-001-QA-01 fixed (Seyi FAIL, 2026-09-20)

**Finding:** the clock contract validated digit ranges only, not calendar validity.
`2026-02-29`, `2026-02-30` and `2026-04-31` were accepted, and `parseInstant` handed them
to `Date.parse`, which silently rolled them to 1 March, 2 March and 1 May. `isUtcInstant`
returned `true`; `isCalendarDate` and both published schemas accepted the equivalent
dates.

**Confirmed before fixing.** Reproduced the exact normalization the review describes. The
finding is correct and the severity is right: a canonical time contract that turns a
supplied audit or device `occurred_at` into a different instant, with no error, is a
data-integrity hole — and at the edge it is also an integrity hole in evidence, because
the skew that would have exposed a wrong device clock is erased by the roll-over.

**Fix.**

| File | Change |
|---|---|
| `contracts/js/clock.mjs` | Added `isLeapYear` (proleptic Gregorian, exported so bindings share one definition) and a real-date check applied by `isUtcInstant`, `isCalendarDate` and `parseInstant`. An impossible day now raises `ClockError` with code `invalid_calendar_date` naming the month and year. `renderLocal` and `assessClockSkew` inherit the rejection because both parse first. |
| `contracts/schemas/utc-instant.schema.json` | Pattern replaced with a calendar-validated one: 31/30-day months, February 1–28, and 29 February only in a leap year including the 400-year rule. |
| `contracts/schemas/calendar-date.schema.json` | Same pattern, date part only. |
| `contracts/js/contracts.test.mjs` | 8 regression tests (`AC02/QA-01`, `AC04/QA-01`). |
| `tests/foundation/BIO-ARC-001.test.sh` | 2 checks: the ADR states the rule, and code and schema both reject the impossible dates. |
| `docs/adr/ADR-0001-…md` | §2.1 now states the calendar-validity rule and why a shape check cannot see it. |

The schema pattern was verified against the code exhaustively — every date string from
1896-01-01 to 2405-12-31 (189,720 values, covering 1900 and 2100 non-leap and 2000 and
2400 leap): **0 mismatches**. A test in the suite re-sweeps the boundary days of 1900,
2000, 2024 and 2026 so the two definitions cannot drift.

**Regression tests requested, and what they assert**

| Case | Assertion |
|---|---|
| `2026-02-29` (non-leap) | rejected by `isUtcInstant`, `isCalendarDate`, `parseInstant` and both schema patterns |
| `2026-02-30` | rejected, and separately asserted *not* to become 2 March |
| `2026-04-31` | rejected, and separately asserted *not* to become 1 May |
| `2028-02-29` (leap) | accepted and round-trips unchanged |

Added beyond the request, because the leap rule is where this class of bug hides:
`2000-02-29` accepted (divisible by 400), `1900-02-29` and `2100-02-29` rejected
(centuries), a per-year day count (366 vs 365) for 2024, 2026, 1900 and 2000, and
rejection of an impossible claimed time in `assessClockSkew` and `renderLocal`.

**Scope.** Only the validated defect and its regression tests. No change to identifiers,
concurrency, the registry or any other schema — AC01 and AC03 were PASS and their code is
untouched.

**Round 2 results**

```
tests/foundation/BIO-ARC-001.test.sh          22/22 checks passed   (exit 0)   was 20/20
node --test contracts/js/                     53 pass, 0 fail       (exit 0)   was 45
scripts/secret-scan.sh --all                  clean                 (exit 0)
evidence/BIO-ARC-001/codex/independent-review.sh   all 5 checks PASS (exit 0)  was exit 1
```

The reviewer's own reproduction script is re-run as evidence in
`reviewer-script-rerun.txt`: both probes that failed on `fc2f7cd` now pass, and it is run
unmodified.

**Note on amending the ADR.** `docs/adr/README.md` says an accepted ADR is superseded, not
edited. ADR-0001 is not yet accepted — the story is in its review loop — so correcting it
in place is the right move rather than opening ADR-0002 against a decision that has never
been through acceptance. Once BIO-ARC-001 is accepted, any further change needs a
superseding ADR.

## Round 3 — BIO-ARC-001-QA-02 fixed (Seyi re-review round 2, 2026-09-20)

**Finding:** my round-2 code applied the leap rule to every four-digit year and accepted
`0000-02-29`, while both schema patterns rejected it. Bindings generated from the schemas
would have been stricter than the reference implementation — a shared-contract parity
break, correctly filed against AC04.

**Confirmed, then bounded.** I reproduced the divergence, and then swept **all 10,000
four-digit years** — 7,440,000 code-vs-schema comparisons — to find out whether `0000-02-29`
was the only one. It was: exactly two divergences, both that date. So the finding was
complete, and the fix does not have to chase further unknowns.

**Decision: the supported year range is 1000–9999 inclusive.** Year `0000` is rejected,
including its proleptic leap day.

Reasoning, rather than preference: the range is the intersection of what every BIOLOG
surface can actually store and read back. MySQL's documented `DATE`/`DATETIME` range is
`1000-01-01`…`9999-12-31` and .NET's `DateTime` covers `0001`…`9999`, so `1000`…`9999` is
what survives a write to the tenant database. Accepting a year outside it would pass
validation and then fail at the database — deferring a failure instead of reporting it,
which is the opposite of the fail-closed boundary rule this contract already sets.

**Assumption on record:** no BIOLOG business timestamp legitimately precedes year 1000. A
tenant needing an earlier historical calendar date — which would be an `*_on` field, not
an instant — needs an amending ADR, not a local workaround. This is stated in ADR-0001
§2.1 so a later story cannot quietly widen it.

**Fix.**

| File | Change |
|---|---|
| `contracts/js/clock.mjs` | Exported `MIN_YEAR`/`MAX_YEAR`; the year bound is applied in `isRealCalendarDate`, so `isUtcInstant`, `isCalendarDate` and `parseInstant` share it. `toInstant` now validates what it produced, so formatting cannot emit a value parsing would refuse. Out-of-range years raise `instant_out_of_range`, distinct from `invalid_calendar_date`. |
| `contracts/schemas/utc-instant.schema.json`, `calendar-date.schema.json` | Year encoded as `[1-9]\d{3}` in the pattern. |
| `contracts/js/contracts.test.mjs` | 7 regression tests (`AC02/QA-02`, `AC04/QA-02`), including the exhaustive sweep. |
| `tests/foundation/BIO-ARC-001.test.sh` | 2 checks: the ADR states the range, and code and schema agree across every four-digit year. |
| `docs/adr/ADR-0001-…md` | §2.1 records the range, the reasoning, the assumption, and the rule that code and schema may differ in diagnostics but never in verdict. |

**One deliberate asymmetry, and why it is not a new parity risk.** The JavaScript shape
regexes still match any four-digit year; the range is applied in the following domain
check. That is what lets the implementation answer *why* a value is bad — `0000-06-15` is
a range problem, not a format problem, and a caller fixing a bad device clock should not
be told its calendar is malformed. A JSON Schema pattern cannot make that distinction, so
the schemas encode the bound directly. The layers differ in diagnostics and are identical
in verdict, which is what AC04 requires, and test 36 proves it across all 10,000 years
rather than asserting it.

**Regression tests added**

| Case | Assertion |
|---|---|
| `0000-02-29`, `0000-01-01`, `0000-12-31`, `0000-02-28`, `0000-03-01` | rejected by code and both schemas; `parseInstant` raises `instant_out_of_range` |
| `0004-02-29`, `0400-02-29`, `0001-01-01`, `0100-02-28`, `0999-02-28` | rejected — below the range whether or not they are leap years |
| `1000-01-01`, `9999-12-31` | accepted at both ends of the range and round-trip unchanged |
| `toInstant` of year 999, year 0 and year 275760 | raises `instant_out_of_range`; formatting and parsing cannot disagree |
| Every date in every year 0000–9999 | code and schema return the identical verdict (7,440,000 comparisons, 0 divergences) |
| Published patterns | contain no lookahead or lookbehind, so they translate to an RE2-backed validator in a generated binding |

**Round 3 results**

```
tests/foundation/BIO-ARC-001.test.sh               24/24 checks passed   (exit 0)   was 22/22
node --test contracts/js/                          60 pass, 0 fail       (exit 0)   was 53
scripts/secret-scan.sh --all                       clean                 (exit 0)
evidence/BIO-ARC-001/codex/independent-review.sh   all 6 checks PASS     (exit 0)   was exit 1
```

Seyi's updated script — which now carries the four-digit-year parity probe — is re-run
unmodified and filed as `reviewer-script-rerun.txt`. The full sweep output is
`year-parity-sweep.txt`; its reproducible form is test 36 in the contract suite, so the
check lives in the suite rather than in a one-off script.

**Scope.** Only QA-02 and its regression tests. QA-01's fix is untouched and still green;
AC01 and AC03 code was not modified.
