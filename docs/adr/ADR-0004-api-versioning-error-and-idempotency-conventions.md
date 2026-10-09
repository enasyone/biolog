# ADR-0004 — API versioning, error, idempotency and pagination conventions

- **Status:** Accepted (BIO-ARC-004, wave W01)
- **Date:** 2026-09-21
- **Owner:** Kemi — Chief Architect & Integrator
- **Story:** BIO-ARC-004 — Define API, error and idempotency conventions
- **Builds on:** [ADR-0001](ADR-0001-canonical-identifiers-clocks-and-versioning.md) (identifiers, versioning, request idempotency), [ADR-0002](ADR-0002-tenant-context-and-database-routing.md) (tenant context, fail-closed denial), [ADR-0003](ADR-0003-audit-evidence-and-event-envelopes.md) (classification/redaction, audit correlation)
- **Applies to:** every public and service-to-service HTTP surface — web, mobile, device/reader integrations, webhooks
- **Change policy:** shared contract. Freezes at W01 exit; changing it needs a superseding ADR and recorded Kemi approval.

## Context

Three ADRs already exist before a single HTTP endpoint has been written. That is deliberate
— API Architecture requires tenant context to resolve before a repository is built (ADR-0002
§1.5), requires idempotency keys for retry-prone writes (already partly defined in
ADR-0001 §3.4), and requires "standard problem responses [with] machine code, human
message, correlation id and safe validation details." This ADR is where those requirements
become one shape that every feature team's endpoints emit, instead of each team inventing
its own error envelope, its own idempotency header, and its own pagination cursor.

**This ADR reuses rather than re-derives wherever a primitive already exists.** ADR-0001
already defines request idempotency (`resolveIdempotency`, `requestFingerprint`,
`INITIAL_VERSION`/optimistic concurrency); ADR-0003 already defines redaction
(`redact`, classification levels) and a canonical `evt_` identifier space. Re-deriving any
of these at the API layer would produce two competing definitions of the same idea, which
is exactly the failure this whole ADR series exists to prevent.

## Decision 1 — API versioning (BIO-ARC-004-AC01)

### 1.1 Path versioning for HTTP

Every public and service-to-service HTTP endpoint is versioned in its path:
`/api/v1/...`. There is no unversioned endpoint and no version carried only in a header or
a media type — a version that can be missed by a caller who does not read the
documentation is not a version.

### 1.2 What "explicitly versioned equivalents" means for non-path interfaces

Not everything is a routed HTTP path. Webhook payloads, device/reader protocol messages
and batch import/export file formats carry their **own** explicit version field in their
envelope (spec: "webhooks are signed, versioned, replay-resistant and auditable"). The
field name is `schema_version` where the envelope already has one meaning (an ADR-0001/0003
event, a document template), and `api_version` otherwise. The rule is the same as path
versioning: a consumer can always determine which shape it is looking at without guessing.

### 1.3 Additive vs breaking, and three different things this ADR does not conflate

Additive changes — a new optional field, a new endpoint, a new enum value a client is
expected to tolerate — do not bump a version. A breaking change — removing or retyping a
field, changing a status code's meaning, tightening validation on an existing field — gets
a new path version (`/api/v2/...`); the old version keeps running, marked with the
standard `Deprecation` and `Sunset` HTTP response headers, for a window the owning story
documents. This ADR mandates the *mechanism*; a specific sunset date is an operational
decision for whoever ships the breaking change.

Three different "version" concepts already exist in this contract series and must not be
confused with each other or with the API path version defined here:

| Version | Means | Defined in |
|---|---|---|
| `/api/v1` (this ADR) | Shape of the HTTP interface itself | ADR-0004 §1.1 |
| `schema_version` | Shape of an event's or template's payload | ADR-0001 §3.6, ADR-0003 §2 |
| `aggregate_version` / `version` | State of a specific record, for optimistic concurrency | ADR-0001 §3.1 |

An endpoint bumping to `/api/v2` does not imply anything about the `schema_version` of
events it emits, and vice versa.

## Decision 2 — Standard problem response (BIO-ARC-004-AC02)

### 2.1 One envelope, because one already almost exists

`TenantContextError` (ADR-0002), `ConcurrencyError` (ADR-0001) and `AuditError` (ADR-0003)
already share the same constructor shape — `(message, code, status, detail)` — without
any of them extending a common base. That is not a coincidence; it is what "one code, one
status, one detail object" looks like when three different ADRs each independently arrived
at it under the same pressure. This ADR promotes that shape to the platform's one HTTP
error envelope rather than inventing a fourth shape:

```
{
  "code": "tenant_context_conflict",
  "message": "tenant channels disagree: host=ten_..., principal=ten_...",
  "status": 403,
  "correlation_id": "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77",
  "detail": { "channels": ["host", "principal"] },
  "errors": [ { "path": "person.date_of_birth", "code": "invalid_format", "message": "..." } ]
}
```

`code`, `message`, `status` and `correlation_id` are always present. `detail` is present
whenever the underlying error carried one. `errors` is present only for a validation
failure (Decision 4.4) and is never present alongside a non-validation `code`.

Content type is `application/problem+json` — a well-known media type that lets tooling
recognise an error payload without inspecting its body, adopted without adopting RFC 7807's
`type`/`title`/`instance` field names, which this platform's existing `code`/`message`
already cover.

### 2.2 This works today, with no change to the three existing error classes

`toProblemResponse(error, { correlation_id })` accepts anything exposing `.code`, `.status`,
`.message` and `.detail` — duck-typed, not by inheritance — so it serialises a
`TenantContextError`, a `ConcurrencyError` or an `AuditError` unchanged. A future domain
error class gets standard HTTP behaviour for free by matching the same four properties. A
shared `BiologApiError` base is provided as a convenience for new code to extend; migrating
the three existing classes onto it is a worthwhile later cleanup and is explicitly **not**
done by this story, to keep this change out of three files it does not own touching.

### 2.3 `detail` may not shadow a reserved field of the envelope itself

A `detail` object carrying a key named `correlation_id`, `code`, `status`, `message` or
`errors` would sit inside a document that already uses that exact name at the top level for
something else — a consumer reading `correlation_id` could get either value depending on
how it walks the object. `toProblemResponse` refuses to serialise a `detail` containing any
of those names, as `reserved_detail_field`, rather than emit a self-contradicting document.
This is a construction-time check on the error, not a caller-facing behaviour — no external
input can trigger it directly, but an error class's own allowlist can, and it is exactly the
kind of small collision the rest of this ADR series has found by asking what happens when
two things reuse the same name for different purposes.

### 2.4 `detail` is redacted, not dumped

The spec requires "safe validation details" — safety is not automatic. `detail` is built
through ADR-0003's `redact()` allowlist projection, the same discipline used for audit
envelopes, not by serialising an internal object wholesale. An error class that wants a
field in `detail` names it in an allowlist the same way an audited action does; nothing
else about how a caller can widen what a `detail` object contains changes because the
consequence — an internal value crossing the wire to a caller who does not have access to
it — is identical to the audit case ADR-0003 §3 already covers.

### 2.5 A builder must enforce the schema it publishes, not merely resemble it

The first version of `toProblemResponse`, `toValidationProblem` and `buildPageEnvelope`
each checked only the *shape* of their inputs — that `code` was a string, that `message`
was present when truthy — and not the *content* constraints their own published schemas
require: `code` matching the snake_case pattern, `status` an HTTP integer in range,
`errors[]` appearing only alongside `validation_failed`, a validation item's `message`
actually being a string, a page's `limit` actually being present, `next_cursor` actually
being a string or `null`, `total_count` actually being a non-negative integer (QA-03,
Seyi review). Every one of those gaps let a builder function hand back an object its own
schema would reject — the shared constructors and the published format were, in that
state, two different things that happened to agree on the easy cases.

Each builder now validates its output against the exact constraints its own schema
publishes, before returning. Duck-typing (Decision 2.2) still governs which *properties* an
error must expose; it was never meant to excuse validating what is *in* them once they are
read. A caller cannot reach these checks from outside — they run against an error class's
own already-constructed `.code`/`.status`/`.errors`, or the caller's own literal function
arguments — but a mistake in a new error class or endpoint handler is caught at the point
it is made, not discovered as a schema-validation failure somewhere downstream.

**The first pass at this rule still left real gaps** (QA-03, Seyi re-review round 2):
`toProblemResponse` accepted a non-string top-level `message`; when `code` was
`validation_failed`, its `errors[]` items were copied through unchecked — a primitive array
entry silently became `{}` on the spread, and a malformed `path`/`code`/`message` inside an
item passed unchanged; `toValidationProblem` checked every item's message but never its own
top-level `message` option. Each of these is the identical mistake Decision 2.5 already
names, just in a spot the first pass missed: checking that a value was present, not that its
*content* satisfied the schema.

Closed by extending the same rule to every field, and by sharing one validator —
`assertValidationErrorItem` — between the two places a validation-error item can originate:
built fresh by `toValidationProblem`, or handed through directly on an error's own
`.errors` in `toProblemResponse`. A single item shape check, used both ways, is what stops
this class of gap from being closed in one caller and left open in the other.

## Decision 3 — Correlation IDs, and where ADR-0003 already defines this ID space

### 3.1 The API correlation ID is a canonical `evt_` identifier — the same one

ADR-0003's audit and domain-event envelopes already carry `correlation_id` as a canonical
`evt_` identifier (ADR-0001 §1). The API layer does not mint a second, HTTP-flavoured
correlation ID next to it — that is precisely the clash the continuity note on this story
warned about. The request's correlation ID **is** the `correlation_id` value threaded into
every audit and domain event produced while handling that request. One identifier, one
field name, zero translation at the boundary between the API layer and the contracts layer
underneath it.

### 3.2 Resolution

A request may present `X-Correlation-Id`. If present and a well-formed canonical `evt_`
identifier, it is used as-is. If absent, or present but malformed, the server mints a fresh
one (`newId('event')`) — the same fail-closed-on-a-bad-caller-value posture as every typed
identifier elsewhere in this contract series (ADR-0001 §1.4, ADR-0002 §1.2): a caller
cannot cause a value that would fail `buildAuditEvent`'s own `correlation_id` validation
(ADR-0003 §1) to reach it. The resolved value is always returned in the response's
`X-Correlation-Id` header and in the `correlation_id` field of a problem response, so a
caller who supplied none still gets one to reference in a support request.

## Decision 4 — Write endpoint idempotency (BIO-ARC-004-AC03)

### 4.1 The mechanism already exists; this defines its HTTP shape

ADR-0001 §3.4 already specifies the primitive: an `idempotency_key` — a canonical
identifier or an opaque client string of at most 255 characters, scoped to tenant plus
operation — replays an identical request and rejects a reused key carrying different
content as `idempotency_key_reuse`. `requestFingerprint` and `resolveIdempotency` in
`contracts/js/concurrency.mjs` already implement the comparison. This ADR does not
reimplement that; it defines how it appears over HTTP and how it is scoped to a specific
endpoint rather than to the sync/production-job callers ADR-0001 originally wrote it for.

### 4.2 The header and the scope key

The header is `Idempotency-Key`. The dedupe key is tenant-scoped and operation-scoped, so
no cross-tenant collision is even representable and the same key value reused against a
different endpoint is not treated as the same request.

**The scope key is not naive string concatenation.** `${tenant_id}:${operation}:${key}`
looked sufficient and was not: `operation` and `idempotency_key` are both caller-controlled
strings that may themselves contain `:`, so `(operation="card",
key="activate:key")` and `(operation="card:activate", key="key")` produced the identical
scope key (QA-02, Seyi review) — one tenant's record for one endpoint could replay or
conflict against a completely different endpoint. This is the same failure ADR-0003's
QA-04 found for `.`-joined redaction paths, one delimiter over, and it is closed the same
way in spirit but not by re-deriving a matching escape scheme: the scope key is
`JSON.stringify([tenant_id, operation, idempotency_key])`. JSON's grammar delimits array
elements with commas outside quoted strings, so a `:` — or any other character — inside
either segment can never be mistaken for a boundary between them. The encoding is
injective by construction, using a well-tested built-in instead of a fifth hand-rolled
escaping rule.

`idempotency_key_required` is a distinct code from ADR-0001's `precondition_required`, even
though both may ride on HTTP 428: one is about retry safety, the other about optimistic
concurrency, and conflating their machine codes would make the two indistinguishable to
whatever is triaging the error.

### 4.3 Which endpoints require it, and the retention floor

The spec names the required set explicitly: card activation, production completion,
offline sync intake and webhook intake. That set is not closed — a later story marks its
own write endpoint as retry-prone the same way, and that marking is what requires the
header, not a registry this ADR owns. Stored idempotency records are kept for **at least
24 hours**; a shorter window is not defined by this ADR because it would make dedup silently
stop working during exactly the kind of extended outage-and-retry scenario it exists for.

### 4.4 The store operation that decides EXECUTE vs REPLAY must be atomic, not a sequence

The first version of this decision described checking for a stored record, executing, then
storing the result — three separate steps, each awaited. **That sequence is not atomic**
(QA-01, Seyi review). Two requests arriving close together both check before either
stores, both see nothing, and both execute — the exact duplicate-effect failure this whole
convention exists to prevent, on precisely the writes the spec names as retry-prone. A
process crash after executing but before storing has the identical effect on a later,
sequential retry: nothing was ever recorded, so the retry executes again as though it were
new.

**The fix moves the atomic operation to before execution, not after.** A store implements
four operations, not two:

| Operation | Contract |
|---|---|
| `claim(key, fingerprint)` | **Atomic.** Creates an `IN_FLIGHT` record only if none exists for `key`; returns whether *this* call won, and the existing record if it did not. A real store provides this through its own infrastructure — a unique-constraint insert, `SET key value NX` — the same way a real audit store provides durability for ADR-0002 §5's audit-before-response rule. |
| `complete(key, record)` | Called only by the request that won the claim, once its write has succeeded. Transitions the record to `DONE` with the result to replay. |
| `release(key)` | Called only by the winner if execution failed unexpectedly. Frees the key so a later retry is not blocked forever by a claim nobody will ever finish. |
| `get(key)` | Read-only lookup, used only to poll for completion after losing a claim. |

A request that wins the claim executes and durably records the outcome. A request that
loses the claim never executes at all: if the existing record is already `DONE`, its
result is replayed — via `resolveIdempotency`, reused unchanged from ADR-0001, so the
same-fingerprint-replays / different-fingerprint-conflicts rule is not re-derived a second
time. If the existing record is still `IN_FLIGHT` with the same fingerprint, this is a
genuine concurrent retry of the identical request, and the loser waits, bounded, for the
winner to finish rather than executing a second time. A wait that exceeds the timeout is a
visible, distinct failure (`idempotency_wait_timeout`, `503`) — never a silent second
execution, and never an unbounded hang either.

**A business failure and an infrastructure failure are handled differently, deliberately.**
If the operation itself resolves with a failure response — a `422` validation rejection,
say — that is a normal *result*, stored and replayed consistently like any success: a
retry of a request that was correctly rejected should see the same rejection, not a second
attempt. If `execute` *throws* — an unexpected, infrastructure-level failure, the crash
case the finding names — the claim is released instead, because nothing about that failure
represents an outcome worth replaying; a later retry should be free to try again.

**A permanently abandoned claim — the original holder crashed and nothing ever calls
`release`** — is a real operational scenario this ADR does not fully solve, on purpose, for
the same reason it does not mandate the exact retention duration: recovering a lease no
one will ever release is the store's responsibility (a TTL on the `IN_FLIGHT` status, an
operational unstick procedure), not a rule this contract can enforce from the client side.
What this ADR guarantees is the client-visible behaviour while that is unresolved: a
bounded wait, then a distinct, actionable failure — never an indefinite hang, and never a
second execution.

The reference implementation, `createInMemoryIdempotencyStore`, demonstrates a store that
satisfies the atomicity requirement: its `claim` performs its check-and-set with no `await`
in the middle, so Node's single-threaded event loop cannot interleave two concurrent
callers between the check and the write — the same guarantee a production store must
provide through its own infrastructure.

### 4.5 `tenantId` is validated too, not only `operation` and `idempotencyKey`

The scope key's injectivity fix (Decision 4.2) closed how `operation` and
`idempotencyKey` are combined; it said nothing about whether `tenantId` itself is
trustworthy, and the first version of `idempotencyStorageKey` never checked it (QA-04,
Seyi re-review round 2). That is not a cosmetic gap. `JSON.stringify(NaN)` is the string
`"null"` — the identical tuple component this convention already uses, deliberately, for a
control-plane operation (Decision 4.1; ADR-0002 §1.4). A raw, unvalidated `tenantId` — a
`NaN` from a failed numeric parse, an object, a concatenation of two real tenant ids — was
therefore not merely wrong input; a `NaN` specifically could **alias a tenant-scoped
idempotency record onto the control plane's own key space**, and any other malformed value
still put a caller-named string directly into a shared-infrastructure scope key.

`tenantId` must now be **explicitly** `null` — a deliberate control-plane operation — or a
canonical `ten_` identifier (ADR-0001 §1). There is no third option and no coercion:
`undefined` is refused exactly like `NaN` or an object, because omitting the tenant by
accident and declaring control-plane work on purpose are different facts, and a caller who
merely forgot to pass a resolved tenant must not have that silently read as "this is
control-plane work." This is the same posture ADR-0002 §2 already takes toward every other
tenant claim: no default, no fallback, fail closed on anything that is not provably what it
claims to be.

### 4.6 How this relates to ADR-0003's event idempotency — two mechanisms, not one

This ADR's write-endpoint idempotency and ADR-0003 §4's domain-event idempotency solve two
different problems, use two different dedupe keys, and share no implementation, even
though both use the word "idempotency" and both compare a content fingerprint:

| | This ADR (§4) | ADR-0003 §4 |
|---|---|---|
| Protects against | A caller retrying the same HTTP request | A producer's event being delivered more than once |
| Dedupe key | `(tenant_id, operation, idempotency_key)` — the caller's `Idempotency-Key` header | `(tenant_id, event_id)` — the event's own canonical id |
| What replays | The stored HTTP `{status, body}` | Nothing — a `DUPLICATE` is discarded, not replayed |
| Fingerprint excludes | Nothing beyond the request payload itself | `received_at` (server-stamped per delivery; ADR-0003 §4) |
| Reuse-with-different-content | `409 idempotency_key_reuse` (this ADR) | `event_id_reuse`, surfaced not absorbed (ADR-0003 §4) |

Neither is a special case of the other, and an endpoint does not choose between them: a
write endpoint that emits a domain event as part of its work uses **both**, for what each
actually protects.

**The interaction that matters: does a replayed API response mean the domain event is not
re-emitted, or can it be emitted twice?**

**On an ordinary caller retry — the case this ADR's `withIdempotency` exists for — the
domain event cannot be re-emitted, and not by coincidence.** A `REPLAY` outcome (Decision
4.4) never calls `execute` again. Whatever domain event `execute` would have emitted is
emitted only when `execute` actually runs, so a request that hits a `DONE` record with a
matching fingerprint causes no code to run at all — the event-emitting logic is exactly as
unreachable as the rest of the write. This is not an added rule; it falls directly out of
`REPLAY` meaning "return the stored result without executing."

**The seam is narrower and specific: a partial failure.** If `execute` emits its domain
event and *then* throws — a crash, a downstream failure, anything after the event but
before `execute` returns — `runClaimedWrite` (Decision 4.4) releases the claim precisely so
a later attempt can run `execute` again in full. That is what "release" is for: retrying
the whole operation from scratch. A second `execute` run mints a fresh event via `newId`
the same way the first one did, and ADR-0003's own dedupe cannot catch it as a duplicate,
because that dedupe keys on `event_id` and the retried attempt produces a *different* one
with equivalent content — `(tenant_id, event_id)` dedup only ever catches the *same* event
delivered twice, not the same fact re-asserted under a new id. **This ADR's write
idempotency does not, and structurally cannot, prevent that: it has no visibility into what
`execute` did internally before it failed, only into whether `execute` is called at all.**

The responsibility this places on an endpoint, stated plainly rather than left implicit: if
`execute` can emit a domain event and then fail before returning, the emission itself must
be made safe against being repeated on a retried attempt — for example, by deriving the
event's own `idempotency_key` (ADR-0001 §3.4, carried optionally on the domain-event
envelope) from the same `Idempotency-Key` the API layer already validated, so a downstream
consumer or the emission code's own check can recognise "this exact operation was already
recorded" independent of which `event_id` any particular attempt happened to mint. This ADR
guarantees the caller-visible response is never duplicated; it does not extend that
guarantee to a side effect `execute` performs before the point where it can still fail, and
does not claim to.

## Decision 5 — Pagination, filtering and validation error shape (BIO-ARC-004-AC04)

### 5.1 Cursor pagination is the default, not offset

The capacity baseline is 500k–2M identities per tenant (Master Spec §3). Offset pagination
degrades linearly with offset depth at that scale and is not the default here. A list
endpoint accepts `limit` (bounded; an endpoint documents its own default and maximum) and
`cursor` — an **opaque** token a client echoes back verbatim and never constructs or
parses. The response envelope is:

```
{ "data": [ ... ], "page": { "next_cursor": "<opaque>" | null, "limit": 50 } }
```

`page.total_count` is optional and present only when an endpoint's specific query makes
counting cheap; it is not a general requirement, for the same reason offset pagination is
not the default.

### 5.2 Filtering is allowlisted per endpoint

Which fields a list endpoint may be filtered or sorted on is documented per endpoint as an
explicit allowlist — the identical "allowlist, never a denylist" reasoning from ADR-0003
§3: a caller who names a field the endpoint does not recognise gets a validation error, not
a silently-ignored filter that leaves them believing something was applied that was not.

### 5.3 Validation error field paths reuse ADR-0003's escaping, not a second scheme

A validation error names the field it is about. ADR-0003 already solved exactly this
problem — naming a possibly-nested, possibly-literally-dotted field unambiguously — the
hard way, after a real collision (its QA-04). A validation error's `path` is encoded with
the identical rule: `escapeSegment` on every raw key before joining with `.`, so a field
literally named with a dot and a nested field can never be confused, here or anywhere else
in the platform. Building a second, independent dotted-path convention for validation
errors would mean re-deriving ADR-0003's fix from scratch, with every chance of reproducing
its bug.

Each entry is `{ path, code, message }`; `code` is a short snake_case machine reason
(`required`, `invalid_format`, `out_of_range`, `unknown_field`, …) specific to the field's
validation rule, documented per endpoint the same way an audited action's own `reason_code`
is — this ADR does not enumerate an exhaustive registry, matching ADR-0003's stance that
`action` names are namespaced by the domain that owns them, not centrally listed here.

## Consequences

**Accepted costs**

- Cursor pagination is harder for a client to implement "jump to page 40" against than
  offset pagination. Accepted: that operation does not scale at this platform's stated
  capacity baseline regardless of how it is implemented.
- Routing every HTTP error through `redact()` for its `detail` object means an error class
  author has to write an allowlist even for what feels like "obviously safe" debug detail.
  Accepted, and intended: "obviously safe" is exactly the judgement ADR-0003 §3 already
  distrusts.
- `idempotency_key_required` and `precondition_required` both riding on HTTP 428 forces a
  caller to read the machine `code`, not just the status, to know which one it hit.
  Accepted: the alternative is two different codes claiming the same status ambiguously in
  logs and dashboards that group by status first.
- A store now implements four operations (`claim`/`complete`/`release`/`get`) instead of
  two (`get`/`set`), and `claim` must be genuinely atomic at the infrastructure level, not
  merely fast. Accepted: a store that cannot offer an atomic insert-if-absent primitive
  cannot honour this convention at all, and finding that out at integration time is far
  cheaper than finding it in a duplicate card production run.
- A concurrent identical retry that loses the claim race waits, bounded, rather than
  returning instantly. Accepted: the alternative to waiting is either executing twice (what
  QA-01 found) or refusing the second caller outright, which is a worse experience for the
  overwhelmingly common case of a client that simply retried a slow request.

**Obligations on later stories**

- Emit errors through `toProblemResponse`; do not hand-serialise a `{code, message}` object
  in a route handler.
- Every retry-prone write documents that it requires `Idempotency-Key` and enforces
  Decision 4 as written, not a bespoke variant, against a store that implements
  `claim`/`complete`/`release`/`get` with a genuinely atomic `claim`.
- A list endpoint documents its filter/sort allowlist and returns a validation error for
  anything not on it.
- A validation error's `path` field is produced with `escapeSegment`, imported from
  `contracts/js/audit.mjs`, never hand-joined.
- An endpoint whose `execute` can emit a domain event and then fail before returning makes
  that emission idempotent against a retried attempt itself (Decision 4.6); this ADR's
  write-idempotency does not do that for it.
- A new response-shaped error class matches `toProblemResponse`'s content constraints
  (Decision 2.5), not only its property names.

## Alternatives rejected

- **RFC 7807's `type`/`title`/`instance` vocabulary wholesale** — would coexist with the
  `code`/`message`/`status`/`detail` shape three ADRs already converged on independently,
  giving the platform two parallel and partially redundant error vocabularies.
- **A separate, HTTP-specific correlation ID distinct from ADR-0003's `correlation_id`** —
  the exact clash this story was dispatched to avoid; would require a translation layer at
  every point an HTTP request's tracing ID needs to appear in an audit or domain event.
- **Reimplementing idempotency-key comparison at the API layer** — ADR-0001 already defines
  it correctly, including the fingerprint-mismatch case; a second implementation is a
  second place for that logic to drift from the first.
- **Offset pagination as the default, with cursor as an opt-in** — inverts the actual risk:
  offset is the one that silently gets slow as a tenant's data grows, so it should be the
  exception an endpoint opts into with a documented reason, not the default nobody has to
  justify.
- **A denylist of non-filterable fields** — the same failure ADR-0003 §3 already rejected,
  one layer up: a field added later walks straight through.
- **A `get`-then-`execute`-then-`set` sequence for idempotency** — the original design,
  closed by QA-01: not atomic, and the duplicate-effect window it opens is exactly the
  failure the whole convention exists to prevent.
- **A second, custom escaping scheme for the idempotency scope key** — `JSON.stringify` on
  a fixed tuple is already injective and well-tested; inventing a colon-specific escape to
  match `escapeSegment`'s dot-specific one would be a fourth hand-rolled delimiter rule in
  this contract series, not a third.
- **Trusting a builder's callers to validate their own inputs** — duck-typing which
  *properties* an object exposes is not the same claim as trusting what is *in* them;
  QA-03 found the gap between the two directly.
- **Treating an omitted `tenantId` the same as an explicit `null`** — the original
  `tenantId ?? null` default, closed by QA-04: forgetting to resolve a tenant and
  deliberately declaring control-plane work must not read as the same fact.
- **Coercing a malformed `tenantId` into a string before scoping it** — would still put a
  caller-named value in a shared scope key; refusing it outright is the only version of
  this rule that cannot be reasoned around.
- **Merging this ADR's write-idempotency with ADR-0003's event idempotency into one
  mechanism** — they dedupe on different keys for different reasons (a caller's retry
  versus a producer's redelivery); forcing one comparison to serve both would mean neither
  fingerprint means quite what its own ADR says it means.
- **Claiming write-idempotency also protects a domain event `execute` emits before failing**
  — it does not, and Decision 4.6 states the boundary explicitly rather than leaving a
  reader to assume a guarantee that was never built.
