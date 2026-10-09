# BIO-RPT-002 — Builder evidence

**Story:** Implement visual report builder foundation
**Result:** PASS

## Restated scope (per the story-stage prompt)

**User story.** As authorised users, I need a governed report builder using approved
dimensions/measures rather than arbitrary tenant SQL.

**Acceptance criteria:** AC01 (dimensions/measures whitelisted), AC02 (CSV/Excel/PDF export
hooks exist), AC03 (tenant isolation enforced), AC04 (large reports are asynchronous/cancellable),
AC05 (audit records report generation/export), AC06 (the approved web surface is connected
through `ReportingService` or an explicitly documented HYBRID state, mapped human workflow
passes without an unapproved UI redesign).

**Dependencies.** BIO-ATT-001 (attendance event core — raw event fields as one whitelisted
dataset), BIO-CRD-007 (card production — my own prior story; one purely additive read method
added, see below).

**Owned write paths.** `api/features/bio-rpt-002/**` (new feature project — `Domain/`, `Api/`,
`Audit/`, `Identifiers/`, `Tenancy/`), `api/Biolog.Api/Modules/BioRpt002Module.cs`,
`tests/features/bio-rpt-002/**`, `web/features/reporting/**` (extended, not new — see "Shared,
unlisted entry points" below), `web/features/reporting/ReportBuilderCard.tsx` (new),
`tests/frontend/playwright/bio-rpt-002-*.spec.ts`, `tests/frontend/unit/bio-rpt-002-*.test.ts`,
`docs/features/bio-rpt-002/**`, `evidence/BIO-RPT-002/**`.

**Shared, unlisted entry points touched** (the same role `module-registry.ts`/`Program.cs` play
for every story in this epic — not exclusively owned by any one story):
`api/Biolog.Api/Program.cs` (registers `BioRpt002Module`),
`api/Biolog.Api/Biolog.Api.csproj` (one new `ProjectReference`),
`web/lib/module-registry.ts` (registers 4 new `ReportingService` port keys),
`web/lib/feature-mode.ts` (`MODULE_PORT_KEYS['ReportingService']` extended to match),
`web/next.config.mjs` (one new `/api/v1/reporting/:path*` rewrite entry),
`web/app/reports/page.tsx` (mounts `ReportBuilderCard` alongside BIO-UX-011's own unmodified
"Run a report" card),
`tests/frontend/unit/bio-ux-005-feature-mode.test.ts` (its own "documents the current shape"
guard test updated to include the new multi-port list, per that test's own stated purpose),
`scripts/quality.sh` (registers this story's `.test.sh` harness and `dotnet test` line),
`tests/frontend/playwright/bio-ux-014-visual-regression.spec.ts-snapshots/reports-darwin.png`
(regenerated — a legitimate baseline update, since this story genuinely added visible content to
`/reports`; not a design change, `ReportBuilderCard` uses only existing `.biolog-*` classes).

**One additive change to BIO-CRD-007 (my own prior, already-accepted story).**
`api/features/bio-crd-007/Domain/IProductionJobRepository.cs` gained exactly one new method,
`ListForTenant(tenantId)` — that story's own original scope never needed a tenant-wide read
(every existing method is keyed to one specific card issue). Purely additive: every prior
method/behaviour unchanged, verified by rerunning BIO-CRD-007's own full suite (52/52 green)
plus one new test covering the addition.

**Read-only shared contracts respected.** `web/features/reporting/live.ts`'s/`mock.ts`'s original
`reportingMockPort`/`reportingLivePort`/`reportingViewAuditTrailMockPort`/
`reportingViewAuditTrailLivePort` (BIO-UX-005's own, backing BIO-RPT-001's real routes — see
"Design decisions" below), `web/app/audit/page.tsx`, `web/app/page.tsx` (Dashboard) — none
modified. BIO-RPT-001's own module/routes/tests — none touched.

**Security/privacy risks identified and addressed.**
- Tenant isolation: `ReportDefinition` carries no `TenantId` field at all — the tenant a report
  runs against always comes from `ICurrentTenantProvider`, never request content. A cross-tenant
  job lookup returns 404, never a confirmed-to-exist 403. Verified by test.
- No PII/biometric data — every whitelisted dimension is an operational enum value (job status,
  device source, attendance mode) from already-synthetic dependency data.
- Audit-before-response discipline for every mutating action (job creation, cancellation, export).

**Tests planned and written.** See "Test evidence" below.

## Design decisions

See `docs/features/bio-rpt-002/README.md` for the full write-up. Summary: exactly two whitelisted
datasets matching the two declared dependencies (no arbitrary tenant SQL); every report runs
through a real `Queued/Running/Completed/Failed/Cancelled` job, never a synchronous inline result;
deterministic (non-timing-based) cancellation test via a `TaskCompletionSource`-gated fake
gateway; CSV export is real (hand-written, RFC 4180), Excel/PDF are real, reachable hooks that
honestly answer 501 not-yet-implemented; one purely additive method added to BIO-CRD-007's own
repository.

**A genuine cross-story route conflict, found and resolved before merge — the main story of this
build.** My first implementation pointed its own report engine at `POST /api/v1/reporting/primary-
action` (the exact path `web/features/reporting/live.ts`, BIO-UX-005, already targeted). Manually
running the real packaged host end to end surfaced an `AmbiguousMatchException`: BIO-RPT-001
(Notifications, already accepted) had already mapped that exact path as its own real, tested AC05
implementation. This was escalated to god rather than resolved unilaterally (see the
hive-protocol query/ruling referenced in `docs/features/bio-rpt-002/README.md`'s "AC06" section) —
ruling: BIO-RPT-002 gets its own dedicated routes/ports; BIO-RPT-001's own routes stay entirely
untouched. Result: four new, dedicated `ReportingService` ports
(`generateReport`/`getReportStatus`/`cancelReport`/`exportReport`), a new `ReportBuilderCard`
mounted on `/reports` alongside (not replacing) BIO-UX-011's own existing card, and a structural
regression test proving this project's own endpoint group never maps
`/primary-action`/`/view-audit-trail`.

## Issues found and fixed during development (not assumed correct, verified)

1. **CSV exporter used platform-dependent line endings, not RFC 4180's mandatory CRLF.**
   `StringBuilder.AppendLine` uses `Environment.NewLine` — `"\n"` on this repo's own Linux/macOS
   containers, silently violating the RFC. Caught by my own `CsvReportExporterTests.cs` on the
   very first run, not assumed correct. Fixed with explicit `"\r\n"` appends.
2. **A `ReportError` factory crashed on the exact invalid value it was meant to report.**
   `ReportError.UnknownDataset` called `.ToWireString()` on an out-of-range `ReportDataset` enum
   value — that extension method's own exhaustive switch throws `ArgumentOutOfRangeException` for
   exactly that case, so validating a genuinely invalid enum crashed instead of returning a
   governed error. Caught by a test that deliberately passed an out-of-range value rather than
   only valid ones. Fixed by using the raw `.ToString()` for the error detail instead.
3. **A DI lifetime bug no unit test could have caught.** `ReportJobRunner`/`ReportGenerationService`
   were registered Singleton (required — `ReportJobRunner`'s cancellation-token registry must
   survive past any one HTTP request's scope) but depended on gateways originally registered
   Scoped. Every one of my own unit/integration tests constructs these services manually, so none
   exercised the real DI container — only actually starting the packaged host surfaced ASP.NET
   Core's own service-provider validation error at startup. Fixed by making the gateways Singleton
   too (correct: neither resolves anything from a scoped accessor — tenant id flows as an explicit
   parameter). Lesson: for any backend story adding new module registrations, boot the real
   packaged host early, not only at the final AC06 manual-verification pass.
4. **Two Playwright locator strict-mode violations in my own tests**, both self-caught: a status
   message ("Report ... is completed") collided with the job-status badge's own "completed" text;
   fixed by scoping to `.biolog-status-badge` within the job-status container specifically (the
   card's own LIVE/SIMULATION mode badge uses the same CSS class, so a bare `card.locator(...)`
   still collided — scoped one level further, to the `role="status"` container).
5. **A vitest ordering hazard in my own boundary test.** The mock offline-gate's call counter is
   shared, per-module-instance state; my first draft of the "every 5th call is offline" test
   assumed a fresh counter, which any other test in the same file calling the same port first
   would have silently broken. Fixed with `vi.resetModules()` + a fresh dynamic import for that
   one test, isolating it from the shared singleton every other test in the file uses.

None of these were review-round findings — all five were caught and fixed during my own
implementation pass, including the route conflict (escalated and resolved before any independent
review began), before any independent review.

## Test evidence

- **Backend unit/integration tests** (`dotnet test`, xunit): 35/35 passing
  (`tests/features/bio-rpt-002/`) — see `backend-unit-test-output.txt`. Covers AC01 (whitelist
  validation, including an out-of-range enum handled gracefully), AC02 (real CSV, honest 501 for
  Excel/PDF), AC03 (tenant isolation at both the generation-engine and job-repository layers),
  AC04 (full job lifecycle, deterministic mid-flight cancellation, a terminal job refuses a second
  cancel, a generation failure ends Failed not stuck Running), AC05 (audit on queue/complete/
  cancel/export, Denied on a refused export), and a structural HTTP-pipeline regression test
  proving no collision with BIO-RPT-001's own routes.
- **BIO-CRD-007 regression** (my own prior story, one additive method added to its repository):
  52/52 passing, including the one new test for `ListForTenant` — see
  `bio-crd-007-regression-output.txt`.
- **API host build**: clean, 0 errors/warnings, `BioRpt002Module` registered and DI-validated at
  startup — see `api-host-build-output.txt`.
- **`tsc --noEmit` + `next build`**: PASS, zero errors — see `web-typecheck-output.txt`,
  `web-build-output.txt`.
- **Frontend unit tests** (vitest): 405/405 passing across 55 files, including 7 new
  `bio-rpt-002-reporting-mock.test.ts` tests (missing-input guards, whitelist enforcement, the
  offline gate, job-not-found/cancel-refused-on-terminal boundaries, real CSV export text, a
  shared job store between generate and status calls) and the updated
  `bio-ux-005-feature-mode.test.ts` multi-port guard — see `frontend-unit-test-output.txt`.
- **Frontend e2e tests** (Playwright + axe-core), SIMULATION mode, single-worker: 409/409 passing
  (including 5 new `bio-rpt-002-report-builder.spec.ts` tests, 3 skipped-by-design
  `bio-rpt-002-reporting-live.spec.ts` LIVE-only tests) — no regression across the pre-existing
  suite; the visual-regression baseline for `/reports` was intentionally regenerated (this story
  genuinely added visible content) — see `frontend-e2e-test-output.txt`. (A parallel-worker run
  showed one pre-existing, unrelated `/cards` theme-toggle flake under load — confirmed passing in
  isolation and single-worker both times; not caused by this story, which never touches `/cards`
  or theming.)
- **AC06 LIVE-mode dual-server proof, actually run, not just asserted**: real `.NET` backend +
  LIVE-built Next.js server, a tenant domain registered and verified through BIO-TEN-002's own
  control plane (the first LIVE connection in this repository to need that step — documented in
  `docs/features/bio-rpt-002/README.md` "Proving AC06"). 3/3 Playwright cases passed
  (`frontend-live-e2e-test-output.txt`), plus direct `curl` verification of the full
  create→poll→completed→CSV-export→Excel-501→cancel-refused-on-terminal-job cycle against the real
  backend.
