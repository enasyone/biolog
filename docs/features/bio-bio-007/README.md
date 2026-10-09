# BIO-BIO-007 — Implement face 1:N identification and duplicate search

Implements the BIO-BIO epic's seventh story: face identification/duplicate search against an
approved tenant population, built on BIO-BIO-001's provider-neutral biometric contracts (user
story, verbatim: "As authorised workflows, I need face identification/duplicate search against
approved tenant populations.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Search returns ranked candidates with similarity | `Service/FaceSearchService.SearchAsync` compares the live probe against every gallery-indexed identity in the tenant, orders by score descending, caps at `maxCandidates` - see `Domain/FaceSearchResult.cs`/`Domain/FaceSearchCandidate.cs`. The gallery itself only ever contains BIO-BIO-003-approved references, resolved via `Domain/IFaceGalleryApprovalGateway.cs` - `IndexAsync` takes no raw sample at all | `FaceSearchServiceTests.AC01_*`, `FaceSearchServiceTests.QA01_*` |
| **AC02** | 1:N requires explicit tenant feature enablement | `Domain/IFaceSearchFeatureFlagPolicy.cs`; `SearchAsync` checks `IsEnabledForTenant(tenantId)` before the gallery is even read - every tenant defaults to disabled (fail-closed) | `FaceSearchServiceTests.AC02_*` |
| **AC03** | No automatic authorization is granted from search alone | `FaceSearchResult`/`FaceSearchCandidate` carry no allow/deny or authorization field of any kind; `FaceSearchService` has no decide/authorize method at all - a search is purely informational | `FaceSearchServiceTests.AC03_*` |
| **AC04** | No automatic PERSON merge occurs | This project has no `ProjectReference` to BIO-IDN-001 at all - structurally cannot call into `PersonService` | `FaceSearchServiceTests.AC04_*` |
| **AC05** | Population scope and audit are explicit | `IFaceGalleryStore` is tenant-keyed; `FaceSearchResult.PopulationScope`/`PopulationSize` name exactly what was searched; every search writes a `Audit/FaceSearchAuditEvent` via `IFaceSearchAuditSink`, and an audit write whose durability cannot be confirmed fails the search | `FaceSearchServiceTests.AC05_*` |

## Design decisions and scope boundaries

**A separate search "gallery", not BIO-BIO-003's per-identity approved reference STORE.** Mirrors
BIO-BIO-006's own established reasoning, for the face modality: BIO-BIO-003's own
`FaceEnrolmentService`/`IFaceBiometricReferenceRepository` are internal, Scoped-registered, and
expose no way to ENUMERATE every enrolled identity in a tenant (only get-latest-for-ONE-person).
Extending that would modify BIO-BIO-003's own owned files, out of this story's write scope. This
story instead defines its own `Domain/IFaceGalleryStore.cs` as the searchable population - but
(see the BIO-BIO-007-QA-01 postmortem immediately below) every entry in it must still be sourced
from an actual BIO-BIO-003-approved reference; the gallery is a separate PERSISTENCE structure
from BIO-BIO-003's own, never a separate APPROVAL standard.

**BIO-BIO-007-QA-01 (Chioma acceptance, High).** The user story's own wording is "search against
APPROVED tenant populations." The first builder round read that as satisfied by the gallery simply
being tenant-scoped and explicitly labelled (`PopulationScope = "tenant_gallery"`) - AC05's own
"population scope... explicit" wording - and let `IndexAsync` accept an arbitrary caller-supplied
raw `BiometricSample`, extract a template directly through `IFaceProvider`, and index it with NO
check of capture quality, liveness, or BIO-BIO-003 approval at all. An acceptance probe correctly
rejected this: labelling a population "explicit" says nothing about whether it is APPROVED, and a
caller could place an unapproved or failed-liveness capture (or outright arbitrary bytes) into the
exact population every search result and audit event calls the approved tenant gallery. Fixed by
removing `IndexAsync`'s raw-sample parameter entirely: it now resolves the claimed identity's
CURRENT approved reference through `Domain/IFaceGalleryApprovalGateway.cs` (real adapter wraps
BIO-BIO-003's already-registered `FaceEnrolmentService.GetLatestReferenceAsync` - the same narrow-
gateway shape BIO-BIO-005's own `IFaceReferenceGateway` already established) and indexes THAT
reference's own already-vetted template. BIO-BIO-003's own `CaptureAsync` only ever constructs a
`FaceBiometricReference` for a capture that passed both its quality and liveness gates, so
resolving a non-null result through this gateway IS sufficient proof of approval - there is no
code path left in this feature that can place an unapproved template into the gallery.

**AC02's feature flag is TENANT-WIDE, not per-actor.** This is a deliberately different axis from
BIO-BIO-006-AC02's own per-actor permission: AC02 here asks whether 1:N face search EXISTS for a
tenant at all, independent of who is calling. `SyntheticFaceSearchFeatureFlagPolicy` defaults
every tenant to disabled; `EnableForTenant`/`DisableForTenant` are configuration-time-only
operations `SearchAsync` never calls itself - the same "ordinary caller has no path to influence
this" property every prior BIO-BIO policy documents.

**AC03 is a stronger separation than BIO-BIO-005's evidence/decision split.** BIO-BIO-005-AC03
keeps evidence and authorization as two separate STEPS (a caller can still reach a decision by
calling a separate, pure function on the evidence). This story's AC03 goes further: there is no
decision step here AT ALL, anywhere in this feature - `FaceSearchResult` and
`FaceSearchCandidate` carry no decision-shaped field, and `FaceSearchService` exposes no
decide/authorize method of any kind. A search is purely informational; any authorization a caller
wants to build on top of a search result happens through an entirely separate mechanism this
feature has no knowledge of and no opinion about.

**No duplicate-review queue in this story**, unlike BIO-BIO-006's own AC03. None of this story's
five ACs ask for possible-duplicate routing to human review - AC04 only requires that no
automatic PERSON MERGE occurs, which this story satisfies structurally (no `ProjectReference` to
BIO-IDN-001 at all) without needing a review-queue subsystem. A later story that wants face
duplicates routed to human review the way BIO-BIO-006 routes fingerprint duplicates adds that on
top of `FaceSearchService.SearchAsync`'s existing ranked-candidate output without this feature's
shape needing to change.

**AC05's audit is a genuine, durability-checked requirement, unlike BIO-BIO-003/006's own
deliberate "no audit" decisions.** Neither of those sibling stories' ACs mentioned auditing, so
neither built a sink. This story's own AC05 explicitly says "audit ... are explicit," so
`FaceSearchService` applies the SAME ADR-0003 Decision 7 discipline every mutating service in this
codebase uses for its own writes: `IFaceSearchAuditSink.WriteAsync`'s ambiguous throw is resolved
via `ExistsAsync` rather than assumed to be a failure, and a write that genuinely cannot be
confirmed durable throws `FaceSearchException.AuditUnavailable` - a search this story cannot
honestly claim was audited does not silently succeed.

**"Population scope explicit" is satisfied by naming it, not by a multi-population subsystem.**
`FaceSearchService.PopulationScope` is a single, fixed, named descriptor ("tenant_gallery") that
every search result and audit event carries - "the population is exactly and only every identity
currently indexed for this tenant" is stated plainly on every record, rather than left for a
reader to infer. Building a richer multi-named-population feature (e.g. "students" vs "staff"
sub-galleries) is scope none of this story's five ACs ask for.

**Provider extraction failures are NOT caught into a typed outcome here**, the same deliberate
scope decision BIO-BIO-006 documents for its own sibling search story: no AC here asks for a
per-attempt evidence record distinguishing an acquisition failure from a routine no-match outcome,
and a search over a population is a different shape of operation from a single 1:1 decision. An
extraction failure propagates as a genuine caller/integration error.

**No HTTP endpoint is mapped by this story**, the same judgment call every prior BIO-BIO story
made: AC01-AC05 are about the search/population/audit model, not an HTTP surface, and the service
takes a caller-resolved tenant/person/actor id rather than resolving one itself.

**Identifiers are plain strings**, the same explicitly documented choice every prior BIO-BIO story
made; no `identifier-registry.json` prefix is registered for a search result or an audit event
(frozen, ADR-governed contract, out of this story's `read_only_shared_contracts` scope).

**Every sample byte array in this suite is synthetic filler**, never a captured face image (this
story's own top-level constraint: "Use synthetic data only").
