# BIO-TEN-002 — Tenant domain mapping and verification

**Owner:** Tunde — Identity & Tenant Engineer
**Wave:** W03 / W03.2
**Conflict group:** `tenant-domain`
**Dependencies:** BIO-TEN-001 (build-wave sequencing only — see "Why no runtime dependency on BIO-TEN-001")

## User story

As a tenant, I need one or more verified hostnames such as `portal.example.edu.ng` to route
to the correct BIOLOG tenant.

## Acceptance criteria and where each is proven

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | `TenantDomain` stores hostname, tenant, status and primary flag | `Domain/TenantDomain.cs` — exactly those four fields plus versioning metadata; `BioTen002Module` registers into the shared host shape | `TenantDomainServiceTests`, `TenantDomainScopeTests`, `TenantDomainEndpointsIntegrationTests` |
| **AC02** | Host resolution is case-normalised and exact | `Resolution/HostNormalizer.cs` (lowercase, strip port, strip one trailing dot, reject non-ASCII outright); `TenantDomainService.ResolveAsync` does an exact dictionary lookup on the normalised value, never a prefix/suffix/wildcard match | `HostResolutionTests` |
| **AC03** | Unverified/unknown hosts fail closed | `TenantDomainError.HostNotRecognised()` — a single, deliberately uninformative 404 used for both "no such hostname" and "hostname exists but is not VERIFIED" | `HostResolutionTests.An_unregistered_host_fails_closed_as_host_not_recognised`, `A_pending_verification_host_fails_closed_identically_to_an_unknown_one`, `A_suspended_host_fails_closed`, `A_retired_host_fails_closed` |
| **AC04** | Duplicate hostname assignment across tenants is prevented | `InMemoryTenantDomainRegistry` keys its store by hostname and uses an atomic `TryAdd`, making two records ever sharing one hostname unrepresentable regardless of tenant | `TenantDomainServiceTests.Register_rejects_a_hostname_already_claimed_by_another_tenant` |
| **AC05** | A-record/CNAME deployment detail is separated from application routing | No DNS deployment field (A-record, CNAME target, IP, TTL) exists anywhere in `Domain/TenantDomain.cs`, `Api/TenantDomainRequests.cs` or `Api/TenantDomainResponse.cs` — enforced by omission, the same mechanism BIO-TEN-001 uses for its own control-plane field-shape guarantee | `TenantDomainScopeTests` |

## Design decisions and scope boundaries

### Why `TenantDomain` has no canonical id of its own

`contracts/schemas/tenant-domain.schema.json`'s required fields are exactly
`hostname, tenant_id, status, is_primary` — no `id`. `Hostname` is this record's natural key:
AC04 already requires it to be globally unique, and ADR-0001 §1.2 registers exactly six
identifier types (tenant, person, credential, device, document session, event) — "a feature
story may not invent a prefix locally" without a registry entry, a superseding/amending ADR
and Kemi's recorded approval. Minting a `dom_` id would be exactly that, for no AC that needs
it. A hostname's `tenant_id` is immutable for the life of the record (no "transfer to another
tenant" operation exists), and a `RETIRED` record's hostname is never freed for reassignment —
see `TenantDomainService`'s class header for the full reasoning, including why that immutability
is what makes it safe to read a hostname's tenant id once, outside any lock, purely to choose
which lock to take.

### Why no runtime dependency on BIO-TEN-001

The story metadata lists `BIO-TEN-001` as a dependency, and `RegisterDomainRequest.TenantId`
is validated for *shape* (`CanonicalId.IsTenantId`) — but this story never calls into
BIO-TEN-001's actual registry to check the tenant *exists*. Each story owns exactly its own
`api/features/<story-key>/**` subtree (`governance/feature-path-ownership.json`); a
`ProjectReference` from one feature story's project to a sibling feature story's project would
cross that boundary the same way writing into a sibling's owned paths would. "Dependencies:
BIO-TEN-001" is a build-wave/conceptual sequencing note (a tenant boundary must exist before a
domain can meaningfully reference one) — `BioTen002Module.cs`'s header makes this explicit.

### ADR-0003 Decision 7's audit-atomicity pattern, applied here

BIO-TEN-001 discovered this pattern the hard way, across five adversarial review rounds; it is
now ADR-0003 Decision 7, a codified, cross-cutting requirement for every audited mutation in
the codebase. `TenantDomainService`'s class header records the full reasoning; in short, every
mutating method:

1. Holds an in-process, per-aggregate lock across the whole read-validate-audit-commit
   sequence.
2. Audits BEFORE committing — nothing is visible to another writer before its audit event is
   confirmed durable.
3. On an ambiguous `ITenantDomainAuditSink.WriteAsync` throw, verifies via `ExistsAsync` before
   deciding, rather than assuming failure.
4. Passes `CancellationToken.None` to the commit call once the audit event is confirmed —
   audit is the genuine point of no return, not the caller's live token.

**This story is not a copy-paste of BIO-TEN-001's implementation — it is a fresh application
of the same mandated pattern to a genuinely different aggregate shape**, and that difference
matters:

- **Two lock scopes, not one.** `RegisterAsync` locks by *hostname* (its only real conflict is
  "did another Register call, from ANY tenant, claim this exact hostname first" — a global,
  cross-tenant invariant a tenant-scoped lock could not serialize). `UpdateStatusAsync` and
  `SetPrimaryAsync` lock by *tenant id* instead, because `SetPrimaryAsync` must atomically flip
  `is_primary` off one existing domain and on another for the SAME tenant — genuinely a
  two-record mutation whose real aggregate boundary is "this tenant's domain set", not one
  domain. Locking the single-record operations by hostname instead (a narrower, more tempting
  scope) would let one of them interleave with a same-tenant `SetPrimaryAsync` using a
  DIFFERENT lock than the one protecting that other domain's read — reproducing the
  audit-says-Allowed-but-commit-fails shape Decision 7 exists to close, just via a lock-scope
  mismatch instead of a missing lock.
- **Verified this design decision is load-bearing, not merely asserted.** For the
  two-record `SetPrimaryAsync` case specifically, temporarily narrowed its lock to
  per-hostname (matching what a less careful design might have shipped), re-ran
  `SetPrimary_on_two_different_domains_of_the_same_tenant_never_leaves_two_domains_primary`,
  and confirmed it then **fails** — both domains ended up `is_primary = true` simultaneously,
  with no error raised anywhere, because each `SetPrimaryAsync` call only ever CAS'd against
  its own record's key and neither lock saw the other's read. Restored the tenant-scoped lock
  and confirmed all 74 tests pass again. (The same "break it on purpose" verification BIO-TEN-001
  round 4 established as a durable practice for any concurrency test.)

### Scope: resolution audit

`TenantDomainService.ResolveAsync` is read-only: no lock, no audit event. ADR-0002 Decision 5
mandates audit-before-deny for the full, multi-channel tenant-context resolution flow (host +
principal + service channels agreeing, cross-tenant record access, control-plane misuse,
forged context) — this story implements only the `host` channel piece of that (ADR-0002
Decision 6 names BIO-TEN-002 as exactly this), not the full resolver that assembles all three
channels and denies on disagreement. Auditing every host lookup miss here would be scope creep
against a hot, read-heavy path with no AC asking for it. The later story that assembles the
full ADR-0002 Decision 1 resolver (consuming this resolver as one input) inherits Decision 5's
audit obligation for that assembled flow.

### Deferred: hostname reuse after retire

A `RETIRED` domain's hostname is never freed for a different tenant to claim —
`RegisterAsync` rejects any hostname that already has a record, regardless of that record's
status. Freeing a retired hostname safely would need to consider in-flight cached resolutions
elsewhere in the platform and is a materially bigger question than flipping a status enum; no
AC requires it, and building it speculatively would be an unrequested feature.

### Deferred: no "reinstate" action

Only `PENDING_VERIFICATION → VERIFIED` (verify), `VERIFIED → SUSPENDED` (suspend) and
`{PENDING_VERIFICATION, VERIFIED, SUSPENDED} → RETIRED` (retire) are implemented
(`TenantDomainService.Transitions`). Reactivating a suspended or retired domain has no AC
asking for it; the honest current answer is "not supported", not a silently-permitted
transition through an unvalidated status field.

### `is_primary` can only be set on an already-VERIFIED domain, never at registration

`RegisterAsync` always creates a domain with `IsPrimary = false`, regardless of any caller
input — there is no `is_primary` field on `RegisterDomainRequest` at all. Only
`SetPrimaryAsync`, which requires the target to already be `VERIFIED`, may set it. This
eliminates a genuine design hazard: if registration could set `is_primary = true` directly, a
new domain's creation (hostname-locked) and an existing sibling's primary-flip (tenant-locked)
would need to coordinate across two different lock scopes for the SAME invariant — the exact
lock-scope-mismatch trap described above, reintroduced through a different door.

### No `Idempotency-Key`

ADR-0004 §4.3 names card activation, production completion, offline sync intake and webhook
intake as the endpoints that require it. None of this feature's endpoints are those.

### No RBAC enforcement / actor header seam

Same disclosed boundary as BIO-TEN-001: `BIO-IDN-006` ("rbac-core", W04) has not landed yet, so
every mutating endpoint requires an explicit `X-Actor-Id` header (`ActorResolver`) instead of a
fabricated identity. The `/resolve` endpoint deliberately requires no actor — it is the
anonymous, request-time host channel, not an admin action.

### No pagination on list-by-tenant

A tenant's domain count is small (a handful of custom hostnames), not a high-cardinality
table, so `GET /control-plane/tenant-domains?tenant_id=...` returns the full list with no
cursor. ADR-0004 does not mandate pagination on every list endpoint, only gives the pattern
when needed.

## BIO-TEN-002-QA-01 (Seyi round 1, High) — the resolve endpoint was not the host channel it claimed to be

Two distinct problems, both real:

1. **The endpoint read the wrong source.** `TenantDomainEndpoints.ResolveAsync` read
   `Request.Query["host"]` — a caller-controlled query parameter. ADR-0002 §1.1 explicitly
   excludes a query parameter as a tenant channel ("Anything else a caller can set — a query
   parameter, a request body field, an `X-Tenant-Id` header, a subdomain guess — is **not a
   channel**"). **Fixed**: it now reads `context.Request.Host.Host` — the request's real HTTP
   `Host` header, ASP.NET Core's own authority parser having already split off any port. The
   `/resolve` route no longer accepts or reads a `?host=` parameter at all.
2. **The docs overclaimed what was shipped.** This README and several doc comments called the
   endpoint "the real host channel" without qualification, when in fact it was neither reading
   the real authority nor reachable from the packaged API. **Fixed**: every such reference now
   says precisely what is and is not true — see below.

### What is proven now (BIO-TEN-002-QA-01 follow-up, integration completed by Kemi)

Seyi's round-2 re-review (High, evidence commit `7cff146`) confirmed the resolution LOGIC was
correct but held the story at `CHANGES_REQUIRED`: the two integration steps below are exactly
what ADR-0002 Decision 6 means by BIO-TEN-002 "shipping" the `host` channel, not an optional
follow-up integration merge like a plain module registration — so they had to land in this
story, not be handed to a later merge. Both are now done, in `api/Biolog.Api/**` (shared,
outside this story's owned paths, per `docs/architecture/SHARED-CONTRACT-FREEZE.md`
BIO-ARC-006-AC03 — Kemi performed this part directly, the same role recorded for
BIO-DEV-001/BIO-DOC-001):

1. `Biolog.Api.Modules.BioTen002Module` (moved out of `api/features/bio-ten-002/**`, the same
   one-way-dependency fix BIO-TEN-001's integration needed — see
   `scripts/check-module-project-references.sh`) is registered in `Program.cs`'s module array.
2. `TenantDomainService.ResolveAsync` is wired into `TenantContextMiddleware`'s `host` channel
   (ADR-0002 Decision 1.3) — see that class's header for exactly what is and is not proven:
   only the `host` channel; `principal`/`service` and cross-channel agreement (Decision 1.2)
   remain for the story that assembles the full resolver.

A real client of the packaged BIOLOG API sending a request under a verified custom hostname now
resolves a tenant — proven through `Biolog.Api`'s actual `Program.cs` and middleware pipeline
via `WebApplicationFactory<Program>`
(`tests/dotnet/Biolog.IntegrationTests/TenantContextHostChannelTests.cs`: verified, unknown and
unverified-pending hosts, plus a DI-resolution regression check on an unrelated endpoint), not
just the feature-local `TestServer` `TenantDomainEndpointsIntegrationTests` already covered.
`tests/dotnet/Biolog.UnitTests/Tenancy/TenantContextMiddlewareTests.cs` covers the same three
cases directly against `TenantContextMiddleware` with a real (non-fake) `TenantDomainService`.

Per ADR-0002 Decision 6, this is the genuine shipped-host positive path: the interim
test-double-evidence accommodation no longer applies to any feature reachable through a
verified custom hostname.

## API surface

- `POST /api/v1/control-plane/tenant-domains` — register (`PENDING_VERIFICATION`, `is_primary: false`)
- `GET /api/v1/control-plane/tenant-domains?tenant_id=...` — list a tenant's domains
- `GET /api/v1/control-plane/tenant-domains/{hostname}` — get one
- `PUT /api/v1/control-plane/tenant-domains/{hostname}/status` — verify/suspend/retire (`{version, status}`)
- `PUT /api/v1/control-plane/tenant-domains/{hostname}/primary` — set primary (`{version}`)
- `GET /api/v1/tenant-domains/resolve` — the `host` channel resolution endpoint (ADR-0002
  Decision 1.3); anonymous, read-only; also reachable indirectly on every request through
  `TenantContextMiddleware`, which populates `ITenantContextAccessor.Context` from the same
  logic — see "What is proven now" above

## TenantDomain fields

`hostname` (natural key, lowercase ASCII, ≤253 chars), `tenant_id` (`ten_` identifier,
shape-validated only), `status` (`PENDING_VERIFICATION` / `VERIFIED` / `SUSPENDED` /
`RETIRED`), `is_primary` (bool), `version`, `created_at`, `updated_at`.

## Post-acceptance fix (2026-10-07): OPS002-F01/OPS002-F02

BIO-OPS-002's acceptance review (Seyi) BLOCKED on two High findings against the demo-session
bridge (`api/Biolog.Api/Tenancy/`), the cross-cutting infra this story owns and has
historically extended directly (`BIO-TEN-002: ship the real host channel into the packaged
API`, `BIO-TEN-002-QA-03`), not BIO-IDN-006 (RBAC) or BIO-TEN-003 (tenant feature
configuration) - the other two dependency stories god asked to check. Routed here.

**OPS002-F01 (High) - the selector-issued tenant claim was ignored.** `DemoSessionTenantResolver`
(added by TD-WEB-PROVENANCE-BRIDGE round 5) always resolved to the ONE configured
`WebSessionBridge:SyntheticTenantId` regardless of the verified token's own `tenantId` claim
("alpha"/"beta"), so switching the shell's tenant selector never changed the backend tenant,
repository data, or permission context - AC01 was cosmetic.

Fix (round 1): `TryVerify` (renamed from the old `TryResolve`, which is now a thin wrapper over
it) looks the verified claim up in a two-entry allowlist: "alpha" → `WebSessionBridge:SyntheticTenantId`
(unchanged), "beta" → the new, OPTIONAL `WebSessionBridge:SyntheticTenantIdBeta` - so every
existing demo-bridge story (BIO-PHO-003, BIO-TRN-003, BIO-ATT-003, BIO-DEV-004, BIO-TEN-003's
own reverify) that only configures the primary key keeps working identically; purely additive,
opt-in behavior.

**Round 2 (SY-OPS002-03, High) - round 1's "beta" still silently fell back to alpha's tenant
when unconfigured.** Seyi's round-2 review correctly rejected this: in the DEFAULT/supported
configuration (nobody sets the beta key today), selecting "beta" still silently resolved to
alpha's tenant - the identical cosmetic-switch defect OPS002-F01 was originally about, just
moved one level down, and round 1's OWN new test (`OPS002_F01_beta_falls_back_to_the_same_tenant_as_alpha_when_the_second_key_is_unconfigured`)
had encoded that fallback as the intended behavior rather than testing against it. Fixed by
following this exact class's own already-established "optional config gap" precedent
(`LoadFromConfiguration`'s secret-too-short handling: unconfigured means the mechanism never
activates): `ResolveSyntheticTenantId` now returns `null` when the claimed tenant's own key is
unset, and `TryVerify` rejects the whole token in that case - falling through to the ordinary
host-channel path, never substituting alpha's tenant. The old test was rewritten
(`SY_OPS002_03_beta_is_rejected_outright_...`) to assert rejection instead of fallback, and a
matching unit test added directly against `TenantContextMiddleware`.

**Why `DemoSessionIdentitySeed` was NOT also extended to seed into beta** (tried first,
reverted): `InMemoryPersonRepository` keys `Person` rows by `Person.Id` alone - a single,
process-lifetime, cross-tenant dictionary, not `(TenantId, PersonId)` - so seeding the same
three fixed demo identities into a second tenant throws a genuine `person_id_collision`, not a
benign no-op (caught by this fix's own new tests before it ever reached a reviewer). Minting
three distinct person ids for beta would dodge the collision, but the web side's
`deriveDemoActorId(roleId)` is deliberately tenant-agnostic by design, so beta's identities
would still never actually be reached without a second, tenant-aware actor-derivation
mechanism on the web side too - well beyond OPS002-F01's own two named findings. Beta is
therefore a genuinely separate, currently-empty backend tenant when configured: still a real,
isolated data context (AC01's own requirement), and arguably a stronger proof of AC02's
cross-tenant isolation than a seeded-but-unreachable-identity design would have been.

**OPS002-F02 (High) - no durable audit record of a tenant/role context change existed at
all.** `startDemoSession` (`web/context/session-actions.ts`) revoked the old token and issued a
new one but called no audit API; no backend endpoint or sink for this existed anywhere.

Fix (round 1): a new backend boundary, `POST /api/v1/tenancy/demo-session/context-change`
(`api/Biolog.Api/Tenancy/DemoSessionContextAuditEndpoints.cs`), backed by a dedicated
`DemoSessionContextAuditEvent`/`IDemoSessionContextAuditSink` (own typed event, not a reuse of
the narrower `Biolog.Api.Audit.AuditEvent`, matching this codebase's own convention for any
feature needing an old/new transition recorded). Authorization is the demo session token
itself (`DemoSessionTenantResolver.TryVerify`), not a second, separate `ActorProvenanceGate` -
see that endpoint class's own header for why a second provenance secret and a permission check
would both be authorization theatre here, not a real boundary. `startDemoSession` called this
endpoint right after issuing the new token, but AFTER committing the session cookie, fire-and-
forget (not awaited), swallowing any failure.

**Round 2 (SY-OPS002-04, High) - round 1's audit call was best-effort AFTER the commit, so a
successful switch could leave zero audit trail.** Seyi's round-2 review required the same
audit-before-commit discipline already established elsewhere on this floor (e.g. BIO-DEV-004's
remote jobs: audited through the real shared sink BEFORE the action, never best-effort after).
Fixed: `recordContextChangeAudit` is now called and AWAITED before `jar.set(...)`, and a failed
write (network error or non-2xx response) clears the cookie and returns early - exactly the
same fail-closed shape `startDemoSession` already uses for every other failure mode (bad
tenant/role id, issuance disabled, token too short). Disclosed, accepted consequence: a web
deployment with no reachable `Biolog.Api` backend can no longer establish a demo session
cookie at all. This is NOT a new regression in practice - nothing backend-dependent would have
worked in that deployment anyway, no SIMULATION-mode test (the default everywhere) reads this
`HttpOnly` cookie, and the only specs that DO touch it
(`BIOLOG_TDWEBPROV_LIVE_TESTS`/`BIOLOG_TEN003_LIVE_TESTS`/`BIOLOG_ATT003_LIVE_TESTS`) already
require a real reachable backend to run at all.

**Orchestration note**: `automation/biolog.py late-commit BIO-TEN-002` was attempted first,
per god's dispatch, and refused (`Cannot late-commit from DONE`) - that verb only works from
`READY_FOR_REVIEW`/`READY_FOR_ACCEPTANCE`, and this story's status is `DONE` (accepted and
merged long ago). `builder-complete` likewise only works from
`{BUILDING,ASSIGNED,CHANGES_REQUIRED}`. Same ceiling TD-WEB-PROVENANCE-BRIDGE-REVERIFY hit
fixing BIO-TEN-003 post-acceptance: no CLI verb re-stamps `builder_commit` for an already-DONE
story, so this fix is committed directly on `feature/BIO-TEN-002` and tracked via a new
`evidence/features/bio-ten-002/builder/ops002-f01-f02-metadata.json` rather than through the
usual `start`/`builder-complete` cycle.

**Tests (round 1)**: 8 new HTTP integration tests
(`tests/dotnet/Biolog.IntegrationTests/DemoSessionContextAuditEndpointsTests.cs`) proving
alpha/beta resolve to genuinely different configured tenants, and the new endpoint's
validation/authorization. Full `Biolog.IntegrationTests` 288/288 (was 280, zero regression),
`Biolog.UnitTests` 210/210 unchanged, `dotnet build` clean, module-reference/contract-freeze/
secret-scan all PASS, web `tsc --noEmit` clean.

**Tests (round 2)**: rewrote the round-1 "beta falls back" HTTP test to assert rejection
(`SY_OPS002_03_beta_is_rejected_outright_...`), rewrote the equivalent unit test against
`TenantContextMiddleware` and added a new one proving beta resolves to its OWN tenant when
configured (`TenantContextMiddlewareTests.cs`, now 2 tests where round 1 had 1 - net +1).
`Biolog.IntegrationTests` 288/288 (same count as round 1 - a rewrite, not an addition, zero
regression), `Biolog.UnitTests` 211/211 (was 210, +1), `dotnet build` clean, module-reference/
contract-freeze/secret-scan all PASS, web `tsc --noEmit` clean. No Playwright re-run either
round: the only web diff (`session-actions.ts`) is a server-side effect with no new UI surface
for any existing spec to observe, and every spec that DOES touch this cookie already requires
a live backend to run at all (see SY-OPS002-04's own note above).

**Round 3 (SY-OPS002-06, High; SY-OPS002-05 accepted as a disclosed residual with no code
change, per Kemi's own ADR-0003 Decision 7 addendum) - final, mechanical fix per Kemi's own
ruling.** Rounds 1-2 built a bespoke `DemoSessionContextAuditEvent`/`IDemoSessionContextAuditSink`/
`InMemoryDemoSessionContextAuditSink` (following every feature-project's own established
per-feature audit-type convention) - Zainab's audit-shape review and Kemi's architecture
ruling concluded this should instead reuse the SHARED `Biolog.Api.Audit.AuditEvent`/`IAuditSink`/
`InMemoryAuditSink`/`AuditActor`/`AuditTarget` directly, since this event carries no classified
value and needed no projection machinery a bespoke type would have genuinely required
elsewhere.

Fix: deleted all three bespoke types. Extended the shared `AuditEvent` (backward compatible -
both new fields optional/nullable, every existing caller unaffected) with `Device` (always
`null` here) and `Detail` (an allowlisted dict, the same "internal technical metadata"
convention `PhotoQualityAuditEventBuilder`/`PhotoAuditEventBuilder` already use on their OWN
bespoke event types, applied to the shared one for the first time) - `previous_tenant_id`/
`previous_role_id`/`previous_actor_id`/`new_role_id` now live in `Detail`; `new_tenant_id`
stays the event's own required `TenantId` since it genuinely is the event's tenant.
`Classification = "internal"` (a literal, not a computed rank, since every `Detail` field here
is uniformly internal-only metadata). `CorrelationId` now validated against the canonical
`evt_` shape via `Biolog.Api.Identifiers.CanonicalId.IsWellFormed` (already used by
`DeviceService`/`DeviceCertificationService` for the exact same purpose) - an arbitrary
caller-supplied `X-Correlation-Id` is discarded and a fresh `evt_` minted instead, never passed
through as-is.

DI registration: `TenantModule` registers `InMemoryAuditSink` by its CONCRETE type
(`services.AddSingleton<InMemoryAuditSink>()`), deliberately NOT through the shared `IAuditSink`
interface slot `DeviceRegistryModule` already owns - `DeviceCertificationModule`'s own header
already discloses that a second `AddSingleton<IAuditSink, ...>()` would silently shadow
whichever module registered that interface last (ASP.NET Core resolves one interface slot to
the LAST registration, not a second independent one), the exact collision this sidesteps while
still reusing the real shared type.

**Tests (round 3)**: no test changes needed - the HTTP response shape (`event_id`/
`new_tenant_id`/`new_role_id`/`new_actor_id`) is unchanged, so all 9 of round 1-2's own
`DemoSessionContextAuditEndpointsTests` passed unmodified against the rewritten
implementation. `Biolog.IntegrationTests` 288/288 (unchanged), `Biolog.UnitTests` 211/211
(unchanged), `dotnet build` clean, module-reference/contract-freeze/secret-scan all PASS.

**Round 4 (ZB-OPS002-01, Medium) - `previous_actor_id` was still caller-controlled free
text.** Zainab's audit-shape review confirmed round 3's envelope conversion, new-tenant
source, canonical correlation handling, and DI registration were all sound, but found one
gap round 3 missed: `previous_tenant_id`/`previous_role_id` were already constrained to closed
allowlists before being written to `AuditEvent.Detail`, but `previous_actor_id` was copied
straight from the request body with NO validation at all - any holder of a genuinely valid
synthetic demo session could persist arbitrary caller-controlled text (personal data, a
secret, an oversized string) into `Detail` at classification `internal`, exactly what Kemi's
canonical-id/non-secret allowlist ruling was meant to prevent for every field, not just two of
three.

Fix: `previous_actor_id` must now equal `ActorIdByRole[previous_role_id]` or the request is
rejected (400 `previous_actor_id_role_mismatch`) - and the value actually written to `Detail`
is the DERIVED one from that lookup, never the caller's own string, so even a
coincidentally-matching caller value can never end up stored by construction, only a value
this endpoint itself computed. Also added, per Zainab's own request: sink-level assertions
(resolving the real `InMemoryAuditSink` singleton directly from the test host, the same
pattern `DeviceRegistryTests.cs` already uses) proving the exact stored `Detail` key set,
`Device == null`, `Classification == "internal"`, and that a malformed `X-Correlation-Id` is
genuinely discarded and replaced with a fresh canonical `evt_` (a well-formed one is
preserved) - the original 9 tests only ever asserted HTTP response shape, never the stored
record.

**Tests (round 4)**: 4 new tests in `DemoSessionContextAuditEndpointsTests.cs` - two proving
`previous_actor_id`/`previous_role_id` mismatch (including an arbitrary-free-text attempt) is
rejected, and two sink-level tests proving the stored `Detail`/`Device`/`Classification` shape
and the correlation-id validate-or-replace behavior. `Biolog.IntegrationTests` 292/292 (was
288, +4, zero regression - one `ObservabilityTests` flake observed and reproduced in isolation
as PASSING and unrelated to this diff: that test swaps the process-wide `Console.Out` and
races against whichever OTHER test classes xUnit happens to run concurrently, a pre-existing
test-isolation hazard in that file, not touched by this change), `Biolog.UnitTests` 211/211
unchanged, module-reference/contract-freeze/secret-scan all PASS.
