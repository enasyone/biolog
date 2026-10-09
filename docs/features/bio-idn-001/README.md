# BIO-IDN-001 — Universal PERSON aggregate

Implements the epic BIO-IDN's foundation story: one permanent PERSON record so all profiles,
credentials, biometrics and events resolve to a single identity (Master Technical
Specification §4).

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | PERSON exists independently of student/staff/visitor roles | `Domain/Person.cs` carries no role/profile field of any kind; `Api/PersonRequests.cs`'s strict JSON options make an unrecognised field (e.g. `role`) a hard deserialization failure | `PersonServiceTests.No_role_or_profile_field_exists_on_the_registration_request_or_the_aggregate`, `PersonEndpointsIntegrationTests.Register_rejects_an_unrecognised_JSON_field_a_role_smuggling_attempt` |
| **AC02** | Tenant boundary is mandatory | `PersonService.RequireTenant()` reads `ICurrentTenantProvider.TenantId`, never a caller-supplied value; `InMemoryPersonRepository` asserts a record's own `TenantId` on every read/write; cross-tenant lookup is 404, never 403 | `PersonServiceTests` (tenant-boundary tests), `PersonHostChannelTests` (real packaged host) |
| **AC03** | Identity lifecycle states are implemented | `Domain/PersonStatus.cs` — the eight states the spec names verbatim; `PersonService.AllowedTransitions` enforces a baseline transition table on every status change | `PersonServiceTests` (transition tests) |
| **AC04** | Deletion is governed; operational history is not orphaned | No delete/remove method exists anywhere on `IPersonRepository` or `PersonService`; the only lifecycle exit is the governed, audited transition to `ARCHIVED` | `PersonServiceTests.No_delete_method_exists_anywhere_on_the_repository_or_the_service` |
| **AC05** | All mutations are audited | `PersonService.AuditAsync` implements ADR-0003 Decision 7 (per-person lock, audit-before-commit, `ExistsAsync` verification, `CancellationToken.None` post-audit) | `PersonAuditTests` |

## BIO-IDN-001-QA-01 (Seyi round 1, High) — the public repository bypassed all governance

**Finding.** `IPersonRepository.CreateAsync`/`ReplaceAsync` were public, `InMemoryPersonRepository`
was public, and `Person`'s properties used a public object initializer with no declared
constructor. An independent probe reproduced: construct a `Person` directly, call
`repository.CreateAsync` to create it in tenant A, then build a modified copy with a different
`TenantId` and `Status: ARCHIVED` and call `repository.ReplaceAsync` directly — no
`PersonService`, no lock, no lifecycle check, no audit event. This violated AC02 (tenant
boundary), AC03 (governed lifecycle), AC04 (governed deletion) and AC05 (audited mutations)
simultaneously, and disproved the round-1 structural check's claim that the repository
enforces tenant identity — it only checked reads, never the shape of a write.

**Fix — three layers, matching the review's explicit required closure:**

1. **Construction is restricted.** `Person`'s constructor is now `internal`, and `Id`/`TenantId`
   have `get`-only accessors with no `init` — immutable for the object's whole life, including
   across a `with` expression (the exact shape the probe used no longer compiles at all).
   Mirrors BIO-TEN-002-QA-03's identical fix for `TenantContext`/`ITenantContextAccessor`.
2. **The mutation surface is restricted.** `IPersonRepository` and `InMemoryPersonRepository`
   are now `internal`, reachable only from this assembly, `Biolog.Api` (DI registration in
   `BioIdn001Module`) and `Biolog.Features.PersonRegistry.Tests` (an explicit, auditable
   `InternalsVisibleTo` test seam, not a backdoor) — "ordinary production consumers" (any
   other assembly) can no longer even reference these types.
3. **The persistence boundary independently re-validates**, not merely trusting that
   restricted access is enough — the same "validated at the request boundary AND the
   repository boundary" discipline `CredentialReferenceValidator` (BIO-DEV-001-QA-04)
   established:
   - `CreateAsync` rejects any `Person` not starting `PRE_ENROLLED` at version 1.
   - `ReplaceAsync` rejects `updated.Id`/`updated.TenantId` differing from `expected`'s.
   - `ReplaceAsync` rejects any status transition `PersonLifecycleTransitions.IsAllowed`
     doesn't permit — the table was extracted out of `PersonService` into its own shared class
     (`Domain/PersonLifecycleTransitions.cs`) specifically so both layers check the identical
     rule, not two copies that could drift.

`tests/features/bio-idn-001/PersonRepositoryGovernanceTests.cs` drives every attack the review
named directly against the repository (this test project has the `InternalsVisibleTo` grant),
plus reflection tests proving `IPersonRepository`/`InMemoryPersonRepository` are not public and
`Person` has no public constructor — the "inaccessible to ordinary production consumers"
requirement, proven adversarially rather than asserted.

## Design decisions and scope boundaries

### Identity lifecycle states, verbatim from the spec

The Master Technical Specification §4 names eight states literally: "PRE_ENROLLED, VERIFIED,
ACTIVE, SUSPENDED, EXPIRED, FORMER/ALUMNI, REVOKED, ARCHIVED." The single slashed token
`FORMER/ALUMNI` is rendered as one state, `FORMER_ALUMNI` — the spec names it as one lifecycle
position (a person who has left the tenant but remains historically relevant), distinct from
BIO-IDN-002's separate "Alumni" **profile** type (a role a person can hold, at a different
layer).

### This story's transition table is a conservative baseline, not the final word

```
PRE_ENROLLED  -> VERIFIED, ARCHIVED
VERIFIED      -> ACTIVE, ARCHIVED
ACTIVE        -> SUSPENDED, EXPIRED, FORMER_ALUMNI, REVOKED, ARCHIVED
SUSPENDED     -> ACTIVE, REVOKED, ARCHIVED
EXPIRED       -> ACTIVE, ARCHIVED
FORMER_ALUMNI -> ACTIVE, ARCHIVED
REVOKED       -> ARCHIVED
ARCHIVED      -> (terminal)
```

This exists so "identity lifecycle states are implemented" is a real, tested, enforced state
machine, not a bare enum field nothing validates against. It is deliberately NOT the epic's
final governance: **BIO-IDN-004** ("Implement identity lifecycle and emergency suspension",
AC01: "Allowed identity transitions are explicit") owns narrowing the final table, emitting
credential/access revocation events for emergency suspension (its AC02), and gating
reactivation behind permission + an audit reason (its AC03). This table is a conservative
superset a later, more restrictive table can narrow without contradicting anything tested
here.

### Governed deletion IS the ARCHIVED transition — no separate delete verb exists

`IPersonRepository` and `PersonService` have no delete/remove/purge/erase method at all — a
structural test proves this adversarially by reflection. The record and its `per_…` id persist
forever once archived, so a future story attaching a profile, credential, biometric or event
record can never be left pointing at a row that stopped existing.

### No name-correction endpoint (deliberately deferred, not blocked)

`PersonService` implements exactly two mutations: `RegisterAsync` (create) and
`UpdateStatusAsync` (lifecycle). Correcting a person's name or date of birth after
registration is natural, low-risk future scope, but no AC requires it, and adding it
preemptively would be unrequested scope beyond this story's Definition of Done (the same
discipline BIO-TEN-002 applied to hostname-reuse-after-retire and a reinstate action). Both
mutations this story does define are fully covered by AC05's audit requirement.

### Full Name is derived, never stored

The Master Technical Specification §7 names six PERSON name fields, including "Full Name" as
a distinct dynamic field. This aggregate stores the four independent components (`Title`,
`FirstName`, `MiddleName`, `LastName`) and computes `full_name` on read
(`Domain/PersonNameFormatter.cs`), rather than storing a fifth, redundant field that could
silently disagree with its own components after a correction.

### `born_on`, not `date_of_birth`

ADR-0001 §2.2: "a field holding a calendar date with no instant ... ends in `_on`." `BornOn`
follows this literally. It is optional (a date of birth is not always known at enrolment
time) and, when present, validated against `contracts/schemas/calendar-date.schema.json`'s
exact rule (year 1000–9999, proleptic Gregorian leap calendar — `2026-02-30` and `2026-02-29`
rejected, `2000-02-29` accepted, `1900-02-29` rejected), ported verbatim into
`PersonService.CalendarDatePattern`.

### Tenant context seam

PERSON is tenant-plane (ADR-0001's identifier registry: `per`, plane `tenant`), so AC02
requires reading the *real* resolved tenant, never a caller-supplied value — exactly the rule
`DeviceService` (BIO-DEV-001) applies against `Biolog.Api.Tenancy.ITenantContextAccessor`
directly, from inside `Biolog.Api` where that type lives.

This project has no `ProjectReference` to `Biolog.Api.csproj` (one-way dependency,
`scripts/check-module-project-references.sh`, learned from BIO-TEN-001's circular-reference
integration failure), so it cannot reach `ITenantContextAccessor` directly. It instead defines
its own minimal interface, `Tenancy/ICurrentTenantProvider.cs` (`string? TenantId { get; }`),
and `api/Biolog.Api/Modules/BioIdn001Module.cs` provides a thin adapter
(`CurrentTenantProviderAdapter`) that reads the real accessor and implements this shape — the
same "feature defines the abstraction it needs; the host wires a concrete implementation" seam
`ITenantDomainRegistry`/`ITenantDomainAuditSink` already use for storage, applied here to
tenant context.

**Why this ships in-story, not as a deferred follow-up integration**, unlike BIO-DEV-001 and
BIO-DOC-001's original disclosed-boundary pattern: ADR-0002 Decision 6 states "the
accommodation ends per-channel, not all at once ... obligation on every W03+ tenant-plane
story ... once the channel it depends on ships, its positive-path evidence is upgraded to a
real shipped-host test." BIO-TEN-002's host channel shipped and merged to `develop` before
this story started — BIO-IDN-001 is a new W04 tenant-plane story built entirely after that
point, so the deferred-integration accommodation those earlier stories used (built before any
real tenant resolver existed) does not transfer here the same way. `BioIdn001Module.cs` was
therefore written directly in `api/Biolog.Api/Modules/` from the start (never inside the
feature project first, learning from BIO-TEN-001/BIO-TEN-002's circular-reference detours),
registered in `Program.cs`, and `tests/dotnet/Biolog.IntegrationTests/PersonHostChannelTests.cs`
proves the real, shipped-host positive path Decision 6 requires: registering and verifying a
domain through BIO-TEN-002's own endpoints, then registering a PERSON through that verified
hostname via the actual packaged `Program.cs` pipeline.

This module only *reads* the already-shipped, already-reviewed `ITenantContextAccessor`
(exactly as `DeviceService` already does directly) — it does not modify
`TenantContextMiddleware`, `TenantContext` or `ITenantContextAccessor` in any way, so it
carries none of the risk BIO-TEN-002's QA-01 required-closure did (that finding was about the
resolution *mechanism* itself being absent; here the mechanism already exists and is stable —
this module only consumes it).

### Registration lifetime: why `PersonService` is Scoped, not Singleton like its siblings

`TenantService`/`TenantDomainService` (BIO-TEN-001/002) are registered Singleton and own their
per-aggregate lock table directly, because control-plane work consumes no scoped dependency.
`PersonService` reads a per-request `ICurrentTenantProvider`, so it *must* be registered
Scoped — a Singleton cannot safely capture a Scoped dependency (a captive-dependency defect
that would pin the first request's tenant context forever). The per-person lock table is
therefore split into its own Singleton class, `PersonLockRegistry`, injected into the Scoped
`PersonService` — the same shape `DeviceLockRegistry` (BIO-DEV-001) already uses for the
identical reason. `PersonHostChannelTests.cs` running successfully through the real DI
container (rather than throwing a captive-dependency validation error) is itself evidence this
is wired correctly, not merely asserted.

### Audit trail privacy: PII values are withheld, not merely allowed

PERSON carries personal data (name, date of birth —
`contracts/schemas/data-classification.json`: both `personal`, rank 2, `allowlist_only`).
`PersonService` never places an actual PII *value* into an audit event's `Detail` — every
mutation lists every PII field name in the event's `Withheld` set instead
(`audit-event.schema.json`: "Field NAMES deliberately not carried. Never values."). This is
stricter than the schema's own `allowlist_only` envelope for `personal` data technically
permits — a deliberate privacy-by-design choice: an audit trail that proves who changed what
*kind* of thing, on which record, when, does not need to also become a second copy of a
person's name to do its job. `Detail` therefore only ever carries non-PII lifecycle metadata
(a status word), which is `internal`.

## Deliberately out of scope for this story

- Name/date-of-birth correction after registration (no AC requires it).
- Multiple profiles per person, organisational/campus targeting (BIO-IDN-002).
- Institutional identifiers (matriculation/staff numbers) (BIO-IDN-003).
- The final, narrower lifecycle transition table, emergency suspension event emission,
  reactivation permission checks (BIO-IDN-004).
- Duplicate detection/review (BIO-IDN-005).
- RBAC enforcement on who may register/transition a person (BIO-IDN-006) — every mutating
  endpoint here requires only an asserted `X-Actor-Id`, the same disclosed pre-auth seam
  BIO-TEN-001/BIO-TEN-002 already use, pending BIO-INT-001.
- Database routing (`BindingKey`) — `ICurrentTenantProvider` deliberately carries only
  `TenantId`, not the full resolved-context shape, since this feature has no present use for
  binding key or channel list.
- Pagination on the list endpoint (matches BIO-TEN-002's own deferred-pagination precedent;
  no AC requires it at this story's expected cardinality).
