# ADR-0001 — Canonical identifiers, clocks and versioning

- **Status:** Accepted (BIO-ARC-001, wave W01)
- **Date:** 2026-09-20
- **Owner:** Kemi — Chief Architect & Integrator
- **Story:** BIO-ARC-001 — Define canonical identifiers, clocks and versioning
- **Applies to:** every BIOLOG surface — .NET services, Next.js web, Flutter mobile/edge, devices, integrations
- **Change policy:** this ADR and the artifacts under `contracts/` are shared contracts. They freeze at W01 exit. No agent may change them without a recorded Kemi approval and a superseding ADR.

## Context

Independent feature teams (identity, credentials, biometrics, document intelligence,
access, mobile/edge) build in parallel in isolated worktrees. Without a single
canonical definition of "what an identifier is", "what a timestamp means" and
"how two writers are ordered", each team invents its own primitives and the
modules cannot be integrated.

Three constraints from the Master Technical Specification (the authoritative source)
drive the decisions below:

1. **Multi-tenant with no cross-tenant joins** (spec §2, §11 Data rules). Identifiers
   must never be usable as a cross-tenant shortcut.
2. **Offline-capable clients** (spec §11, Offline & Edge Architecture). Devices create
   records while disconnected, so identifiers must be mintable on the client, and
   ordering must not depend on a device's wall clock.
3. **Capacity baseline of millions of verification events per day** (spec §3).
   Identifiers must index well in MySQL and sort close to creation order.

## Decision 1 — Canonical identifier strategy (BIO-ARC-001-AC01)

### 1.1 Form

Every canonical BIOLOG identifier on the wire is a **typed identifier**:

```
<type-prefix> "_" <uuid>
```

where `<uuid>` is a **UUID version 7** (RFC 9562), rendered lowercase, dash-separated,
8-4-4-4-12. Example: `per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77`.

The regular expression that defines a well-formed canonical identifier is published in
`contracts/schemas/canonical-id.schema.json` and implemented once in
`contracts/js/identifiers.mjs`.

### 1.2 Registered types

Exactly six identifier types are registered by this ADR. They are the primitives named
in the story; nothing else is registered here.

| Entity | Prefix | Plane | Notes |
|---|---|---|---|
| Tenant | `ten` | control plane | The tenant boundary itself. Never a tenant-plane record. |
| Person | `per` | tenant plane | The permanent aggregate (spec §4). |
| Credential | `crd` | tenant plane | Logical credential. A physical card issue is a child record, not this id. |
| Device | `dev` | tenant plane | Readers, terminals, enrolment stations, mobile installs. |
| Document session | `dcs` | tenant plane | One Document Verify session (spec §9). |
| Event | `evt` | tenant plane | Any domain event (spec Event Architecture). |

Adding a type requires a registry entry in `contracts/schemas/identifier-registry.json`,
a superseding or amending ADR, and Kemi's recorded approval. A feature story may **not**
invent a prefix locally.

### 1.3 Why UUIDv7 rather than an auto-increment key or UUIDv4

- **Client-mintable.** An offline reader or a Flutter app enrolling a visitor in a
  basement with no connectivity must create the record's final identifier immediately.
  A database-assigned key cannot do this without a rewrite on sync, which would break
  every queued reference to it.
- **Time-ordered.** UUIDv7's leading 48 bits are a Unix millisecond timestamp, so values
  sort near creation order. In MySQL this keeps the primary-key B-tree appends local
  instead of scattering writes across the index the way UUIDv4 does — the difference
  between a workable and an unworkable event table at millions of rows per day.
- **No coordination.** No sequence server, no round trip, no collision negotiation
  between edge and cloud.

The timestamp inside a UUIDv7 is an implementation detail for ordering. **It is not a
business timestamp**: never read it as `occurred_at`, and never trust it for anything
that matters, because an offline device sets it from its own clock (see Decision 2.4).

### 1.4 Storage form

- **MySQL:** `BINARY(16)` holding the 16 raw UUID bytes, not the 36-character text. The
  type prefix is *not* stored — it is implied by the column's table and re-attached when
  the value leaves the service boundary.
- **SQLite (mobile/edge):** `BLOB` of the same 16 bytes.
- **Wire (REST, events, webhooks, exports, logs):** always the prefixed text form.

Parsing the prefixed form and rejecting a mismatched type is how a service catches
"a credential id was passed where a person id was expected" at the boundary, instead of
returning another tenant's record.

### 1.5 Identifiers are not authorisation and not tenant-scoped lookups

A canonical identifier is globally unique but carries **no authority**. Tenant context
resolves *before* any tenant repository is constructed (spec API Architecture), and every
tenant-plane read and write is filtered by the resolved tenant. Holding `per_…` from
tenant A must fail closed when presented in tenant B's context — as *not found*, never as
*forbidden*, so identifiers cannot be probed for existence across tenants.

Identifiers are also **not secrets**. They appear in URLs and logs. Nothing may be
authorised merely because the caller can name it.

### 1.6 Business identifiers are separate

Institutional identifiers (matriculation number, staff number), the per-person physical
card sequence `001, 002, 003…` (spec §6) and any other human-facing code are **business
identifiers**. They are attributes of a record, never its primary key, never a foreign
key, and never a canonical identifier. They may be reissued, corrected or formatted by
tenant policy; canonical identifiers may not. The card sequence in particular is never
reused or reset, which is a business rule about that attribute, not about identity.

## Decision 2 — Clocks (BIO-ARC-001-AC02)

### 2.1 UTC everywhere internally

Every timestamp that is **stored, transported, compared, logged or audited** is a UTC
instant. There is no local time and no UTC offset anywhere inside the system.

- **Wire format:** RFC 3339 with a literal `Z` and exactly three fractional digits —
  `2026-09-20T21:05:00.000Z`. Millisecond precision, fixed width, so string comparison
  and chronological comparison agree.
- **MySQL:** `DATETIME(3)` holding UTC. **Not** `TIMESTAMP`, whose value MySQL converts
  using the session time zone — two connections with different session zones would read
  back different instants from the same row.
- **Naive timestamps are prohibited.** A value with no zone, an offset other than `Z`, or
  a bare date is rejected at the boundary rather than guessed at.
- **The supported year range is 1000–9999 inclusive.** That is the intersection of what
  every BIOLOG surface can store and read back: MySQL's documented `DATE`/`DATETIME`
  range is `1000-01-01`…`9999-12-31`, and .NET's `DateTime` covers `0001`…`9999`. A year
  outside it would pass validation and then fail at the tenant database, which defers a
  failure instead of reporting it. Year `0000` is therefore rejected, including its
  proleptic leap day `0000-02-29`. **Assumption on record:** no BIOLOG business timestamp
  legitimately precedes year 1000. A tenant needing an earlier historical calendar date —
  which would be an `*_on` field, not an instant — needs an amending ADR, not a local
  workaround.
- **The date must be a real date.** The day has to exist in that month of that year under
  the proleptic Gregorian calendar: `2026-02-30` and `2026-02-29` are rejected,
  `2028-02-29` and `2000-02-29` are accepted, `1900-02-29` is not. A shape check alone
  cannot see this, and most platform parsers roll an impossible day forward silently —
  `Date.parse("2026-02-30")` yields 2 March. Accepting that would change a supplied audit
  or device time into a different instant with no error, which is the one failure a
  canonical time contract exists to prevent. Both the published schemas and
  `contracts/js/clock.mjs` enforce the rule, and a test sweeps them against each other so
  the two definitions cannot drift apart.
- **Code and schema must give the same verdict, and that is tested exhaustively.** The
  reference implementation reports *why* a value is bad — an out-of-range year and an
  impossible day get different machine codes — while a schema pattern can only say yes or
  no. That is a difference in diagnostics, never in verdict. The suite compares the two
  layers across every four-digit year, and the published patterns use no lookahead or
  lookbehind so they survive translation to an RE2-backed validator in a generated
  binding.

### 2.2 Naming

A field whose name ends in `_at` is a UTC instant as defined above: `created_at`,
`updated_at`, `occurred_at`, `received_at`, `expires_at`, `revoked_at`. A field holding a
calendar date with no instant (a date of birth, a term start) ends in `_on` and is a plain
`YYYY-MM-DD`, because it is a date in a human calendar and not a point on the timeline.

### 2.3 Local time is presentation only, and always explicit

Rendering a UTC instant for a human requires an **explicit IANA time zone identifier**
(`Africa/Lagos`, `Europe/London`) supplied by tenant, site or user preference. It is
never taken from the server's or the container's local zone — a report must not change
meaning because it ran on a differently configured host.

Every rendered local time carries the zone id and the resolved offset with it, so a
displayed or exported time is self-describing and reversible. `contracts/js/clock.mjs`
implements this as the only sanctioned rendering path.

### 2.4 Device clocks are claims, not facts

An offline device's clock can be wrong, or set wrong deliberately. Therefore every event
that originates at an edge device carries two instants:

- `occurred_at` — what the device claims. Kept, shown, and used for human interpretation.
- `received_at` — stamped by the server on intake. Authoritative.

Security-relevant ordering — did this presentation happen before or after the revocation
— uses `received_at` and the aggregate `version`, **never** `occurred_at`. A device that
reports an `occurred_at` implausibly far from `received_at` is recorded and flagged, not
silently corrected, because the skew is itself evidence.

Sequence and ordering within an aggregate come from `version` (Decision 3), not from
wall-clock comparison. Two events can share a millisecond; two versions cannot.

## Decision 3 — Optimistic concurrency and versioning (BIO-ARC-001-AC03)

### 3.1 Every mutable aggregate carries a version

A mutable tenant aggregate carries an integer `version`, starting at **1** on creation and
incremented by exactly **1** on each committed mutation. It is opaque to clients: they
echo it, they do not compute it. `updated_at` moves with it but is never the concurrency
token, because two writes can land in the same millisecond.

### 3.2 Compare-and-set on every mutating write

A mutating write states the version it believes it is changing (`expected_version` in a
body, `If-Match` on HTTP). The write commits only if the stored version still matches.

| Situation | Outcome |
|---|---|
| `expected_version` equals stored version | Commit; version becomes `version + 1` |
| `expected_version` differs | Reject — `409`, machine code `concurrency_conflict`, response carries the current version |
| `expected_version` absent on a mutating write | Reject — `428`, machine code `precondition_required` |

Omitting the precondition is a **failure, not a last-write-wins fallback**. Fail closed:
a client that does not say what it thinks it is overwriting is not permitted to overwrite
anything. This is the rule that stops a stale admin tab from silently resurrecting a
revoked credential.

### 3.3 Immutable and append-only records have no version

PhotoMaster, published card and document template versions, raw document evidence,
biometric capture evidence, audit events and domain events are immutable or append-only
(spec §5, §7, Data rules). They carry **no** `version` field and reject updates outright.
Correcting them means appending a new record that supersedes the old one and points at
it — the original stays readable, which is what makes the audit trail worth having.

### 3.4 Idempotency for retry-prone writes

Card activation, production completion, offline sync intake and webhook intake carry an
`idempotency_key` (spec API Architecture). The key is a canonical identifier or an opaque
client string of at most 255 characters, scoped to tenant plus operation.

| Situation | Outcome |
|---|---|
| New key | Execute; store key with the result and a fingerprint of the request |
| Same key, same request fingerprint | Return the **original stored result**; do not execute again |
| Same key, different request fingerprint | Reject — `409`, machine code `idempotency_key_reuse` |

A retried activation must never mint a second card. Replaying a key with different content
is a client defect and is surfaced, not absorbed.

### 3.5 Offline sync conflicts

A queued offline mutation carries its global id, the originating `device_id`, the
`base_version` it was composed against, and its `idempotency_key`.

On intake, if `base_version` no longer matches the server's version:

- For **identity, credential and biometric** aggregates the mutation moves to `CONFLICT`
  and enters the human **Sync Resolution Queue**. There is **no silent last-write-wins**
  for security-sensitive data (spec §11), and "the device is newer" is not a tiebreak —
  see Decision 2.4.
- For other aggregates, a deterministic merge is permitted **only** where the owning
  story defines the merge rule in writing. Absent such a rule, the default is `CONFLICT`.

Revocation deltas are the deliberate exception to queue ordering: they take a priority
path, because a revocation arriving late is a security failure while a telemetry batch
arriving late is not.

### 3.6 Event schema versions are a different thing

A domain event's `schema_version` is an integer describing the **shape of the payload**.
It is unrelated to an aggregate's `version`, which describes the **state of a record**.
Adding an optional field is additive and keeps the schema version. Removing or retyping a
field, or changing the meaning of an existing one, is breaking and creates a new schema
version (Event Architecture). Consumers must be idempotent and must ignore unknown fields.

## Consequences

**Accepted costs**

- A 16-byte key is larger than a `BIGINT`, costing index space and memory at the capacity
  baseline. Accepted: offline minting is not optional for this product, and UUIDv7's
  ordering recovers most of what UUIDv4 would have lost.
- Compare-and-set makes every mutating client handle `409`. Accepted deliberately: the
  alternative is silent data loss in exactly the security-sensitive paths that matter.
- Millisecond precision cannot distinguish two events within the same millisecond.
  Accepted: ordering of record state is `version`'s job, not the clock's.

**Obligations this places on every later story**

- Use `contracts/js/*` or a binding generated from `contracts/schemas/*` — do not
  re-implement identifier parsing, instant formatting or version checks locally.
- Do not add an identifier type, change a prefix, or add a timestamp format without an
  amending ADR approved by Kemi.
- Migrations that introduce a mutable aggregate must include `version` and `updated_at`;
  migrations introducing an append-only table must include neither.

## Alternatives rejected

- **Auto-increment integer keys** — cannot be minted offline; leak row counts; make merge
  from edge devices a rewrite problem.
- **UUIDv4** — mintable offline but randomly distributed, which fragments the primary-key
  index under the specified event volume.
- **Bare UUIDs with no type prefix** — cheaper on the wire, but removes the boundary check
  that catches a wrong-type identifier before it reaches a repository.
- **Storing local time with an offset** — an offset is not a time zone: it loses the rule
  that produced it, so historical and future times cannot be recomputed when a zone's
  rules change.
- **Last-write-wins on sync** — simplest to build, and it silently discards a revocation.
  Rejected outright by spec §11.
