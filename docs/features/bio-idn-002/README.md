# BIO-IDN-002 — Configurable person profiles and hierarchy assignment

As a tenant, one person may be Student, Employee, Visitor, Contractor, Alumni, Parent or a
tenant-defined profile without duplicate identities.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Multiple simultaneous profiles are supported | The repository's only uniqueness rule is `(PersonId, Type)` — a person may hold a `STUDENT` and an `EMPLOYEE` profile at once, each with its own independent status/expiry | `PersonProfileServiceTests.AC01_a_person_can_hold_two_different_profile_types_at_once` |
| **AC02** | Profiles can target organisation/campus/branch | `PersonProfile.OrganisationId`/`CampusId`/`BranchId` — optional, format-validated-only strings (no hierarchy registry exists yet) | `PersonProfileServiceTests` (AC02 tests) |
| **AC03** | Profile activation/expiry is independent | `ActivatedAt`/`ExpiresAt` belong to each profile alone; `IsCurrentlyInEffect` computes "in effect right now" at read time, never a background-mutated field | `PersonProfileServiceTests` (AC03 tests) |
| **AC04** | No duplicate PERSON is created solely because of a second role | `PersonProfileService` has no person-creation operation at all — every profile attaches to an existing, *verified-to-exist-in-this-tenant* `per_` id, proven via `IPersonExistenceGateway` (see "BIO-IDN-002-QA-01" below) | `PersonProfileServiceTests.AC04_assigning_a_second_profile_type_never_creates_a_second_person`, QA-01 tests |

## Design decisions and scope boundaries

### Applying BIO-IDN-001-QA-01's lesson from the start

BIO-IDN-001's round-1 review found a High finding: its repository and aggregate were fully
public, so a probe could bypass `PersonService` entirely and forge a record. That exact class
of gap — publicly forgeable aggregate, publicly reachable persistence surface — is prevented
here from the first commit, not after a review round:

- `PersonProfile`'s constructor is `internal`; `PersonId`/`TenantId`/`Type` are `get`-only
  (immutable even across a `with` expression).
- `IPersonProfileRepository`/`InMemoryPersonProfileRepository` are `internal`, reachable only
  from this assembly, `Biolog.Api` (DI registration) and this project's own test assembly via
  an explicit `InternalsVisibleTo` grant.
- The repository independently re-validates identity immutability and initial state at the
  persistence boundary (`InvalidInitialState`, `IdentityImmutable`, `AlreadyRevoked`), not
  merely trusting restricted access — the same dual-boundary discipline
  `CredentialReferenceValidator` (BIO-DEV-001-QA-04) established.
- `tests/features/bio-idn-002/PersonProfileRepositoryGovernanceTests.cs` drives the same
  attacks BIO-IDN-001's probe found (foreign tenant, unrelated identity, illegal replace of an
  already-revoked profile), plus reflection tests proving the surface is inaccessible to
  ordinary production consumers.

### No canonical id of its own kind

Unlike PERSON (`per_`, a registered ADR-0001 type), a profile has no dedicated identifier —
its natural key is `(PersonId, Type)`, the same "hostname as natural key" shape BIO-TEN-002's
`TenantDomain` used. Adding a seventh registered identifier type would need an amending ADR
and Kemi's approval for no expressed need.

### BIO-IDN-002-QA-01 (Seyi round 1, High) — profiles could attach to a missing or foreign-tenant PERSON

**Finding.** The original implementation validated only that `person_id` had well-formed
canonical `per_` syntax — it never proved the PERSON actually existed in the resolved tenant.
An independent probe assigned (and permanently audited) a `STUDENT` profile for a wholly
synthetic person id that was never created anywhere, and the identical path let a tenant-B
caller attach its own profile data to a real tenant-A person id (canonical identifiers carry
no authority, ADR-0001 §1.5). Both are orphan/false-linked operational history, and both
defeated this story's own AC04 premise ("attach to an **existing** person").

**Why the original "shape only, no cross-feature coupling" design was wrong here.** BIO-TEN-002
never calling into BIO-TEN-001's real tenant registry was the right call for that pairing —
control-plane tenant *existence* isn't something a tenant-plane domain-mapping story needs to
re-verify at the point of use (a caller-supplied `tenant_id` there is a labelling detail, not
an ownership relationship the aggregate's own integrity depends on). PERSON and PersonProfile
are a different relationship: a profile has no meaning at all without the person it attaches
to, and BIO-IDN-002-AC04's entire premise is "attach to an *existing* person" — so proving
existence isn't optional scope, it's the acceptance criterion. Applying the BIO-TEN-002
precedent here without re-examining whether the underlying relationship was actually the same
shape was the mistake.

**Fix — a narrow, read-only existence gateway, not a broad coupling.**
`Domain/IPersonExistenceGateway.cs` is a one-method, boolean-returning interface *this*
project owns (the same "feature defines the abstraction it needs; the host wires a concrete
implementation" seam `Tenancy/ICurrentTenantProvider.cs` already uses for tenant context) —
still **no** `ProjectReference` to BIO-IDN-001's project. `api/Biolog.Api/Modules/BioIdn002Module.cs`
implements it with a thin adapter that wraps BIO-IDN-001's own already-registered, already
tenant-scoped `PersonService.GetAsync` directly: both modules resolve from the same scoped DI
container for a given request, so the adapter gets BIO-IDN-001's own tenant scoping "for
free" — a person that exists only in a different tenant already reads as "does not exist"
through this gateway with no extra logic. `AssignAsync` calls it before the per-`(person,
type)` lock and before any audit write — a request with no real target is not itself
audit-worthy, the same posture the duplicate-profile-type check already takes. Missing and
foreign-tenant identifiers return the identical `person_not_found` (404) — distinguishing them
would be a cross-tenant enumeration oracle (ADR-0001 §1.5).

Tested at three levels: `PersonProfileServiceTests` (unit, fake gateway, proves the wholly-
unknown case and that a rejected attempt writes no audit event and stores nothing);
`PersonProfileRepositoryGovernanceTests` (unaffected — this is a service-layer check, not a
repository one); and `PersonProfileHostChannelTests.cs` (the real packaged host — registers a
real tenant-A person, then proves a tenant-B caller cannot attach a profile to it, the exact
scenario the review's own probe used, now through the real production adapter, not an
injected double).

### Organisation/campus/branch have no dedicated registry yet

No ADR or contract defines Organisation/Campus/Branch as a registered entity. The Master
Technical Specification §2 names the hierarchy conceptually ("Tenant -> Organisation/Subsidiary
-> Campus/Branch -> Building -> Zone -> Access Point/Device") without specifying its own
storage. `OrganisationId`/`CampusId`/`BranchId` are therefore optional, non-blank,
length-bounded strings — validated for shape only, matching BIO-TEN-002's identical choice for
a caller-supplied tenant id. A future organisational-hierarchy story can extend this without
changing this story's shape.

### Only one governed transition: `ACTIVE -> REVOKED`, terminal

No AC asks for a richer lifecycle than "activation/expiry is independent." `ProfileStatus` has
exactly two states; revoking is the profile's own "governed deletion" — the record and any
operational history that referenced it are never removed. Repeating a revoke is a no-op
(matching BIO-IDN-001's own "same status is a no-op" convention).

### Natural, time-based expiry is computed, never a stored transition

No background scheduler exists anywhere in this codebase to flip `Status` when `ExpiresAt`
passes. `PersonProfile.IsCurrentlyInEffect(nowUtc)` computes "in effect right now" from
`Status` and `ExpiresAt` at read time instead — simpler, and cannot drift out of sync with the
clock the way a stored, mutated field could.

### Tenant context seam

Identical shape to BIO-IDN-001's `Tenancy/ICurrentTenantProvider.cs` (duplicated, not shared,
matching this codebase's established per-feature-project convention). Ships the real
`Biolog.Api` wiring in-story (`api/Biolog.Api/Modules/BioIdn002Module.cs`,
`Program.cs` registration, a thin adapter reading the already-shipped
`ITenantContextAccessor`) rather than deferring it, per ADR-0002 Decision 6's per-channel
accommodation-ending rule — the same reasoning BIO-IDN-001 established, since BIO-TEN-002's
host channel shipped before this wave began.

## Deliberately out of scope for this story

- Re-assigning a profile's organisation/campus/branch after creation, or extending
  `expires_at` — no AC requires either; both are natural, low-risk future extensions.
- A tenant-configurable profile-type registry (beyond the fixed list plus one `TENANT_DEFINED`
  slot per person, described by a free-text `custom_label`).
- Institutional identifiers (BIO-IDN-003).
- The epic's final lifecycle transition table and emergency-suspension mechanics (BIO-IDN-004)
  — this story's own two-state `ProfileStatus` is deliberately not that.
- Duplicate detection (BIO-IDN-005).
- RBAC enforcement on who may assign/revoke a profile (BIO-IDN-006) — every mutating endpoint
  here requires only an asserted `X-Actor-Id`, pending BIO-INT-001.
- Pagination on the list endpoint.
