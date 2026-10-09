# BIO-ACC-001 — Access policy model and assurance levels

As a tenant security administrator, I need policies expressing WHO + CREDENTIAL + WHERE + WHEN +
STATUS and required assurance.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Policy model supports person/profile, credential class, location, schedule, identity/card status and assurance level | `PolicyRule` composes `SubjectMatch` (person/profile), `CredentialClassMatch`, `LocationMatch`, `ScheduleMatch` (recurring weekly windows), `IdentityStatusMatch`/`CardStatusMatch` and `AssuranceLevel` - every AC01-named dimension is its own typed, independently matchable field | `AccessPolicyServiceTests` (AC01 tests), `AccessPolicyEndpointsIntegrationTests` (HTTP boundary) |
| **AC02** | Draft/publish versioning exists | `AccessPolicy` - a full, permanent version history (`AccessPolicyVersionContent`), Draft->Published workflow (`ReviseDraft`/`Publish`), maker-checker on publish (publisher != author) | `AccessPolicyServiceTests` (AC02 tests) |
| **AC03** | Deny precedence is explicit | `PolicyEvaluator.Evaluate` - a fixed, explicit three-step order: any matching DENY wins outright; otherwise a matching ALLOW wins only if presented assurance satisfies the rule; otherwise DENY by default (fail-closed) | `PolicyEvaluationServiceTests` (AC03 tests - deny-wins-regardless-of-order, default-deny, insufficient-assurance) |
| **AC04** | Policy evaluation is auditable and testable | `PolicyEvaluationService.EvaluateAsync` follows ADR-0003 Decision 7's audit-atomicity pattern for every evaluation, not only authoring mutations; `PolicyEvaluator` itself is a pure, dependency-free function | `AccessPolicyServiceTests`/`PolicyEvaluationServiceTests` (audit tests), `PolicyEvaluationServiceTests` (pure evaluator unit tests) |

## Design decisions and scope boundaries

### Dependencies: BIO-IDN-006 and BIO-ARC-006 only

The approved story declares two dependencies. BIO-ARC-006 turned out to be the shared-contract
freeze mechanism itself (`docs/architecture/SHARED-CONTRACT-FREEZE.md`,
`governance/contract-freeze/**`), not a domain contract to build on - it defines what is frozen
(`contracts/**`, `docs/adr/**`) and the process for amending it, and confirms
`governance/feature-path-ownership.json` already scopes this story to
`{api,web,mobile,tests,docs,evidence}/features/bio-acc-001/**`. BIO-IDN-006 (RBAC role
catalogue/maker-checker) is a genuine runtime dependency: `PermissionService.ActorHasPermissionAsync`
is this story's RBAC integration seam (see "Integration seam" below).

**No other feature project is a declared dependency** - specifically not BIO-CRD-005 (physical
card issuance/status) or BIO-IDN-002 (profile catalogue), even though AC01 asks the policy model
to reference credential class, card status and (via subject) a profile. This shaped a real design
choice - see "Integration seam".

### Integration seam: which AC01 dimensions are gateway-resolved vs caller-supplied

This project takes real runtime gateways only to services a declared (or transitively sanctioned,
per this codebase's established identity-wave convention - every BIO-IDN-00x story gateways to
BIO-IDN-001's `PersonService` directly) dependency makes reachable:

- **Identity status** (`IIdentityStatusGateway`) - resolved LIVE from BIO-IDN-001's
  `PersonService.GetAsync` on every evaluation. A caller-supplied identity status would be a
  pointless, spoofable duplicate of a fact the system already owns.
- **Subject existence** (`IPersonExistenceGateway`) - a rule naming a `SPECIFIC_PERSON` subject is
  existence-checked at authoring time (BIO-IDN-002-QA-01's lesson, applied from the start).
- **RBAC authority** (`IPermissionGateway`) - wraps BIO-IDN-006's `PermissionService.ActorHasPermissionAsync`
  directly; every authoring/publishing/evaluating endpoint requires the calling actor to already
  hold the relevant permission.

### BIO-ACC-001-QA-01 (Chioma's acceptance audit, High): reads are the sensitive asset too

Round 1 left `GET`/`LIST` unauthenticated, reasoning from BIO-IDN-006's own
`RoleAssignmentEndpoints.ListAsync` convention (read endpoints open to any asserted actor). That
precedent does not transfer here: a role ASSIGNMENT list names one person's roles, while an access
POLICY's full content - named subjects, credential classes, locations, schedules, DENY rules and
required assurance levels - is itself the blueprint for bypassing the tenant's own physical
security. An unauthenticated read let any caller who could reach the tenant context enumerate
that blueprint. Fixed: both `GetAsync` and `ListAsync` now require `ReadPermission`
(`access_policy.read`) via `IPermissionGateway`, and both HTTP endpoints require a verified
provenance proof exactly like every mutating one - "read" and "write" are both authorization
decisions here, not just "write."

Everything else AC01 names - **credential class, location, presented card status, and profile
key** - has no reachable system of record within this story's declared dependency graph, so these
are **caller-supplied evaluation inputs** (`PolicyEvaluationContext`), not gateway-resolved facts.
In practice this means the actual access-control system (a reader/gate integration, which already
resolved its own credential-class/location/card-status facts independently through whatever
system owns them) supplies them at evaluation time. This is a deliberate, disclosed scope
boundary, not an oversight - taking a live runtime coupling to BIO-CRD-005 or BIO-IDN-002 without
either being a declared dependency would be introducing an undeclared cross-story dependency this
codebase's wave/dependency rules do not sanction.

### ADR-0006 Decision 3, applied from the start

`docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md` (accepted 2026-09-22, produced by
BIO-IDN-006's own security review) rules that any story making an authorization decision off the
asserted `X-Actor-Id` must verify that identity's provenance before trusting it, not merely check
its shape. Evaluation is itself an authorization decision here (an unauthenticated caller could
otherwise use it as an oracle to probe a tenant's policy rules one evaluation at a time), so every
mutating AND evaluating endpoint is gated behind `ActorProvenanceGate` (an HMAC-SHA256 proof bound
to the specific asserted actor id, mirroring BIO-IDN-006's own implementation exactly) in addition
to its RBAC permission check. `GET`/`LIST` are gated the same way (BIO-ACC-001-QA-01 - see
"Integration seam" above): a policy's own content is itself the sensitive asset, not merely its
ability to change, so this story does not repeat BIO-IDN-006's "reads stay open" convention for
its own read surface.

**What this does not buy** (same disclosed residual as BIO-IDN-006's own `ActorProvenanceGate`):
not authentication, no session revocation/replay protection, and a shared-secret holder can still
compute a valid proof for any actor id. Accepted per ADR-0006 Decision 3, closed by BIO-INT-001.

### AC02: draft/publish modeled after BIO-DOC-005's `ParserRecord`, persisted as an immutable aggregate

BIO-DOC-005's `ParserRecord` is the closest existing precedent for a Draft/Publish version
workflow, but it is a *mutable* class holding a mutable version list, persisted in a plain
`Dictionary` with no per-key lock and no audit trail. `AccessPolicy` borrows `ParserRecord`'s
workflow shape (in-place edit while Draft, a new version once Published, publish requires a
different actor from the draft's own author) but stays a fully **immutable** record instead -
every mutation returns a new `AccessPolicy` via `with`, committed through
`InMemoryAccessPolicyRepository`'s compare-and-set, exactly like BIO-IDN-006's own
`PersonRoleAssignment`. A mutable aggregate shared by reference through a dictionary risks an
unlocked concurrent reader observing a torn, partially-updated object; immutability plus CAS
avoids that class of bug without requiring every read path to also take the per-key lock. This
also lets `ReviseDraft`/`Publish` validate everything and return the new value BEFORE any shared
state changes, so the same "build the new value, then audit, then commit" discipline
`RoleAssignmentService` uses applies here unchanged (ADR-0003 Decision 7).

### AC03: deny precedence is a per-rule field, never list order

Each `PolicyRule` carries its own `Effect` (`ALLOW`/`DENY`). `PolicyEvaluator.Evaluate` collects
every rule that structurally matches the request (WHO+CREDENTIAL+WHERE+WHEN+STATUS) regardless of
its position in the version's rule list, then applies effects in one fixed order: any matching
DENY wins outright; only then is a matching ALLOW considered, and only if the caller's presented
assurance satisfies that specific rule's `RequiredAssuranceLevel`; otherwise DENY by default. A
tenant administrator who writes a DENY rule can therefore always trust it overrides every ALLOW
rule that also matches, regardless of where either rule sits in the array.

### Deliberately out of scope

- **Real trusted-edge actor verification** (ADR-0006's own "edge half") - Emeka's infrastructure
  concern, BIO-INT-001/deployment boundary, not this story's surface.
- **A canonical Location/Site or Schedule-owning story** - neither exists yet anywhere in this
  codebase; `LocationMatch`/`ScheduleMatch` are this story's own minimal, feature-local shapes
  (a plain location id string; UTC-only recurring weekly windows), not validated against any
  external catalogue.
- **BIO-CRD-005/BIO-IDN-002 live gateways** - see "Integration seam" above.
- **Retrofitting policy evaluation into any physical access-control call path** - this story ships
  the model and the evaluation engine/endpoint; wiring an actual reader/gate integration to call
  it is a future, disclosed integration step.

## Verification

- `dotnet build api/Biolog.Api/Biolog.Api.csproj` - clean.
- `dotnet test tests/features/bio-acc-001/Biolog.Features.AccessPolicy.Tests.csproj` - unit/integration tests for the service layer, the pure evaluator, and the real HTTP pipeline (self-hosted TestServer).
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj --filter "FullyQualifiedName~AccessPolicyHostChannelTests"` - real packaged-host proof.
- `bash tests/features/bio-acc-001/BIO-ACC-001.test.sh` - offline structural checks (file/type existence, gateway wiring, no forbidden project references).
- `bash scripts/quality.sh` - repo-wide gate (contract-freeze, traceability, module-reference-direction, secret-scan).
