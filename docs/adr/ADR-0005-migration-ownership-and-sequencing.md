# ADR-0005 — Migration ownership and sequencing policy

- **Status:** Accepted (BIO-ARC-005, wave W01)
- **Date:** 2026-09-21
- **Owner:** Kemi — Chief Architect & Integrator
- **Story:** BIO-ARC-005 — Define migration ownership and sequencing policy
- **Builds on:** [ADR-0001](ADR-0001-canonical-identifiers-clocks-and-versioning.md) (append-only records), [ADR-0003](ADR-0003-audit-evidence-and-event-envelopes.md) (append-only audit), [ADR-0004](ADR-0004-api-versioning-error-and-idempotency-conventions.md) (additive vs breaking)
- **Applies to:** every schema migration against a tenant or control-plane database, from every feature team
- **Change policy:** shared contract and governance mechanism. Freezes at W01 exit; changing it needs a superseding ADR and recorded Kemi approval.

## Context

Multiple feature teams will each need schema changes — identity, credentials, biometrics,
document evidence, access — and they build in parallel, isolated worktrees. A database has
exactly one history. If two teams each mint their own "next" migration number, or apply
migrations in whatever order their own branch merges, the result is not two histories that
can later be reconciled — it is one broken one, on every environment that ever applied both
in a different order.

The governing documents already say who resolves this: "database migrations are serialised
by the Architecture/Integration agent" (Data Architecture), "Kemi assigns sequence and
reconciles forward-safe migration order" (Conflict Resolution), and the build rules already
route `migration-core` through the same single-conflict-group lock every shared-contract
story has used since ARC-001 (`automation/locks/migration-core.lock.json`, held by exactly
one story at a time). **This ADR does not introduce a new locking mechanism** — it defines,
precisely, what that lock is protecting and what "serialised" is required to mean, because
a rule that is only enforced by a lock file and never written down is a rule the next
wave-parallelism deadline will find a reason to route around.

## Decision 1 — Feature agents propose; only Kemi assigns a sequence number (BIO-ARC-005-AC01)

### 1.1 Two distinct artifacts, two distinct authorities

A **proposal** is written by the feature agent that needs the schema change. It names the
story, the conflict group the *change itself* belongs to (identity, credential, biometric —
not `migration-core`, which is reserved for the sequencing process itself), a human
description, and its `up` and `down`/repair content (Decision 3). It carries **no sequence
number**. A proposal is not part of the migration history and applying it is not defined —
it is a request to join the history, nothing more.

A **record** is a proposal that Kemi has assigned a sequence number to, from a single,
monotonically increasing counter this ADR owns. Only a record is ever applied to a real
database. The two schemas (`migration-proposal.schema.json`,
`migration-record.schema.json`) are deliberately different shapes — a proposal has no
`sequence`, a record requires one — so that a proposal can never be mistaken for something
ready to run, and a caller cannot forge readiness by adding a number to an unreviewed file.

### 1.2 Assignment is a decision, not a computation

The next sequence number is **not** "one more than the highest number a proposer can see."
A proposer reading the current registry and computing `max(sequence) + 1` itself would make
assignment a race between however many proposers do that computation at the same moment —
exactly the failure this ADR exists to prevent, moved one level up instead of closed.
Assignment is Kemi opening the registry, deciding the number, and writing the record. A
proposal is inert until that happens, however confident its author is about which number
comes next.

## Decision 2 — Exactly one migration-core conflict group is active at a time (BIO-ARC-005-AC02)

### 2.1 What "active" means, precisely

The `migration-core` conflict group has exactly one holder at any moment — the story
currently assigning or reconciling sequence numbers — enforced by
`automation/locks/migration-core.lock.json`, released only on acceptance (`DONE`) or an
explicit unblock, identically to every `shared-contract` lock this wave has already used.
"Active" means: a proposal may be *written* by anyone, at any time, in parallel, from any
worktree — nothing about drafting a migration is serialised. What is serialised is the one
act of **turning a proposal into a numbered record**. That act may only happen while the
lock is held, and the lock has one holder.

### 2.2 The rule stated precisely, because it will be tested

This is the sentence a later wave-parallelism deadline will try to find an exception to, so
it is stated as a rule with no implicit carve-outs:

**No two migration records may ever be assigned sequence numbers without one fully
completing — written, and durably recorded in the registry — before the next assignment
begins. There is no condition under which two agents, two temps, or two Kemi-spawned
helpers assign numbers concurrently, regardless of whether their proposals touch
overlapping tables.**

Three specific rationalisations are pre-empted, because each is the kind of reasoning that
sounds locally correct and is not:

- *"Our migrations don't touch the same tables, so they can be assigned in parallel."*
  Sequence numbers order the **whole history**, not a per-table dependency graph. Two
  assignments racing each other can each observe the same "current highest number" and
  both mint the next one, producing two different migrations claiming the same sequence
  position on different environments — a forked history, not a merge conflict a person
  can resolve after the fact, because by the time it is noticed some environments have
  already applied one branch of it and some the other.
- *"Batch several proposals through in one sitting to save time."* This is fine — but only
  as **serial single assignments inside one held lock session**, one number at a time, in
  the order Kemi decides (which need not be the order requests arrived, if a dependency
  requires reordering before assignment). It is never a reason to assign two numbers from
  two independent computations of "next."
- *"Split the assignment work across two trusted helpers to go faster."* Rejected
  outright: assignment is not parallelisable by adding more trusted assigners, for the
  identical reason two builders cannot each mint their own sequence number — the lock has
  one holder because the resource has one history, not because Kemi personally must type
  the number.

### 2.3 A registry write is the same kind of operation ADR-0002 already governs

Assigning a sequence number is a write to shared, tenant-independent infrastructure state,
gated by holding a named lock first — the identical shape as ADR-0002's tenant-scoped
repository writes, one level up the stack. The parallel is deliberate: neither rule is
enforced by asking a caller to be careful: both are checks the code and the process refuse
to proceed without.

### 2.4 A registry is a handle, minted once, never reconstructed — not a shape any caller can produce (BIO-ARC-005-QA-01)

Round 1 of `assignSequence` computed `nextSequence` directly from the `registry` argument's
own `.records` content on every call: a caller reusing a stale reference from before an
earlier assignment got the identical `nextSequence` back both times. Round 2 fixed that by
tracking authoritative state in a `WeakMap` keyed by the registry **object's identity**,
trusting a reference's content only the first time that exact object was seen.

That fix was still wrong, for a reason the identity approach cannot see past: two
**independent deserializations of the identical persisted state** —
`JSON.parse(JSON.stringify(registry))`, called twice — are two different object identities.
Neither has been "seen" before, so both are trusted as fresh, correct starting points, and
both compute the same `nextSequence`. This is not an exotic input. It is the ordinary shape
of a real caller reloading a persisted registry from disk twice — exactly the situation a
real migration tool sits in front of, and object identity was never actually standing in for
*registry* identity; it was standing in for *reference* identity, which a JSON round-trip
trivially defeats without needing to be adversarial about it.

**There is no fix to "which snapshot do I trust" that works, because a snapshot — any value
reconstructible from `.records` content alone — is not a thing this module can tell apart
from a forged or merely-reloaded one.** The only sound fix is to stop accepting snapshots as
assignment authority at all. `createMigrationRegistry(...)` is now the sole way to
mint a registry **handle**: it brands the returned object with a `Symbol` this module never
exports, and records its authoritative state in a `WeakMap` keyed by that exact object.
(Its exact signature — a required `registryId` — is Round 4's fix, below.)
Nothing outside this file holds a reference to the brand `Symbol`, so nothing outside this
file can construct an object carrying it — not by guessing its shape, not by copying every
enumerable field of a real handle, not by a JSON round-trip (`Symbol` keys do not survive
`JSON.stringify` in the first place). `assignSequence` calls `assertRegistryHandle` before
touching anything else and refuses a plain object, a reconstructed snapshot, or any other
non-handle outright, with `invalid_registry_handle` — it does not silently adopt an unbranded
object's `.records` as a fresh lineage, which is precisely the mistake both Round 1 and
Round 2 made.

A real caller now reads the persisted registry **once** per assignment session, passes it to
`createMigrationRegistry`, and holds that one handle for every `assignSequence` call in the
session — exactly what "one held lock session" (Decision 2.2) already required of a correct
caller.

**Round 3's own scope claim was wrong, and it is worth recording exactly how.** It stated:
"there is no longer a way to accidentally hold two independent, unsynchronized views of the
same registry, because there is no longer a way to construct an authoritative view except by
minting a genuinely new, empty-until-composed one." That sentence's own second half names
the gap it failed to see the consequence of: `createMigrationRegistry` is a public factory,
callable as many times as anyone likes. Two calls to it — each perfectly legitimate on its
own, each producing a real, correctly-branded handle — mint **two independent authorities**
for what a caller believes is the one real migration-core history. Object identity, even
*branded* object identity, was never registry identity; it was still only ever reference
identity, one constructor call removed from Round 2's mistake rather than free of it.

**The actual requirement, named precisely: at most one live authority may ever exist for a
given real registry, for the life of the process — not a way to make two references resolve
to the same in-memory object, a way to make a SECOND authority impossible to mint at all.**
`createMigrationRegistry` now takes a required `registryId` — a real identifier for *which*
registry this is — and keeps its live handles in a `Map` keyed by that id, which outlives any
single call. The first call for a given id mints the one authority, seeded from
`initialRecords`. Every later call for that SAME id, from anywhere in the process, returns
that exact SAME object; `initialRecords` is ignored on a re-open, because a second seed for
an already-live authority is not a second source of truth, it is a stale one competing with
the real one. Two calls therefore cannot mint two authorities for the same real registry,
structurally — there is only ever one live entry per id. In production there is exactly one
legitimate `registryId`: reuse `SEQUENCING_CONFLICT_GROUP`, reflecting the premise this ADR
opens with, that there is exactly one migration-core history. Distinct ids exist so that
independent scenarios *in the same test process* do not collide with each other — a testing
concern this module must accommodate, not a second production registry this ADR endorses.

**Scope, stated precisely so it is not overclaimed a second time:** this makes it
structurally impossible, within a single Node process, for two calls naming the SAME
`registryId` to ever produce two live authorities — closing exactly the gap Round 3's
identical-sounding claim did not. It does not, and cannot, prevent two **separate Node
processes** each calling `createMigrationRegistry` against the same persisted file; that
guarantee is, and remains, `automation/locks/migration-core.lock.json` (Decision 2.1), which
this ADR still does not duplicate or reimplement. A pure, dependency-free, offline contract
module (`contracts/README.md` rule 10) has no standing to observe another process's lock
file, so it is not this function's job to substitute for one. `lockHeldBy` and the
registry-id-keyed handle are two independent, complementary checks; neither is a stand-in
for the other.

**That scope claim was overclaimed a second time anyway, and it is worth recording exactly
how, because it is a genuinely different mistake from Rounds 2 and 3's, not a repeat of
them.** "Two calls naming the SAME `registryId`" was the wrong condition to guard: nothing
stopped `openMigrationRegistry('migration-core')` and
`openMigrationRegistry('migration-core-alias')` — two DIFFERENT ids, each individually valid
— from being opened over identical persisted content, each becoming its own authority. A
caller-namespaced `Map` only closes "the same key mints one authority"; it does nothing
about "which key is authoritative" when the caller is the one choosing the key. Rounds 2-4
all made the same category of error — asking this module to recognise "the same registry"
from something the caller supplies (an object, then an object's identity, then a
caller-chosen name) — and every one of those signals is, definitionally, something the
caller controls and can therefore vary without the module having any way to tell.

**The only closure that does not repeat this: remove the caller's ability to choose at all.**
`createMigrationRegistry` is retired from the production surface. `openMigrationRegistry()`
takes no id — none — and always resolves to the one hardcoded id this module owns,
`SEQUENCING_CONFLICT_GROUP`. There is no longer a parameter for a caller to alias, correctly
or by mistake. `registryId` survives only on `createMigrationRegistryForTests`, an
unmistakably, separately-named export `contracts/README.md` never documents for production
use — test isolation needs distinct ids so unrelated scenarios in one process do not
collide; production needs exactly one caller-inaccessible id, which is what it now has.

## Decision 3 — Forward-safe, rollback and repair rules (BIO-ARC-005-AC03)

### 3.1 Forward-safe reuses ADR-0004's additive-vs-breaking split, one layer down

A migration is **forward-safe** if applying it does not require every consumer of the
database to change behaviour at the same instant — the identical additive-versus-breaking
distinction ADR-0004 §1.3 already makes for the API surface, applied here to schema shape
instead of response shape:

- **Additive** (forward-safe on its own): a new table, a new nullable or defaulted column,
  a new index built without locking readers out. Old and new code both keep working.
- **Breaking** (never forward-safe as a single step): dropping or renaming a column or
  table, tightening a constraint on existing data, changing a type in a way old readers
  cannot parse. A breaking change is only permitted as the **second** of two separately
  numbered migrations: an additive step first (introduce the new shape alongside the old,
  with a documented deprecation window matching ADR-0004 §1.3's `Deprecation`/`Sunset`
  pattern), and a destructive step later, once nothing depends on the old shape. A record
  marked `breaking` must declare which earlier record's deprecation window it depends on;
  one that cannot is refused, not merely warned about.

### 3.2 Every record is reversible, or explicitly and honestly not

A migration record declares exactly one of:

- a genuine `down` script that undoes its `up` exactly — for additive-only changes, this is
  usually mechanical (drop what was added); or
- `reversible: false` with a required `repair_procedure` — for a migration where a true
  rollback would destroy data a rollback should never destroy (nothing honest can "roll
  back" the loss of already-written rows). The repair path is explicit: something wrong is
  corrected by a **new, forward, numbered migration**, never by reversing history.

There is no third option and no silent default. A record with neither is refused at
construction — the same posture ADR-0003 §3 takes toward an unclassified field: absence of
a stated answer is never treated as the safe answer.

### 3.3 A deprecation dependency must reference a record that actually opened that window (BIO-ARC-005-QA-03)

Round 1's check that a breaking record's `depends_on_deprecation_sequence` was satisfied
asked only "does a record exist at this sequence position?" That is not the claim Decision
3.1 makes. A breaking migration depends on an **earlier deprecating record's window** — a
specific, deliberate act of opening a deprecation window for something, matching ADR-0004
§1.3's `Deprecation`/`Sunset` pattern — not on whatever happens to occupy that numeric slot.
Any unrelated additive record assigned earlier in the registry satisfied the old check by
coincidence of position, which is not evidence the dependency it claims is real.

An additive record that deliberately opens a deprecation window now says so, explicitly, in
`forward_safety.deprecation_window.target` — naming what it is deprecating. A breaking
record's `depends_on_deprecation_sequence` is only accepted if the record at that sequence
position both exists **and** carries this marker. A record without it — however real,
however correctly assigned — is not a relevant dependency, and assignment refuses it with
the same `missing_deprecation_dependency` code as a dependency that does not exist at all:
from a breaking migration's perspective, a sequence position that was never a deprecation
window is exactly as unusable as one nobody ever assigned.

This is intentionally scoped to "the referenced record self-declares as a deprecation
window," not to verifying that what it names matches what the breaking migration removes
word-for-word — that would require this contract to parse `up`/`down` script bodies, which
are opaque strings to it by design (Decision 1.1). Confirming the target names line up is a
human review responsibility at assignment time, the same as confirming `up` and `down`
actually do what they claim.

### 3.4 An assigned record is immutable

Once a sequence number is assigned, the record's content is frozen — the identical
append-only posture ADR-0001 §3.3 and ADR-0003 already apply to audit and domain events,
for the identical reason: a history that can be quietly edited after the fact is not a
history anyone can trust to reconstruct what actually happened. A mistake discovered later
is corrected by a new record, numbered after it, never by rewriting the original.

### 3.5 A deprecation dependency must name the SAME target the referenced window opened, not just self-declare as *a* window (BIO-ARC-005-QA-03)

Decision 3.3 requires the referenced record to have declared a `deprecation_window` at all —
closing the "any record occupying that sequence position counts" gap. It does not, on its
own, stop a *different* window from authorising the dependency: a record declaring
`deprecation_window: { target: "person.legacy_email" }` satisfied 3.3's check for a breaking
migration that names `depends_on_deprecation_sequence` pointing at it, **even when that
breaking migration drops something else entirely** — `credential.secret`, unrelated to
`person.legacy_email` in every way except the coincidence of a shared sequence reference. A
window opened for one target is not standing permission to break a different one; "self-
declares as a deprecation window" and "is the window THIS migration's removal depends on"
are still two different claims, and 3.3 alone only checked the first.

A breaking record now names what it removes explicitly, in `forward_safety.breaks`. The
dependency is only honoured when the referenced record's `deprecation_window.target` and the
breaking record's `breaks` are the SAME string — an exact, canonical match, not a fuzzy or
partial one. `breaks` is deliberately **not** required at proposal time (Decision 3.1's
proposal/record split keeps a proposal provisional); its absence is refused at *assignment*
instead, with the identical `missing_deprecation_dependency` code a nonexistent or
wrongly-typed dependency already used — an unnamed target can never equal a real one, so
omitting it is exactly as unassignable as naming the wrong one, by construction, not by a
special case written to catch the omission.

This is still, deliberately, a name comparison, not a semantic one: this contract does not
parse `up`/`down` bodies (Decision 1.1) and cannot verify that dropping `credential.secret`
is actually what a migration claiming `breaks: "credential.secret"` does. Confirming the
declared name matches the migration's real effect remains a human review responsibility at
proposal and assignment time — this closes the gap where the target was never compared *at
all*, not the gap where a proposer could still lie about what their own migration does.

## Decision 4 — Fresh-create and upgrade-from-previous tests are mandatory (BIO-ARC-005-AC04)

### 4.1 Two ways to reach the same state, both proved equal

Every record ships with two proofs, both mandatory before assignment, not optional
afterthoughts:

- **Fresh-create**: applying every record from sequence 1 through N against an empty
  database produces a specific, asserted schema state.
- **Upgrade-from-previous**: applying records 1 through N−1, snapshotting, then applying
  record N alone, produces the **identical** state fresh-create reached at N.

A migration that only has a fresh-create test has only ever been proved correct starting
from nothing — which is not the situation any real tenant database is ever actually in by
the time record N ships. The upgrade path is what every existing tenant will actually take,
and it is the one a fresh-create-only test cannot catch a defect in: an `up` script that
happens to work against an empty table but assumes something about existing rows a real
upgrade would violate.

### 4.2 What "mandatory" means here

A proposal without both proofs attached is not eligible for sequence assignment — this is
enforced the same way a missing `Idempotency-Key` on a required write fails closed in
ADR-0004 §4.2, not as a review comment to address later. `verifyFreshCreateMatchesUpgrade`
is the shared check both paths run through, so "fresh-create" and "upgrade" can never be
validated by two different pieces of logic that quietly drift apart from each other.

### 4.3 The upgrade proof must start from a real prior state, or it proves nothing

Folding records 1 through N−1 over the same synthetic empty state fresh-create starts
from gives the upgrade path exactly the intermediate value fresh-create already passes
through on its way to N. For a deterministic migration script, that makes the two paths
structurally unable to disagree — a check built that way cannot fail, whether or not the
migration is actually upgrade-safe, which is not a proof, it is a tautology wearing one.

`verifyFreshCreateMatchesUpgrade` therefore **requires** a real `priorState` — not a
synthetic replay from empty, but a state reflecting what an actual tenant that has been
running since record 1 looks like, including whatever a synthetic replay cannot represent:
accumulated row data a computed default, a backfill, or a tightened constraint could behave
differently against. Omitting it is refused as `missing_prior_state`, because the situation
Decision 4.1 exists to test for is exactly the one a tautological check cannot see.

### 4.4 A failed proof refuses assignment; it does not merely get reported (BIO-ARC-005-QA-02)

Round 1's `verifyFreshCreateMatchesUpgrade` returned `{ matches: false, ... }` on a genuine
mismatch instead of refusing outright, and `assignSequence` never called it at all — a
proposal's `proofs` were checked only for presence (`assertProofs`: two non-empty strings),
never for agreement. A caller could attach two arbitrary, unrelated strings, or run the
verifier, see `matches: false`, and proceed to request assignment anyway; nothing between
that verdict and a durable record enforced it. A check whose result a caller can silently
ignore is not a gate, it is a comment.

Two independent, complementary closures, matching the pattern ADR-0004 §4.2 already uses
for a missing `Idempotency-Key`:

- `verifyFreshCreateMatchesUpgrade` now **throws** `proof_mismatch` on a genuine hash
  disagreement instead of returning a value describing one. The one place this check runs
  is the one place a failure can be silently dropped, so it fails closed there.
- `assignSequence` independently requires `proofs.fresh_create_state_hash` to equal
  `proofs.upgrade_from_previous_state_hash` before allocating a sequence number, refusing
  with the identical `proof_mismatch` code otherwise. This does not re-run the caller's
  `applyUp`/`hashState` — `assignSequence` is not given those, by design (Decision 1.1: a
  proposal's `up` is opaque to this contract) — it is the minimum assignment-time
  enforcement that a proposal whose two attached proofs simply disagree, whether or not
  they were ever run through the real verifier, cannot reach a durable record. Running the
  verifier and attaching hashes it never actually produced is still possible for a
  proposer determined to lie to this contract; it is not possible to attach two
  self-contradictory hashes and have them accepted.

### 4.5 The module runs the check itself; it does not accept a claim to have run it (BIO-ARC-005-QA-02)

Decision 4.4's equality check closed the case of two proofs that *disagree*. Round 3's fix
closed the easier attack that leaves open — two arbitrary EQUAL strings — by requiring
`proofs` to carry `matches: true` and three SHA-256-hex-format hashes, on the theory that
this was "the shape that only a caller who actually ran the real check would produce by the
ordinary course of running it."

**That theory was wrong, and it is worth recording exactly why, because the same shape of
mistake produced Round 2 and Round 3's QA-01 fixes too.** A shape is data. Data that this
module checks for is, by definition, data an external caller can construct, because the
check that recognises it is sitting in a file anyone with the repository can read. A
determined caller does not need to run `verifyFreshCreateMatchesUpgrade` to produce
`{ matches: true, fresh_create_state_hash: 'a'.repeat(64), ... }` — they need to read this
file once. "The two proofs are shaped like a real result" and "the two proofs ARE a real
result" are still different claims, one level further out than Round 2's mistake, not a
different kind of mistake.

**There is no shape check, however elaborate, that closes this — the fix has to change WHO
computes the proof, not WHAT the proof looks like.** `assignSequence` no longer accepts
`applyUp`/`hashState`/`priorState` as something a proposer ran elsewhere and reports the
result of. It accepts them as its OWN required inputs (`emptyState` optional, defaulting to
`{}`) and runs the identical fresh-create/upgrade-from-previous computation
`verifyFreshCreateMatchesUpgrade` performs — over the real candidate record, against the
real registry, inside the assignment call itself, before anything is durably written. This
is the literal "run verification inside the assignment boundary" alternative: there is
nothing left for a caller to forge, because nothing a caller hands in is trusted as the
proof — the proof is computed, synchronously, by this module, in this call, from inputs the
caller cannot pre-arrange to lie through (the fold runs over the CANDIDATE record `assignSequence`
itself just built, not a record the caller assembled). Omitting `applyUp`/`hashState`
refuses outright as `verification_required` — there is no path to assignment that skips
running the real check, because running it is no longer optional input validation, it is
what assignment now IS.

`proposal.proofs` — the two hash strings a proposer attaches to their portable, JSON
proposal file — still exists, and is still required at proposal time (Decision 4.2). Its
role changes: it is no longer the safety gate, because Round 3 proved no gate built from
caller-supplied data can be. It is cross-checked, after the real computation, against what
assignment actually computed: a mismatch there does not mean the migration is unsafe (the
real computation, run moments earlier in the SAME call, already proved it converges) — it
means the proposer's own record of their proof disagrees with reality, which is worth
surfacing under its own distinct code, `proof_declaration_mismatch`, separate from
`proof_mismatch` (which means the migration itself does not converge).

**Why this is architecturally different from, not just stricter than, Round 3:** a
Symbol-branded "verified" token — the obvious next escalation from shape-checking, and the
one considered and rejected here — cannot survive the propose-then-hand-to-Kemi JSON
handoff Decision 1.1 already establishes as how a proposal reaches assignment; `Symbol` keys
do not serialize, and a proposer and an assigner are not guaranteed to share a process.
Fusing the check into assignment sidesteps that entirely: there is no receipt to transport,
because the checking and the assigning are now one atomic act, performed by whoever holds
the lock, at the moment they hold it — which is exactly where ADR-0005 has located every
other piece of assignment authority from Decision 1.2 onward.

### 4.6 What this module can and cannot prove about a supplied runner — a scope boundary, not a further round of fixing (BIO-ARC-005-QA-02, round 4 re-review)

Round 4's fusion closed "a caller can forge a proof shape." It did not close, and cannot
close, the next thing tried against it: `applyUp: (state) => state` — a runner that ignores
the candidate record entirely — makes fresh-create and upgrade-from-previous converge
trivially (both paths leave state untouched), so a migration whose `up` literally reads
`"THIS MIGRATION BODY IS NEVER EXECUTED"` still assigns. This is real, and it is not another
instance of the same shallow-fix shape as Decisions 2.4's three rounds — it is a genuinely
different kind of gap, and it is worth being precise about why.

**Every prior round of this ADR's QA-01/QA-02 history was a case of this module checking
something weaker than the guarantee it claimed, where a stronger check was available and
simply hadn't been built yet — object identity when a registry-id singleton was available;
a proof shape when running the real computation was available.** This one is different: the
stronger check — confirming that a supplied `applyUp`/`hashState` pair is a FAITHFUL
interpreter of an opaque `up`/`down` script — is not something this module can build at
all, not this round, not with more effort, without changing what this module fundamentally
is. `up` is opaque to this contract **by design** (Decision 1.1: "a real SQL/DDL body ... or
an equivalent script reference," never a form this contract parses or executes). Proving an
interpreter faithfully executes arbitrary opaque SQL/DDL requires actually running it,
against a real database, and comparing real, introspected schema state — infrastructure no
story in this series has built, and building it here would not be "closing QA-02," it would
be building a migration execution engine inside a story titled "define migration ownership
and sequencing policy."

**The guarantee `assignSequence`'s fused verification actually provides, stated precisely
so it is never overclaimed again:** IF the supplied `applyUp`/`hashState` are a faithful
interpreter of the candidate migration, assignment proves fresh-create and
upgrade-from-previous converge before writing the record — a real, non-bypassable
computation, not a shape a caller can fake. This module does not, and structurally cannot,
verify the "IF." Choosing a trustworthy `applyUp`/`hashState` at assignment time is Kemi's
own responsibility, the identical shape of trust this ADR already places in `lockHeldBy`
and `assigned_by` — and the SAME record already carries that accountability:
`assigned_by: 'Kemi'` names who supplied the runner at the exact call that ran it, on the
permanent, immutable record (Decision 3.4).

**This is recorded as an accepted, explicit scope boundary, not a defect carried forward
silently.** A future story that gives this platform a real migration execution or schema
introspection capability is exactly where a stronger check — one this module cannot honestly
build today — would plug in, at the same `applyUp`/`hashState` seam `assignSequence` already
exposes.

**Accepted costs**

- Every feature team writes migrations against a two-artifact process (propose, then wait
  for assignment) instead of just committing a numbered file themselves. Accepted: that
  wait is what the single history actually costs, and it is cheaper than reconciling a
  forked one.
- A breaking schema change always costs two migrations and a deprecation window instead of
  one. Accepted, for the same reason ADR-0004 §1.3 accepts it for API versions: the
  alternative breaks every consumer that has not moved yet, at a moment nobody chose.
- Requiring both fresh-create and upgrade-from-previous proofs is more work per migration
  than testing one path. Accepted: the upgrade path is the one every real tenant actually
  takes, and it is exactly the path a fresh-create-only proof cannot validate.

**Obligations on later stories**

- Write a proposal against `migration-proposal.schema.json`; never write a numbered record
  yourself, however confident you are about which number is next.
- A breaking migration names the exact target it removes (`forward_safety.breaks`),
  matching an earlier record's declared `deprecation_window.target` (Decision 3.5).
- Every record carries a `down` script or an explicit `reversible: false` plus
  `repair_procedure` — never neither.
- Attach `proofs` from an actual `verifyFreshCreateMatchesUpgrade` run before proposing, as
  a self-check — but know that assignment does not trust it: `assignSequence` runs the real
  fresh-create/upgrade-from-previous computation itself, over `applyUp`/`hashState`/
  `priorState` supplied at assignment time, and refuses to proceed without them
  (Decision 4.5) — and know that this proves convergence conditional on that runner being a
  faithful interpreter of the migration, a fact this module cannot itself verify
  (Decision 4.6). Choose that runner with the same care `lockHeldBy`/`assigned_by` already
  demand elsewhere in this ADR.
- Load the persisted registry once per assignment session through `openMigrationRegistry()`
  — it takes no id, and always resolves to the one real migration-core authority — and hold
  that one handle for every call in the session. `createMigrationRegistryForTests` exists
  for test isolation only; production code never imports it (Decision 2.4).

## Alternatives rejected

- **Each feature team owns its own migration counter, reconciled at merge time** — is
  exactly the forked-history failure this ADR exists to prevent, deferred to the moment it
  is hardest to fix.
- **A proposer computes `max(sequence) + 1` itself** — moves the race this ADR closes up
  one level instead of closing it; two proposers can compute the identical "next" number at
  the identical moment.
- **Splitting assignment across multiple trusted helpers for throughput** — rejected in
  Decision 2.2 by name: the lock has one holder because the resource has one history, not
  because of who is trusted to hold it.
- **Skipping the upgrade-from-previous proof when a migration is "obviously additive"** —
  the exact judgement call ADR-0003 §3 already distrusts applied to schema changes; "obvious"
  is not a proof, and it is precisely the migration nobody scrutinised that turns out to
  assume something about existing rows.
- **A single reversibility rule for every migration ("always write a down script")** —
  produces down scripts that quietly discard data no rollback should ever discard, which is
  a worse outcome than an honest `reversible: false` and a real repair procedure.
- **Building a migration execution/verification engine inside this story so `assignSequence`
  can independently confirm a supplied `applyUp`/`hashState` faithfully interprets an opaque
  `up`/`down` script (Decision 4.6)** — rejected as out of this story's scope, not as
  infeasible in principle. `up` is opaque to this contract by design (Decision 1.1); proving
  a runner faithfully executes arbitrary SQL/DDL requires actually running it against a real
  database, which is production migration *infrastructure*, categorically different from
  every other artifact in this ADR series (a rule plus a tested reference implementation).
  Building it here, to close one review finding, would smuggle a database dependency and a
  runtime execution engine into an architecture-definition story under cover of a bug fix.
  Accepted instead: the narrower, honestly-stated guarantee in Decision 4.6, and an explicit
  seam (`applyUp`/`hashState`) a later, dedicated story can occupy.
