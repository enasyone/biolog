# BIO-IDN-003 — Institutional identifiers

As a tenant, I need matriculation, employee and other business identifiers linked to PERSON but
not used as primary electronic credential IDs.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Identifiers are typed and tenant-scoped | `InstitutionalIdentifier.Type` (`IdentifierType`: `MATRICULATION_NUMBER`/`STAFF_NUMBER`/`TENANT_DEFINED`); every read/write resolves the tenant via `ICurrentTenantProvider`, never a caller-supplied id | `InstitutionalIdentifierServiceTests` (AC01 tests) |
| **AC02** | Uniqueness rules are configurable per type | A separate `IdentifierUniquenessRule` aggregate, keyed `(TenantId, Type, CustomLabel)`, toggled via `/api/v1/identifier-types/{type}/uniqueness-rule`; `AssignAsync` resolves it before committing, defaulting to enforced when unset | `InstitutionalIdentifierServiceTests` (AC02 tests) |
| **AC03** | History/status is preserved | `IdentifierStatus` (`ACTIVE`/`RETIRED`, one-way); `Value` is immutable once created - a correction/reissue creates a NEW record at the next `SequenceNumber` for the same `(PersonId, Type, CustomLabel)`, never edits the old one in place | `InstitutionalIdentifierServiceTests` (AC03 tests) |
| **AC04** | Electronic credentials use opaque credential IDs | This feature has no field, method or endpoint that names, mints or verifies a credential of any kind - see "BIO-IDN-003-AC04" below | `InstitutionalIdentifierServiceTests` (AC04 tests, by reflection) |

## Design decisions and scope boundaries

### Applying BIO-IDN-001-QA-01 and BIO-IDN-002-QA-01 from the start

Two prior review rounds found the same class of gap in earlier stories - a public,
forgeable aggregate/repository (BIO-IDN-001-QA-01), and a person-linked aggregate that never
proved the PERSON it attached to actually existed (BIO-IDN-002-QA-01). Both lessons are applied
here from the first commit, not after a review round finds them again:

- `InstitutionalIdentifier`'s constructor is `internal`; `PersonId`/`TenantId`/`Type`/
  `CustomLabel`/`SequenceNumber`/`Value` are `get`-only (immutable even across a `with`
  expression - `Value` included, since AC03 requires a "correction" to be a new historical
  record, never an in-place edit).
- `IInstitutionalIdentifierRepository`/`InMemoryInstitutionalIdentifierRepository` and
  `IIdentifierUniquenessRuleRepository`/`InMemoryIdentifierUniquenessRuleRepository` are
  `internal`, reachable only from this assembly, `Biolog.Api` (DI registration) and this
  project's own test assembly via an explicit `InternalsVisibleTo` grant.
- The identifier repository independently re-validates identity immutability and initial state
  at the persistence boundary (`InvalidInitialState`, `IdentityImmutable`, `AlreadyRetired`,
  `IdentifierAlreadyActive`), not merely trusting restricted access - the same dual-boundary
  discipline `CredentialReferenceValidator` (BIO-DEV-001-QA-04) established.
  `tests/features/bio-idn-003/InstitutionalIdentifierRepositoryGovernanceTests.cs` drives the
  same attacks BIO-IDN-001's probe found (foreign tenant, unrelated identity, a forged
  value-changing replace, illegal re-replace of an already-retired record), plus reflection
  tests proving the surface is inaccessible to ordinary production consumers.
- `Domain/IPersonExistenceGateway.cs` is the same narrow, read-only, one-method interface
  BIO-IDN-002's remediation established, owned by this feature (still no `ProjectReference` to
  BIO-IDN-001's project). `AssignAsync` calls it - before the per-(person, type, custom_label)
  lock and before any audit write - and rejects a missing or foreign-tenant person identically
  as `person_not_found` (404), per ADR-0001 §1.5's no-enumeration-oracle rule.
  `api/Biolog.Api/Modules/BioIdn003Module.cs` implements it by wrapping BIO-IDN-001's own
  already-registered, already tenant-scoped `PersonService.GetAsync` directly - the adapter
  gets BIO-IDN-001's own tenant scoping "for free" for the identical reason BIO-IDN-002's
  adapter does (see that interface's header).
  Proven at three levels: `InstitutionalIdentifierServiceTests` (unit, fake gateway),
  `InstitutionalIdentifierAuditTests` (fail-closed/no-audit-write proof), and
  `InstitutionalIdentifierHostChannelTests.cs` (the real packaged host - an unknown canonical
  person id, and a real tenant-A person referenced through tenant B's host, both rejected
  identically).

### BIO-IDN-003-QA-01 (Seyi round 1, High) - enforced tenant-wide uniqueness was only a per-person lock

**Finding.** `AssignAsync` acquired only a per-`(PersonId, Type, CustomLabel)` lock, but AC02's
uniqueness invariant is scoped to `(TenantId, Type, CustomLabel)` **across every person** in the
tenant. Two callers assigning the SAME value to two DIFFERENT people take two different lock
keys and race straight through `ValueExistsAsync` unserialized - a deterministic two-person race
admitted the same matriculation value twice, each with its own ALLOWED audit event. The
uniqueness-rule lock (`IdentifierUniquenessRuleLockRegistry`) was also entirely separate from
the identifier-assignment path, so a rule change was not serialized against an in-flight
assignment relying on that rule either.

**Why this is the same shape BIO-TEN-002 already solved.** `TenantDomainService`'s two-lock
scheme locks `Register` by hostname (the record's own natural key) but locks
`Verify`/`Suspend`/`SetPrimary` by `tenant_id` (the SCOPE the invariant those operations protect
actually spans). A global collision invariant needs a lock keyed by the scope it spans, not by
a narrower per-record key that two colliding callers might not even share - the exact mistake
repeated here: the per-person key is the right lock for "does THIS person already have an
active identifier of this type" (AC03), but the wrong lock for "is this VALUE already used by
ANYONE of this type" (AC02).

**Fix.** `AssignAsync` now acquires `IdentifierUniquenessRuleLockRegistry`'s `(TenantId, Type,
CustomLabel)` lock FIRST (the broader, outer lock - the SAME lock and the SAME key
`IdentifierUniquenessRuleService.SetRuleAsync` already used) and the narrower per-person lock
second, holding both across the history-read, the uniqueness check and the commit. This closes
both parts of the finding at once: two assignments of the same value to different people are
now fully serialized, and so is a rule change racing an assignment. `RetireAsync` is unaffected
- it never touches cross-person uniqueness (a retired value stays reserved forever when
enforced), so it keeps only the narrower per-person lock.

Verified with a break-it-on-purpose test
(`BIOIDN003_QA01_two_genuinely_concurrent_assigns_of_the_same_value_for_two_different_people_never_both_succeed`):
confirmed it fails with the predicted symptom (both callers succeed, two records stored) when
the new outer lock is temporarily removed, then restored and re-confirmed green.

**Closure item 2 - enabling enforcement over already-violated data.** The review's required
closure also named a second gap: the only way two identical values can ever exist in one
`(Type, CustomLabel)` scope is while that scope's rule is `Enforced = false` - but nothing
stopped a tenant from then flipping it back to `Enforced = true`, silently recording a rule the
existing data already broke. `IInstitutionalIdentifierRepository.HasDuplicateValuesAsync` scans
the same population `ValueExistsAsync` checks (tenant-wide, active and retired records alike)
for any value used more than once; `IdentifierUniquenessRuleService.SetRuleAsync` calls it -
under the SAME outer lock the method already holds, so no assignment can slip a fresh duplicate
in between the check and the write - and rejects the request as `uniqueness_already_violated`
(409) rather than persisting a broken rule.
`QA01_enabling_enforcement_over_an_already_duplicated_scope_is_rejected` and
`QA01_enabling_enforcement_over_a_clean_scope_still_succeeds` prove the rejection and the
non-regression respectively.

### No canonical id of its own kind

ADR-0001 Decision 1.6 names this story's whole subject matter verbatim: "Institutional
identifiers (matriculation number, staff number) ... are business identifiers. They are
attributes of a record, never its primary key, never a foreign key, and never a canonical
identifier." `contracts/schemas/identifier-registry.json`'s `business_identifiers` block lists
both examples. This story is not minting a seventh ADR-0001 identifier type - it is the story
that models the thing ADR-0001 already excluded from getting one. The natural key is
`(PersonId, Type, CustomLabel, SequenceNumber)` - the identifier-registry's own named example,
"the per-person physical card sequence", is exactly this shape: a natural, per-person-per-type
incrementing sequence, not a canonical id, matching BIO-TEN-002's `TenantDomain` and
BIO-IDN-002's `PersonProfile` precedent.

### AC02: a separate, deliberately-not-CAS'd configuration aggregate

`IdentifierUniquenessRule` is a small, tenant-scoped admin setting, not a record any single
caller "owns" a view of the way a profile or a person is. Its own header documents in full why
`IIdentifierUniquenessRuleRepository.UpsertAsync` is an unconditional write rather than a
compare-and-set: the per-`(TenantId, Type, CustomLabel)` lock in
`IdentifierUniquenessRuleLockRegistry` already fully serializes writers to that exact key, so a
client-supplied `expected_version` would be redundant defense with no real race to defend
against. `Version` still exists and still increments on every write (ADR-0001 Decision 3), as
an audit trail of how many times a tenant has flipped the setting.

A missing rule for a `(Type, CustomLabel)` means the codebase DEFAULT applies -
`AssignAsync` treats a missing rule as `Enforced = true`, never as "no policy": a business
identifier uniqueness invariant should fail closed, favouring a false 409 over silently
admitting a genuine duplicate matriculation or staff number.

### AC03: a "correction" is a new record, never an in-place edit

`Value` is `get`-only, exactly like the identity fields. Real institutional records need to
survive a correction (a mistyped matric number) or a reissue (a lost staff number reassigned)
without losing the fact that the OLD value was once genuinely assigned and used - operational
history (e.g. an attendance event that cited the old value) must never be silently invalidated
by an in-place edit. `RetireAsync` is the only governed transition (`ACTIVE` -> `RETIRED`,
terminal, idempotent no-op if already retired); a correction is `Retire` followed by a fresh
`AssignAsync` for the same `(Type, CustomLabel)`, which lands at the next `SequenceNumber`.
`ListHistoryAsync` returns every record ever created for a `(person, type, custom_label)`,
oldest first, so the full chain is always inspectable.

`AC02`'s uniqueness check runs against ALL records for a `(Type, CustomLabel)` scope - active
AND retired - when enforcement is on, so a retired value is never silently recycled by a
different person while the tenant still treats that type as unique (see
`AC02_a_retired_value_is_never_silently_recycled_when_enforcement_is_on`).

### AC04: never a credential id, and never an authentication surface

`contracts/schemas/data-classification.json` and the Master Technical Specification §4 both
draw the same line this story's own user story draws: an institutional identifier is a business
identifier a person carries (matriculation number, staff number), never the primary electronic
credential used to authenticate them. This feature enforces the boundary structurally, not just
by naming convention:

- No field, property, method or endpoint anywhere in `api/features/bio-idn-003/**` is named
  anything resembling `credential_id` - proven both by an offline structural grep
  (`BIO-IDN-003.test.sh`) and a reflection test
  (`No_field_or_method_anywhere_on_this_service_or_the_aggregate_names_a_credential_id`).
- This feature defines no login/verify/authenticate endpoint of any kind - it is pure
  assign/retire/list/get for a descriptive attribute, never a mechanism a caller could use to
  prove who they are. `No_authentication_or_verification_method_exists_anywhere_on_this_service`
  proves this by reflection.
- No `ProjectReference` to BIO-CRD-001's `crd_` credential aggregate or to BIO-DEV-001's
  `CredentialReferenceValidator`/opaque `vault://`-style device credential reference exists -
  this feature has no relationship to either at all, structurally or by naming.
- The identifier `Value` itself is classified `personal`
  (`contracts/schemas/data-classification.json` names "institutional identifier" as a worked
  example, alongside name and date of birth) and is never placed into the audit trail's
  `Detail` - every mutation lists `"value"` in the event's `Withheld` set instead, mirroring
  BIO-IDN-001's identical posture for name/date_of_birth. See
  `InstitutionalIdentifierAuditEventBuilder`'s header and
  `Assign_records_exactly_one_allowed_audit_event_carrying_the_real_tenant_id_and_withholding_the_value`.

### At most one ACTIVE identifier per (person, type, custom_label)

Structural invariant, independent of AC02's uniqueness-rule configurability: a person may never
hold two simultaneously-active records of the same type - `AssignAsync` while one is still
active is rejected as `identifier_already_active` (409), and
`InMemoryInstitutionalIdentifierRepository.CreateAsync` independently re-checks the same
invariant at the persistence boundary. This is the mechanism through which AC03's "reissue"
flow works: retire first, then assign again.

### Organisation/campus/branch, RBAC and pagination

Out of scope, matching BIO-IDN-002's identical choices for the same reasons: no organisational
hierarchy registry exists yet (BIO-IDN-003 has no AC requiring one - an institutional
identifier is a PERSON attribute, not itself org/campus/branch-scoped); no RBAC enforcement
beyond the asserted `X-Actor-Id` header, pending BIO-INT-001; no pagination on the list/history
endpoints.

### Tenant context seam

Identical shape to BIO-IDN-001's and BIO-IDN-002's own `Tenancy/ICurrentTenantProvider.cs`
(duplicated, not shared). Ships the real `Biolog.Api` wiring in-story
(`api/Biolog.Api/Modules/BioIdn003Module.cs`, `Program.cs` registration, a thin adapter reading
the already-shipped `ITenantContextAccessor`) rather than deferring it, per ADR-0002 Decision
6's per-channel accommodation-ending rule.

## Deliberately out of scope for this story

- A tenant-configurable identifier-type registry beyond the fixed list plus one
  `TENANT_DEFINED` slot per `(person, custom_label)`.
- Organisational hierarchy targeting (no registry exists yet).
- Emergency suspension/lifecycle mechanics beyond `ACTIVE`/`RETIRED` (BIO-IDN-004).
- Duplicate detection across records (BIO-IDN-005).
- RBAC enforcement on who may assign/retire an identifier or change a uniqueness rule
  (BIO-IDN-006), pending BIO-INT-001.
- Pagination on the list/history endpoints.
