# BIO-DOC-005 — Builder Evidence

**Story:** Implement barcode parser registry and tenant parser workflow
**Commit:** `8ffaa1c37ba29fc95ece230042ac77249a9e23ac`
**Branch:** `feature/BIO-DOC-005`
**Owner:** Ifeanyi — Document Intelligence Engineer

## What was built

`api/features/bio-doc-005/Biolog.Features.ParserRegistry` — a plain .NET 10 class library, no
ASP.NET Core dependency, no NuGet package references. One-way `ProjectReference` to BIO-DOC-003's
`Biolog.Features.BarcodeEngine` (this story's one declared dependency — a parser interprets the
raw payload bytes a barcode decoder already produced).

- `ParserCaller`/`ParserOwnerScope` — BTS vs. tenant-scoped authorship (AC01/AC05), taken as plain
  domain parameters rather than resolved actor identity.
- `TenantParserAuthorityRegistry` — which tenants are permitted to author their own parsers (AC01).
- `ParserRecord` — the versioned aggregate (ADR-0001 Decision 3 optimistic concurrency) driving
  Draft → Tested → Published (AC02) with maker-checker approval and "editing anything already
  Tested/Published starts a new version, never mutates it" (AC04).
- `ParserCatalog` — the store enforcing AC05's fail-closed cross-tenant isolation via
  `ParserNotFoundException`, applied to every mutating operation, not just reads.
- `ParserApplicability`/`ParserFieldMapping`/`ParserValidationRule` + `ParserExecutor` — AC03's
  "document/issuer applicability, field mapping and validation", closing the loop from
  BIO-DOC-003's `BarcodeDecodeAttempt.RawPayload` to structured field output.
- `ParserVersionContent`/`ParserFieldResult`/`ParserExecutionResult` are locked down (internal
  init, no public constructor) against post-hoc `with`-tampering of `Status`/`Valid`/`Success`.

## A governance/authorization-shaped story, scoped against ADR-0006

Unlike the four prior BIO-DOC-00x recognition-engine stories, this one's AC01 ("BTS and permitted
tenants can define parsers") and AC05 ("tenant parser cannot inspect another tenant") are genuinely
**authorization** decisions, not audit-attribution ones — the exact distinction ADR-0006 draws,
triggered by the `BIO-IDN-006` finding that a forgeable `X-Actor-Id` header cannot safely gate an
authorization decision before `BIO-INT-001` (real authentication) ships. Rather than resolve "who
is really calling" from any header, this story keeps the same "no HTTP surface at all" scope every
`BIO-DOC-00x` story before it has kept: `ParserCaller` is a plain value this library's own caller
supplies (the same shape as `DocumentSession.Open`'s plain `tenantId` string in BIO-DOC-001), and
every authorization/isolation rule is enforced against that value, never against a resolved
identity. Full reasoning in `docs/features/bio-doc-005/PARSER-REGISTRY.md`, "Why no HTTP surface,
why no actor resolution".

AC05's isolation reuses BIO-DOC-001's exact fail-closed pattern: `ParserNotFoundException` mirrors
`DocumentSessionNotFoundException` (ADR-0002 §4.3) — a parser that does not exist and one that
exists but belongs to another tenant are indistinguishable to the caller, proven directly by
`ParserIsolationTests.AC05_a_nonexistent_parser_and_a_cross_tenant_parser_are_indistinguishable_to_the_caller`.

## Test/quality evidence

- `dotnet test tests/features/bio-doc-005/Biolog.Features.ParserRegistry.Tests/...` — 35/35
  passed, all on the first run (no self-caught bugs this time, unlike BIO-DOC-002/003/004).
- `bash tests/foundation/BIO-DOC-005.test.sh` — 27/27 structural checks passed.
- `bash scripts/quality.sh --full` — every BIO-DOC-005-specific check passes; see
  `quality-output.txt`. `.NET integration tests` passed clean this run (the
  `Biolog.IntegrationTests.ObservabilityTests` `StringBuilder`/`chunkLength` failure documented as
  pre-existing/flaky in BIO-DOC-002/003/004's evidence did not reproduce here, confirming it is
  genuinely nondeterministic rather than a permanent regression). Overall `QUALITY: FAIL` only from
  `tests/frontend`/Playwright having no `node_modules` installed — the same pre-existing,
  unrelated gap documented in every prior BIO-DOC-00x story's evidence this batch.

## Round 1 review: CHANGES_REQUIRED, fixed and resubmitted

**Commit:** `1b2fcb413e0e227cbead4c22f0f4f2bb072efa46`

Seyi's independent review (`evidence/BIO-DOC-005/codex/REVIEW-Seyi-round1.md`, BIO-DOC-005-F01,
High, Security FAIL) found `ParserExecutor.Validate` calling `Regex.IsMatch` with a tenant-authored
`ParserValidationRule.Pattern` and no timeout: a permitted tenant can author a catastrophic-
backtracking pattern (e.g. `^(a+)+$`) that, matched against a crafted long field value from the
(also untrusted) raw barcode payload, makes .NET's backtracking regex engine consume unbounded
CPU — a genuine denial-of-service vector, since both the pattern and the input it runs against are
attacker-reachable. Confirmed as a real, valid defect (not a false positive) and fixed:

- `ParserExecutor.MatchTimeout` (250ms) bounds every `Regex.IsMatch` call, the standard .NET
  mitigation for a caller-supplied pattern matched against caller-supplied input.
- A `RegexMatchTimeoutException` is caught and reported as an honest, distinct failure reason
  (`pattern_timeout`) — never left to crash the caller, and never silently treated as a pass.
- Added `ParserExecutorTests.SEC_F01_a_catastrophic_backtracking_pattern_times_out_instead_of_hanging_the_thread`,
  using the exact pattern/input shape from the review's own reproduction steps, asserting
  wall-clock elapsed time — proof the fix returns promptly, not just that it eventually returns
  some answer. Full test run (36 tests, including this one) completed in 274ms.
- Two new structural harness checks (`F01 pattern matching uses a bounded timeout...`,
  `F01 a regex timeout is caught and reported honestly...`).

Reran everything after the fix: 36/36 developer tests pass, 29/29 structural checks pass,
`scripts/quality.sh --full` clean on every BIO-DOC-005-specific check (see
`round2-quality-output.txt`) — `.NET integration tests` also passed clean this run, confirming the
`ObservabilityTests` failure noted in round 1's evidence (and in BIO-DOC-002/003/004's) really is
flaky/nondeterministic. Only the pre-existing frontend/Playwright `node_modules` gap remains.

## Scope notes (deliberately not built)

- No HTTP endpoint or `IBiologModule`/DI registration, and no actor-identity resolution — see
  "A governance/authorization-shaped story, scoped against ADR-0006" above.
- Tenant provisioning/revocation workflow for `TenantParserAuthorityRegistry` — this story records
  and checks the resulting grant; granting it is a later admin story's job.
- Parser retirement/deprecation states beyond Draft/Tested/Published — not asked for by any AC.
