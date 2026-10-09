# BIO-ARC-003 — builder evidence

- **Story:** BIO-ARC-003 — Define audit, evidence and domain-event envelopes
- **Owner:** Kemi — Chief Architect & Integrator (claude-opus-5)
- **Wave / batch:** W01 / W01.3 · conflict group `shared-contract`
- **Reviewer:** Seyi · acceptor Chioma
- **Branch:** `feature/BIO-ARC-003`, off `develop` @ `ebbed9b`
- **Date:** 2026-09-21
- **Remote:** none. Nothing pushed. `governance/release-state.yaml` untouched.

## Read this first: two judgment calls and one scope exception

### 1. AC02 says "version" and ADR-0001 forbids `version` on an event

AC02 lists both "version" and "schema version". ADR-0001 §3.6 forbids a `version` field on
an event, because an event is a fact and a version implies it can be updated — the schema
actively refuses one.

The reading that satisfies both: the criterion asks for the version **of the aggregate the
event is about**, not a version of the event. That is a fact about the state being
described, so it does not make the event mutable. It is recorded as `aggregate_version`,
and the prohibition on a bare `version` stands unchanged and still tested. Written up in
ADR-0003 Decision 2 rather than quietly implemented, because the next reader meets the
same apparent contradiction.

### 2. "The audit envelope MUST line up with the ARC-002 denial event"

Taken in the strong sense: there is now **one** audit shape, and ARC-002's denial event is
a conforming instance of it with one extra field (`attempted_tenant_id`). Field names moved
to the canonical ones — `context_tenant_id` → `tenant_id`, `principal` → `actor {type,id}`,
`resource_type/​resource_id` → `target {type,id}` — and the event gained `action`, `outcome`
and `occurred_at`, which it did not have.

The weaker reading — design the new envelope around ARC-002's existing field names — was
rejected because ARC-002's event genuinely lacks `action`, `outcome`, `occurred_at` and
`device`, so a general audit envelope shaped around it could not describe an ordinary
allowed action. Every ARC-002 guarantee is intact: written before the response, both
tenants recorded, no payload, fails closed on a missing or failing sink.

Recorded as an amendment in ADR-0003 Decision 6.2, made while W01 is still open.

### 3. Scope exception — one line in `tests/foundation/BIO-ARC-002.test.sh`

The stated write scope is `contracts/**`, `docs/adr/**`, `tests/foundation/BIO-ARC-003.*`,
`evidence/BIO-ARC-003/**`. The rename above breaks exactly one assertion outside it:

```
-      if (seen[0].context_tenant_id !== a || seen[0].attempted_tenant_id !== b) process.exit(1);
+      if (seen[0].tenant_id !== a || seen[0].attempted_tenant_id !== b) process.exit(1);
```

I made that one-line change rather than hand the reviewer a red ARC-002 harness. It
asserts the same thing about the same event; only the field name moved. It is not on the
prohibited list, but it is outside the enumerated write scope, so it is flagged here and
reported to Chinedu. **If the weaker reading of the alignment requirement was intended, say
so and I will revert both the rename and this line.**

## What was built

| Artifact | Purpose |
|---|---|
| `docs/adr/ADR-0003-…md` | The decisions: audit envelope, event envelope, classification and redaction, idempotency, evidence by reference, two recorded amendments |
| `contracts/schemas/audit-event.schema.json` | One audit envelope (AC01) |
| `contracts/schemas/data-classification.json` | Five levels and the four redaction rules (AC03) |
| `contracts/schemas/event-envelope.schema.json` | Extended with `aggregate_version` (required) and `idempotency_key` (optional) (AC02) |
| `contracts/schemas/tenant-access-denial-event.schema.json` | Re-profiled: `allOf` the audit envelope, `outcome: DENIED`, plus `attempted_tenant_id` |
| `contracts/js/audit.mjs` | `buildAuditEvent`, `redact`, `correlatableReference`, `dedupeKey`, `eventFingerprint`, `classifyDelivery`, `recordDelivery` |
| `contracts/js/tenant-context.mjs` | `buildDenialEvent` now delegates to `buildAuditEvent` |
| `contracts/js/audit.test.mjs` | 35 tests, each named with the AC it proves |
| `tests/foundation/BIO-ARC-003.test.sh` | 24 artifact/consistency checks, including ARC-001 and ARC-002 regression runs |
| `scripts/quality.sh` | ARC-003 harness line added, as the dispatch asked |

## AC → evidence map

| AC | Proven by |
|---|---|
| **AC01** audit envelope: tenant, actor, device, action, target, reason, correlation, time | ADR §1; `audit-event.schema.json`; tests 1–9 (`AC01: …`), incl. structured-actor rejection of a bare string, `outcome`/`reason_code` required on success, namespaced-action validation, null tenant for control-plane, append-only with no payload, ADR-0001 instant rules, and the ARC-002 denial event asserted to satisfy every required audit field; shell checks AC01 ×7 |
| **AC02** domain event envelope | ADR §2; extended `event-envelope.schema.json`; tests 10–13 (`AC02: …`), incl. the bare-`version` prohibition still asserted and ordering by `aggregate_version` with identical timestamps; shell checks AC02 ×3 |
| **AC03** classification + redaction | ADR §3; `data-classification.json`; tests 14–23 (`AC03: …`) — see the hostile pass below; shell checks AC03 ×5 |
| **AC04** idempotency | ADR §4; tests 24–30 (`AC04: …`), incl. tenant-scoped dedupe key, `received_at` excluded from the fingerprint, and the conflict rule; shell checks AC04 ×2 |

## The hostile pass, applied as the dispatch asked

The standing question — *what would a caller have to control for this rule to be false, and
do they control it?* — on the two criteria it bites hardest.

**AC03, redaction.** Under a denylist the answer is "the field names", which a caller
controls completely: a field added by a later story, a nested object, or a near-miss
spelling all walk through untouched, and it fails open on exactly the change nobody
reviews. So redaction is an allowlist projection, and three tests are written from the
attacker's side: a field added after the allowlist was written is withheld
(`AC03/hostile: a field added later…`); an allowlisted-but-unclassified field is withheld,
because unclassified is not "safe"; and an allowlist **cannot promote a secret**, even when
the caller names it explicitly. A fourth rule falls out of the same question: a hash of a
date of birth or a national identifier is reversible by enumeration, so
`correlatableReference` refuses one and directs the caller to the canonical identifier
instead.

**AC04, idempotency.** The caller controls `event_id` — it is producer-minted, and at the
edge the producer is a device. Under a key-only dedupe, a producer that reuses an id with
different content has its second event silently dropped, which is a way to make a
revocation vanish behind a replayed identifier. So a redelivery is classified by content
fingerprint and a mismatch is `CONFLICT`/`event_id_reuse`, surfaced and never absorbed; a
test asserts the first event is not overwritten. The matching subtlety, also tested:
`received_at` is excluded from the fingerprint, because the server stamps it per delivery
and including it would make every redelivery look like new content and defeat the dedupe
entirely.

## Test results

```
tests/foundation/BIO-ARC-003.test.sh   24/24 checks passed   (exit 0)
node --test contracts/js/              156 pass, 0 fail      (exit 0)   35 new, 121 inherited
tests/foundation/BIO-ARC-001.test.sh   24/24 checks passed   (exit 0)   no regression
tests/foundation/BIO-ARC-002.test.sh   30/30 checks passed   (exit 0)   no regression
scripts/quality.sh                     QUALITY: PASS         (exit 0)   now runs all three harnesses
scripts/secret-scan.sh --all           clean                 (exit 0)
```

Four inherited tests were updated where the two contract changes made them assert an old
shape: ARC-001's constructed sample event now carries `aggregate_version`, and three ARC-002
assertions use the canonical field names. No assertion was weakened — each still checks the
same guarantee against the new names, and the ARC-002 denial test gained two checks
(`allOf` the audit envelope, `outcome: DENIED`).

## Constraints observed

- Synthetic data only: every tenant, actor, hostname, identifier and value is fabricated.
- No GitHub remote added, nothing pushed.
- No other story's evidence touched; the biolog engine and runtime-state untouched.
- Carried forward from ARC-002: the audit-before-effect ordering and the async denial
  surface are unchanged, and ADR-0003 §1 states that the ordering applies to every audited
  effect, not only denials.

## One thing the repository's own tooling caught

`scripts/secret-scan.sh --all` failed on my first tracked run, on a line in
`audit.test.mjs` that maps three field names to the `secret` classification. One of those
field names is the word the scanner watches for, followed by a colon and a quoted value —
a classification map, not a credential, but the same shape, and the scanner's pattern is
right to be blunt about it.

I reshaped the fixture (quoted keys, so the name is no longer immediately followed by a
colon) rather than touch the scanner or add an exclusion. Weakening another story's
security control so a fixture of mine passes is the wrong trade, and the next real match
would be the one that mattered.

Then it failed a second time — on this file, because my first write-up quoted the offending
line verbatim. Evidence about a scanner finding has to describe the shape rather than
reproduce it, or it becomes the finding. Rewritten accordingly.

Worth noting why it surfaced only after committing: `secret-scan.sh --all` scans *tracked*
content, so the file was invisible to it until the first `git add`. Anything relying on
that scan should run after staging, not before.

## Round 2 — QA-01, QA-02 and QA-03 fixed (Seyi review, 2026-09-21)

All three findings are correct. I reproduced each against `8622cd8` before changing
anything, and QA-02 is slightly worse than the write-up says — see below.

### QA-01 (High) — the allowlist was shallow, and an unregistered level leaked

**Reproduced:** `redact({card:{sequence, template}}, {allow:['card'], classifications:{card:'internal'}})`
returned the whole nested object, secret included, with `withheld: []`. Separately, a
classification of `'made_up'` copied the value **and** emitted `made_up` as the envelope's
`classification`, which the published schema rejects.

This is my own stated hostile question failing one level down. I asked "what would a caller
have to control for this rule to be false" and answered "the field names" — then checked
only the top level. The caller nests the secret.

**Fix.** The allowlist is a list of **paths** and the projection recurses. A container is
descended into only when its own path is allowlisted and confers no permission on its
children; every leaf is decided on its own path. A leaf is emitted only if its
classification is one of the registered levels an envelope may carry, so missing,
unregistered and `secret` all fail closed — which also makes an out-of-schema
`classification` unreachable. Four more cases came out of the same pass: an array holding
anything but primitives is withheld outright, an array of primitives is **copied** rather
than referenced, cycles and depth beyond `MAX_REDACTION_DEPTH` are withheld, and a key that
can reach `Object.prototype` is never projected whatever the allowlist says.

### QA-02 (High) — a built audit event was mutable through nested references

**Reproduced, and worse than filed.** Mutating the source after construction did not only
change a value: it **injected a new field** into an already-built, append-only event —
`detail.card.injected = 'LEAK'` — which the event's `classification` had never seen and no
allowlist had approved. With the async sink from ARC-002 QA-03, that lands between
construction and persistence.

**Fix.** The envelope is deep-frozen at construction, and the projection builds new objects
rather than borrowing the caller's, so nothing inside a built event is reachable for
mutation. Denial events are deep-frozen too.

### QA-03 (High) — two AC-required slots were optional in the schemas

`device` (AC01) and domain-event `causation_id` (AC02) were defined but not in `required`.
Both are now required and explicitly nullable, which is the point: "no device" and "nobody
recorded the device" have to stay different answers, and so do "no cause" and "cause not
stated". A field that may be omitted cannot tell them apart.

I did **not** also make the audit envelope's `causation_id` required. AC02 is about the
domain event, and the finding named that one; flagging the asymmetry here rather than
widening the fix unasked.

### Regression tests added — 15, all from the attacker's side

| Case | Assertion |
|---|---|
| Nested secret under an allowlisted container | Withheld; the value appears nowhere in the record |
| Field added later beneath an allowed container | Projection is byte-identical to before; new field withheld |
| Secret nested four levels deep | Withheld at depth; `LEAK` absent |
| Unregistered/missing/odd-cased classification (7 values) | Value withheld **and** envelope level always schema-valid |
| Built event with an unregistered classification | `classification` is always in the schema enum |
| Array of objects, cycle, depth limit, `__proto__` | Each withheld; `Object.prototype` untouched |
| Array of primitives, then pushed to via the source | Copy unaffected |
| Source mutated after construction (value, new field, replaced object) | Event byte-identical; no field classification never saw |
| Every level of a built event | Frozen; four mutation attempts throw |
| Denial event | Deeply immutable too |
| Audit schema / built event | `device` required and present as `null` |
| Event schema | `causation_id` required, nullable for a root event |
| Documents missing either field | Exactly that field is the one missing from `required` |

**One of these initially passed for the wrong reason.** My depth-limit test allowlisted only
the outermost key, so the chain was withheld by the allowlist and never reached the bound.
It now allowlists every path down the chain, asserts the leaf is still withheld, and adds a
one-level-shallower chain that *does* project — so the bound is demonstrably the cause. Same
class of mistake as the `continue` I removed in BIO-ARC-002; worth stating that I keep
having to check my own tests are not self-serving.

### Round 2 results

```
tests/foundation/BIO-ARC-003.test.sh                30/30 checks passed  (exit 0)   was 24/24
node --test contracts/js/                           171 pass, 0 fail     (exit 0)   was 156
tests/foundation/BIO-ARC-001.test.sh                24/24 checks passed  (exit 0)   no regression
tests/foundation/BIO-ARC-002.test.sh                30/30 checks passed  (exit 0)   no regression
scripts/quality.sh                                  QUALITY: PASS        (exit 0)
scripts/secret-scan.sh --all                        clean                (exit 0)
evidence/BIO-ARC-003/codex/independent-review.sh    all 9 checks PASS    (exit 0)   was exit 1
```

Seyi's script is re-run unmodified and filed as `reviewer-script-rerun.txt`; all three
previously-failing probes now pass.

### What I take from this

Every finding against this wave has been the same shape: a rule stated correctly in the
ADR, enforced one step short of where a caller can reach. ARC-002 QA-01 checked a
context's fields instead of its provenance; QA-02 made the audit sink optional; QA-03
called the sink without awaiting it. Here the allowlist was checked at one level while the
caller controls all of them, and the freeze stopped at the first.

The hostile question needs a second clause, which I have added to my notes: *what would a
caller have to control for this rule to be false — and is there a **deeper or later** place
they still control it?* Nesting is deeper. Post-construction mutation is later.

### Scope

QA-01, QA-02 and QA-03 only. Same write scope as before; the one approved ARC-002 test line
is untouched. One ARC-001 test line updated — the sample event now carries the newly
required `causation_id`, which is the contract change, not a weakened assertion.

## Round 3 — causation_id aligned across both envelopes (god request, 2026-09-21)

god confirmed the QA-01..03 fixes and the "strong reading" and scope exception from round 2
(no revert), and asked for one more change: the audit envelope's own `causation_id` was
still optional while the domain-event's had just been made required + explicitly nullable
in round 2. That is the fourth instance of the exact pattern this story keeps closing — a
rule enforced correctly in one place and left one field over, unenforced in the twin place.

**Fix.** `causation_id` is now required and explicitly nullable on the audit envelope too,
matching the domain-event envelope exactly. `buildAuditEvent` already emitted it (defaulting
to `null`), so the schema change alone would have closed the gap — but leaving its value
unvalidated while `correlation_id` right next to it is shape-checked would have been a
smaller version of the same asymmetry, so `causation_id` now gets the identical check:
`null`, or a canonical `evt_` identifier, nothing else. Disclosed as a small, analogous
addition rather than the literal minimum, because leaving it out would recreate the pattern
this round exists to close.

ADR-0003 Decision 1 now states the rule explicitly: both envelopes hold `causation_id` to
the identical requirement, for the identical reason.

### Regression tests added — 6

| Case | Assertion |
|---|---|
| Built audit event | `causation_id` present, `null` by default |
| Document missing `causation_id` | Fails the audit schema's `required` list, same as `device` |
| Valid canonical id / garbage value | Accepted / rejected with `invalid_causation_id`, same shape as `correlation_id` |
| Both schemas' `causation_id` rules | Asserted identical: both required, both explicitly nullable |
| ADR text | States the alignment is deliberate |

### Round 3 results

```
tests/foundation/BIO-ARC-003.test.sh                32/32 checks passed  (exit 0)   was 30/30
node --test contracts/js/                           176 pass, 0 fail     (exit 0)   was 171
tests/foundation/BIO-ARC-001.test.sh                24/24 checks passed  (exit 0)   no regression
tests/foundation/BIO-ARC-002.test.sh                30/30 checks passed  (exit 0)   no regression
scripts/quality.sh                                  QUALITY: PASS        (exit 0)
scripts/secret-scan.sh --all                        clean                (exit 0)
evidence/BIO-ARC-003/codex/independent-review.sh    all 9 checks PASS    (exit 0)
```

### Routing note

god flagged that round 2's `builder-complete` did not actually reach Seyi through the usual
channel (no floor activity recorded against him). Per instruction, this round's review
request is sent directly to Seyi's hive inbox by me, in addition to running
`python automation/biolog.py builder-complete BIO-ARC-003`, and a copy goes to god so it is
visible on the board. If the engine's own routing is the thing that is broken, that is
worth Chinedu's attention independent of this story.

### Scope

Only `causation_id` on the audit envelope, its validation, and its tests. Nothing else from
round 2 was touched.

## Round 4 — QA-04 fixed (Seyi re-review round 3, 2026-09-21)

**Finding, High:** the recursive projector joined raw keys with `.` with no escaping. A
flat top-level field literally named `"profile.name"` therefore produced the identical path
string as the nested traversal `profile` -> `name`. An allowlist entry and classification
written with one location in mind silently governed the other, and since the source is
caller-controlled, the caller decides which shape is actually supplied.

**Reproduced first**, exactly as filed: `redact({profile:{name:secret}, "profile.name":"..."}, {allow:['profile','profile.name'], classifications:{'profile.name':'personal'}})`
emitted the nested value under a classification the map's author may have written thinking
only of the flat field.

**Two closures, not one — the first alone was not enough.** My first attempt escaped every
raw key (`\` then `.`) before joining, so `profile.name` (unescaped) and `profile\.name`
(escaped) became genuinely distinct, non-colliding strings. That closes half the problem —
no string can accidentally govern two different locations — but it does not, by itself,
stop a caller who supplies *both* shapes at once from picking whichever one the allowlist
author happened to write. I verified this by running Seyi's own probe against the
escaping-only fix: it still failed, because the probe's `"profile.name"` entry, being
unescaped, now meant *only* nested under my scheme, and nested was still reachable while a
different, adversarial flat key sat right next to it.

**Second closure:** a raw dotted key that also resolves to a real nested value in the same
source is withheld outright, on **both** sides, regardless of the allowlist. A pre-pass
walks the whole source once, and for every raw key containing a literal `.`, splits it on
those dots and attempts to resolve that split as an actual property path from the same
object (`hasOwnProperty` at every step, so it can never walk through an inherited or
prototype value). When it resolves, both the flat key's escaped path and the nested value's
escaped path are added to a forced-withhold set, checked before the allowlist on every
subsequent lookup. This only fires when a genuine collision exists in the caller-supplied
data — a flat dotted key with no competing nested structure still needs its escaped form
named explicitly to be emitted, exactly as under the first closure.

**Stress-tested from the hostile side before writing tests**, beyond what was asked:
collision detection at a non-root depth, a three-level chain (`a.b.c`), a partial match
that must *not* false-positive (profile exists but has no colliding property), and a
crafted `__proto__.x` key to confirm the collision walk cannot be used to reach the
prototype chain. All behaved correctly on the first implementation.

**A mistake I found and fixed in my own test file before running it clean:** several of the
new regression tests were generated through a Python heredoc, and Python's own string
escaping silently collapsed my intended `\\` (JS source for one literal backslash) down to
a single `\`. JavaScript then drops an unrecognized `\.` escape in a string literal,
so several assertions were comparing against a 12-character string with no backslash at
all, instead of the intended 13-character string containing one. Three tests initially
failed against my own correct implementation, for this reason — verified by printing char
codes rather than guessing, then rewritten with the Edit tool directly (or via
`escapeSegment(...)` calls instead of hand-typed backslash literals, so the assertion
cannot fall prey to the same mistake it exists to catch).

### Regression tests added — 9

| Case | Assertion |
|---|---|
| Seyi's exact reported collision | Neither flat nor nested value is emitted; both paths withheld |
| `escapeSegment` on flat vs. nested, and against a crafted-backslash key | All three strings pairwise distinct |
| Flat dotted key with no colliding nested structure | Still requires its escaped form to be emitted (no relaxation) |
| Partial match — `profile` exists, no `.name` property | No false-positive collision; ordinary allowlist rule applies |
| Collision at non-root depth (`a.profile.name` style) | Detected and withheld the same as at the root |
| Three-level dotted key (`a.b.c`) aliasing a deep nested value | Detected; both sides withheld |
| Crafted `__proto__.x` raw key | Withheld; `Object.prototype` provably untouched |
| Ordinary nested case with no colliding flat key | Unaffected — proves the detector does not fire on nothing |
| ADR text | States both the rule and when it does not fire |

### Round 4 results

```
tests/foundation/BIO-ARC-003.test.sh                34/34 checks passed  (exit 0)   was 32/32
node --test contracts/js/                           185 pass, 0 fail     (exit 0)   was 176
tests/foundation/BIO-ARC-001.test.sh                24/24 checks passed  (exit 0)   no regression
tests/foundation/BIO-ARC-002.test.sh                30/30 checks passed  (exit 0)   no regression
scripts/quality.sh                                  QUALITY: PASS        (exit 0)
scripts/secret-scan.sh --all                        clean                (exit 0)
evidence/BIO-ARC-003/codex/independent-review.sh    all 10 checks PASS   (exit 0)   was exit 1
```

Seyi's script, run unmodified, now passes including their new
`AC03 literal dotted keys cannot alias nested allowlist paths` probe.

### Routing

Per Chinedu's instruction after the causation_id alignment (round 3): this round's review
request goes directly to Seyi's own hive inbox, not only through
`python automation/biolog.py builder-complete`, with a copy to Chinedu for board tracking.

### Scope

Only QA-04 and its regression tests. No change to QA-01/02/03, the causation_id alignment,
or any BIO-ARC-001/002 file.
