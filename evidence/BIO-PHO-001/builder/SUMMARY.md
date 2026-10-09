# BIO-PHO-001 — Builder evidence

**Story:** Implement immutable photo master and derivatives
**Owner:** Ngozi — Credential & Card Engineer
**Conflict group:** `photo-core`
**Dependencies:** BIO-IDN-001 (genuine runtime dependency via `IPersonExistenceGateway`), BIO-CRD-001 (sequencing only — no runtime coupling)

## Source hierarchy check

No conflict found between the Master Technical Specification, accepted ADRs (ADR-0001,
ADR-0002, ADR-0003, ADR-0004, ADR-0005, ADR-0006) and the approved story/AC text. The one
apparent tension — AC01 "immutable/versioned" vs. ADR-0001 §3.3's "PhotoMaster ... carr[ies]
no version field" — is reconciled in `docs/features/bio-pho-001/README.md` ("`Version` is a
plain sequence number identifying which immutable row this is, not the mutable CAS token §3.3
forbids") using the identical resolution BIO-CRD-001's `BrandAsset` already established for the
same apparent contradiction; not escalated as a genuine conflict.

## AC-to-evidence map

- **AC01 (PhotoMaster is immutable/versioned):** `Domain/PhotoMaster.cs` (no `init`/`set`
  accessor anywhere), `Domain/IPhotoMasterRepository.cs` (no update/replace/delete method).
  Tests: `PhotoMasterServiceTests` (AC01 tests, incl. a 10-way concurrent-append test proving no
  duplicate/skipped version), `PhotoEndpointsIntegrationTests` (AC01 tests),
  `PhotographyHostChannelTests.AC01_appending_a_master_through_a_verified_hostname_resolves_the_real_tenant`.
- **AC02 (derivatives reference the master):** `Domain/PhotoDerivative.cs` (`MasterVersion`
  pointer). Tests: `PhotoDerivativeServiceTests.AC02_a_regenerated_derivative_references_its_masters_version`,
  `PhotoEndpointsIntegrationTests.AC02_AC03_regenerating_a_derivative_references_the_master_and_leaves_it_unchanged`.
- **AC03 (regeneration does not replace the master):** structural — `IPhotoDerivativeRepository`
  has no reference to the master repository's write side; `PhotoDerivativeService.RegenerateAsync`
  only ever calls the master repository's read-only `GetVersionAsync`. Tests:
  `PhotoDerivativeServiceTests.AC03_regenerating_a_derivative_many_times_never_changes_the_masters_own_content_or_version`,
  `AC03_regenerating_replaces_only_the_previous_derivative_of_the_same_kind`.
- **AC04 (assignment is versioned and audited):** `Domain/PhotoAssignment.cs` (real ADR-0001
  §3.1/§3.2 CAS `Version`), `PhotoAssignmentService.AssignAsync` (audit-then-commit, ADR-0003
  Decision 7). Tests: `PhotoAssignmentServiceTests` (AC04 tests, incl. a concurrent-reassignment
  race test proving exactly one winner), `PhotographyHostChannelTests.AC04_...`.
- **AC05 (synthetic demo photos remain consistently assigned in tests):** `DemoPhotoCatalog`
  (deterministic, hash-derived content; idempotent seeding). Tests: `DemoPhotoCatalogTests` (all
  three AC05 tests), `PhotoEndpointsIntegrationTests.AC05_...`.

## CHANGES_REQUIRED round 1 — BIO-PHO-001-QA-01 (Seyi)

**Finding:** `PhotoMaster` and `PhotoDerivative` exposed `Content` as a get-only `byte[]`
auto-property. A get-only property does not make a reference type immutable: the backing
array could still be mutated in place, either by the caller retaining its own reference to
the array it passed into the constructor, or by any later reader mutating the array handed
back by `Content` (a plain auto-property returns the live backing array, not a copy) -
corrupting what every subsequent reader saw too. Both channels defeated AC01's "immutable"
premise.

**Fix:** the standard "defensive copy in, defensive copy out" idiom on both types - the
constructor clones the caller's array before storing it (`_content = (byte[])content.Clone();`),
and `Content` clones the stored array on every read (`public byte[] Content => (byte[])_content.Clone();`).
Added direct regression tests (`PhotoMasterTests.cs`, `PhotoDerivativeTests.cs`) proving both
tamper channels are closed, plus two structural harness checks. Re-ran the full suite after the
fix: `tests/features/bio-pho-001/BIO-PHO-001.test.sh` 35/35 (was 32), feature unit suite 41/41
(was 37), shared unit/integration suites unaffected (160/160, 91/91), `.NET vulnerability audit`
clean. Committed as `f376d46`. `evidence/BIO-PHO-001/builder/metadata.json`'s `commit_hash`
updated to the fix commit.

## Decisions and assumptions (read `docs/features/bio-pho-001/README.md` for the full reasoning)

- **Followed the now-established project-per-feature convention** (`api/features/bio-pho-001/Biolog.Features.Photography.csproj`,
  a thin `api/Biolog.Api/Modules/BioPho001Module.cs` adapter, one-way `ProjectReference`), the
  pattern every story since BIO-DOC-001 (BIO-TEN-001/002, BIO-IDN-001..006) uses — not my own
  earlier BIO-CRD-001 pattern (built directly inside `Biolog.Api`), which predates this
  convention settling.
- **Structural test harness lives at `tests/features/bio-pho-001/BIO-PHO-001.test.sh`**, matching
  every story built under the current convention. Per `scripts/check-traceability.py`'s own
  discovery rule (`tests/foundation/*.test.sh` only), this story is therefore correctly outside
  the `governance/stories/*.json` traceability gate — the same status every BIO-TEN/BIO-IDN
  story already has. No `governance/stories/BIO-PHO-001.json` was created.
- **Used the real BIO-TEN-002 host-channel tenant resolution** (`ITenantContextAccessor`, via
  the same thin per-feature `ICurrentTenantProvider` adapter every sibling module uses) —
  BIO-CRD-001's own bespoke header-plus-shared-secret tenant authenticator predates BIO-TEN-002
  shipping and was an explicitly-scoped stopgap for that reason; it does not apply here.
- **Actor identity is audit-attribution only** (ADR-0006 Decision 1/2/4): this feature defines
  no authorization/role-gating logic anywhere, confirmed by direct inspection, so it is the same
  disclosed-residual category as BIO-IDN-001 through BIO-IDN-005, not BIO-IDN-006's
  authorization-decision category — no `ActorProvenanceGate`-style mitigation is required, and
  this does not trigger `security_review` under the same convention that correctly scoped one to
  BIO-IDN-006 alone.
- **No new ADR-0001 canonical identifier type minted.** `PhotoMaster`/`PhotoDerivative`/`PhotoAssignment`
  are keyed by natural/composite keys against the already-registered `per_` PersonId, the same
  choice `PersonProfile` (BIO-IDN-002) made for an identical reason.
- **Derivative generation computes real target-size metadata but does not resample pixels** —
  no image-processing library exists anywhere in this host, and adding one is outside this
  story's declared scope without further specification. `IPhotoDerivativeGenerator` is the seam
  a later story (BIO-PHO-003) swaps for real resampling.
- **No bytes-delivery/download endpoint.** List/get endpoints return metadata only, matching
  `BrandAssetResponse`'s (BIO-CRD-001) identical boundary; no AC asks for one.
- **Persistence is in-memory** (ADR-0005: every schema migration goes through Kemi's
  `migration-core` lock; `photo-core` has no standing to start one unilaterally). All three
  repository interfaces are the seams a real, migration-backed implementation drops in behind.

## Test results (commit `58883c9`)

- `bash tests/features/bio-pho-001/BIO-PHO-001.test.sh` — 32/32 PASS
- `dotnet test tests/features/bio-pho-001/Biolog.Features.Photography.Tests.csproj` — 37/37 PASS
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` — 160/160 PASS
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` — 91/91 PASS
  (includes 5 new `PhotographyHostChannelTests`, through the REAL packaged host)
- `dotnet test tests/features/bio-doc-001/Biolog.Features.DocumentVerify.Tests/...` — 77/77 PASS (unaffected, sibling story)
- `bash scripts/check-module-project-references.sh` — PASS
- `python3 scripts/check-contract-freeze.py` — PASS (no shared-contract file touched)
- `bash scripts/quality.sh` (base tier) — **PASS**, see `evidence/BIO-PHO-001/builder/quality-output.txt`
- `bash scripts/quality.sh --full` — see `evidence/BIO-PHO-001/builder/quality-full-output.txt`.
  Every .NET check passes (unit 160/160, integration 91/91, document-verify 77/77,
  photography 37/37, `.NET vulnerability audit` PASS). `frontend unit tests` and
  `Playwright tests` fail with `vitest: command not found` / `playwright: command not
  found` — `tests/frontend/node_modules` is missing on the untouched canonical checkout
  too (confirmed directly), a pre-existing, unrelated gap this story never touches
  (`web/`, `tests/frontend/`). `Flutter tests` pass (1/1). Overall script exit reflects
  that pre-existing gap, not a regression from this story.

Full transcripts captured alongside this file.
