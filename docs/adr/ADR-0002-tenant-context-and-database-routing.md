# ADR-0002 — Tenant context and database routing

- **Status:** Accepted (BIO-ARC-002, wave W01)
- **Date:** 2026-09-20
- **Owner:** Kemi — Chief Architect & Integrator
- **Story:** BIO-ARC-002 — Define tenant context and database routing contract
- **Builds on:** [ADR-0001](ADR-0001-canonical-identifiers-clocks-and-versioning.md) — canonical identifiers, UTC instants, versioning
- **Applies to:** every BIOLOG surface that reaches tenant data — API modules, background workers, sync intake, webhook intake, exports, devices
- **Change policy:** shared contract. Freezes at W01 exit; changing it needs a superseding ADR and recorded Kemi approval.
- **Post-freeze amendment (Decision 6, snapshot 1.1.2):** added 2026-09-22 under BIO-ARC-006-AC04's change-control process. BIO-DEV-001's review surfaced a real gap: no W03+ tenant-plane story can produce real shipped-host positive-path evidence until a resolution channel ships, since `TenantContextMiddleware` unconditionally nulls the context by design (Decision 2). Decision 6 records the interim evidence standard, confirmed by Seyi and god. Kemi's approval recorded here; impact review, regression suite and the new snapshot are in `governance/contract-freeze/v1.1.2/`.
- **Post-freeze amendment (Decision 7, snapshot 1.1.8):** added 2026-09-23 under BIO-ARC-006-AC04's change-control process. BIO-MOB-003's acceptance review found no existing Decision 1.1 channel resolves tenant for a central sync intake with no host, no interactive principal and no internal-job provenance - and that the frozen `offline-mutation.schema.json`'s `tenant_id` body field is exactly the caller-controlled string Decision 1.1 already forbids as a channel. Decision 7 names device-secret authentication as a realization of the existing `principal` channel, not a new one, and records the narrow, internal `ICrossTenantDeviceLookup` mechanism that makes it possible without weakening Decision 4.1/4.3, confirmed by independent review (AC04 step 6) from god. Kemi's approval recorded here; impact review, regression suite and the new snapshot are in `governance/contract-freeze/v1.1.7/` (content) and `v1.1.8/` (confirmation).

## Context

Cross-tenant data access is the first item in the baseline threat model, and the Master
Technical Specification forbids application-level cross-tenant joins outright (§2). The
Tenant Domain Routing spec adds the rule that matters most here: *"the host and
authenticated tenant must not contradict each other silently."*

Every module in this build — identity, credentials, biometrics, document evidence,
access, mobile sync — reads tenant data. If each one resolves "which tenant is this" its
own way, the weakest implementation sets the security boundary for the whole platform.
So resolution is defined once, here, and the modules consume it.

One framing decision shapes everything below. A tenant identifier arriving in a request
is a **claim**, not an authorisation. ADR-0001 §1.5 already says identifiers carry no
authority. This ADR says what the platform does with competing claims.

## Decision 1 — Resolution rules (BIO-ARC-002-AC01)

### 1.1 The three channels

A request may present a tenant through exactly three channels, and no others.

| Channel | Source | Trust |
|---|---|---|
| `host` | The verified `TenantDomain` record matching the request's hostname | Infrastructure-verified; the hostname alone proves nothing until it matches a `VERIFIED` record |
| `principal` | The `tenant_id` claim inside an authenticated token — user session, service account or device certificate | Cryptographically authenticated |
| `service` | An explicit tenant named by a queued job, sync batch or scheduled worker that has no host and no interactive principal | Internal; only valid where the job itself was created under a resolved context |

Anything else a caller can set — a query parameter, a request body field, an `X-Tenant-Id`
header, a subdomain guess — is **not a channel**. Those are caller-controlled strings, and
treating one as a tenant claim is the cross-tenant identifier substitution in the threat
model, written as a feature.

### 1.2 Channels must agree; they are not ranked

Resolution collects every channel that supplied a tenant, then applies two rules:

1. **At least one channel must supply a tenant.** Zero channels is a failure, never a
   default (Decision 2).
2. **Every channel that supplied a tenant must name the same tenant.** Any disagreement
   fails closed and is audited.

There is deliberately **no precedence order**. Ranking the channels would mean that when
the host says tenant A and the token says tenant B, the platform picks a winner and
continues — and a disagreement between an infrastructure-verified fact and a
cryptographically authenticated claim is not a tie to be broken. It is either a
misconfigured domain or an attempt to reach another tenant's data with a valid token.
Both need a human to look, and neither is served by silently choosing. Unanimity turns an
invisible compromise into a logged, refused request.

This is what "must not contradict each other silently" resolves to in code.

### 1.3 Host resolution

The hostname is normalised before lookup — lowercased, trailing dot removed, port
removed — and then matched against `TenantDomain`. Only a record with status `VERIFIED`
resolves. `PENDING_VERIFICATION`, `SUSPENDED` and `RETIRED` do not.

Normalisation is a security boundary, not a convenience, so it is strict about what it
will normalise. A hostname containing non-ASCII characters is **rejected rather than
converted**: `pаypal.example` with a Cyrillic а and `paypal.example` are different byte
strings that render identically, and a resolver that quietly converts one into the other
has built a homoglyph confusion into the tenant boundary. Punycode conversion belongs at
the edge, before this contract sees the host.

An unrecognised or unverified host fails closed as `host_not_recognised`, surfaced as
**404, not 403**. A 403 would confirm that a hostname exists but is not yet verified,
which is a free enumeration oracle over the tenant registry.

### 1.4 Control-plane context is a separate thing

Control-plane work — the tenant registry, domains, licensing, routing metadata — resolves
**no tenant**. It uses an explicit control-plane context, which is a distinct type rather
than a tenant context with a null tenant, so "no tenant resolved" can never be mistaken
for "tenant resolved successfully".

A control-plane context may never be used to open a tenant repository. Attempting it is a
programming error and throws `control_plane_context_misuse`. Platform-wide reporting uses
explicit control-plane workflows, not a loop over tenant databases.

### 1.5 Resolution happens once, before data access

Tenant context resolves before any tenant repository is constructed (API Architecture).
A repository cannot be built without a resolved context, which is what makes "we forgot
to filter by tenant" unrepresentable rather than merely discouraged.

## Decision 2 — No implicit or default tenant (BIO-ARC-002-AC02)

There is no default tenant, no fallback tenant, no environment-configured tenant, no
"first tenant in the table", no "single tenant in development", and no inference from a
host suffix that did not match a `VERIFIED` record.

If no channel supplies a tenant, the request fails with `tenant_context_missing`. It does
not proceed with an empty, null or wildcard tenant.

This rule reads as obvious and is the one most likely to be quietly broken, because a
default tenant is the shortest path to a working local dev environment. It is prohibited
precisely there: a fallback that exists in development is a fallback that ships. Local
development uses an explicitly seeded synthetic tenant and a verified local hostname, the
same way production does.

## Decision 3 — Tenant context is immutable for the request (BIO-ARC-002-AC03)

Once resolved, the context is frozen for the lifetime of the request or job:

- No setter, no swap, no re-resolution, no "act as tenant X" mid-request.
- Mutating a resolved context throws `tenant_context_immutable`.
- Asynchronous and nested work inherits **the same frozen object**, not a copy that could
  drift from it.
- A background job resolves its own context once at the start and holds it for that job;
  a job may not inherit a context from the request that enqueued it, because the two have
  different lifetimes and a long-running job would otherwise outlive its authorisation.

Immutability is what makes a tenant check at the boundary meaningful. If the context can
change after the check, the check describes a moment rather than the request, and every
later authorisation decision rests on a value that may since have moved.

## Decision 4 — Database routing (BIO-ARC-002-AC01, AC04)

### 4.1 The binding is looked up, never supplied

Tenant → database is resolved from a `TenantDataBinding` record in the control plane,
keyed by the resolved `tenant_id`.

The request may **never** carry a database name, schema, host, connection string or
binding key, in any channel, including internal ones. If a caller can name the database,
the tenant boundary is whatever the caller says it is, and every other rule in this ADR
becomes decoration. DNS configuration likewise never exposes or determines the internal
tenant database address (Tenant Domain Routing).

A tenant with no `ACTIVE` binding fails closed as `tenant_binding_unavailable`. The
platform does not fall back to a shared or default database.

**A context must be provably the product of resolution.** Checking that an object *looks*
like a context — right `kind`, a canonical tenant, a non-empty binding — is not enough,
because field shape is exactly what a forged object imitates. A hand-built object carrying
a `binding_key` would otherwise select its own database, which is "a caller can name the
database" arriving through the back door.

So provenance is tracked out of reach of callers, keyed by object identity, and the
authoritative tenant and binding are held there rather than on the object that is handed
out. Opening a repository reads the binding that control-plane resolution recorded. A
`binding_key` set on any object a caller supplies is inert: it is never read, so it can
never route anything. An object that did not come from resolution is refused as
`tenant_context_not_resolved`, and that refusal is audited like any other denial.

### 4.2 Every tenant-plane access is checked against the context

Each tenant-plane record carries `tenant_id` (ADR-0001 §3.1). Reading or writing one
asserts the record's `tenant_id` equals the context's. This is belt and braces over the
routing: routing should mean a foreign record is unreachable, and the assertion is what
catches the case where it was reachable anyway — a mis-seeded binding, a cached
connection, a query that escaped the repository layer.

### 4.3 A denied cross-tenant access answers "not found"

A cross-tenant access is denied and surfaced to the caller as 404 `not_found`, never 403.
ADR-0001 §1.5 requires this: a 403 confirms the record exists, which turns any identifier
into an existence oracle over another tenant's data. The caller learns nothing; the audit
learns everything.

## Decision 5 — Cross-tenant denials are audited (BIO-ARC-002-AC04)

Every denial — a channel conflict, a cross-tenant record access, a control-plane misuse, a
forged context — writes an append-only audit event **before** the response is returned.
The audit write is part of the denial path, not a side effect that a caller or a later
refactor can skip.

**The audit sink is a hard dependency of denying, not an option.** If the sink is missing,
or fails, the denial is not returned: the call fails closed with `audit_unavailable`
instead.

**"Written" means the sink's promise has settled, not that the call was made.** A real
audit store is asynchronous. Calling it and moving on returns the denial while the write
is still in flight, and a rejection arriving after that can no longer become
`audit_unavailable` — the caller already has its answer. That is the same defect as an
optional sink, wearing a timer.

**Denying is asynchronous because durable audit is asynchronous.** The operations that can
deny — resolving a context, opening a repository, checking a record against its tenant —
therefore return promises, and await the write before refusing. Making the audit
dependency visible in the shape of the call is the point: a caller cannot forget to wait
for something it has to await anyway. A synchronous sink is still accepted; it simply has
nothing to await.

A sink that never settles is also a failure to persist. The write is bounded by
`AUDIT_TIMEOUT_MS` (5 s by default, overridable per call), after which the denial fails
closed the same way. A hung audit store is not a reason to hold a request open, and not a
reason to answer without the record. An optional audit means the rule above holds only when someone remembered to
wire a sink, and an unaudited denial is the one outcome worse than the access it refused —
the caller is stopped and nobody learns it happened. Fail-closed here costs availability
on a path that is already refusing; silence costs the security signal permanently.

`audit_unavailable` is therefore not one of the reason codes an event can carry. It names
the failure to write an event, so no event exists to carry it.

The event carries: its own canonical `evt_` id, the resolved (or attempted) tenant, the
tenant that was reached for, the principal, the resource type and id, the correlation id,
the machine reason code, and `received_at` as the authoritative instant (ADR-0001 §2.4).

Two constraints on its contents:

- **No payload, ever.** The audit records that access was refused and to what, not the
  data that was refused. An audit trail that copies the record it protected is a second
  copy of the thing outside the boundary.
- **Both tenants are recorded.** Logging only the context's tenant makes a cross-tenant
  attempt indistinguishable from an ordinary miss, and the pair is the whole signal.

Audit events are append-only: no `version`, no updates, no deletion (ADR-0001 §3.3).

## Decision 6 — Interim review evidence while no resolver exists yet (BIO-ARC-002-AC01)

`TenantContextMiddleware` (BIO-FND-008) is a hook, not a resolver: `Context` is `null` on
every request, unconditionally, until a real resolution story ships (Decision 1's three
channels are specified here; nothing implements them yet). Every W03+ tenant-plane
feature that reviews a mutation needs *some* positive-path evidence that its logic is
correct when a tenant **is** resolved — and, before any resolution story ships, that
evidence cannot come from the real shipped host, because the real shipped host cannot
produce a non-null context yet. Demanding it anyway asks for proof of something that does
not exist.

**Interim closure, until the first real resolver ships:** a feature's audited-mutation
acceptance evidence is a real shipped-host request proving today's actual, accepted
behavior — `tenant_context_missing` on every unresolved request, unconditionally —
**plus** an injected-resolved-context test proving the feature's own logic is correct
given a tenant. The second half is necessarily a test double; it is evidence of the
feature's logic, not of the host's resolution behavior, and must not be described as the
latter. This is a bounded accommodation for a real, temporary sequencing gap — it is not
permission for an implicit tenant, a default tenant, or test-only wiring anywhere near
production code (Decision 2 continues to apply in full).

**The accommodation ends per-channel, not all at once.** BIO-TEN-002 (tenant domain
mapping and verification, W03) implements the `host` channel exactly as Decision 1
specifies it — a real, production resolution mechanism, not a shim. Once it ships, any
feature reachable through a verified custom hostname has a genuine shipped-host positive
path and the interim closure above no longer applies to it. A later story implementing the
`principal` channel (token/session-claim resolution) extends coverage further; it does not
gate or postpone the first real positive path BIO-TEN-002 already provides.

**Obligation on every W03+ tenant-plane story accepted under this interim closure:** once
the channel it depends on ships, its positive-path evidence is upgraded to a real
shipped-host test — tracked as a named follow-up on that story or the channel story that
unblocked it, not silently left as permanent test-double coverage.

## Decision 7 — Device-secret authentication realizes the `principal` channel (BIO-ARC-002-AC01)

BIO-MOB-003's central offline-sync intake has none of Decision 1.1's three channels
available in their existing shape. There is no `host`: the endpoint is a single fixed
address (`sync.biolog.invalid` in production), not a verified per-tenant hostname a mobile
client resolves against. There is no interactive `principal`: no authentication story has
shipped (`BIO-INT-001`, W12), so there is no session or token to carry a claim. It is not
`service` either: `service` is for "a queued job, sync batch or scheduled worker that has
no host and no interactive principal," explicitly scoped to internal work "created under a
resolved context" — an external mobile device sending its own HTTP request is not that.

`offline-mutation.schema.json` carries `tenant_id` as a request body field. Decision 1.1 is
unambiguous that a body field is not a channel, and building this endpoint to trust it would
be "the cross-tenant identifier substitution in the threat model, written as a feature" —
this ADR's own words, not weakened by this being a device rather than a browser.

**The resolution:** the calling *device* authenticates via a provisioned shared secret — a
device id / device secret header pair, checked by constant-time hash comparison against an
out-of-band-provisioned credential store (`BIO-PHO-002`'s `IDeviceAuthenticator`/
`IDeviceCredentialStore` shape, mirrored for this story). Once authenticated, the tenant is
resolved from that device's own registered `TenantId` (`Device.cs`, `BIO-DEV-001-AC01`) —
never from anything the request supplies. The body's own `tenant_id` is still read, but only
as a claim cross-validated against this independently resolved value; a mismatch is a hard
rejection (`tenant_mismatch`), the same claim-vs-resolution discipline Decision 1.1 already
applies to every other channel.

**This is a realization of the `principal` channel, not a fourth channel.** Decision 1.1's
`principal` row names its trust source as "a cryptographically authenticated token — user
session, service account or device certificate." A device that has proven possession of a
provisioned, hashed, constant-time-compared secret is authenticated by the same structural
property a token or certificate provides — proof of possession of something an attacker
does not have — even though this specific attestation is honestly weaker than PKI/mTLS
(no certificate chain, no real-world identity binding, no revocation infrastructure). It is
named here as an instance of `principal`, not a new row in Decision 1.1's table, because
the property Decision 1.1 actually requires of that channel — a claim that arrives
authenticated, not asserted — holds.

**The mechanism, and why it needed a new capability rather than reusing an existing one.**
`IDeviceRepository`/`DeviceService` (`BIO-DEV-001-AC04`) already require a resolved
`TenantContext` on every method, by design — the same "cannot be built without a resolved
context" discipline this ADR requires everywhere else. Resolving a tenant FROM the device is
therefore structurally impossible through that surface: it needs the tenant to already be
known to ask about a device in it. `ICrossTenantDeviceLookup`
(`api/Biolog.Api/Features/DeviceRegistry/ICrossTenantDeviceLookup.cs`) is the fix — a single
new method, `GetByIdAsync(deviceId)`, with no `TenantContext` parameter, added as its own
narrow interface rather than an overload, so the exception is visible at the type level.
Three properties keep it from reopening Decision 4.3's cross-tenant protection for anyone
else:

1. **`internal`, and reachable only from `Biolog.Api`'s own composition code.** The one-way
   feature-project dependency rule (`scripts/check-module-project-references.sh`) means no
   feature project — including `api/features/bio-mob-003/**` itself — can reference it even
   if it wanted to; only a module adapter living directly in `Biolog.Api.Modules` can.
2. **It answers "what tenant does THIS device belong to," never "which devices exist."**
   There is no enumeration surface, no query by anything other than an exact device id the
   caller already named.
3. **The one adapter that calls it (`BioMob003Module`'s
   `DeviceTenantResolutionGatewayAdapter`) does so only after device-secret authentication
   has already succeeded.** An unauthenticated caller reaches nothing; the lookup is never
   itself the authentication step.

Decision 4.1's "the binding is looked up, never supplied" continues to hold without
modification: the database binding is still resolved from a `tenant_id`, and that
`tenant_id` is still never accepted from the caller directly — what changed is which
authenticated input resolution reads (a device's own record, instead of a host or a token),
not that resolution is optional or that a caller-supplied value can shortcut it.

**Obligation on every future device-authenticated story.** A story that needs its own
device-to-tenant resolution reuses this exact shape — device-secret authentication, then a
narrow, internal cross-tenant lookup scoped to exactly the registry it needs, never a
broader "list devices" or "list tenants" capability — and adding a new cross-tenant lookup
to any tenant-scoped registry needs Kemi's approval, the same as this one, because it
touches the same must-be-resolved-not-supplied invariant Decision 4.1 protects everywhere
else.

**Explicit non-goal, stated so it is never overclaimed later.** This is not PKI/mTLS device
authentication and does not claim to be. It proves possession of a provisioned secret, not a
certificate-chain-verified real-world identity. A future story that gives devices real
certificate-based authentication is where a stronger channel realization would plug in at
the same seam; nothing about this Decision blocks that or requires revisiting it — the
`principal` channel already anticipated exactly this class of upgrade ("device certificate"
is already its own stated example).

## Consequences

**Accepted costs**

- Unanimity rejects some legitimately confusing setups — a tenant that moves a domain
  while tokens still carry the old tenant will see refusals until the migration finishes.
  Accepted: the alternative silently serves one tenant's data under another's hostname.
- No default tenant makes local development marginally more work. Accepted, and the point:
  see Decision 2.
- The per-record tenant assertion duplicates work the routing should already guarantee.
  Accepted: it is cheap, and it is the check that catches the failure the routing missed.
- Making the audit sink mandatory means a caller that never wired one now gets a 503 where
  it previously got a clean refusal. Accepted, and intended: that 503 is a wiring defect
  becoming visible, on a path that was already denying access either way.
- Denying asynchronously makes `resolveTenantContext`, `bindingForRepository` and
  `assertRecordInTenant` return promises, so every caller awaits them even on the allow
  path. Accepted: each of those calls already sits in front of an asynchronous database
  access, and the alternative is a rule that holds only until someone wires a real audit
  store behind it.

**Obligations on every later story**

- Take a resolved context; never resolve tenancy locally, and never accept a tenant
  identifier from a request field.
- Construct repositories from the context's binding only.
- Every story touching tenant data ships a cross-tenant negative test (Threat Model).
- New background jobs resolve their own context at start; they do not inherit one.
- Before a real resolution channel ships, audited-mutation acceptance evidence follows
  Decision 6's interim closure; once the channel it depends on ships, that evidence is
  upgraded to a real shipped-host positive path, tracked as a named follow-up.
- A story adding its own device-authenticated (or otherwise non-host, non-token) tenant
  resolution follows Decision 7's shape exactly — authenticate first, then a narrow,
  internal, single-purpose cross-tenant lookup — and needs Kemi's approval for the new
  lookup, the same as `ICrossTenantDeviceLookup`.

## Alternatives rejected

- **Precedence between channels** (host wins, or token wins) — resolves conflicts by
  picking a side, which is exactly the silent contradiction the spec forbids.
- **Tenant id in a request header or body field** — caller-controlled, so the boundary
  becomes advisory.
- **A default tenant for development** — ships.
- **403 on cross-tenant access** — correct-looking, and an existence oracle over every
  other tenant's identifiers.
- **Validating a context by its field shape** — the check a forged object passes most
  easily, because shape is the part a caller controls entirely.
- **Treating the audit sink as optional** — leaves the "audit before response" rule true
  only by convention, and fails silently in exactly the case it exists for.
- **Calling an asynchronous sink without awaiting it** — looks identical to a correct
  implementation in every synchronous test, and is wrong against every real audit store.
- **Requiring the sink to be synchronous** — would keep the API simple and would push the
  durability problem onto each caller, where it is invisible and unenforceable.
- **A nullable tenant on one context type** — makes "unresolved" and "control plane"
  structurally identical, so one forgotten null check crosses the boundary.
- **A shared interim tenant-resolution shim built ahead of Decision 1's real channels
  (Decision 6)** — BIO-TEN-002 already owns building the real `host` channel exactly as
  specified here; a separate shim would duplicate work already correctly sequenced,
  arrive with weaker guarantees than the real mechanism, and risk becoming, in practice,
  the implicit tenant Decision 2 exists to prevent.
- **Relaxing acceptance to accept test-double proof indefinitely, with no upgrade
  obligation (Decision 6)** — would let temporary evidence become permanent by omission;
  the named follow-up is what keeps the accommodation bounded.
- **Trusting the offline-mutation payload's own `tenant_id` field to route the mutation
  (Decision 7)** — Decision 1.1 already forbids a request body field as a channel; doing it
  anyway would be the cross-tenant identifier substitution the threat model exists to catch,
  not a lesser case of it because the caller happens to be a device rather than a browser.
- **Declaring device-secret authentication a fourth, new channel rather than a realization
  of `principal` (Decision 7)** — the property Decision 1.1 actually requires of that
  channel (a claim that arrives authenticated, not asserted) already holds; adding a fourth
  row would fragment one requirement into two without changing what either has to prove.
- **Widening `IDeviceRepository`/`DeviceService`'s existing public methods to accept a
  device id without a `TenantContext`, instead of a separate narrow interface (Decision
  7)** — would put the one, deliberate exception to "every method needs a resolved context"
  behind the same names as sixteen other tenant-scoped methods, invisible at the type level
  and one accidental overload-resolution away from a real caller using it by mistake.
