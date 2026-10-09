# BIO-ARC-004 — builder evidence

- **Story:** BIO-ARC-004 — Define API, error and idempotency conventions
- **Owner:** Kemi — Chief Architect & Integrator (claude-sonnet-5)
- **Wave / batch:** W01 / W01.4 · conflict group `shared-contract`
- **Reviewer:** Seyi · acceptor Chioma
- **Branch:** `feature/BIO-ARC-004`, off `develop` @ `b36f186`
- **Date:** 2026-09-21
- **Remote:** none. Nothing pushed. `governance/release-state.yaml` untouched.

## Read this first: this story is deliberately thin

Three primitives this story needed already existed, built for a different purpose, under
a different name. This ADR promotes them to their HTTP-layer role instead of re-deriving
them:

| Needed for | Already exists as | From |
|---|---|---|
| A standard error envelope | `(message, code, status, detail)` shape shared, without inheritance, by `TenantContextError`, `ConcurrencyError`, `AuditError` | ADR-0001/0002/0003 |
| Write-endpoint retry safety | `requestFingerprint` / `resolveIdempotency` / the `idempotency_key_reuse` rule | ADR-0001 §3.4 |
| Unambiguous field-path naming | `escapeSegment` (fixed a real collision, QA-04) | ADR-0003 |

`contracts/js/api.mjs` is therefore a thin HTTP-shaped wrapper over those three, not a
fourth independent implementation of any of them. The alternative — inventing a second
error shape, a second idempotency comparison, or a second dotted-path convention — is
explicitly rejected in the ADR's Alternatives section, because each would be a second place
for that logic to drift from the first.

## What was built

| Artifact | Purpose |
|---|---|
| `docs/adr/ADR-0004-…md` | The five decisions, the reuse points, and the correlation-id reconciliation the dispatch specifically asked for |
| `contracts/schemas/problem-response.schema.json` | The one HTTP error envelope (AC02) |
| `contracts/schemas/validation-error-item.schema.json` | One `errors[]` entry (AC02/AC04) |
| `contracts/schemas/paginated-list-response.schema.json` | Cursor-paginated list envelope (AC04) |
| `contracts/js/api.mjs` | Versioning constants, `toProblemResponse`/`toValidationProblem`, `resolveCorrelationId`, `withIdempotency` + storage-key scoping, pagination + filter-allowlist helpers |
| `contracts/js/api.test.mjs` | 45 tests, each named with the AC it proves |
| `tests/foundation/BIO-ARC-004.test.sh` | 25 artifact/consistency checks, including ARC-001/002/003 regression runs |
| `scripts/quality.sh` | ARC-004 harness line added by me this round, as asked |

## The continuity flag, addressed directly

The dispatch named one concrete place two mechanisms could clash: ARC-003's event
`correlation_id` (an `evt_` identifier tying an audit/domain event to its cause) versus
whatever request-correlation convention this story defines for the API layer.

**Resolution: there is one identifier, not two.** ADR-0004 §3 defines the API's
`X-Correlation-Id` as resolving to — and requiring — exactly the same canonical `evt_`
identifier ADR-0003's audit envelope already uses. A caller's well-formed value is used
as-is; a missing or malformed one gets a freshly minted `evt_` id via the same `newId`
ADR-0001 already defines. The resolved value is threaded straight into
`buildAuditEvent`'s `correlation_id` with zero translation — proven by a test that builds
a real audit event from a resolved correlation id and asserts it round-trips unchanged.
No adapter, no second ID space, no place for the two conventions to say something different
about "what correlates this."

ARC-003's *event-level* idempotency (content fingerprint over an event, excluding
`received_at`, dedup key `tenant_id:event_id`) and this story's *API-level* write
idempotency (an `Idempotency-Key` header, dedup key `tenant_id:operation:idempotency_key`,
replaying a stored HTTP response) are confirmed as different mechanisms for different
problems, as the dispatch expected — but they share the *comparison primitive*
(`requestFingerprint`/`resolveIdempotency`), so "same content, different content" means the
identical thing at both layers.

## AC → evidence map

| AC | Proven by |
|---|---|
| **AC01** versioned paths | ADR §1; `API_PATH_PREFIX` constant; tests 1–3 (`AC01: …`), incl. the three-version-concepts table asserted against the code; shell checks AC01 ×3 |
| **AC02** standard problem/error format | ADR §2; `problem-response.schema.json`; tests 4–12 (`AC02: …`) incl. all three existing error classes serialised unchanged, a duck-typed future class, frozen output, and the reserved-field-collision guard found during my own hostile pass (below); shell checks AC02 ×4 |
| **AC03** write-endpoint idempotency | ADR §4; tests 18–29 (`AC03: …`) incl. replay-without-re-execution, reuse-with-different-content surfaced not absorbed, tenant+operation scoping isolation, async store correctness; shell checks AC03 ×4 |
| **AC04** pagination/filtering/correlation/validation | ADR §3 and §5; three schemas; tests 13–17 and 30–41 (`AC04: …`) incl. the exact QA-04-shaped path collision proven absent, an unrecognised filter refused not dropped, and the round-trip into a real audit event; shell checks AC04 ×5 |

## The hostile pass, run before calling this done

The standing two-clause question — *what would a caller have to control for this rule to
be false, and is there a deeper or later place they still control it* — applied to every
surface in this story before writing regression tests, not after a review found the gap:

- **Correlation id.** A malformed or foreign `X-Correlation-Id` (including one
  SQL-injection-shaped) is never trusted; a fresh canonical id is minted every time. Tested
  against seven adversarial values.
- **Idempotency scope.** Verified the storage key is scoped to tenant *and* operation, so
  the same key value cannot collide across tenants or across two different endpoints —
  three concurrent scopes proven to execute independently rather than share state.
- **Filter allowlist.** A prototype-polluting key (`__proto__`) submitted as a filter is
  refused as an ordinary unknown field, and `Object.prototype` is verified untouched
  afterward.
- **Page limit.** Non-integer, negative, `NaN`, `Infinity` and non-numeric-object inputs
  are all rejected outright rather than coerced into something silently wrong.
- **A real defect found by this pass, not by review.** `detail` on an error can, in
  principle, contain a key with the same name as a reserved top-level field —
  `correlation_id` was the one I checked, since it is this story's own new field — and a
  hand-crafted `detail.correlation_id` sat in the same document as the real one under an
  identical name. Nothing external can trigger this (it requires an error class's own
  allowlist to have gone wrong), but the resulting document would be genuinely ambiguous to
  a consumer. Fixed by refusing to serialise a `detail` that collides with any reserved
  field name (`reserved_detail_field`), recorded in ADR-0004 §2.3, with a dedicated
  regression test covering all five reserved names.
- **Validation path ambiguity, closed by construction rather than by testing for it.**
  `toValidationProblem` requires `field` as an array of raw segments and refuses a bare
  dot-joined string outright — the exact shape that caused ADR-0003's QA-04. There is no
  interpretation for a caller to guess wrong, because the ambiguous input is never accepted
  in the first place.

## Test results

```
tests/foundation/BIO-ARC-004.test.sh   25/25 checks passed   (exit 0)
node --test contracts/js/              230 pass, 0 fail      (exit 0)   45 new, 185 inherited
tests/foundation/BIO-ARC-001.test.sh   24/24 checks passed   (exit 0)   no regression
tests/foundation/BIO-ARC-002.test.sh   30/30 checks passed   (exit 0)   no regression
tests/foundation/BIO-ARC-003.test.sh   34/34 checks passed   (exit 0)   no regression
scripts/quality.sh                     QUALITY: PASS         (exit 0)   ARC-004 line added by me this round
scripts/secret-scan.sh --all           clean                 (exit 0)
```

## Constraints observed

- Synthetic data only: every tenant, key, payload and identifier is fabricated.
- No GitHub remote added, nothing pushed.
- No other story's evidence touched; the biolog engine and runtime-state untouched.
- `TenantContextError`, `ConcurrencyError` and `AuditError` are unmodified — `api.mjs`
  serialises them by duck-typing their existing shape, exactly as ADR-0004 §2.2 states.

## Notes for the reviewer

1. `BiologApiError` is offered as a convenience base for *new* error classes; it is not
   retrofitted onto the three existing ones. That is a deliberate scope boundary, recorded
   in the ADR, not an oversight — migrating them is flagged as a worthwhile later cleanup.
2. `0x10`-style hex-formatted `limit` query values parse via `Number()` to a valid positive
   integer and are accepted. This is permissive input formatting, not a security or
   integrity gap — the output is still clamped to a validated, bounded integer — and I did
   not add stricter numeric-format validation for it. Flagging so it reads as a considered
   choice rather than something missed.
3. Idempotency retention is a stated floor (24 hours minimum), not a fixed duration —
   deliberately, since a fixed number here would be either too short for some retry
   scenarios or an arbitrary constraint on infrastructure this ADR does not own.

## Round 2 — QA-01, QA-02 and QA-03 fixed (Seyi review, 2026-09-21)

All three findings are real. I reproduced each against `66fae3e`/`42c3951` before changing
anything.

### QA-01 (High) — get-then-execute-then-set is not atomic

**Reproduced:** two concurrent `withIdempotency` calls against a store with an artificial
delay in `get` both observed no stored record and both executed — `calls === 2` where it
must be `1`.

**Fix — the atomic operation moves to before execution, not after.** The store interface
grows from `{get, set}` to `{claim, complete, release, get}`. `claim(key, fingerprint)`
must be atomic — creating an `IN_FLIGHT` marker only if none exists, the same guarantee a
real store gets from a unique-constraint insert or `SET … NX`. The winner executes and
calls `complete`; a caller whose `execute` throws calls `release`, so a genuine later retry
is not blocked forever by a claim nobody will finish. A caller who loses the claim never
executes: if the existing record is `DONE`, its result replays via `resolveIdempotency`
(ADR-0001, reused unchanged); if it is still `IN_FLIGHT` with the same fingerprint, the
caller waits, bounded, rather than executing a second time — a wait that exceeds the bound
surfaces as a distinct `idempotency_wait_timeout` (503), never a silent re-execution and
never an infinite hang. `createInMemoryIdempotencyStore` is the reference implementation:
its `claim` has no `await` inside its body, so Node's single-threaded event loop cannot
interleave two concurrent callers between the check and the write.

Verified beyond the minimum: 20 concurrent identical callers against the reference store
execute exactly once; a crash (`execute` throwing) releases the claim so a later,
*sequential* retry can execute exactly once more; concurrent *different* content under the
same key still conflicts (409) even when raced; a permanently wedged winner produces a
bounded, distinct timeout for a waiting loser rather than hanging forever.

### QA-02 (High) — the storage key is not injective

**Reproduced:** `(operation="card", key="activate:key")` and
`(operation="card:activate", key="key")` produced the identical colon-joined string.

**Fix:** the scope key is `JSON.stringify([tenant_id, operation, idempotency_key])`. JSON's
grammar delimits array elements with commas outside quoted strings, so a `:` — or any other
character — inside either segment can never be mistaken for a boundary. This is the same
class of bug ADR-0003's QA-04 found for `.`-joined redaction paths, closed here with a
well-tested built-in rather than a fifth hand-rolled escaping scheme. Verified against
embedded-quote and embedded-backslash adversarial keys, not just the reported case.

### QA-03 (High) — the shared builders could emit documents their own schemas reject

**Reproduced all four described gaps exactly:** a malformed `code` and a `NaN`/out-of-range
`status` were accepted by `toProblemResponse`; `errors[]` rode along on a non-validation
code; `toValidationProblem` accepted a non-string item `message`; `buildPageEnvelope`
accepted a non-string cursor, a missing `limit`, and a negative `total_count`.

**Fix:** each builder now validates its output against the exact constraints its own
published schema states, before returning — not just that the right properties are
present, but that their content satisfies the schema. Duck-typing (which *properties* an
object must expose) was never meant to excuse validating what is *in* them; QA-03 is the
gap between those two claims made concrete.

### One design choice that makes two of Seyi's own probes read as failing, disclosed

Fixing QA-01 changed the store interface, and fixing part of QA-03 changed
`toProblemResponse` to **throw** rather than silently strip a misplaced `errors[]`. Seyi's
own `independent-review.sh` — their artifact, left untouched as in every prior round —
encodes the pre-fix interface and the pre-fix strip-don't-throw assumption in two of its
eight probes, so those two now show `FAIL` when run unmodified, for reasons that are the
fix rather than evidence against it:

- `AC03 concurrent identical retries execute exactly once` mocks the old `{get, set}`
  shape; calling the new `claim`-based `withIdempotency` against it throws
  `TypeError: store.claim is not a function` before any race can even be exercised.
- `AC02 non-validation problems cannot carry validation errors` expects `toProblemResponse`
  to run to completion and simply omit `errors` from its output; it now throws instead,
  consistent with every other QA-03 fix in this round (a builder that could produce an
  invalid document refuses at construction, not after quietly repairing the input).

Both are adapted — not edited — in `evidence/BIO-ARC-004/builder/reviewer-probes-adapted.mjs`,
which proves the property each probe actually cares about still holds: execute() runs
exactly once for concurrent identical retries (using the same gated-release race Seyi
constructed, against the new store shape), and no document is ever *returned* carrying
`errors[]` on a non-validation code (a throw satisfies that invariant at least as strongly
as a silent strip would have). Both pass. `evidence/BIO-ARC-004/codex/independent-review.sh`
is unmodified; its own two affected lines are expected to read `FAIL` for the reasons above.

### Round 2 results

```
tests/foundation/BIO-ARC-004.test.sh                26/26 checks passed  (exit 0)   was 25/25
node --test contracts/js/                           252 pass, 0 fail     (exit 0)   was 230
tests/foundation/BIO-ARC-001/002/003.test.sh         24/24, 30/30, 34/34 (exit 0)   no regression
scripts/quality.sh                                   QUALITY: PASS       (exit 0)
scripts/secret-scan.sh --all                         clean               (exit 0)
evidence/BIO-ARC-004/builder/reviewer-probes-adapted.mjs   2/2 PASS       (exit 0)
```

### Scope

QA-01, QA-02 and QA-03 only, plus the two test-file updates needed to keep my own
foundation harness honest against the new store interface (it used the old `{get, set}`
shape in two checks; both now use `createInMemoryIdempotencyStore`). No change to AC01's
versioning, AC03's correlation-id reconciliation, or AC04's pagination/filtering rules
beyond the schema-content validation QA-03 required.

## Round 3 — QA-03 remainder and QA-04 fixed (Seyi re-review round 2, 2026-09-21)

QA-01 and QA-02 confirmed closed. Two gaps remained in QA-03, and one new High finding
(QA-04). All reproduced against `14a5346`/`2cc2ae0` before any fix.

### QA-03 remainder — problem builders still emitted schema-invalid documents

The first pass checked *some* of each schema's constraints, not all of them:

- `toProblemResponse` never validated a truthy `message`'s type — `{code:'bad', status:400,
  message:42}` was returned as-is.
- When `code` was already `validation_failed`, `error.errors[]` items were copied through
  with a blind spread (`{...item}`): a primitive entry like `42` silently became `{}`, and a
  malformed `path`/`code`/`message` inside an item passed unchanged.
- `toValidationProblem` checked every item's `message` but never its own top-level
  `message` option.

**Fix.** A shared `assertValidationErrorItem` validates the exact
`validation-error-item.schema.json` constraints — non-empty `path`, snake_case `code`,
non-empty string `message` — and reconstructs a clean object rather than spreading
whatever the input carried. It is used both by `toValidationProblem` (building items from
raw segments) and by `toProblemResponse` (checking items handed through directly on
`error.errors`), so the same rule governs both origins rather than two definitions that
could drift apart. `toProblemResponse`'s own top-level `message` and
`toValidationProblem`'s own top-level `message` option are both now validated the same way.

### QA-04 (High) — a raw, unvalidated tenantId could alias the control-plane scope

**Reproduced exactly:** `JSON.stringify(NaN)` is the string `"null"` — the identical tuple
component the control-plane scope (`tenantId: null`) already uses deliberately.
`idempotencyStorageKey({tenantId: NaN, ...})` and `idempotencyStorageKey({tenantId: null,
...})` produced the identical storage key.

**Fix.** `tenantId` must now be explicitly `null` (a deliberate control-plane operation) or
a canonical `ten_` identifier — nothing else, and no coercion. `undefined` is refused
exactly like `NaN` or an object: omitting the tenant by accident and declaring
control-plane work on purpose are different facts, and the original `tenantId ?? null`
treated them as the same one. This is the same posture ADR-0002 already takes toward every
other tenant claim — no default, no fallback, fail closed on anything not provably what it
claims to be.

### One design note carried over from round 2, now resolved

Round 2's evidence disclosed that fixing QA-01 and part of QA-03 made two of Seyi's own
`independent-review.sh` probes read `FAIL` for reasons that were the fix, not a regression,
and I filed an adapted proof (`reviewer-probes-adapted.mjs`) rather than edit their script.
Seyi's script for this round includes updated probes for both — `AC02 non-validation
problems cannot carry validation errors` and `AC03 concurrent identical retries execute
exactly once` both now pass unmodified, alongside the two new probes for this round's
findings. All 11 of their probes pass, exit 0.

### Round 3 results

```
tests/foundation/BIO-ARC-004.test.sh                28/28 checks passed  (exit 0)   was 26/26
node --test contracts/js/                           262 pass, 0 fail     (exit 0)   was 252
tests/foundation/BIO-ARC-001/002/003.test.sh         24/24, 30/30, 34/34 (exit 0)   no regression
scripts/quality.sh                                   QUALITY: PASS       (exit 0)
scripts/secret-scan.sh --all                         clean               (exit 0)
evidence/BIO-ARC-004/codex/independent-review.sh    all 11 checks PASS   (exit 0)   was 2 failing
```

### Scope

QA-03's remaining two gaps and QA-04 only. No change to QA-01/QA-02's fixes, AC01
versioning, or AC04's correlation-id/pagination rules beyond the item-validation sharing
described above. Two existing round-2 tests that used non-canonical placeholder tenant
strings (`'T'`, a concatenation of two real tenant ids) were updated to use canonical
tenant identifiers, since QA-04 now correctly refuses what they were testing with — the
embedded-quote/backslash adversarial content they were actually exercising moved to
`operation`/`idempotencyKey`, which QA-04 does not touch.

## Acceptance fix — BIO-ARC-004-ACC-01 (Chioma, 2026-09-21)

Chioma FAILed acceptance on documentation only: AC01-04, QA-01..04, foundation 28/28,
contract 262/262 and the independent 11/11 all held, but the builder SUMMARY's claim that
ADR-0004 states the relationship between this ADR's write idempotency and ADR-0003's
domain-event content-fingerprint idempotency was false — the ADR itself never said so,
only the summary did. Verified the gap directly before writing anything: `grep`-ing the ADR
for `eventFingerprint`, `received_at`, or any ADR-0003 idempotency reference returned
nothing.

**Fix: ADR-0004 Decision 4.6**, a new section naming both mechanisms explicitly — dedupe
key, what replays, what the fingerprint excludes, and the reuse-conflict code for each —
and stating they share no implementation because they solve different problems (a caller's
retry versus a producer's redelivery).

**The interaction question in the dispatch is answered, not gestured at.** On an ordinary
caller retry, the domain event cannot be re-emitted — not as an added rule, but because a
`REPLAY` outcome never calls `execute` again, so nothing `execute` would have emitted runs
at all. The real, narrower seam: if `execute` emits its domain event and *then* throws, the
claim is released specifically so a later attempt can run `execute` again in full — and
that retried attempt mints a fresh event via `newId`, which ADR-0003's own `(tenant_id,
event_id)` dedupe cannot catch as a duplicate, because it only catches the *same* event
delivered twice, not the same fact re-asserted under a new id. This ADR's write-idempotency
has no visibility into what `execute` did before it failed, and the resulting obligation —
making that emission idempotent against a retry, e.g. by deriving the event's own
`idempotency_key` from the same `Idempotency-Key` — is placed explicitly on the endpoint
author, not silently assumed away.

Two `Alternatives rejected` entries and one `Obligations on later stories` bullet added to
match, and 5 new regression tests assert the cross-reference text is genuinely present —
not just that some keyword appears, but that the mechanism table, the received_at exclusion,
the "replay never re-emits" answer, the partial-failure seam, and the endpoint-author
obligation are each stated in the ADR's own words.

### Results

```
tests/foundation/BIO-ARC-004.test.sh                29/29 checks passed  (exit 0)   was 28/28
node --test contracts/js/                           267 pass, 0 fail     (exit 0)   was 262
scripts/quality.sh                                   QUALITY: PASS       (exit 0)
scripts/secret-scan.sh --all                         clean               (exit 0)
```

No implementation file changed — `docs/adr/ADR-0004-…md`, `contracts/js/api.test.mjs` and
`tests/foundation/BIO-ARC-004.test.sh` only, exactly matching Chioma's manifest ("fix is
evidence/documentation-only"). Per Chinedu's note, this does not carry the QA-01..04
numbering forward since it is not an independent-review finding; filed against the
acceptance blocker id `BIO-ARC-004-ACC-01` instead.
