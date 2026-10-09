# ADR-0003 — Audit, evidence and domain-event envelopes

- **Status:** Accepted (BIO-ARC-003, wave W01)
- **Date:** 2026-09-21
- **Owner:** Kemi — Chief Architect & Integrator
- **Story:** BIO-ARC-003 — Define audit, evidence and domain-event envelopes
- **Builds on:** [ADR-0001](ADR-0001-canonical-identifiers-clocks-and-versioning.md), [ADR-0002](ADR-0002-tenant-context-and-database-routing.md)
- **Amends:** ADR-0001 §3.6 (adds `aggregate_version` to the event envelope) and ADR-0002 §5 (the denial event becomes a profile of the audit envelope). Both amendments are recorded in §6 below.
- **Change policy:** shared contract. Freezes at W01 exit; changing it needs a superseding ADR and recorded Kemi approval.
- **Post-freeze amendment (Decision 7, snapshot 1.1.3):** added 2026-09-21/22 under BIO-ARC-006-AC04's change-control process, after Seyi's BIO-TEN-001 review and an architecture review triggered by god (W03.1) found the same real gap independently: this ADR fixed audit ordering for a denial (ADR-0002 Decision 5) but never stated the rule for an allowed, successful mutation. Went through five real BIO-TEN-001 review rounds plus a parallel BIO-DEV-001 finding before landing (snapshots `1.1.0` through `1.1.3`, each prior one superseded, none edited in place — see Amendment history in `docs/architecture/SHARED-CONTRACT-FREEZE.md`) — `1.1.0`/`1.1.1` each presented a shape later proven insufficient; `1.1.3` records the final pattern, with each element's actual confirming review cited individually in the Decision text (BIO-TEN-001 round 5, `evidence/features/bio-ten-001/codex/REREVIEW-ROUND5.md`, reviewer commit `03086c1`, for the lock/audit-before-commit/`ExistsAsync`-as-pattern/crash-boundary elements; BIO-DEV-001 round 3, `evidence/BIO-DEV-001/codex/REREVIEW-ROUND3.md`, reviewer commit `1e8eccb`, for the `CancellationToken.None` element specifically). Kemi's approval recorded here; impact review, regression suite and the new snapshot are in `governance/contract-freeze/v1.1.3/`.
- **Post-freeze amendment (Decision 8, snapshot 1.1.11, confirmed at 1.1.12):** added 2026-10-07 under BIO-ARC-006-AC04's change-control process, after Seyi's BIO-OPS-002 round-3 review (`SY-OPS002-05`, evidence/features/bio-ops-002/codex/REVIEW-Seyi-round3.md) found that BIO-TEN-002's demo tenant/role context-change audit reopens Decision 7's residual-acceptance condition: the audited effect (a signed, HMAC-verified session cookie held by an independent Next.js process and browser) outlives the API's own in-memory audit queue across a restart, so the two sides of the pair are not same-lifetime. Kemi's ruling, requested by god: this is a real reopening by Decision 7's own text, not a defect to re-patch — closed as a narrow, explicitly scoped exception naming exactly this mechanism, not a general relaxation. Kemi's approval recorded here; impact review and the snapshot are in `governance/contract-freeze/v1.1.11/`. **Independent review (AC04 step 6) is confirmed by Zainab** (`evidence/BIO-ARC-006/codex/ADR-0003-DECISION8-CONFIRMATION-ZAINAB.md`, reviewer commit `0ad4339`): the text accurately records the reopened boundary without claiming technical atomicity, the three bounding constraints and the non-precedent language both hold, and the later shared `InMemoryAuditSink` replacement for the historical `InMemoryDemoSessionContextAuditSink` the review names carries the identical process-local volatility, so the reasoning stays current.

## Context

Audit is the control that every other control falls back on. The threat model lists audit
destruction as a primary threat, the spec requires immutable audit (§13), and ADR-0002
already makes a denial undeliverable unless its audit record was written first.

What did not exist until now is **one shape**. ADR-0002 emits a tenant-access denial event
with its own field names; ADR-0001 sketched a domain-event envelope as part of the
identity and clock work. Left alone, every feature team would add a third and a fourth,
and a query like "everything that touched this person last week" would have to know all of
them. This ADR defines the envelopes once and makes the existing two conform.

## Decision 1 — The audit envelope (BIO-ARC-003-AC01)

Every audited action, allowed or denied, is recorded as one append-only audit event:

| Field | Meaning |
|---|---|
| `event_id` | Canonical `evt_` identifier (ADR-0001 §1) |
| `schema_version` | Shape of this envelope |
| `tenant_id` | Tenant the action was performed in; `null` only for control-plane actions |
| `actor` | `{ type, id }` — `user`, `service_account`, `device`, `system` or `anonymous` |
| `device` | `{ device_id, workstation }` where the action came from a station or reader. **Required**, and explicitly `null` otherwise, so "no device" and "nobody recorded the device" stay different answers |
| `action` | Namespaced verb, e.g. `credential.activate`, `tenant.access`, `person.export` |
| `target` | `{ type, id }` — what was acted on |
| `outcome` | `ALLOWED`, `DENIED` or `ERROR` |
| `reason_code` | Machine code; the human-readable reason never replaces it |
| `correlation_id`, `causation_id` | Request and cause chain. Both **required**; `causation_id` is explicitly `null` for a root event |
| `occurred_at`, `received_at` | Claim and authority (ADR-0001 §2.4) |
| `classification` | Highest classification of anything the event names (Decision 3) |
| `withheld` | Names — never values — of fields deliberately not carried (Decision 3) |

**`actor` is a structured value, not a free string.** A bare name cannot distinguish the
user `sync-worker` from the service account `sync-worker`, and an audit trail whose subject
is ambiguous cannot answer the only question it is asked.

**Both `outcome` and `reason_code` are required, including on success.** An audit that
records only failures cannot show that something was permitted, and "no record" then means
either "did not happen" or "happened and was allowed" — the two answers that must never be
confusable when investigating.

**Audit events are append-only** (ADR-0001 §3.3): no `version`, no updates, no deletion.
They carry no payload — see Decision 3.

**`causation_id` is required here too, for the same reason it is required on the domain
event (Decision 2): an unstated cause and no cause are different facts, and a field that
may be omitted cannot tell them apart.** Leaving the audit envelope's own `causation_id`
optional while the domain-event's was required and nullable would have been the same
enforced-one-step-short pattern this ADR exists to close, one field over from where it was
just closed — so both envelopes hold the field to the identical rule.

### Audit ordering carries forward from ADR-0002

ADR-0002 §5 established that a denial is not returned until its audit record has settled,
that the sink is a hard dependency, and that a rejected or hung write fails closed with
`audit_unavailable` (default 5 s). **That ordering applies to every audited effect, not
only denials**: the record is durable before the effect is acknowledged. Anything that
writes audit therefore awaits it, for the same reason denying does.

## Decision 2 — The domain-event envelope (BIO-ARC-003-AC02)

The envelope from ADR-0001 §3.6 stands, with two additions:

- **`aggregate_version`** — the version of the aggregate that this event describes.
- **`idempotency_key`** — see Decision 4.

`causation_id` is **required** and explicitly `null` for a root event, for the same reason
`device` is: an unstated cause and no cause are different facts, and a field that may be
omitted cannot tell them apart.

### On "version" in the acceptance criterion

AC02 lists both "version" and "schema version". They are different things, and one of them
looked at first like a conflict with ADR-0001 §3.6, which forbids a `version` field on an
event because an event is a fact and a `version` implies it can be updated.

The reading that satisfies both: the criterion asks for the **version of the aggregate the
event is about**, not a version *of the event*. That is a fact about the state the event
describes, so it does not make the event mutable. It is therefore recorded as
`aggregate_version`, and the prohibition on a bare `version` field stands unchanged — the
schema still refuses one. Recorded here rather than quietly implemented, because a later
reader will meet the same apparent contradiction.

`aggregate_version` is what lets a consumer order two events about the same aggregate
without trusting a clock, which ADR-0001 §2.4 forbids for security-relevant ordering.

## Decision 3 — Classification and redaction (BIO-ARC-003-AC03)

### Five levels

| Level | Examples | May appear in an envelope |
|---|---|---|
| `public` | Tenant display name, campus name | Yes |
| `internal` | Device serial, policy id, job id | Yes |
| `personal` | Name, institutional identifier, photo reference, date of birth | Only through an allowlist |
| `sensitive` | Biometric metadata, document evidence fields, access decisions about a person | Only through an allowlist |
| `secret` | Biometric templates, raw biometric images, card and signing keys, passwords, tokens | **Never** |

### Secrets are absent, not redacted

A `secret` value is never copied into an envelope in any form — not masked, not truncated,
not hashed. Redaction implies the value was there and was covered up; for these fields the
requirement is that it was never carried. The field **name** may appear in `withheld`, so
the record is honest about what it did not keep without keeping it.

### Redaction is an allowlist, never a denylist

An envelope is built by projecting the source object through a list of fields explicitly
permitted for that action. Anything not named is withheld.

**The allowlist is a list of paths, and it projects recursively.** `card` does not permit
`card.template`. A container is descended into only when its own path is allowlisted, and
it confers no permission on its children: every leaf is decided on its own path. Anything
not named is withheld, at any depth.

This is the whole rule, and the reason is the hostile question: *what would a caller have
to control for this to be false?* Under a denylist, the answer is "the field names" — and
the caller controls those completely. A new field added by a later story, or a nested
object, or a misspelling of a blocked name, all pass a denylist untouched. It fails open on
exactly the change nobody remembers to review. An allowlist fails closed on the same
change: a new field simply does not appear, and someone notices the gap in the record
rather than the leak in it.

A shallow allowlist fails the same question one level down: the caller nests the secret.
Permitting `card` and copying the object wholesale hands over `card.template` and every
field a later story adds beneath it. So the projection recurses, and four more cases fail
closed with it: an array holding anything but primitives is withheld outright (an object
array in an audit detail is a shape nobody needs), an array of primitives is **copied**
rather than referenced, a cycle or excessive depth is withheld, and a key that can reach
`Object.prototype` is never projected whatever the allowlist says.

### A dot-joined path is a caller-controlled string, and strings can be forged to collide

Recursion introduced its own version of the same hole (QA-04, Seyi re-review round 3): a
path is built by joining raw key names with `.`, and nothing stopped a raw key from
*containing* a literal `.` of its own. A flat field named `"profile.name"` and the nested
traversal `profile` -> `name` then joined to the identical string, so an allowlist entry
and a classification written with one location in mind silently governed the other too —
and since the source is caller-controlled, the caller decides which shape shows up.

Two things close this, and they close different halves of it:

- **Every raw key is escaped before it is joined.** `\` becomes `\` first, then `.`
  becomes `\.`, so an unescaped `.` in a path is always a real delimiter between two
  distinct keys, never a literal dot sitting inside one. This makes the two locations
  reachable by two different, non-colliding strings — `profile.name` for the nested value,
  `profile\.name` for a flat field literally named `profile.name`. It does not, by itself,
  stop a caller who supplies *both* shapes at once from picking whichever one the person
  who wrote the allowlist happened to name.
- **A raw dotted key that also resolves to a real nested value in the same object is
  withheld outright, on both sides, regardless of the allowlist.** If a source contains
  both `{ "profile.name": X }` and `{ profile: { name: Y } }`, splitting the flat key's own
  name on its literal dots and walking it from that same object finds `Y` — a live
  collision, not a hypothetical one. A classification map can only have been written with
  one of the two locations in mind; the shape of the caller-supplied data decides which one
  a naive implementation would honour, and that is not this contract's decision to make for
  them. Both the flat entry and the nested value it aliases are withheld, exactly the way a
  cycle or an unregistered classification is — an ambiguity the caller can manufacture is
  refused, not resolved by guessing.

The second rule only fires when a real collision exists. A flat key with a literal dot and
no competing nested structure still needs its escaped form named explicitly in the
allowlist to be emitted at all; nothing about detecting collisions relaxes that.

### An unregistered classification is not a classification

A field whose classification is missing, or is a string that is not one of the registered
levels, is **withheld** — not copied with an odd label attached. Two things go wrong
otherwise: the value escapes, and the unregistered string propagates into the envelope's
`classification`, where the published schema rejects it. A level nobody registered cannot
be reasoned about, so it is treated as the most restrictive, exactly like an unclassified
field.

### An audit event is immutable all the way down

Freezing the envelope and its `detail` object is not enough: a retained reference to a
nested object lets a caller change an already-built append-only record, add a field its
`classification` never saw, and race whatever an asynchronous sink was about to persist.
The envelope is therefore deep-frozen at construction, and the projection builds new
objects rather than borrowing the caller's, so nothing inside a built event is reachable
for mutation.

### Hashing is not redaction for low-entropy personal data

A hash of a date of birth, a national identifier, a staff number or a phone number is
reversible by anyone willing to enumerate the input space, which for each of those is
small. Publishing such a hash in an audit trail is pseudonymisation at best and is not
permitted to be described as redaction. Where a record must be correlatable without
carrying the value, it carries the canonical identifier of the record instead — which is
already non-secret, already meaningless outside its tenant (ADR-0002 §1.5), and already
the thing the rest of the system joins on.

### Classification is the maximum, and it travels

An envelope's `classification` is the highest level of anything it names. It never drops
below that of any field it carries, so a consumer can make a retention or export decision
from the envelope alone without re-deriving it — and re-deriving it is where consumers
disagree.

## Decision 4 — Events are idempotency-capable (BIO-ARC-003-AC04)

**The dedupe key is `(tenant_id, event_id)`.** Consumers must be idempotent (Event
Architecture); this is the key they deduplicate on.

A redelivery is classified by comparing a **content fingerprint**:

| Situation | Outcome |
|---|---|
| Unseen key | `FIRST` — apply, and record the key with its fingerprint |
| Seen key, same fingerprint | `DUPLICATE` — ignore; already applied |
| Seen key, different fingerprint | `CONFLICT` — reject as `event_id_reuse`; never absorb |

The third row is the one that matters, and again it comes from the hostile question.
`event_id` is minted by the producer, which at the edge is a device (ADR-0001 §1.3). A
producer that reuses an identifier with different content — by defect or deliberately —
would, under a key-only dedupe, have its second event silently dropped and the projection
left showing the first. That is a way to make a revocation disappear behind a replayed
identifier. Comparing content makes the collision loud instead.

**`received_at` is excluded from the fingerprint.** The server stamps it per delivery, so
including it would make every redelivery look like new content and defeat the dedupe
entirely. Everything else the producer asserted is included.

An `idempotency_key` may also be carried for the retry-prone writes ADR-0001 §3.4 lists;
it is scoped to tenant plus operation and follows the same replay-or-reject rule.

## Decision 5 — Evidence references, not evidence copies

Audit and domain events reference evidence — a document image, a biometric capture, a
production artifact — by canonical identifier. They never embed it.

Raw evidence is immutable and append-only (spec Data rules), stored under its own
retention and access policy. Copying it into an audit event creates a second copy on a
different retention clock, outside the access controls that governed the first, and a
retention purge that misses it silently fails. One copy, referenced.

## Decision 6 — Amendments to accepted ADRs

Both changes below are made by this ADR, with Kemi's approval as contract owner, while
W01 is still open. After W01 exit either would need a superseding ADR.

1. **ADR-0001 §3.6** — the domain-event envelope gains `aggregate_version` (required) and
   `idempotency_key` (optional). The prohibition on a bare `version` field is unchanged.
2. **ADR-0002 §5** — the tenant-access denial event becomes a **profile of the audit
   envelope**: it uses the canonical field names and adds one denial-specific field,
   `attempted_tenant_id`. Its guarantees are untouched — still written before the response,
   still carrying both tenants, still carrying no payload. What changes is only that it is
   now the same shape as every other audit record, which is what makes "show me everything
   that touched this tenant" a single query instead of a union of formats.

## Decision 7 — Audit is a hard dependency of every audited mutation, not only denials

ADR-0002 Decision 5 makes the audit sink a hard dependency of *denying* — the write is
awaited before the response, and a failed or missing sink fails the call closed rather
than let an unaudited refusal through. That rule was written for the denial path only. It
never said what an *allowed* mutation must do, and independent feature implementations
(BIO-TEN-001, BIO-DEV-001 — both built against this ADR, both in W03) each committed a
domain mutation first and awaited the audit write second: correct enough that the caller
never sees a false success, but wrong in a way Decision 5 already named the cost of — if
the audit write then fails, the mutation has already landed, committed, with no audit
record naming it.

**This Decision went through five real BIO-TEN-001 rounds plus a parallel BIO-DEV-001
finding before the pattern below was confirmed as actually closing it, each ruling out a
specific way to produce a false record rather than a hypothetical one. It is recorded here
in its final shape, with each element's actual confirming review cited individually below
— not every element was confirmed by the same review, and this Decision does not claim
otherwise. The history is kept because each ruled-out shape is a real trap the next
feature could otherwise re-walk into:**

1. **Commit-then-rollback (round 1) is not concurrency-safe.** Commit via compare-and-swap
   (CAS), roll back to the prior value on audit failure. Writer A commits, blocks in a
   failing audit write; writer B reads A's committed-but-unaudited value, makes its own
   change, commits a newer version; A's rollback CAS now conflicts with B's version and is
   silently swallowed — A's unaudited change survives permanently inside B's committed
   state, with no audit event ever naming it. An expected-version check on the rollback
   does not close this; it is the exact mechanism that swallows the rollback.
2. **Audit-before-commit alone (round 2/3) can durably claim success for a mutation that
   never commits.** Writing the audit event first closes round 1's race (nothing
   uncommitted is ever visible to another writer), but the event durably asserts
   `Outcome = Allowed` before the commit's CAS is known to succeed. If a genuinely
   concurrent writer of the SAME aggregate wins that commit, the audited writer's own call
   correctly rejects with `concurrency_conflict` — but its permanent audit event still
   falsely claims success.
3. **Required: an in-process, per-aggregate lock held across the whole read-validate-
   audit-commit sequence** (keyed by whatever id the mutation targets — tenant id, device
   id — via e.g. `ConcurrentDictionary<string, SemaphoreSlim>`, one shared instance per
   process). Only one mutation of a given aggregate can be in flight in this process at a
   time, so by the time a candidate's audit event is written, no other writer in this
   process could have raced ahead of it — the commit that follows is guaranteed to observe
   exactly the state the audit event already describes. This is a real substitute for "one
   atomic transaction" in an in-memory, single-process store — an aggregate-scoped lock,
   the same shape a real database gives a single row via row locking.
4. **The lock closes writer concurrency, not the two-store failure boundary.** An audit
   sink can durably persist a write and then throw only on acknowledgement (a timeout, a
   dropped connection after commit) — treating every throw as "definitely did not happen"
   can leave a durably-written success event with no corresponding commit, from a cause the
   lock cannot see. **Required for this tier: on a throw from the audit write, verify
   ground truth via a dedicated `ExistsAsync(eventId)` check before deciding** —
   confirmed-present is treated as success (the lock-protected commit that follows then
   makes the event's claim true); confirmed-absent throws `audit_unavailable` with nothing
   committed; if `ExistsAsync` itself throws, that is a genuine, total audit-store outage,
   the same fail-closed boundary Decision 5 already accepts for a missing or unreachable
   sink. This mirrors Decision 5's own framing exactly: "written" means the sink's promise
   has settled, not that the call was made. **Scope of what round 5 actually confirmed:**
   the concrete `InMemoryTenantAuditSink.WriteAsync` cannot append and then throw (it
   enqueues and returns `Task.CompletedTask` unconditionally), so this element was not
   exercised against a genuine append-then-throw case — it is the right pattern to have in
   place for this tier, not a general solution to durable-acknowledgement ambiguity.
   `ExistsAsync` cannot make two independently durable stores atomic if the verification
   call itself fails or lies; that case still needs the shared transaction/outbox named in
   element 6 once either store becomes genuinely durable.
5. **A caller cancellation can produce the identical false-success shape from a third
   cause.** If the commit call checks its `CancellationToken` before attempting its write,
   a cancellation arriving after the audit event is confirmed durable but before that check
   aborts the commit without ever attempting it — the durable success event now describes a
   change that never touched the store, via neither a race nor an ambiguous acknowledgement
   but the caller's own cancellation. **Required: once the audit event is confirmed durable
   (via 4 above), the commit call passes `CancellationToken.None`, not the caller's token.**
   Audit becomes this operation's genuine point of no return; only a real fault can stop the
   commit after that, never a mere cancellation signal. **Independently confirmed in
   BIO-DEV-001 round 3** (`evidence/BIO-DEV-001/codex/REREVIEW-ROUND3.md`, reviewer commit
   `1e8eccb`, PASS) — not BIO-TEN-001 round 5: at the commit BIO-TEN-001 round 5 actually
   reviewed (`1d1e261`), `TenantService` still passed the caller's token straight through
   to the registry. Tunde applied the equivalent fix to BIO-TEN-001 afterward (commit
   `a91d116`, matching hardening) but that specific commit has not yet had its own
   independent review.
6. **The one residual this pattern does not close, accepted as inert for today's concrete
   infrastructure tier, not merely disclosed:** a process failure between a *confirmed*-
   durable audit write and the immediately following commit. Closing this in general needs
   a durable write-ahead log/outbox spanning both stores — infrastructure that exists
   nowhere in this codebase. **The failure itself is not what is ruled out — a process can
   fail at any point.** What is ruled out at today's tier is the failure's bad consequence:
   a durable audit record surviving to describe a registry state that no longer exists.
   Every current audit sink and registry implementation is an in-process, same-lifetime,
   volatile singleton (verified directly, e.g. `InMemoryTenantAuditSink`/
   `InMemoryTenantRegistry`, both `AddSingleton`), so a process death removes both
   together — there is no reachable state in which a phantom durable audit record outlives
   the registry entry it describes, because neither survives the process that would need to
   lose only one of them. **This boundary reopens the moment either side of a feature's
   pair becomes durable, remote, independently transactional, or shared across
   processes** — at that point it needs a real shared transaction/outbox or an explicitly
   modeled attempt-then-finalize recovery protocol, not a restatement of this same argument.

**This does not require a durable, transactional store spanning both the domain repository
and the audit sink, and no story is asked to build an outbox pattern to close it** — every
required element above (lock, `ExistsAsync` verification, `CancellationToken.None`) is
cheap against today's in-memory, single-process stores. `audit_unavailable` remains what it
already meant under Decision 5: the failure to write an audit event, not a reason code any
event itself carries.

**Documentation precision, not a defect:** an audit-sink interface's own doc comment must
not call the abstraction "durable" when its only implementation is volatile in-memory
storage (flagged in BIO-TEN-001 round 5 review as a non-blocking forward obligation) —
describe what the concrete implementation actually provides, and name the later-story seam
that would make "durable" true, rather than asserting a guarantee this tier cannot give.

**Obligation on every later story, added to Consequences below:** any feature that mutates
audited state implements all five required elements above (or an equivalent that gives the
identical guarantee under concurrent writers, ambiguous acknowledgements, and caller
cancellation — commit-then-rollback and audit-before-commit-without-a-lock are neither)
before that mutation's story can be accepted, and explicitly names the crash/durability
boundary and its reopening trigger rather than presenting the pattern as unconditional. A
reviewer checks for all five the same way as any other Decision in this ADR — a disclosed
limitation that leaves a committed-but-unaudited or falsely-audited mutation reachable
through any of the first five is not a closure.

## Decision 8 — A named, scoped exception to Decision 7 for the demo-session context-change audit

**Trigger.** BIO-TEN-002's demo tenant/role context-change audit (`BIO-OPS-002-AC04`) records
that a browser's demo session switched tenant/role. Seyi's round-3 review (`SY-OPS002-05`)
found that the accepted session cookie — independently HMAC-verified and held by the
Next.js process plus the browser — survives an API process restart, while
`InMemoryDemoSessionContextAuditSink` does not. Decision 7's residual-acceptance (element 6)
is conditioned on both sides of an audited pair being same-lifetime, same-process, volatile
singletons; here they are not, so the boundary genuinely reopens, exactly as Decision 7's
own text says it must whenever either side becomes "durable, remote, independently
transactional, or shared across processes." This is not a defect to re-patch a further
round — Decision 7 names only two things that would fully close a reopened boundary: a
real shared transaction/outbox, or an explicitly modeled attempt-then-finalize recovery
protocol. Neither exists anywhere in this codebase yet (ADR-0005's real migration-backed
stores remain pending); building either for only this one mechanism, while every other
feature's own audit trail has the identical restart-loses-history property at today's
infrastructure tier, would be disproportionate engineering pulled ahead of that real work.

**Decision.** The context-change audit record for BIO-TEN-002's demo-session mechanism
(the endpoint at `POST /api/v1/tenancy/demo-session/context-change`, whatever audit sink
backs it) is an explicit, narrow exception to Decision 7's residual-acceptance condition,
not a general relaxation of it. Three facts make this acceptable for this mechanism
specifically, and would NOT generalize to a real credential, authorization, or
tenant-boundary audit trail:

1. **Nothing downstream makes an authorization decision off this record's presence or
   absence.** It is pure attribution/observability, the same safe-use category Decision 1
   and ADR-0006 Decision 3 already establish for the whole demo-session mechanism — a
   forged or lost attribution record misattributes who claims responsibility, it does not
   let anyone cross a privilege boundary they could not already cross.
2. **The audited effect has a bounded lifetime.** The session token this record describes
   carries its own expiry; the window in which the cookie can outlive the audit record that
   described issuing it is bounded by that expiry, not indefinite.
3. **The behavior being audited is itself the already-approved behavior.** Self-selecting
   any demo tenant/role is TD-WEB-PROVENANCE-BRIDGE's own sanctioned mechanism (Kemi's
   ruling); this audit record exists so an operator can see that a self-selection happened,
   not to gate whether it may happen.

**Scope, named explicitly so this is not read as a precedent.** This exception covers only
the one named mechanism above. Any future feature whose audited pair reopens Decision 7's
boundary for a real (non-synthetic) effect — a credential, a tenant grant, anything a later
authorization decision could depend on — must either implement Decision 7's named remedy
(a real transaction/outbox, or an explicit attempt-then-finalize protocol) or bring its own
separately justified exception to Kemi; "this audit lags its effect across a restart" is
never by itself sufficient reasoning to reuse this Decision.

## Consequences

**Accepted costs**

- An allowlist per audited action is more work than a denylist, and a forgotten field means
  a gap in the record. Accepted: a gap is visible and fixable; a leak is neither.
- Path-based allowlists are more verbose than field names — a nested field must be named at
  its full path. Accepted: that verbosity is the boundary doing its job.
- Requiring `outcome` and `reason_code` on successful audits makes successful records
  bigger. Accepted: the alternative is an audit that cannot prove something was allowed.
- Renaming the denial event's fields changes an accepted contract mid-wave. Accepted, and
  cheaper now than after feature teams consume it.

**Obligations on later stories**

- Emit audit through the shared builder; do not assemble an envelope by hand.
- Declare an allowlist for each audited action. No allowlist means no fields, not all of
  them.
- Consumers deduplicate on `(tenant_id, event_id)` and treat a fingerprint mismatch as a
  defect to surface, not a duplicate to drop.
- Reference evidence; never copy it into an envelope.
- Any audited mutation implements Decision 7's full pattern — per-aggregate lock,
  audit-before-commit, `ExistsAsync` verification on an ambiguous throw, and
  `CancellationToken.None` past the confirmed-audit point — and names the crash/durability
  residual and its reopening trigger explicitly. Commit-then-rollback and audit-before-
  commit without the other three elements are rejected, not accepted alternatives. A
  disclosed atomicity gap that leaves a committed-but-unaudited or falsely-audited mutation
  reachable through any element other than the one named residual is not an accepted
  limitation, it is an open finding.

## Alternatives rejected

- **A denylist of sensitive fields** — fails open on every field added after it was written,
  and the caller controls the field names.
- **A shallow allowlist** — the same failure one level down, reached by nesting the secret.
- **Copying an unregistered classification through with its label** — leaks the value and
  puts a level in the envelope that the published schema rejects.
- **A shallow freeze** — leaves an append-only record editable through any nested object it
  kept a reference to.
- **Hashing personal identifiers "so they are safe"** — reversible for every low-entropy
  field that matters, while reading as though the problem were solved.
- **Separate audit shapes per subsystem** — what this ADR exists to stop; makes the one
  question an audit trail is for unanswerable without a union of formats.
- **Dedupe on `event_id` alone** — lets a producer hide a second, different event behind a
  reused identifier.
- **Embedding evidence in audit events** — a second copy on a different retention clock,
  outside the controls that governed the first.
- **Requiring a durable, transactional store or an outbox pattern before any story may
  emit an audited mutation (Decision 7)** — the actual gap W03 stories hit is closed
  cheaply against today's in-memory repositories by the five-element pattern above;
  mandating real cross-store distributed-transaction infrastructure this early would block
  every feature story on persistence-layer work that does not exist yet, to guarantee
  something the cheaper pattern already guarantees at the current infrastructure tier.
- **Commit-then-rollback as an accepted alternative to audit-before-commit (Decision
  7)** — this ADR's own first draft proposed it as equally valid. Rejected after
  BIO-TEN-001's round-1 re-review proved it: a rollback's compare-and-swap can be silently
  swallowed by a concurrent writer's intervening commit, permanently baking an unaudited
  value into that writer's state with no audit event ever naming it.
- **Audit-before-commit alone, without a per-aggregate lock, as sufficient closure
  (Decision 7)** — this ADR's own second draft (snapshot `1.1.1`) proposed it, and FAILed
  Seyi's independent review: it durably claims success before a concurrent writer's commit
  outcome is known, producing a false `Allowed` record when that writer loses the race. The
  lock removes the race the ordering alone cannot.
- **The per-aggregate lock alone, without `ExistsAsync` verification or
  `CancellationToken.None` (Decision 7)** — closes writer concurrency but not an audit
  write that durably persists and then throws on acknowledgement, nor a caller cancellation
  landing between a confirmed audit write and the commit that follows it — two further,
  distinct ways to produce the identical false-success shape from causes the lock cannot
  see. Each superseded snapshot (`1.1.0` through `1.1.2`) is kept, not edited in place, as
  the record of which specific shape was tried and why it did not hold, so the same mistake
  is not tried again on a future story.
- **Treating a documented, disclosed atomicity gap as an accepted limitation (Decision
  7)** — tried by two independent stories before this Decision existed; both were correctly
  rejected. Disclosure makes a gap visible, it does not close it.
- **Building a real durable audit store or an attempt-then-finalize protocol for only the
  demo-session context-change mechanism (Decision 8)** — the actual gap Seyi's SY-OPS002-05
  found is closed cheaply, at this specific mechanism's own low stakes, by a narrow, named
  exception instead; mandating real cross-process durability for one synthetic mechanism
  while every other feature's audit trail has the identical restart-loses-history property
  would be inconsistent engineering effort pulled ahead of ADR-0005's own pending real
  persistence work, to guarantee something a bounded, disclosed exception already guarantees
  at the current infrastructure tier.
- **Treating Decision 8's exception as general precedent for any audit that lags its
  effect across a restart (Decision 8)** — rejected explicitly in the Decision's own text:
  the exception is bounded to the one named mechanism and the three facts that make it
  low-stakes, not to the shape of the gap itself. A future feature with a real, non-synthetic
  effect must bring its own separately justified exception or implement Decision 7's named
  remedy.
