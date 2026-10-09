# BIO-PHO-001 — Immutable photo master and derivatives

As BIOLOG, an approved master photo plus print and display derivatives preserve both card
quality and UI performance, without either ever undermining the other.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | PhotoMaster is immutable/versioned | `PhotoMaster` has no `init`/`set` accessor anywhere — structurally immutable; `IPhotoMasterRepository` exposes only `AppendAsync`, no update/replace/delete. Each person's lineage is versioned via a plain, ever-increasing sequence number (`(PersonId, Version)`), computed under `PhotoMasterLockRegistry` | `PhotoMasterServiceTests` (AC01 tests), `PhotoEndpointsIntegrationTests` (AC01 tests), `PhotographyHostChannelTests.AC01_...` |
| **AC02** | Print and display derivatives reference the master | `PhotoDerivative.MasterVersion` is the pointer; `IPhotoDerivativeRepository`'s own interface carries no reference to the master's write side at all | `PhotoDerivativeServiceTests.AC02_a_regenerated_derivative_references_its_masters_version` |
| **AC03** | Derivative regeneration does not replace the master | Structural, not conventional: `PhotoDerivativeService.RegenerateAsync` can only call `IPhotoMasterRepository`'s read-only `GetVersionAsync` — that interface has no write method reachable from anywhere in this feature | `PhotoDerivativeServiceTests.AC03_regenerating_a_derivative_many_times_never_changes_the_masters_own_content_or_version`, `AC03_regenerating_replaces_only_the_previous_derivative_of_the_same_kind` |
| **AC04** | Person-photo assignment is versioned and audited | `PhotoAssignment` carries a real ADR-0001 §3.1/§3.2 compare-and-set `Version`; every committed change is recorded via `PhotoAuditEvent` BEFORE the repository commit (ADR-0003 Decision 7 audit-atomicity) | `PhotoAssignmentServiceTests` (AC04 tests), `PhotographyHostChannelTests.AC04_...` |
| **AC05** | Synthetic demo photos remain consistently assigned in tests | `DemoPhotoCatalog.SyntheticContentFor` is a pure, deterministic function of the person id (SHA-256-derived, never `Random`/wall-clock); `SeedIdempotentlyAsync` is safe to call any number of times — only the first call creates anything | `DemoPhotoCatalogTests` (AC05 tests) |

## Design decisions and scope boundaries

### "Immutable/versioned" (AC01) reconciled with ADR-0001 §3.3's "no version field"

ADR-0001 §3.3 names PhotoMaster explicitly as immutable/append-only, carrying **no** mutable
`version` field, and states correcting one means "appending a new record that supersedes the
old one and points at it." `PhotoMaster.Version` here is not that mutable CAS token — it is a
plain sequence number identifying WHICH immutable row this is within one person's lineage, the
same resolution `BrandAsset` (BIO-CRD-001) already used for an identical apparent
contradiction. Nothing about this type or `IPhotoMasterRepository` can mutate a previously
appended row: there is no `init`/`set` accessor anywhere on `PhotoMaster`, and the repository
interface has no update/replace/delete method at all — the invariant is structural, not
conventional.

### PhotoAssignment is the opposite shape on purpose

`PhotoAssignment` (AC04) IS a mutable, CAS-versioned tenant aggregate under ADR-0001 §3.1/§3.2
— the opposite immutability category from `PhotoMaster` in the same feature. That contrast is
deliberate: AC01 and AC04 name two different ADR-0001 categories on purpose, and a
person-child aggregate in this codebase can be either, matching `PersonProfile`'s (mutable) vs.
raw document evidence's (immutable) split precedent elsewhere in this repository.

### No canonical id of its own kind

None of `PhotoMaster`, `PhotoDerivative` or `PhotoAssignment` mints a new ADR-0001 registered
identifier type. Each is keyed by a natural/composite key against the already-registered `per_`
PersonId — `(PersonId, Version)`, `(PersonId, MasterVersion, Kind)` and `PersonId` respectively
— the same "no new registered type for a person-child aggregate" choice `PersonProfile`
(BIO-IDN-002) made, for an identical reason: adding an eighth registered identifier type would
need an amending ADR and Kemi's approval for no expressed need.

### Applying BIO-IDN-001-QA-01's lesson from the start

Every aggregate's constructor and its repository are `internal`, reachable only from this
assembly, `Biolog.Api` (DI registration) and this project's own test assembly via an explicit
`InternalsVisibleTo` grant — the same "public repository + public constructor lets a caller
bypass the service entirely" class of gap BIO-IDN-001's round-1 review found, closed here
before a review round ever finds it.

### Cross-feature person existence: a narrow gateway, not a ProjectReference

`Domain/IPersonExistenceGateway.cs` is this project's own one-method interface — no
`ProjectReference` to `api/features/bio-idn-001` at all (this project has zero
`ProjectReference`s to any other feature project). `api/Biolog.Api/Modules/BioPho001Module.cs`
implements it with a thin adapter wrapping BIO-IDN-001's own already-registered, already
tenant-scoped `PersonService.GetAsync` — tenant-scoping "for free," the same
BIO-IDN-002-QA-01-derived shape every sibling identity/card-wave story since has used from the
start.

### Dependency: BIO-CRD-001 is sequencing, not coupling

The approved story lists `BIO-CRD-001` as a dependency. Nothing in this feature calls into
`BrandAssetsModule` or references brand-asset types — a photo master/derivative and a brand
asset are sibling card-production concerns with no runtime overlap in this story's own
acceptance criteria. This matches the epic's established "Dependencies in story metadata is
build-wave sequencing, not a runtime service dependency" precedent (BIO-IDN-002/003 etc.).

### Tenant context: the real BIO-TEN-002 host channel, not a bespoke scheme

`Tenancy/ICurrentTenantProvider.cs` reads the real, already-shipped
`ITenantContextAccessor`/`TenantContextMiddleware` (BIO-TEN-002's real host-channel
resolution) through the same thin per-feature adapter shape every sibling identity-wave module
uses. This is a different (and now-current) mechanism from BIO-CRD-001's own bespoke
header-plus-shared-secret tenant authenticator, which predates BIO-TEN-002 shipping and was
explicitly scoped as a stopgap for that reason.

### Actor identity: audit-attribution only (ADR-0006 Decision 1/2/4)

This feature defines no `RoleType`, permission check, or any other gate that makes what a
caller may do depend on the asserted `X-Actor-Id` — every endpoint is uniformly open to any
caller with API access. This is ADR-0006's safe "audit-attribution use" shape, the same
disclosed-residual category the ADR already catalogues for BIO-IDN-001 through BIO-IDN-005: a
forged actor id can misattribute who claims responsibility for a mutation this feature already
allowed any caller to perform, but crosses no privilege-escalation boundary, since there is no
differential authority here for a forged identity to escalate into. It does not require an
`ActorProvenanceGate`-style mitigation (BIO-IDN-006's own interim closure for a genuine
authorization-decision gap), and does not trigger a security review under the same convention
that correctly scoped one to BIO-IDN-006 alone.

### Audit trail privacy (ADR-0003 Decision 3)

"Photo reference" is classified `personal` (allowlist only); raw biometric/photo image bytes
sit at `sensitive`/`secret` (never carried in any form, not even redacted). This feature's audit
`Detail` therefore only ever carries `internal` technical metadata (format, pixel dimensions,
size, version numbers) — `PhotoAuditEventBuilder` has no allowlist projection for `personal`/
`sensitive`/`secret` at all, so a caller cannot smuggle a photo reference or the photo bytes
through `Detail` by simply naming a higher classification. `AuditTarget.Id` carries `PersonId`,
the same structural split (Target identifies the record; Detail is allowlist-only) every
sibling audit builder already uses.

### Derivative generation computes real metadata, not real pixels — a disclosed scope boundary

No image-processing library (`SixLabors.ImageSharp`, `SkiaSharp`, `System.Drawing`) is
referenced anywhere in this host yet, and adding one is outside this story's own declared scope
without further specification. `MetadataOnlyPhotoDerivativeGenerator` computes real target
dimensions for each derivative kind (named constants, since no master-spec section or ADR names
a pixel target for either use — the same "one named constant to revise, not a magic number"
resolution BIO-CRD-001 used for its own unspecified DPI floor) but does not decode, resample or
re-encode the master's pixels; the master's own bytes pass through unchanged. `IPhotoDerivativeGenerator`
is the seam a later story (BIO-PHO-003, photo quality) swaps for real resampling without
changing `PhotoDerivativeService` or anything above it.

### No pagination, no delivery/download endpoint

List/get endpoints return metadata only (never raw bytes) — the same "the response describes
the record, a separate concern serves the bytes" boundary `BrandAssetResponse` (BIO-CRD-001)
already established. No AC in this story asks for a bytes-delivery endpoint, a CDN path, or
list pagination on a per-person lineage that is expected to stay small (a handful of versions
per person, not an unbounded collection).

## Deliberately out of scope for this story

- Real pixel resampling for derivatives (BIO-PHO-003).
- Photo booth capture UX/hardware integration (BIO-PHO-002).
- Photo quality/liveness checks beyond raster header parsing (BIO-PHO-003).
- RBAC enforcement on who may append a master or reassign a photo (BIO-IDN-006's own catalogue
  exists, but nothing wires it into this feature) — every mutating endpoint here requires only
  an asserted `X-Actor-Id`, pending BIO-INT-001.
- A bytes-delivery/download endpoint or CDN integration for served photo content.
- Real MySQL-backed persistence (ADR-0005: every schema migration goes through Kemi's
  `migration-core` lock; the `photo-core` conflict group has no standing to start one
  unilaterally). `IPhotoMasterRepository`/`IPhotoDerivativeRepository`/`IPhotoAssignmentRepository`
  are the seams a real, migration-backed implementation drops in behind.
