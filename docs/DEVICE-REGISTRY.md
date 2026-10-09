# Device registry foundation (BIO-DEV-001)

A registry for readers, terminals, scanners, printers and edge devices, before any vendor
adapter exists. Implemented as `Modules/DeviceRegistryModule.cs`, following the module
pattern established by BIO-FND-008/009.

## Device record (AC01)

`Features/DeviceRegistry/Device.cs`: `Id` (canonical `dev_<uuidv7>`, ADR-0001 §1.2 - Device
is one of the six identifier types that ADR already registers), `TenantId`, `Serial`,
`Vendor`, `Model`, `Firmware`, `Sdk`, `Site`, `Network` (`IpAddress`/`MacAddress`/`Hostname`,
all optional - a newly-registered device may not have connected yet), `Status`
(`PendingActivation`/`Active`/`Inactive`/`Retired` - this story's own small vocabulary; no
shared schema registers device status values yet), `LastSeenAt` (nullable), `Version`
(ADR-0001 Decision 3: starts at 1, +1 per mutation, optimistic concurrency via
`expected_version`), `CreatedAt`/`UpdatedAt`.

## Credential reference, not credential (AC02)

`CredentialReference` is an opaque pointer (e.g. a future secret store's lookup key) -
never a certificate, shared secret or password. Nothing in `Features/DeviceRegistry/`
resolves it to an actual secret value; that belongs to whichever secret store a later
story wires up. `data-classification.json`'s "secret" tier (card keys, signing keys,
passwords, tokens) is never absent, and a device credential is the same tier applied to a
device instead of a person.

**Resolved (BIO-DEV-001-QA-04, Chioma, acceptance): naming a property "CredentialReference"
was not itself an enforceable boundary.** The acceptance probe submitted an ordinary
plaintext string (`synthetic-plaintext-password`) straight through
`InMemoryDeviceRepository.CreateAsync`, which accepted, stored and returned it unchanged -
nothing validated that the value was actually a reference rather than credential material.
`Features/DeviceRegistry/CredentialReferenceValidator.cs` closes this with a strict
supported-reference grammar: a required scheme prefix (`vault://`, `kv://`, `kms://` or
`secretref://`) followed by an opaque identifier, rejecting plaintext, empty/whitespace
values, control characters, and any value containing `@` (which rejects every
"userinfo@host" credential-bearing URL shape outright, without needing to parse and inspect
a URI). Enforced at **both** boundaries the finding named, matching
`InMemoryDeviceRepository`'s own belt-and-braces posture for tenant isolation: the request
boundary (`DeviceService.RegisterAsync`, before a tenant-scoped write is even attempted) and
the repository boundary (`InMemoryDeviceRepository.CreateAsync`, so a caller that reaches
the repository directly - exactly what the acceptance probe did - cannot slip plaintext past
by skipping the service layer). An invalid reference throws
`InvalidCredentialReferenceException`, surfaced by the HTTP layer as `400
invalid_credential_reference`. The reference is still optional (`null`): a device can be
registered before its credential is provisioned.

## Audit on status change (AC03)

`Audit/AuditEvent.cs` is a **minimal .NET port** of ADR-0003's envelope - the fields this
story actually populates (`event_id`, `tenant_id`, `actor`, `action`, `target`, `outcome`,
`reason_code`, `correlation_id`, `occurred_at`/`received_at`, `classification`), not the
ADR's general allowlist/redaction projection engine or idempotency dedup. A device status
change carries no free-text payload and no classified value, so the full projection
machinery has nothing to protect here. **A future story that needs to audit a classified
value should not copy this file** - it should port `contracts/js/audit.mjs`'s allowlist
projection properly, so a second, narrower audit shape doesn't become the de facto standard
by accident.

`DeviceService.ChangeStatusAsync` awaits the audit write before returning (ADR-0002
Decision 5 / ADR-0003: "the audit sink is a hard dependency of the audited effect, not an
optional side effect"). If the sink throws, the caller never receives an acknowledged
success.

**Resolved (BIO-DEV-001-QA-02), corrected once: an audit failure no longer leaves a
committed, unaudited mutation - because the audit write now happens BEFORE the repository
commit, not after it with a rollback.** The first version of this story treated "the
caller never sees success" as sufficient and documented the repository's status change as
already committed by the time an audit exception propagates - Seyi's review correctly
rejected that as not satisfying AC03. The next version fixed it with commit-then-rollback:
write the status, then on an audit failure, compensate back to the pre-change status. Kemi's
review found THAT pattern itself breaks under concurrent writers (ADR-0003 Decision 7): a
second writer can read the first writer's committed-but-unaudited value and build a
legitimately-audited change on top of it before the first writer's rollback runs,
permanently baking the never-audited value into a record whose own audit trail names only
the second writer's delta - version-rechecking the rollback doesn't close this, it's the
exact mechanism that lets the rollback get silently skipped.

The fix removes the race by construction instead of managing it: `ChangeStatusAsync`
looks up the device and rejects a stale `expected_version` **before** ever writing the
audit event, then awaits the audit write, and only calls
`IDeviceRepository.UpdateStatusAsync` **after** the audit write succeeds. Checking the
device's existence/tenant and version *before* the audit write also preserves the
pre-existing, already-correct behaviour that an operation against an unknown/cross-tenant
device, or one whose version was already stale when the call started, records no audit
event at all.

**Corrected a second time (still BIO-DEV-001-QA-02): audit-before-commit alone is not
enough, because `IDeviceRepository.UpdateStatusAsync` re-checks the version independently
at its own call time - separate from `ChangeStatusAsync`'s earlier pre-check.** A real
second writer for the SAME device could still change the version in the gap between the
two, so the eventual commit rejects a mutation whose audit event has ALREADY durably
recorded `Outcome: Allowed`. An earlier version of this document called that scenario
"honest audit-log behaviour, materially different from the original defect" - Kemi's
review found that Seyi had already rejected the byte-for-byte identical shape on
BIO-TEN-001 (evidence commit `92e95ce`): a durable `Allowed` record for a mutation that
then gets rejected is a **false success claim**, not an honest attempt, however narrow the
window.

`DeviceLockRegistry` closes it: a per-device `SemaphoreSlim`, held across the *entire*
pre-check-audit-commit sequence, so no second writer for the same device id can even begin
its own pre-check until the first writer's audit AND commit have both resolved. Nothing
about a change is ever visible to another writer until its own audit event already durably
exists, and the two version checks (the pre-check and the repository's own) can never
disagree, because nothing can change the device between them - the race is removed by
construction, not merely narrowed. Registered as a singleton (unlike the scoped
`DeviceService`) so the same lock is shared across every request; keyed by device id alone,
matching `InMemoryDeviceRepository`'s own key space (device ids are globally unique, not
scoped per tenant). `docs/OBSERVABILITY.md`'s `InMemoryAuditSink` also logs every event
through `ILogger`, wrapped explicitly in `SafeLogValue` (BIO-FND-009), as a visible
local-dev side channel.

**Corrected a third time (BIO-DEV-001-QA-03): the lock closes the concurrent-WRITER race,
but not a caller CANCELLATION arriving in the same gap.** `InMemoryDeviceRepository.UpdateStatusAsync`
checks the caller's cancellation token before its own compare-and-set; if that token were
cancelled after the audit event already succeeded but before that check ran, the repository
would throw `OperationCanceledException` without ever attempting the write - the identical
false-success shape the lock was built to prevent, just via cancellation instead of a
second writer. Seyi's re-review reproduced this with a deterministic probe cancelling in
exactly that window. Fixed by treating a successfully-recorded audit event as this
operation's point of no return: the repository commit call now uses
`CancellationToken.None`, not the caller's token, so a cancellation request arriving after
that point can no longer abandon the write it describes - only a genuine fault (a real
version conflict, an actual store failure) can stop it now.

**Resolved (ADR-0003 Decision 7, element 4 - Kemi, closing an identical BIO-TEN-001 finding
by Tunde/Seyi): a thrown `IAuditSink.RecordAsync` does not, by itself, prove the event was
never durably recorded.** A real store can commit a write and still throw on the
acknowledgement (a timeout, a dropped connection after commit); treating every throw as
"definitely didn't happen" could leave a genuinely-recorded success event with no
corresponding commit, from a cause `DeviceLockRegistry` cannot see (it serializes writers
against each other, not the audit sink's own acknowledgement path). `DeviceService.AuditAsync`
(private, called from `ChangeStatusAsync`) now resolves that ambiguity instead of assuming
it: on a `RecordAsync` throw, it calls `IAuditSink.ExistsAsync(eventId, ...)` - confirmed
present is treated as success (the caller's own lock-protected commit then makes the event's
claim true); confirmed absent throws `AuditUnavailableException` with nothing committed; and
if `ExistsAsync` itself throws, that is a genuine, total audit-store outage (the same
fail-closed boundary as a missing sink), also `AuditUnavailableException`. `IAuditSink.ExistsAsync`
and `InMemoryAuditSink`'s implementation mirror `ITenantAuditSink`/`InMemoryTenantAuditSink`
(BIO-TEN-001) exactly, and `DeviceService.AuditAsync` mirrors `TenantService.AuditAsync`
line-for-line in structure - this is the confirmed, settled ADR-0003 Decision 7 pattern, not
a second independent design.

**Confirmed inert (ADR-0003 Decision 7, element 5): a true process crash between the audit
write and the repository commit remains a real crash-consistency window, but is inert for
this story specifically because both stores are same-process, same-lifetime, volatile
singletons.** `DeviceRegistryModule.RegisterServices` registers both `IAuditSink`
(`InMemoryAuditSink`) and `IDeviceRepository` (`InMemoryDeviceRepository`) as
`AddSingleton` - one shared instance of each, living and dying with the same process. A
crash between the two steps loses BOTH the "durable" audit event and the repository's
in-memory state together, in the same instant, because neither one actually survives the
process independently of the other; there is no observable state afterward where one
persisted and the other didn't. **This is not a general proof that the gap is safe - it is
inert only as long as this exact condition holds**, and reopens the instant either side of
this pair becomes durable, remote, transactional, or multi-process on its own: a real
database-backed `IDeviceRepository`, an audit sink that writes to a separate store or
process, a multi-instance deployment, or anything else that lets one side's write outlive
the other's crash. Whichever story introduces any of those must provide the equivalent
guarantee a real system needs there (a write-ahead log/outbox spanning both stores, or an
equivalent reconciliation path) - this story's inertness argument does not travel with it.
This mirrors `TenantService`'s identical, independently-confirmed finding for BIO-TEN-001
exactly (same singleton registrations, same reasoning, same reopening triggers).

`actor` is accepted as an explicit request field (`actor_type`/`actor_id`), not derived
from a verified session - the same "hook, not resolver" gap BIO-FND-008 documents for
tenant context, applied to identity: no auth story has shipped a real session yet. A future
auth story replaces the parameter with a session-derived value with no change to the audit
shape it feeds.

## Tenant isolation (AC04)

`DeviceService` reads `ITenantContextAccessor.Context` itself for every operation and
throws `Tenancy/TenantContextMissingException` when it is `null` - ADR-0002 Decision 2's
"no default tenant, ever," applied here exactly like any other tenant-plane read or write.

**Every real HTTP client hits `tenant_context_missing` today**, because BIO-FND-008's
tenant hook deliberately resolves no tenant yet (no host, principal or service channel
exists in .NET) - proven by `DeviceRegistryTests.AC04_*_fails_closed_with_no_resolved_tenant_context`,
and still true after BIO-DEV-001-QA-01. **The specific, named blocker is BIO-TEN-002
("Implement tenant domain mapping and verification," Tunde, W03.2) - not yet shipped as of
this commit.** That story is what would let a real HTTP request resolve a host to a tenant
in the first place; until it ships, there is no host/principal/service channel for any
.NET tenant-plane feature to resolve from, regardless of what BIO-DEV-001 does. A future
story that implements real tenant resolution populates the context this feature already
checks for, and device registry starts working end to end with no code change here.

**Resolved (BIO-DEV-001-QA-01), settled as the cross-story W03 standard by god/Seyi/Kemi
after a genuine escalation**: proving only the fail-closed path left AC01 with no evidence
the feature's own logic works at all - Seyi correctly rejected that as "the delivered API
cannot register or retrieve any device," full stop. A first fix (a test-only
`ITenantContextAccessor` proving the positive path in isolation) was then rejected on its
own as insufficient, on the grounds that it doesn't create "a successful path in the
shipped host." The settled bar, applying to every W03 tenant-plane story sitting on this
same BIO-FND-008/BIO-TEN-002 dependency gap, not just this one: **both** an explicitly
injected resolved context proving the feature's own logic (kept, unchanged) **and** a real
shipped-host integration test proving today's middleware correctly fails closed (also kept,
never dropped) - plus this document explicitly naming the unavailable production resolver
(BIO-TEN-002, above) and a **tracked follow-up obligation**: once BIO-TEN-002 ships and
BIO-FND-008's tenant hook can populate a real context, this story needs a genuine
production-only positive-path integration test added (no test-only accessor), to prove the
feature actually works end to end for a real client, not only in a synthetic host. `DeviceRegistryTests`
proves the positive path through the real composed host without changing
`TenantContextMiddleware` or `TenantContextAccessor` in any way: a test-only
`ITenantContextAccessor` implementation resolves a tenant from a per-request test header
instead of the ambient (always-null) value, and its setter ignores the middleware's own
unconditional `Context = null` so the resolved tenant survives it. All three endpoints, and
cross-tenant isolation between two resolved tenants sharing the same underlying store, are
exercised end to end - this is test-only scaffolding the tracked follow-up above will make
unnecessary, not a second implementation of tenant resolution, and not an excuse to wire
anything test-only into production code.

`InMemoryDeviceRepository` is a single, process-lifetime, cross-tenant dictionary standing
in for the real per-tenant database ADR-0002 Decision 4.1 describes (a `TenantDataBinding`
lookup selecting an actual separate database) - real per-tenant routing is a later story's
concern, the same scope boundary BIO-FND-008 drew around tenant resolution itself. What
this repository DOES implement, on top of that simplification, is Decision 4.2's
belt-and-braces rule: every read and write asserts the record's own `TenantId` equals the
calling context's, so a device from tenant B can never leak into a query made under tenant
A's context. A cross-tenant access answers `null`/`404`, never `403` (ADR-0002 §4.3): the
caller cannot tell "this device doesn't exist" from "this device exists in someone else's
tenant."

## API conventions

Endpoints live under `/api/v1/devices` (ADR-0004 §1.1's path versioning). This story does
**not** otherwise implement ADR-0004: no `Idempotency-Key` header handling, no fully
validated `application/problem+json` builder (Decision 2.5's content-constraint
validation), no pagination envelope. None of BIO-DEV-001's four ACs names any of these, and
no other .NET endpoint in this codebase exercises ADR-0004 yet either - building a partial
version of a shared, general-purpose HTTP contract here risked producing a second,
competing implementation for whichever story ports it properly next. ASP.NET Core's own
`Results.Problem(...)` is used for errors instead, which already produces a reasonable
`application/problem+json` body (`title`, `status`, `detail`) without the generic builder.

A status-change write requires `expected_version` (ADR-0001 §3.2) and returns `409` with
the current version on a mismatch - "a client that does not say what it thinks it is
overwriting is not permitted to overwrite anything."

## Developer tests

- `tests/dotnet/Biolog.UnitTests/Identifiers/CanonicalIdTests.cs` - `dev_`/`evt_` minting
  and shape validation, including the wrong-prefix rejection ADR-0001 §1.4 calls for.
- `tests/dotnet/Biolog.UnitTests/DeviceRegistry/DeviceServiceTests.cs` - tenant-missing
  fail-closed (AC04), cross-tenant read/write returns nothing (AC04), status change
  increments version and records a correctly-shaped audit event (AC03), a stale
  `expected_version` throws without auditing, a confirmed-absent audit sink means the
  repository is never touched at all (QA-02, audit-before-commit), two genuinely and
  *provably* concurrent status-change attempts for the same device (via a gated audit
  sink proving real interleaving, not just synchronous completion order) are serialized by
  `DeviceLockRegistry` so the loser's own pre-check rejects it before it ever writes a
  false-success audit event (QA-02, corrected a second time), the repository commit
  still lands even when the caller's token is cancelled right after the audit write
  succeeds (QA-03), a `RecordAsync` throw that `ExistsAsync` confirms as actually-recorded
  still proceeds to commit (ADR-0003 Decision 7 element 4), a total outage during the
  `ExistsAsync` verification itself still fails closed with nothing committed, and
  registering a device with a plaintext or credential-bearing-URL reference throws
  `InvalidCredentialReferenceException` while a device with no reference yet still succeeds
  (QA-04).
- `tests/dotnet/Biolog.UnitTests/DeviceRegistry/CredentialReferenceValidatorTests.cs` -
  the supported-reference grammar in isolation: accepts `vault://`/`kv://`/`kms://`/`secretref://`
  references and a `null` (not-yet-provisioned) reference; rejects plaintext, empty/whitespace,
  control characters, unsupported schemes, and credential-bearing URLs (QA-04).
- `tests/dotnet/Biolog.UnitTests/DeviceRegistry/InMemoryDeviceRepositoryTests.cs` - two
  concurrent writers racing on the same `expected_version`: exactly one wins; and the
  repository's own credential-reference validation rejects a plaintext reference even when
  a caller bypasses `DeviceService` entirely - the exact reproduction from
  `evidence/BIO-DEV-001/acceptance/QA-04.md` (QA-04).
- `tests/dotnet/Biolog.IntegrationTests/DeviceRegistryTests.cs` - through the real composed
  host: every endpoint correctly fails closed with no resolved tenant context (the true
  current end-to-end behaviour), the routes exist under `/api/v1`, (QA-01) a genuine
  positive path given a resolved tenant - register/read, a status change that records an
  audit event, and cross-tenant isolation between two resolved tenants sharing one store -
  and (QA-04) a plaintext credential reference is rejected with `400 invalid_credential_reference`
  through the full HTTP pipeline while a supported reference still succeeds.
- `tests/foundation/BIO-DEV-001.test.sh` - offline-safe structural wiring, wired into
  `scripts/quality.sh` in the same commit it was added.
