# BIO-RPT-002 — Implement visual report builder foundation

**Story:** As authorised users, I need a governed report builder using approved dimensions/
measures rather than arbitrary tenant SQL.

**Wave:** W11 / W11.1 · **Owner:** Ngozi · **Dependencies:** BIO-ATT-001, BIO-CRD-007 ·
**Conflict group:** `report-builder`

## Acceptance criteria and where each is met

- **AC01 — dimensions/measures are whitelisted.** `api/features/bio-rpt-002/Domain/ReportSchema.cs`'s
  `ReportSchemaRegistry` is a static, code-defined table (not configuration, not caller-suppliable)
  mapping each `ReportDataset` to its own allowed `ReportDimension`/`ReportMeasure` set.
  `ReportDefinitionValidator.Validate` rejects anything outside it — structurally (an out-of-range
  enum value) and semantically (a real dimension the *other* dataset allows but this one does not,
  e.g. asking for `Mode` on `CardProduction`) — before any gateway is ever called. A caller-supplied
  request only ever reaches a `ReportDefinition` through `Api.ReportEndpoints.ParseDefinition`'s own
  `TryParse` gate on a fixed wire-string set; there is no code path that accepts an arbitrary
  field/table name anywhere in this project. See `ReportSchemaTests.cs`.
- **AC02 — CSV/Excel/PDF export hooks exist.** `Domain/IReportExporter.cs`: all three formats are
  real, named `ReportExportFormat` cases and each has a registered `IReportExporter`. CSV
  (`CsvReportExporter`) actually renders RFC 4180 bytes (hand-written — no library reference exists
  anywhere in this repository's `.csproj`/NuGet configuration, and none was addable offline). Excel/
  PDF (`UnimplementedReportExporter`) are reachable, real hooks that throw a dedicated, documented
  `report_export_format_not_implemented` (501) rather than being silently absent — the disclosed
  HYBRID gap AC06 explicitly allows. See `CsvReportExporterTests.cs`.
- **AC03 — tenant isolation is enforced.** `ReportDefinition` carries no `TenantId` field at all —
  the tenant a report runs against always comes from `Tenancy.ICurrentTenantProvider`
  (`ReportService.RequireTenant`), never request content, so a caller cannot even *ask* to see
  another tenant's data. `IReportJobRepository.Get` returns `null` — never a confirmed-to-exist 403
  — for a job that belongs to a different tenant, the same "don't confirm existence" posture as
  every 404 elsewhere in this codebase. `ReportGenerationService`'s two gateways are called with
  exactly the caller's own `tenantId` and nothing else. See `ReportGenerationServiceTests.cs`
  (`AC03_a_tenant_never_sees_another_tenants_records...`) and `ReportServiceTests.cs`
  (`AC03_a_job_created_under_one_tenant_cannot_be_read_under_a_different_tenant_context`).
- **AC04 — large reports are asynchronous/cancellable.** Every report — large or small — runs
  through the same `Queued → Running → Completed/Failed/Cancelled` state machine
  (`Domain/ReportJobRunner.cs`, `Task.Run`), never a synchronous inline result, so "asynchronous" is
  a property of every report rather than a size-dependent branch. Cancellation is cooperative:
  `ReportGenerationService` checks `CancellationToken` before the gateway read and once per
  25-record batch while grouping. `ReportJobRunnerTests.cs`'s own cancellation test is deterministic,
  not timing-based — `Fakes.cs`'s `GatedCardProductionGateway` blocks on a `TaskCompletionSource`
  awaited via `.WaitAsync(cancellationToken)`, so cancelling the token throws
  `OperationCanceledException` the instant it is requested, with zero real-time sleeps to race
  against and zero flakiness.
- **AC05 — audit records report generation/export.** `Audit/IReportAuditSink.cs` (own sink, own
  `ReportAuditEvent` envelope, the same siloed-per-feature shape every sibling story in this
  codebase establishes). `ReportJobRunner` audits job creation (`report_job_queued`) and every
  terminal outcome (`report_job_completed`/`report_job_cancelled`/error code); `ReportService`
  audits every export attempt, `Allowed` for a real CSV export and `Denied` for a refused Excel/PDF
  one. See `ReportJobRunnerTests.cs` and `ReportServiceTests.cs`.
- **AC06 — the approved web surface is connected.** See "AC06: connecting the existing UI" below.

## Why exactly these two datasets

The story's own declared dependencies are BIO-ATT-001 and BIO-CRD-007 — not "every domain the
frontend's pre-existing `lib/report-rows.ts` happens to name" (that file's four report types
include `enrolment-summary`/`device-health`, which depend on People/Devices, neither a declared
dependency here). `ReportDataset` therefore has exactly two members: `CardProduction` and
`Attendance`. A later story extends the enum plus `ReportSchemaRegistry` when it adds a real
dependency on another domain — never by accepting a caller-supplied table/column name, which is the
exact "arbitrary tenant SQL" this story's user story exists to rule out.

## Why these specific dimensions, not the full domain model

Each gateway (`Domain/ICardProductionGateway.cs`, `Domain/IAttendanceGateway.cs`) projects its
dependency's real entity onto a small, whitelist-shaped record — `CardProductionRecord(Status,
Source, CardClass)` from BIO-CRD-007's `ProductionJob`; `AttendanceRecord(Mode, Source,
VerificationMethod)` from BIO-ATT-001's raw `AttendanceEvent` fields — rather than exposing the full
entity. Attendance's *derived* present/late/absent status is deliberately excluded: computing it
requires also reading the tenant's `AttendancePolicy` via `AttendanceStatusEvaluator`, a second real
dependency this foundation story's own declared scope does not ask for. Raw event fields (mode,
source, verification method) are genuine, governed dimensions on their own.

## The one additive change to BIO-CRD-007 (my own prior story)

`IProductionJobRepository` had no tenant-wide read — every existing method is keyed to one specific
`(personId, cardClass, sequenceNumber)`. This story adds exactly one new method,
`ListForTenant(tenantId)`, to that interface and its in-memory implementation — purely additive,
every prior method/behaviour unchanged (verified by rerunning BIO-CRD-007's own full test suite,
52/52 green, plus one new test covering the new method:
`BIO_RPT_002_ListForTenant_returns_every_job_for_that_tenant...`). BIO-ATT-001's own
`IAttendanceEventRepository.ListForTenantAsync` already existed and needed no change at all.

Both gateway adapters (`api/Biolog.Api/Modules/BioRpt002Module.cs`) wrap these repositories'
**public** interfaces directly — unlike most cross-feature gateways in this epic, no
`InternalsVisibleTo` grant from either dependency's own csproj was needed, since neither repository
interface is internal.

## AC06: connecting the existing UI

**A genuine cross-story route conflict, found and resolved before merge, not silently avoided.**
BIO-UX-011 already built real (non-placeholder) `/reports` and `/audit` pages, each with a generic
"Run report"/"View audit trail" button calling `ReportingService`'s `primaryAction`/`viewAuditTrail`
ports — the exact two paths `web/features/reporting/live.ts` (BIO-UX-005) already targeted. This
story's first implementation pointed its own real report engine at those same two paths, matching
what the frontend had already guessed at. Manually running the real packaged host end to end (not
just unit tests) surfaced an `AmbiguousMatchException`: BIO-RPT-001 (Notifications, already accepted
and merged) had *already* mapped those exact two paths as its own real, tested AC05 implementation
(`BioRpt001Module.cs` — publishes a real domain event through its notification pipeline, summarises
real delivery-audit outcomes). Both stories independently built real, working, accepted-or-in-
progress handlers at the identical paths — a module-port design gap (`ReportingService` only ever
named two generic action keys, never anticipating two independent backend stories both wanting to be
the real implementation behind them), not a mistake by either story. This was escalated rather than
resolved unilaterally; **god's ruling (2026-09-24): give BIO-RPT-002 its own dedicated routes/ports;
leave BIO-RPT-001's `primaryAction`/`viewAuditTrail` entirely untouched** — neither story loses
already-accepted work.

**What "connected" means here, concretely:**

- `/reports`' original "Run report" card (BIO-UX-011) is **completely unchanged** — still calls
  `ReportingService.primaryAction`, still genuinely live once `NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE=LIVE`
  is set, but that connects to **BIO-RPT-001's own** notification-demo backend, not this story's
  report engine. This is BIO-RPT-001's domain, disclosed here rather than presented as a BIO-RPT-002
  gap.
- `/audit`'s "View audit trail" button is likewise untouched and stays on `viewAuditTrail`
  (BIO-RPT-001's own real, live implementation once LIVE mode is set) — `/audit`'s approved content
  is an access-control decision log (BIO-IDN's own domain, rendered from BIO-UX-004 synthetic data),
  not this story's own report-generation/export audit trail (AC05, real and tested at the API level,
  independent of this route).
- **This story's own connection is a new `ReportBuilderCard`** (`web/features/reporting/ReportBuilderCard.tsx`),
  mounted on `/reports` alongside (not replacing) BIO-UX-011's existing card — no redesign of the
  approved route/navigation, an addition within it. Four new, dedicated `ReportingService` ports
  (`generateReport`/`getReportStatus`/`cancelReport`/`exportReport` — `web/features/reporting/ports.ts`,
  `mock.ts`, `live.ts`), each independently mode-switchable like every other multi-port module in
  this epic (`CredentialService`'s own nine-port precedent). The live adapters hit this story's own
  dedicated routes (`POST /api/v1/reporting/reports`, `GET .../reports/{jobId}`,
  `POST .../reports/{jobId}/cancel`, `GET .../reports/{jobId}/export`) — none of which collide with
  BIO-RPT-001's pair. `web/next.config.mjs` gained one rewrite entry (`/api/v1/reporting/:path*`),
  the same pattern BIO-DOC-009/BIO-ATT-001/BIO-VIS-001 already established (this also, as a side
  effect, makes BIO-RPT-001's own `primaryAction`/`viewAuditTrail` reachable through the Next.js dev/
  prod server for the first time — that rewrite genuinely did not exist before this story, though
  BIO-RPT-001's own backend endpoints did).
- A real, working create→poll→(cancel|export) human workflow: pick a whitelisted dataset/dimension,
  generate, watch the job reach a terminal status, view the result table, export CSV. Verified for
  real, not just asserted — see "Proving AC06" below.

`createLiveAdapter` (the shared zero-argument, fixed-path helper every other module's generic button
uses) could not have served this connection even without the route conflict: it ignores any `input`
and always POSTs to one fixed path, and this workflow genuinely needs a JSON body on create and a
job id interpolated into the URL for status/cancel/export. `live.ts`'s four new ports are therefore
hand-written `ModulePort` objects, not `createLiveAdapter` stubs — the first ports in this codebase
to actually need that, now that a real backend exists to receive real input.

## Proving AC06 (manual verification, actively excluded from the default full-suite run)

Every other BIO-RPT-002 Playwright/xunit test runs with no live cross-process dependency.
`bio-rpt-002-reporting-live.spec.ts` is the one spec that needs BOTH servers running together — the
same pattern `bio-doc-009-authenticity-live.spec.ts` already established for this repository. It
carries its own `test.skip(!process.env.BIOLOG_RPT002_LIVE_TESTS, ...)` guard so the default
`pnpm --dir tests/frontend test:e2e` run (whose server never sets the LIVE env var) skips it cleanly
instead of failing against mock data.

**One extra setup step this story needs that no prior LIVE spec in this repository has**: BIO-RPT-002
is the first LIVE-mode connection to call an endpoint that requires a genuinely resolved,
non-null tenant (`ReportService.RequireTenant`). Tenant resolution (`TenantContextMiddleware`) reads
the real HTTP `Host` header through BIO-TEN-002's own control plane — nothing is pre-registered, so a
fresh backend instance resolves every request's tenant to `null` and every report-creation call
fails closed with `tenant_context_missing` until a domain is registered AND verified for the host the
test traffic actually presents. Confirmed empirically (not assumed): registering **and separately
verifying** `127.0.0.1` was required — `ResolveAsync` only accepts `TenantDomainStatus.Verified`, not
the `PendingVerification` status `RegisterAsync` alone leaves a domain in.

```bash
# Terminal 1 — the real .NET backend
dotnet run --project api/Biolog.Api/Biolog.Api.csproj --urls http://127.0.0.1:5080

# One-time setup — register AND verify a tenant domain for the host requests will present
# (confirmed empirically: ASP.NET Core's HostString.Host strips the port, so "127.0.0.1" alone
# matches whether the request arrives directly or proxied through Next.js's own rewrite)
TENANT_ID=ten_018f2e5a-1234-7abc-89ab-0123456789ab
curl -s -X POST http://127.0.0.1:5080/api/v1/control-plane/tenant-domains \
  -H "Content-Type: application/json" -H "X-Actor-Id: setup" \
  -d "{\"hostname\":\"127.0.0.1\",\"tenant_id\":\"$TENANT_ID\"}"
curl -s -X PUT http://127.0.0.1:5080/api/v1/control-plane/tenant-domains/127.0.0.1/status \
  -H "Content-Type: application/json" -H "X-Actor-Id: setup" \
  -d '{"version":1,"status":"VERIFIED"}'

# Terminal 2 — a production Next.js build with ReportingService's LIVE mode baked in
# (NEXT_PUBLIC_* vars are inlined at BUILD time, not read at runtime)
cd web
rm -rf .next
NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE=LIVE BIOLOG_API_BASE_URL=http://127.0.0.1:5080 pnpm build
BIOLOG_API_BASE_URL=http://127.0.0.1:5080 pnpm start -p 3015

# Terminal 3 — the spec itself, with BIOLOG_RPT002_LIVE_TESTS=1 to lift the test.skip guard
cd tests/frontend
BIOLOG_BASE_URL=http://127.0.0.1:3015 BIOLOG_RPT002_LIVE_TESTS=1 ./node_modules/.bin/playwright test bio-rpt-002-reporting-live.spec.ts
```

Actually run end to end while building this story (not just written and assumed to work): all three
Playwright cases passed against the real backend, plus direct `curl` verification of create → poll →
completed → CSV export → Excel 501 → cancel-refused-on-a-terminal-job, all producing real, correct
responses (an empty `card_production` dataset genuinely returns zero rows, not an error, since no
production jobs exist for a freshly registered tenant in this demo environment).

## Tests

- `tests/features/bio-rpt-002/ReportSchemaTests.cs` — AC01 (every whitelisted combination
  validates, an out-of-dataset dimension is rejected, an unknown enum value is rejected without
  crashing, no arbitrary string is ever accepted).
- `ReportGenerationServiceTests.cs` — AC01/AC03 (grouping/counting correctness for both datasets,
  tenant isolation, an ungoverned definition is rejected before any gateway call, an empty dataset
  is a real zero-row result, not an error).
- `ReportJobRunnerTests.cs` — AC04 (Queued→Completed lifecycle with a real result and audit trail,
  deterministic mid-flight cancellation via `GatedCardProductionGateway`, a terminal job cannot be
  cancelled again, a generation failure ends Failed with an error code rather than hanging Running
  forever).
- `CsvReportExporterTests.cs` — AC02 (real RFC 4180 CSV incl. quoting of commas/quotes/newlines,
  Excel/PDF answer honestly with 501, not silently).
- `ReportServiceTests.cs` — AC01 (schema exposure), AC03 (cross-tenant job lookup is 404), AC02/AC05
  (CSV export succeeds and is audited Allowed; Excel export is refused and audited Denied; exporting
  a still-Queued/Running job is refused, never a partial result).
- `ReportEndpointsIntegrationTests.cs` — real HTTP pipeline (`TestServer`) coverage of every
  endpoint: schema, create→poll→complete, an ungoverned request is refused with 422 and never
  creates a job, a missing actor header is 400, CSV export returns real bytes, Excel export returns
  501, cancel never 500s, and a structural regression guard proving this project's own endpoint
  group never maps `/primary-action`/`/view-audit-trail` (BIO-RPT-001's own routes).
- `tests/features/bio-crd-007/ProductionJobRepositoryTests.cs`'s own
  `BIO_RPT_002_ListForTenant_...` test — the one additive method this story added to BIO-CRD-007's
  own repository.
- `tests/frontend/unit/bio-ux-005-feature-mode.test.ts`'s own multi-port guard test, updated to
  include `ReportingService`'s four new keys, per that test's own stated purpose.
- `tests/frontend/playwright/bio-rpt-002-reporting-live.spec.ts` — AC06's dual-server, real-backend
  proof of the `ReportBuilderCard`'s own create→poll→export workflow (see "Proving AC06" above);
  excluded from the default suite by its own `test.skip` guard.
