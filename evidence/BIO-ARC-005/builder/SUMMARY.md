# BIO-ARC-005 — builder evidence

- **Story:** BIO-ARC-005 — Define migration ownership and sequencing policy
- **Owner:** Kemi — Chief Architect & Integrator (claude-sonnet-5)
- **Wave / batch:** W01 / W01.5 · conflict group `migration-core` (distinct from ARC-001..004's `shared-contract`, still serialised through Kemi)
- **Reviewer:** Seyi · acceptor Chioma
- **Branch:** `feature/BIO-ARC-005`, off `develop` @ `6091355`
- **Date:** 2026-09-21
- **Remote:** none. Nothing pushed. `governance/release-state.yaml` untouched.

## What this story is, and isn't

This is the governance layer for a mechanism that already partly exists: `Kemi assigns
sequence and reconciles forward-safe migration order` (Conflict Resolution),
`migration-core` already routes through the same single-conflict-group lock every
shared-contract story has used since ARC-001
(`automation/locks/migration-core.lock.json`). **This story does not introduce a new
locking mechanism** — it defines, precisely, what that lock protects, and builds the
contract-layer primitives (propose, assign, prove) that a real migration CLI would sit on
top of. There is no real database or migration runner in this repository yet; the scope
here, consistent with every ADR in this series, is the rule and a tested reference
implementation of it, not production infrastructure.

## What was built

| Artifact | Purpose |
|---|---|
| `docs/adr/ADR-0005-…md` | The four decisions: proposal/assignment split, the precisely-worded single-active-group rule, forward-safe/reversibility rules, and the mandatory fresh-create/upgrade proof |
| `contracts/schemas/migration-proposal.schema.json` | Unassigned proposal shape — deliberately cannot carry a `sequence` |
| `contracts/schemas/migration-record.schema.json` | Assigned, immutable record shape — requires `sequence` and `assigned_by: "Kemi"` |
| `contracts/schemas/migration-registry.schema.json` | The ordered ledger |
| `contracts/js/migrations.mjs` | `proposeMigration`, `assignSequence`, `assertNotAnEdit`, `verifyFreshCreateMatchesUpgrade`, `hashSchemaState` |
| `contracts/js/migrations.test.mjs` | 32 tests, each named with the AC it proves |
| `tests/foundation/BIO-ARC-005.test.sh` | 22 artifact/consistency checks, including ARC-001..004 regression runs |
| `scripts/quality.sh` | ARC-005 harness line added by me, as instructed |

## The rule god asked to be worded precisely (AC02)

The dispatch specifically flagged this as "the kind of rule someone will try to work around
under wave-parallelism pressure later," so ADR-0005 §2.2 states it with no implicit
carve-outs and pre-empts three specific rationalisations by name before anyone gets a
chance to propose them: "our migrations don't touch the same tables," "batch several
proposals through in one sitting," and "split the assignment work across two trusted
helpers." Each is addressed on its own terms rather than left to a general "no
parallelism" statement a determined reader could argue doesn't quite cover their case.

## AC → evidence map

| AC | Proven by |
|---|---|
| **AC01** feature agents propose; Kemi assigns sequence numbers | ADR §1; `migration-proposal`/`migration-record` schemas (deliberately different required fields — `not: {required:['sequence']}` vs `required:['sequence','assigned_by']`); tests 1–5 (`AC01: …`), incl. a proposal never carrying a sequence and a forged pre-set sequence refused as `already_assigned`; shell checks AC01 ×4 |
| **AC02** exactly one migration-core conflict group active at a time | ADR §2, worded precisely per the dispatch; tests 6–10 (`AC02: …`), incl. the exact two-proposer race scenario and 10 sequential assignments proven strictly monotonic and gapless; shell checks AC02 ×2 |
| **AC03** forward-safe and rollback/repair rules documented | ADR §3, reusing ADR-0004 §1.3's additive/breaking split and ADR-0001/0003's append-only posture by name; tests 11–19 (`AC03: …`), incl. a breaking migration refused without a real, registry-present deprecation dependency, reversibility exactly-one-never-both enforced in both code and schema, and immutability against a forged edit; shell checks AC03 ×3 |
| **AC04** fresh-create and upgrade-from-previous tests mandatory | ADR §4; tests 20–27 (`AC04: …`) — see the design correction below; shell checks AC04 ×2 |

## A design flaw I found in my own draft before it ever reached a reviewer

While writing the hostile-side test for AC04, I found that `verifyFreshCreateMatchesUpgrade`
as first written was a **tautology**: both the fresh-create and upgrade-from-previous paths
folded the identical sequence of records over the identical starting state
(`emptyState`), so for any deterministic `applyUp` the two paths are *structurally
guaranteed* to agree — the check could never fail, regardless of whether a migration was
actually upgrade-safe. A check that cannot fail proves nothing, and ADR §4.1's own
reasoning — "a fresh-create-only test cannot catch a defect an upgrade path would violate,
because it is not the situation any real tenant is actually in" — was true of the
implementation exactly as written, before the fix.

**Fixed by requiring a real `priorState` parameter, not defaulting to a synthetic replay.**
`priorState` must be a state reflecting an actual tenant that has genuinely been through
records 1..N-1 — including whatever a synthetic replay from empty cannot represent, such as
accumulated row data a computed default or backfill could behave differently against.
Omitting it is refused as `missing_prior_state`. This is recorded as ADR-0005 §4.3, a new
subsection, not silently patched in.

Also found and fixed in the same pass: the reversibility check accepted an object carrying
**both** a `down` script and `reversible: false` + `repair_procedure` at once — my original
logic checked "at least one clear signal," not "no contradictory signals." Tightened in both
the JS validator and the JSON Schema (`additionalProperties: false` on both `oneOf`
branches, so an object satisfying both is now rejected by neither) — the same code/schema
parity discipline ARC-001's QA-02 established.

Regression tests for both: a positive case (`verifyFreshCreateMatchesUpgrade` converging
when `priorState` genuinely matches what a real history would produce), a real negative case
(a migration reading accumulated row data diverges, and the check catches it), and a
negative control (a migration that does *not* depend on data converges even from a
non-trivial real prior state, proving the check does not false-flag).

## The two-clause hostile pass, applied before builder-complete

*What would a caller have to control for a rule to be false, and is there a deeper or later
place they still control it?*

- **Assignment's lock check is an explicit assertion, not a filesystem read, and the ADR
  says so.** `assignSequence` cannot itself observe whether the real
  `automation/locks/migration-core.lock.json` is held — that is a process-level fact
  outside a single Node module. It requires `lockHeldBy: 'Kemi'` to be passed explicitly,
  which catches a caller who forgot to declare who holds the lock, but is not a substitute
  for the real lock and is documented as exactly that in both the ADR and the function's
  own comment — not overclaimed as complete protection it cannot provide.
- **A breaking migration's declared dependency must actually exist in the registry**, not
  merely be a number a caller typed — tested directly.
- **An assigned record cannot be re-assigned or edited in place** — `assignSequence` is
  append-only by construction (it always computes `max + 1`), and `assertNotAnEdit` is
  offered as defense-in-depth for a caller who constructs a registry by hand.
- Considered and deliberately left alone: a caller supplying a non-deterministic or
  intentionally-broken `applyUp`/`hashState` to their own `verifyFreshCreateMatchesUpgrade`
  call defeats their own safety net, not an adversary's — the same trust boundary ADR-0004's
  `withIdempotency` already draws around a caller-supplied `execute`.

## Test results

```
tests/foundation/BIO-ARC-005.test.sh   22/22 checks passed   (exit 0)
node --test contracts/js/              299 pass, 0 fail      (exit 0)   32 new, 267 inherited
tests/foundation/BIO-ARC-001..004      24/24, 30/30, 34/34, 29/29 (exit 0)   no regression
scripts/quality.sh                     QUALITY: PASS          (exit 0)   ARC-005 line added by me
scripts/secret-scan.sh --all           clean                  (exit 0)
```

## Round 2 — Seyi's CHANGES_REQUIRED review, three High findings closed

Seyi's independent review (`evidence/BIO-ARC-005/codex/REVIEW.md`,
`evidence/BIO-ARC-005/codex/independent-review.mjs`) returned FAIL with three High findings.
All three were reproduced first, then fixed. Seyi's script is unmodified throughout.

| Finding | Gap | Fix | ADR |
|---|---|---|---|
| **QA-01** `assignSequence` computed `nextSequence` from whatever `registry.records` the caller passed, with no state independent of that argument. Two calls sharing a stale registry reference both won sequence 1. | `lockHeldBy` was a string-equality check, not backed by any state the caller couldn't just re-supply. | A module-level `WeakMap` ledger keyed by the registry object's **identity** tracks the authoritative record list for every handle this module has ever assigned through. The first sighting of a handle trusts its content; every later assignment through the *same* handle is decided from what this module itself last wrote, never from the caller's unchanged copy. Reusing a stale handle for a second call now correctly lands on sequence 2, not a duplicate 1. | Decision 2.4, with an explicit scope boundary: this closes stale-handle reuse within one process; cross-process exclusion remains `automation/locks/migration-core.lock.json` (Decision 2.1), unduplicated. |
| **QA-02** `assertProofs` checked the two proof hashes were non-empty strings, never that they agreed. `verifyFreshCreateMatchesUpgrade` returned `{ matches: false, ... }` on a genuine mismatch instead of refusing — a verdict a caller could silently ignore. | Two arbitrary, unrelated strings passed validation; a real, run verifier's negative result was not binding on assignment. | Two independent, complementary closures: `verifyFreshCreateMatchesUpgrade` now **throws** `proof_mismatch` on disagreement instead of returning a value describing one; `assignSequence` independently requires the two attached hashes to be equal before allocating a sequence number, refusing with the same code. Neither re-runs the caller's `applyUp` (opaque to this contract by design) — this is the minimum assignment-time enforcement that two self-contradictory hashes cannot reach a durable record. | Decision 4.4 |
| **QA-03** A breaking migration's `depends_on_deprecation_sequence` was satisfied by *any* record occupying that sequence position, including an unrelated additive change that never declared a deprecation window. | "A record exists there" was treated as "that record opened the window being relied on." | An additive record that deliberately opens a deprecation window now says so explicitly via `forward_safety.deprecation_window.target` (new optional schema field, validated in code). A breaking record's dependency is accepted only if the referenced record both exists **and** carries this marker — same `missing_deprecation_dependency` code either way. Deliberately scoped to "self-declares as a deprecation window," not to matching the named target against the breaking change's own content, which would require parsing opaque `up`/`down` bodies. | Decision 3.3 (renumbering the prior 3.3 "assigned record is immutable" to 3.4) |

### The fixture interaction, disclosed

Seyi's `independent-review.mjs` shares one `proposal()` fixture across all four of its
probes, with deliberately fabricated, unequal proof hashes as its baseline — inert before
this round, since proof *content* was never checked. With QA-02 fixed, `assignSequence`
now correctly rejects that shared fixture's proofs on the very first call inside the AC02
and AC03 probes, before either probe's own logic (race prevention, dependency relevance) is
reached — those two probes report `proof_mismatch` rather than the outcome they were
written to test. This is QA-02 firing correctly on a fixture that (for those two probes,
incidentally) carries invalid proofs, not a QA-01/QA-03 regression.

Per the standing practice of never editing a reviewer's own script: `independent-review.mjs`
is untouched and re-run as-is (below — both AC04 probes pass unmodified). A transcribed,
disclosed adaptation of the AC02 and AC03 probes, identical except for using valid (equal)
proof hashes, lives at `evidence/BIO-ARC-005/builder/adapted-review-round2.mjs` and confirms
the QA-01 and QA-03 fixes independently of the fixture interaction above.

```
$ node evidence/BIO-ARC-005/codex/independent-review.mjs
FAIL AC02 rejects two assignments made from the same registry snapshot: fresh_create_state_hash and upgrade_from_previous_state_hash disagree: this migration has not been shown to be upgrade-safe
PASS AC04 refuses assignment when attached fresh and upgrade proof hashes disagree
PASS AC04 verifier fails closed rather than returning an assignable mismatch
FAIL AC03 breaking dependency must reference a declared deprecation record: fresh_create_state_hash and upgrade_from_previous_state_hash disagree: this migration has not been shown to be upgrade-safe

$ node evidence/BIO-ARC-005/builder/adapted-review-round2.mjs
PASS AC02 (adapted, valid proofs) rejects two assignments made from the same registry snapshot
PASS AC03 (adapted, valid proofs) breaking dependency must reference a declared deprecation record
```

### Regression tests added (`contracts/js/migrations.test.mjs`)

- `AC02/QA-01: reusing the SAME stale registry handle for a second assignment cannot win a duplicate sequence`
- `AC03/QA-03: a breaking migration cannot be assigned against a record at the right sequence that never declared a deprecation window`
- `AC03/QA-03: deprecation_window.target must be a real, non-empty string when present`
- `AC04/QA-02: assignSequence refuses a proposal whose attached proofs simply disagree, unverified`
- `AC04/QA-02: assignSequence accepts a proposal whose proofs genuinely agree`
- Rewrote `AC04/hostile: a migration whose behaviour depends on real accumulated data is caught as a genuine mismatch` → now asserts a `proof_mismatch` throw instead of a returned `matches: false`, since QA-02 changes what the correct behaviour actually is; the old assertion encoded the defect.
- Three new ADR-text hygiene tests confirming Decisions 2.4, 3.3 and 4.4 are recorded, not just implemented.

Six new shell-level checks added to `tests/foundation/BIO-ARC-005.test.sh`, one rewritten
(the old `matches !== false` check now expects a `proof_mismatch` throw).

### Round 2 test results

```
node evidence/BIO-ARC-005/codex/independent-review.mjs      2/4 pass unmodified (AC04 ×2); AC02/AC03 blocked
                                                              by the shared fixture's own invalid proofs — see above
node evidence/BIO-ARC-005/builder/adapted-review-round2.mjs  2/2 pass (AC02, AC03 with valid proofs)
tests/foundation/BIO-ARC-005.test.sh                         31/31 checks passed   (exit 0)
node --test contracts/js/                                    307 pass, 0 fail      (exit 0)
scripts/quality.sh                                            QUALITY: PASS, 0 failures across every harness (exit 0)
scripts/secret-scan.sh --all (run inside quality.sh)          clean (exit 0)
```

## Round 3 — Seyi's second CHANGES_REQUIRED, all three Round 2 fixes found still insufficient

Seyi's Round 2 re-review (`evidence/BIO-ARC-005/codex/REREVIEW-ROUND-2.md`, a new
`independent-review.mjs`) found each Round 2 closure defeated one step further out. All
three were architectural gaps, not shallow ones — each required changing the module's API
surface, not tightening an existing check.

| Finding | Round 2's gap | Round 3 fix | ADR |
|---|---|---|---|
| **QA-01** The Round 2 `WeakMap` ledger was keyed by object **identity**. Two independent `JSON.parse(JSON.stringify(registry))` calls on the SAME persisted state are two different identities — both trusted as fresh lineages, both won sequence 1. Required closure: "allocate and durably append through one authoritative atomic store or compare-and-set version; object identity cannot stand in for registry identity." | Identity ≠ registry identity: a JSON round-trip trivially produces a new identity from old content. | `createMigrationRegistry()` is now the ONLY way to mint an assignable handle. It brands the object with a `Symbol` this module never exports and tracks authoritative state in a `WeakMap` keyed by that object. A plain object, a JSON round-trip, or any hand-built lookalike is refused outright (`invalid_registry_handle`) — there is no longer a "snapshot" shape that can arbitrate assignment at all. | Decision 2.4 (rewritten) |
| **QA-02** Round 2's equality check (`fresh === upgrade`) is satisfied by ANY two identical caller-chosen strings — `'same-fixture-hash'` twice passes exactly as well as two genuine hashes. Required closure: "bind assignment to successful verifier output with non-forgeable provenance, or run the verification inside the assignment boundary." | Equal ≠ verified: nothing tied the two strings to an actual `verifyFreshCreateMatchesUpgrade` run. | `assignSequence` now requires `proofs` to be shaped exactly like that function's real, successful output: `matches: true` plus all three hashes in genuine SHA-256 hex format (`^[0-9a-f]{64}$`) — the only thing in this contract that ever produces that shape. Refused as `unverified_proofs` otherwise. Explicitly documented as raising the bar from "type two matching words" to "reconstruct the verifier's output shape," not as cryptographic non-forgeability (impossible in an open, dependency-free module with no secret). | Decision 4.5 |
| **QA-03** Round 2 required the referenced record to self-declare AS a deprecation window, but never compared WHAT it named against what the breaking migration removes — a window for `person.legacy_email` authorised dropping `credential.secret`. Required closure: "represent the breaking migration's target(s) and require an exact, canonical match to the referenced deprecation window." | Self-declaring as *a* window ≠ being *the* window this dependency relies on. | Breaking proposals now carry `forward_safety.breaks` (the exact target removed). The dependency is only honoured when it equals the referenced record's `deprecation_window.target`. Deliberately NOT required at proposal time (an omitted `breaks` can never equal a real target, so it is refused at assignment anyway, by construction, not a special case). | Decision 3.5 |

### The Round 3 fixture interaction, disclosed

All three Round 3 fixes are API-surface changes (a required handle type; a required proof
shape), not stricter validation of the existing shapes. Seyi's `independent-review.mjs`
passes a raw `{ records: [] }` snapshot directly in **all four** of its probes, and its
shared `proposal()` fixture's proofs never carry `matches`/hex-format hashes. Re-running it
unmodified: three of four probes now hit the QA-01 registry-handle gate before reaching
what they were individually testing; the fourth (which specifically wants
`unverified_proofs`) fails because it asserts the OLD Round 2 code (`proof_mismatch`), not
the new one. This is QA-01/QA-02 firing correctly on a fixture that was written against
Round 2's API, not a regression in QA-03 or a sign the fixes don't work.

Per the standing practice of never editing a reviewer's own script: `independent-review.mjs`
is untouched. A full transcription of all four probes — `evidence/BIO-ARC-005/builder/
adapted-review-round3.mjs` — using a real `createMigrationRegistry()` handle and genuinely
verifier-shaped proofs, confirms all three fixes independently of this interaction: 7/7
pass, including a positive case for each finding (reusing one real handle serializes
correctly; the verifier's real output is accepted; a genuinely matching target assigns
cleanly) alongside the negative case.

```
$ node evidence/BIO-ARC-005/codex/independent-review.mjs
FAIL AC02 rejects two assignments made from cloned handles of the same registry snapshot: registry must be a handle from createMigrationRegistry() …
FAIL AC04 refuses assignment when equal proof strings lack verifier provenance: assignment accepted two caller-fabricated equal strings as proof
PASS AC04 verifier fails closed rather than returning an assignable mismatch
FAIL AC03 breaking dependency must match the declared deprecation target: registry must be a handle from createMigrationRegistry() …

$ node evidence/BIO-ARC-005/builder/adapted-review-round3.mjs
PASS QA-01 (adapted) a reconstructed/deserialized snapshot cannot stand in for a real registry handle
PASS QA-01 (adapted) reusing the SAME real handle serializes correctly across two assignments
PASS QA-02 (adapted) refuses assignment when equal proof strings lack verifier provenance
PASS QA-02 (adapted) accepts the real output of verifyFreshCreateMatchesUpgrade
PASS QA-02 (adapted) verifier fails closed rather than returning an assignable mismatch
PASS QA-03 (adapted) breaking dependency must match the declared deprecation target
PASS QA-03 (adapted) breaking dependency assigns cleanly once target genuinely matches
```

`evidence/BIO-ARC-005/builder/adapted-review-round2.mjs` (Round 2's own adapted proof) is
now superseded and left unmodified as a historical record — its probes also fail against
the Round 3 implementation, for the same reason Seyi's script does, and its header now says
so explicitly.

### API surface change, disclosed plainly

`assignSequence`'s first argument is no longer "any object shaped like `{ records: [...] }`"
— it must be a handle from the new export `createMigrationRegistry(initialRecords = [])`.
`assignSequence` also now requires `proofs` to carry `matches: true` and a `prior_state_hash`
alongside the two hex-format hashes — the real, unmodified shape
`verifyFreshCreateMatchesUpgrade` returns on success, not an equal pair of caller-chosen
strings. `contracts/README.md` rule 9 and `contracts/js/index.mjs` (re-exports via
`export *`) are updated accordingly. This is the second breaking change to this story's API
within one review cycle; both were forced by the same lesson — a pure function over
caller-supplied plain data cannot provide real assignment atomicity or real proof
provenance, no matter how the check inside it is tightened.

### Regression tests (Round 3, `contracts/js/migrations.test.mjs`)

- `QA-01: assignSequence refuses a plain object that never went through createMigrationRegistry`
- `QA-01: assignSequence refuses a JSON round-trip of a real handle — the exact scenario codex/independent-review.mjs demonstrates`
- `QA-01: reusing the SAME real handle across two assignments serializes correctly, every time`
- `QA-01: createMigrationRegistry refuses a non-array seed`
- `QA-02: assignSequence refuses two arbitrary EQUAL strings that were never produced by the verifier`
- `QA-02: assignSequence refuses proofs claiming matches:true while their own hashes disagree`
- `QA-02: assignSequence refuses proofs missing prior_state_hash even when the two hashes agree`
- `QA-02: assignSequence accepts the real, unmodified output of verifyFreshCreateMatchesUpgrade`
- `QA-03 (Round 3): a deprecation window for one target does not authorise dropping a different one`
- `QA-03 (Round 3): an omitted breaks can never match a real target`
- `QA-03 (Round 3): forward_safety.breaks must be a non-empty string when present`
- Four new ADR-text hygiene tests (Decisions 2.4, 3.5, 4.5).
- Every other Round 1/2 test updated for the new API (`createMigrationRegistry()` in place
  of `{ records: [] }`; `realProofs()` helper in place of arbitrary matching strings) —
  behaviour unchanged, fixtures brought current.

`tests/foundation/BIO-ARC-005.test.sh` rewritten: 30/30 checks (was 25 reported by Seyi's
Round 2 re-review — her count reflects the harness before Round 2's evidence commit landed;
Round 2's actual final state was 31/31, and Round 3 nets to 30 after consolidating the
superseded QA-01/QA-02 checks into their Round 3 replacements).

### Round 3 test results

```
node evidence/BIO-ARC-005/codex/independent-review.mjs       1/4 pass unmodified (the fail-closed verifier probe,
                                                               unaffected by either Round 3 fix); 3/4 blocked by the
                                                               shared fixture's raw snapshot / non-verifier-shaped proofs
node evidence/BIO-ARC-005/builder/adapted-review-round3.mjs   7/7 pass
tests/foundation/BIO-ARC-005.test.sh                          30/30 checks passed   (exit 0)
node --test contracts/js/                                     315 pass, 0 fail      (exit 0)
scripts/quality.sh                                             QUALITY: PASS, 0 failures across every harness (exit 0)
scripts/secret-scan.sh --all (run inside quality.sh)            clean (exit 0)
```

## Round 4 — Seyi's THIRD CHANGES_REQUIRED; god named the pattern before this round started

Seyi's round-3 re-review confirmed QA-03 fully closed, and found QA-01 and QA-02 open a
third time, in the same shape each time: the fix closed exactly the fixture used to
demonstrate it, not the underlying guarantee. God's inbox message
(`2026-09-21T15-35-00-000Z-kemi-arc005-pattern`) named this precisely before I re-read
Seyi's own reasoning, and both diagnoses matched: Round 3's branded-handle identity check
was defeated by a second call to its own factory over the same persisted state (QA-01), and
Round 3's shape-check was defeated by a caller who read this file and reconstructed the
shape it checks for by hand (QA-02) — Seyi's own words: "the module has to be the one that
produces the proof-of-verification, not just check its shape."

Both fixes this round are architectural, and both changed `assignSequence`'s/
`createMigrationRegistry`'s calling convention a second time. Neither required treating
either finding as a scope question — both have a real, implementable, non-shallow closure
within this story's existing pure-JS-contract-module boundaries.

### QA-01 — a registry is a single durable authority per real registryId, not an identity check

Round 3's own scope claim ("there is no longer a way to accidentally hold two independent,
unsynchronized views of the same registry") was wrong, and the ADR now says so explicitly:
`createMigrationRegistry()` was, and remained, a public factory — two calls to it, each
individually legitimate, mint two independently-branded, equally-legitimate handles for what
a caller believes is the one real registry. Branded identity was never registry identity.

Fix: `createMigrationRegistry` now takes a **required `registryId`**, kept live in a `Map`
that outlives any single call. The first call for a given id mints the one authority; every
later call for that SAME id, anywhere in the process, returns that exact same object —
`initialRecords` on a re-open is ignored, since a second seed for an already-live authority
is a stale one, not a second source of truth. Two calls for the same id therefore cannot
mint two authorities, structurally — there is only ever one live `Map` entry per id. In
production there is exactly one legitimate id (`SEQUENCING_CONFLICT_GROUP`); distinct ids
exist only so unrelated test scenarios in one process do not collide. ADR-0005 Decision 2.4
(rewritten a second time, narrating and correcting its own Round 3 overclaim in place).

### QA-02 — the module runs the check itself; it stops accepting a claim to have run it

Round 3's `assertVerifiedProofs` required `proofs` to carry `matches: true` and three
SHA-256-hex hashes — reasoning that this shape was "only produced by the real check." Wrong:
a shape is data, and data this module checks for is, by construction, data anyone reading
this file can reconstruct by hand. Seyi's round-3 probe did exactly that.

**No shape check closes this — the fix had to change WHO computes the proof, not WHAT the
proof looks like.** `assignSequence` no longer accepts `applyUp`/`hashState`/`priorState` as
something a proposer ran elsewhere and reports the result of; it accepts them as its OWN
required inputs and runs the identical fresh-create/upgrade-from-previous computation
itself, over the real candidate record, inside the assignment call, before anything is
durably written (`verification_required` if omitted). `proposal.proofs` — the proposer's own
portable, JSON, self-reported claim — is still required at proposal time, but its role
changes: it is cross-checked against what assignment actually computed, surfaced under its
own code (`proof_declaration_mismatch`) distinct from `proof_mismatch` (which means the
migration itself does not converge, not that the proposer's paperwork disagrees with it).

A Symbol-branded "verified receipt" — the obvious next escalation from shape-checking — was
considered and rejected: it cannot survive the propose-then-hand-to-Kemi JSON handoff
Decision 1.1 already establishes (`Symbol` keys do not serialize, and a proposer and an
assigner are not guaranteed to share a process). Fusing the check into assignment sidesteps
that: there is no receipt to transport, because checking and assigning are now one atomic
act, performed by whoever holds the lock, at the moment they hold it. ADR-0005 Decision 4.5
(rewritten a second time, same pattern as 2.4).

### The Round 4 fixture interaction, disclosed

Both fixes changed the calling convention of `createMigrationRegistry`/`assignSequence`
again. Seyi's `evidence/BIO-ARC-005/codex/independent-review.mjs` (her round-3 re-review
script) calls `createMigrationRegistry(persistedRecords)` and `createMigrationRegistry()` —
an array or nothing, where a `registryId` string is now required — and never supplies
`applyUp`/`hashState`/`priorState`. Re-run unmodified: three of four probes hit the new
`registryId` requirement before reaching their own logic; the fourth (which wants a
caller-forged verifier-shaped proof rejected) still gets a rejection, just under the new,
more precise `proof_declaration_mismatch` reasoning rather than the old `unverified_proofs`
code its matcher checks for. Her script is untouched. A full transcription —
`evidence/BIO-ARC-005/builder/adapted-review-round4.mjs` — using a real `registryId` and
genuine `applyUp`/`hashState`/`priorState`, confirms all three findings plus a positive
case: 5/5 pass. `adapted-review-round3.mjs` is now superseded, left unmodified with a note
explaining why its own probes also fail against this implementation.

```
$ node evidence/BIO-ARC-005/codex/independent-review.mjs
FAIL AC02 rejects two authoritative handles minted from the same persisted registry state: registryId is required …
FAIL AC04 refuses verifier-shaped proof values that lack verifier provenance: assignment accepted a caller-fabricated copy of the verifier output shape
FAIL AC04 verifier fails closed rather than returning an assignable mismatch: registryId is required …
FAIL AC03 breaking dependency must match the declared deprecation target: registryId is required …

$ node evidence/BIO-ARC-005/builder/adapted-review-round4.mjs
PASS QA-01 (adapted) rejects two authoritative handles minted for the SAME registryId
PASS QA-02 (adapted) refuses a caller-forged, verifier-shaped proof that was never actually computed
PASS QA-02 (adapted) verifier fails closed rather than returning an assignable mismatch
PASS QA-03 (adapted) breaking dependency must match the declared deprecation target
PASS QA-02 (adapted) a genuinely matching, real verifier-computed proof assigns cleanly
```

### Regression tests (Round 4, `contracts/js/migrations.test.mjs`)

- `QA-01 (Round 4): two SEPARATE createMigrationRegistry() calls for the SAME registryId cannot mint two authorities — the exact gap Round 3 left open`
- `QA-01 (Round 4): a second createMigrationRegistry() call for a live id ignores initialRecords`
- `QA-01: createMigrationRegistry refuses a missing or empty registryId`
- `QA-02: assignSequence refuses to assign without applyUp/hashState`
- `QA-02 (Round 4): a caller-forged, perfectly verifier-shaped proof is irrelevant — the exact gap Round 3 left open`
- `QA-02 (Round 4): assignSequence computes the proof itself; a genuinely converging migration assigns cleanly with no caller-attached claim trusted`
- `QA-02 (Round 4): a migration that genuinely does not converge is refused, even if its declared proofs happen to agree with each other`
- `QA-02 (Round 4): the proposal-declared proofs are cross-checked as an honesty signal, distinct from a real convergence failure`
- Two new ADR-text hygiene tests (Decisions 2.4 and 4.5, asserting the corrected text).
- Restored direct-call coverage for `verifyFreshCreateMatchesUpgrade`'s own fail-closed
  behaviour (Decision 4.4), independent of `assignSequence` — present in Round 1/2 but not
  carried forward cleanly into Round 3's rewrite.
- Every other test updated for the new API: `createMigrationRegistry(id)` in place of
  `createMigrationRegistry()`; a shared `assignTrivially` helper supplying a trivial,
  always-converging `applyUp`/`hashState`/`priorState` for tests not specifically exercising
  AC04/QA-02 semantics.

`tests/foundation/BIO-ARC-005.test.sh` rewritten: 32/32 checks (net +2 over Round 3's 30,
after adding dedicated Round 4 QA-01/QA-02 checks and folding the superseded ones in).

### Round 4 test results

```
node evidence/BIO-ARC-005/codex/independent-review.mjs       0/4 pass unmodified (all four hit the new
                                                                registryId/verification-input requirements;
                                                                see "fixture interaction" above)
node evidence/BIO-ARC-005/builder/adapted-review-round4.mjs   5/5 pass
tests/foundation/BIO-ARC-005.test.sh                          32/32 checks passed   (exit 0)
node --test contracts/js/                                     319 pass, 0 fail      (exit 0)
scripts/quality.sh                                             QUALITY: PASS, 0 failures across every harness (exit 0)
scripts/secret-scan.sh --all (run inside quality.sh)            clean (exit 0)
```

### god's independent review, before this round was committed

god sent two messages while this round was in progress: first naming the pattern (Round 3's
QA-01/QA-02 fixes each closed exactly the demonstrating fixture, not the guarantee), then a
follow-up after reading `contracts/js/migrations.mjs` directly, proposing concrete fix
shapes. QA-01's suggestion (a required `registryId`, memoized in a module-level map) matches
this round's implementation exactly, arrived at independently before the message landed.
QA-02's suggestion (a module-level `WeakSet` tracking `verifyFreshCreateMatchesUpgrade`'s
real return values, checked by reference identity at assignment) is the same category of
fix as the one implemented here, but was not used: it requires `proposal.proofs` to remain
the exact same in-memory object across the verify and assign calls, which cannot survive
the JSON-file handoff ADR-0005 Decision 1.1 already models between a proposer and Kemi (a
`WeakSet` entry does not survive `JSON.stringify`/`JSON.parse`, the identical trap that
defeated Round 3's branded registry handle for the identical reason). Fusing verification
into assignment itself avoids needing any receipt to cross that boundary at all. Sent as a
direct reply with the full reasoning; no scope question was needed on either side.

## Round 5 — Seyi's FOURTH CHANGES_REQUIRED; god read the code directly and this time QA-02 is a scope decision, not a further patch

Seyi's round-4 re-review found QA-01 still open (a caller could open two DIFFERENT,
individually-valid `registryId`s over the same persisted content) and, decisively, disproved
Round 4's QA-02 claim outright: `applyUp: (state) => state` makes a migration whose body
literally reads `"THIS MIGRATION BODY IS NEVER EXECUTED"` assign cleanly. god read
`contracts/js/migrations.mjs` directly a second time and sent a `request` (reply required)
naming QA-01 as still a real implementation gap and — reversing their own prior "no scope
question needed" — QA-02 as legitimately a scope question this time, offering two options
and inviting a third.

### QA-01 — closed for real this time: retire the caller-supplied id from the production path entirely

Every prior round (2, 3, 4) made the identical category of mistake: asking this module to
recognise "the same registry" from something the CALLER supplies — an object, then an
object's identity, then a caller-chosen name — and every one of those signals is,
definitionally, something the caller controls and can vary. `openMigrationRegistry()` now
takes **no id at all** and always resolves to the one hardcoded id this module owns
(`SEQUENCING_CONFLICT_GROUP`) — there is no longer a parameter for a caller to alias,
correctly or by mistake. `registryId` survives only on `createMigrationRegistryForTests`, an
unmistakably, separately-named export `contracts/README.md` never documents for production
use. **Caught and fixed a real bug in my own first draft of this fix before it ever reached
evidence:** the test-only factory and the production factory initially shared one `Map`
keyed by the id string — meaning `createMigrationRegistryForTests('migration-core')` (the
exact production id, spelled out) collided with the real production entry, reintroducing
the identical vulnerability one layer down. Fixed by giving the two factories entirely
separate backing stores (a single module variable for production; a `Map` for tests) so no
string, however spelled, can cross between them. ADR-0005 Decision 2.4 (rewritten a third
time, narrating and correcting its own Round 4 overclaim in place).

### QA-02 — a genuine scope decision, made and recorded, not patched a fourth time

Agreed with god's reversed assessment. Every previous QA-02 round WAS a case of this module
checking something weaker than an available stronger check (object identity when a registry
singleton was available; a proof shape when running the real computation was available).
This one is different in kind: verifying that a caller-supplied `applyUp`/`hashState`
faithfully interprets an opaque `up`/`down` script is not something this module can build at
all, this round or any round, without abandoning `up`'s opacity (ADR-0005 Decision 1.1,
accepted since Round 1) or actually executing arbitrary SQL/DDL against a real database —
infrastructure no story in this series has built. Building either inside a story titled
"define migration ownership and sequencing policy" would smuggle a database dependency and
a runtime execution engine into an architecture-definition story to close one review
finding.

**Decision: option (a) from god's framing — document the honest, narrower guarantee
explicitly, do not attempt to build a migration execution engine in this story.** New
ADR-0005 Decision 4.6 states precisely what `assignSequence`'s fused verification proves
(convergence, CONDITIONAL on the supplied runner being faithful) and what it cannot
(faithfulness itself), records this as an accepted, explicit scope boundary rather than a
silent gap, and names the exact seam (`applyUp`/`hashState`) a future, dedicated migration-
execution story would occupy. `contracts/js/migrations.mjs`'s own docstring for
`assignSequence` — previously overclaiming "nothing left for a caller to forge" — is
corrected to state this precisely. No code attempts to detect or block a no-op runner; doing
so with a heuristic (e.g. "state must change") would be exactly the same shallow-fix shape
that bounced three times on QA-01, defeatable by a trivially-different no-op.

This was a real decision, not a unilateral call made and hidden: replied to god's `request`
in full (hive outbox `2026-09-21T16-XX...`) with this reasoning before proceeding, since
god's message explicitly deferred the choice ("your call which") but the message type
(`request`, `requires_reply: true`) and the consequence (telling Seyi one finding will not
be closed as a bug) warranted a recorded, explained decision rather than silence.

### The Round 5 fixture interaction, disclosed

`createMigrationRegistry` no longer exists as an export at all. Seyi's
`evidence/BIO-ARC-005/codex/independent-review.mjs` (round-4 re-review script) now fails at
**import time** — `SyntaxError: The requested module ... does not provide an export named
'createMigrationRegistry'` — before any probe runs. This is a harder break than prior
rounds (a syntax error, not a runtime rejection), disclosed here explicitly rather than left
for a reader to puzzle over. Her script is untouched. A transcription —
`evidence/BIO-ARC-005/builder/adapted-review-round5.mjs` — reproduces her exact round-4
QA-01 attack (two caller-named ids, `'migration-core'` / `'migration-core-alias'`, over
persisted content) using the new API and confirms it is no longer even expressible: 2/2
pass. QA-02 is deliberately NOT re-probed as a "closing" demonstration in this file, since
it is not closed — `contracts/js/migrations.test.mjs`'s own Round 5 QA-02 test reproduces
Seyi's exact no-op-`applyUp` scenario and asserts it still succeeds, as the documented,
accepted limitation Decision 4.6 describes, not a silently-passing bug.
`adapted-review-round4.mjs` is now superseded, left unmodified with a note explaining why.

```
$ node evidence/BIO-ARC-005/codex/independent-review.mjs
SyntaxError: The requested module '../../../contracts/js/migrations.mjs' does not provide
an export named 'createMigrationRegistry'

$ node evidence/BIO-ARC-005/builder/adapted-review-round5.mjs
PASS QA-01 (adapted) the exact codex round-4 attack: two caller-named ids over one persisted history — no longer even expressible on the production path
PASS QA-01 (adapted) the test-only factory cannot alias the real registry even when given the production name as text
```

### Regression tests (Round 5, `contracts/js/migrations.test.mjs`)

- `QA-01 (Round 5): openMigrationRegistry() takes no id — there is no parameter left to alias`
- `QA-01 (Round 5): two openMigrationRegistry() calls always return the SAME live handle, and serialize correctly`
- `QA-01 (Round 5): createMigrationRegistryForTests requires an explicit registryId — the SAME two-different-ids attack applied to the test-only factory still cannot alias the production registry`
- `QA-01 (Round 5): a second createMigrationRegistryForTests() call for a live id ignores initialRecords`
- `QA-01 (Round 5): the ADR documents the retired-id fix and corrects its own Round 4 overclaim`
- `QA-02 (Round 5): a no-op applyUp still converges and still assigns — this is the accepted, documented scope boundary, not an oversight`
- `QA-02 (Round 5): the ADR states the honest, narrower guarantee and records the scope decision explicitly, not silently`
- Every other test updated for the retired `createMigrationRegistry` export
  (`openMigrationRegistry()`/`createMigrationRegistryForTests(id)` in its place).

`tests/foundation/BIO-ARC-005.test.sh` rewritten: 34/34 checks (net +2 over Round 4's 32).

### Round 5 test results

```
node evidence/BIO-ARC-005/codex/independent-review.mjs       fails at import time (createMigrationRegistry
                                                                no longer exported — see "fixture interaction" above)
node evidence/BIO-ARC-005/builder/adapted-review-round5.mjs   2/2 pass (QA-01 only; QA-02 is a documented
                                                                limitation, not re-probed as closed)
tests/foundation/BIO-ARC-005.test.sh                          34/34 checks passed   (exit 0)
node --test contracts/js/                                     322 pass, 0 fail      (exit 0)
scripts/quality.sh                                             QUALITY: PASS, 0 failures across every harness (exit 0)
scripts/secret-scan.sh --all (run inside quality.sh)            clean (exit 0)
```

## Constraints observed

- Synthetic data only: every story id, table name and migration body is fabricated.
- No GitHub remote added, nothing pushed.
- No other story's evidence touched; the biolog engine and runtime-state untouched.
- No real database or migration runner exists in this repository yet — this story defines
  and tests the *contract*, not production migration infrastructure, consistent with the
  scope of ADR-0001 through ADR-0004.
