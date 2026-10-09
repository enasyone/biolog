# BIO-DOC-005 — Barcode Parser Registry and Tenant Parser Workflow

`api/features/bio-doc-005/Biolog.Features.ParserRegistry` is a plain .NET 10 class library (no
ASP.NET Core dependency, no NuGet package references) implementing a versioned, approval-gated
registry of barcode payload parsers, authored either by BTS (platform-wide) or by a permitted
tenant (tenant-scoped). One-way `ProjectReference` to BIO-DOC-003's `Biolog.Features.BarcodeEngine`
(this story's one declared dependency) - a parser interprets the raw payload bytes a barcode
decoder already produced.

## Model

| Type | Kind | Purpose |
| --- | --- | --- |
| `ParserCaller` | record hierarchy | `Bts` or `Tenant(tenantId)` - a plain value this API's caller supplies, not an authentication result (see "Why no HTTP surface, why no actor resolution"). |
| `ParserOwnerScope` | record hierarchy | `Global` (BTS-authored, every tenant can see) or `Tenant(tenantId)` (visible only to that tenant, plus BTS). |
| `TenantParserAuthorityRegistry` | class | Which tenants are permitted to author their own parsers (AC01). |
| `ParserApplicability` | record | Symbology + issuing authority + optional document code a parser version applies to (AC03). |
| `ParserFieldMapping`, `ParserValidationRule` | record | A field's byte-offset/length slice, and its own required/pattern validation rule (AC03). |
| `ParserStatus` | enum | `Draft` → `Tested` → `Published` (AC02). |
| `ParserVersionContent` | record (locked down) | One immutable version's content and workflow state. |
| `ParserRecord` | class | The versioned, ADR-0001-concurrency-safe aggregate root: `Draft`, `ReviseDraft`, `RecordTestResult`, `Publish`. |
| `ParserNotFoundException` | exception | ADR-0002 §4.3 fail-closed not-found, reused for cross-tenant parser isolation (AC05). |
| `ParserCatalog` | class | The store + authorization/visibility enforcement wrapping `ParserRecord` (AC01/AC05). |
| `ParserFieldResult`, `ParserExecutionResult` | record (locked down) | The result of applying a published parser to a raw payload. |
| `ParserExecutor` | static | Applies a **published** version's field mapping and validation to raw payload bytes (AC03). |

## Round 1 review finding, fixed

Seyi's independent review (BIO-DOC-005-F01, High) found `ParserExecutor.Validate` calling
`Regex.IsMatch` with a tenant-authored `ParserValidationRule.Pattern` and no timeout: a permitted
tenant can author a catastrophic-backtracking pattern (e.g. `^(a+)+$`) that, matched against a
crafted long field value, makes .NET's backtracking regex engine consume unbounded CPU - a real
denial-of-service vector, since both the pattern (tenant-authored) and the input it is matched
against (the raw barcode payload, untrusted) are attacker-reachable. Fixed with a bounded
`ParserExecutor.MatchTimeout` (250ms) on every match attempt; a timeout is treated as an honest,
distinct validation failure (`pattern_timeout`) - never left to crash the caller, and never
silently treated as a pass. `ParserExecutorTests.SEC_F01_a_catastrophic_backtracking_pattern_times_out_instead_of_hanging_the_thread`
proves the fix returns promptly (asserts wall-clock elapsed time, not just an eventual answer)
against the exact pattern/input shape the review's reproduction steps named.

## Design decisions worth a reader's attention

### Why no HTTP surface, why no actor resolution

AC01 ("BTS and permitted tenants can define parsers") and AC05 ("tenant parser cannot inspect
another tenant") are genuinely **authorization** decisions, not audit-attribution ones - exactly
the distinction ADR-0006 ("Actor identity and the authorization boundary pending real
authentication") draws, triggered by `BIO-IDN-006`'s finding that a forgeable `X-Actor-Id` header
cannot safely gate an authorization decision before `BIO-INT-001` (real authentication) ships.
Rather than resolve "who is really calling" from any header the way `BIO-IDN-006` round 1 did
unsafely, this story keeps the same scope every `BIO-DOC-00x` story before it has kept: no
`IBiologModule`/HTTP wiring at all. `ParserCaller` is a plain value this library's own caller
supplies - like `DocumentSession.Open`'s plain `tenantId` string (BIO-DOC-001) - and every
authorization/isolation rule (`ParserCatalog`'s `RequireAuthorityToAuthor`, `ParserOwnerScope.IsVisibleTo`)
is enforced against that value, not against a resolved identity. Wiring a real, provenance-verified
actor into that parameter is later work, once a verified-identity mechanism exists - this library's
job is to have the *rules* correct and ready for that seam, not to resolve identity itself.

### AC05 reuses BIO-DOC-001's exact fail-closed pattern

`ParserNotFoundException` is deliberately the same shape as `DocumentSessionNotFoundException`
(ADR-0002 §4.3): a parser that does not exist and one that exists but belongs to another tenant
are indistinguishable to the caller. `ParserCatalog.Get` checks `ParserOwnerScope.IsVisibleTo`
before ever returning a record, and every other mutating operation (`ReviseDraft`, `RecordTestResult`,
`Publish`) calls `Get` first - so a cross-tenant attempt to *revise* or *publish* another tenant's
parser fails exactly the same way a cross-tenant *read* does, proven directly by
`ParserIsolationTests.AC05_tenant_b_cannot_revise_test_or_publish_tenant_as_parser`.

### AC01's "permitted tenants" is a tenant entitlement, not actor RBAC

`TenantParserAuthorityRegistry` records which tenants may author their own parsers at all - a
tenant-level entitlement (closer to ADR-0002's tenant-context/routing concern) rather than a
per-actor role grant. BTS may always author or administer any parser, global or tenant-scoped,
without needing a grant itself (the platform operator role). Provisioning/revoking a tenant's
authority is out of this story's scope (an admin workflow for a later story); this library only
records and checks the resulting grant.

### Two different "versions" in the same aggregate, deliberately not conflated

`ParserRecord.ConcurrencyVersion` is ADR-0001 Decision 3's optimistic-concurrency safety mechanism
for the aggregate's own writes (the same shape as `DocumentSession.Version`). This is a wholly
separate concept from `ParserVersionContent.VersionNumber`, the BUSINESS versioning AC04 asks for
("published parser changes create a new version"). Naming them distinctly, rather than reusing
"version" for both, is deliberate - conflating them would make every future reader re-derive which
one a given method call's `expectedVersion` parameter refers to.

### AC04: a version is frozen the moment it is Tested, not only once Published

`ParserRecord.ReviseDraft` edits the latest version **in place** only while it is still
`ParserStatus.Draft` (unpublished, untested) - editing anything already `Tested` or `Published`
instead starts a brand-new version. This is slightly stricter than AC04's literal wording
("*published* parser changes create a new version") because a `Tested` version has already been
through the one gate that makes `Publish` trustworthy; silently mutating a tested-but-not-yet-
published version's content would let a change bypass re-testing while still carrying its old
`TestedByActorId` stamp - the same "don't let a trust flag survive a change to what it attested to"
principle behind locking down `ParserVersionContent` itself.

### Locked down against post-hoc tampering (the QA-03 lesson, again)

`ParserVersionContent`, `ParserFieldResult` and `ParserExecutionResult` all carry the fields a
downstream caller actually trusts (`Status`, `Valid`, `Success`). Every property is `internal init`
with an explicit `internal` constructor - the same reasoning as every prior `BIO-DOC-00x` result
type this batch: a public `init` would let a caller launder a Draft version into one that claims
`Published`, or a failed field into one that claims `Valid`, via `with`, without this record's own
governed workflow (`ParserRecord.Publish`, `ParserExecutor.Apply`) ever running.

## Reproducing the tests

```sh
dotnet test tests/features/bio-doc-005/Biolog.Features.ParserRegistry.Tests/Biolog.Features.ParserRegistry.Tests.csproj
bash tests/foundation/BIO-DOC-005.test.sh
```
