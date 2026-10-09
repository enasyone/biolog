# BIO-RPT-003 — Implement immutable audit investigation views

## Restated scope

**User story.** As auditors, I need searchable investigation views for sensitive actions without
allowing audit mutation.

**Acceptance criteria:**
- **AC01** — Filters include actor/person/action/time/device/reason.
- **AC02** — Audit records are read-only to tenant users.
- **AC03** — Export is permissioned and audited.
- **AC04** — Sensitive payload fields respect redaction policy.
- **AC05** — The approved web surface (`/reports`, `/audit`, `/dashboard`) is connected through
  `ReportingService` to this story's live implementation or an explicitly documented HYBRID
  state, and the mapped end-to-end human workflow passes without an unapproved UI redesign.

**Dependencies (build-wave gating).** BIO-ARC-003 (ADR-0003 — the audit/event-envelope contract;
Accepted), BIO-IDN-006 (RBAC — the one genuine cross-story runtime dependency, for the
export/view-sensitive-detail permission gates).

**Conflict group.** `audit-ui`.

**Owned write paths.** `api/features/bio-rpt-003/**` (new feature project),
`api/Biolog.Api/Modules/BioRpt003Module.cs`, `tests/features/bio-rpt-003/**`,
`web/features/bio-rpt-003/**` (new), `web/app/audit/page.tsx` (additive mount only),
`tests/frontend/playwright/bio-rpt-003-*.spec.ts`, `tests/frontend/unit/bio-rpt-003-*.test.ts`,
`docs/features/bio-rpt-003/**`, `evidence/BIO-RPT-003/**`.

**Shared, unlisted entry points touched.** `api/Biolog.Api/Program.cs`,
`api/Biolog.Api/Biolog.Api.csproj`, `web/lib/module-registry.ts`, `web/lib/feature-mode.ts`,
`web/next.config.mjs`, `tests/frontend/unit/bio-ux-005-feature-mode.test.ts` (its own multi-port
guard test updated), `scripts/quality.sh`.

**Zero edits to any other story's own owned files.** Unlike BIO-CRD-008 (which needed two
additive fields on BIO-CRD-007's `ProductionJob`), this story reads every one of its four
sibling-feature audit sources through their own **already-public, already-registered** in-memory
sinks, composed entirely inside `BioRpt003Module.cs` — no other feature's file was touched at all.

**Security/privacy risks identified and addressed.**
- Tenant isolation: every source is filtered by the ambient tenant (`ICurrentTenantProvider`,
  resolved from the same `ITenantContextAccessor` every other feature uses); a cross-tenant record
  never appears in a search result. Verified by test.
- Two independent redaction gates, not one: the producing feature's own capture-time
  classification (already-shipped precedent, `*AuditEventBuilder` across the codebase) decides
  what is safe to *carry*; this story's own `AuditViewRedaction` decides, at read time, what is
  safe to *show the specific viewer asking* — a genuinely different question (need-to-know vs.
  safe-to-capture). See "Design decisions" below.
- Export is real, permissioned (`audit.export`) and itself audited before its bytes are ever
  returned to the caller (ADR-0003 Decision 7's ordering rule, generalised: "that ordering applies
  to every audited effect, not only denials").
- AC02 is a structural guarantee, not a policy one: there is no mutation method on
  `AuditInvestigationService` and no `MapPut`/`MapPatch`/`MapDelete` anywhere under
  `/api/v1/audit-investigations` — verified by a real-HTTP integration test and the offline
  structural harness.

**Tests planned and written.** See "Test evidence" below.

## Design decisions

### The redaction engine is NOT re-implemented; a second, independent gate is added instead

ADR-0003 Decision 3 defines a full recursive allowlist-projection engine (`contracts/js/audit.mjs`
`redact()`). No feature in this codebase has ported that engine in full — every feature instead
runs a smaller, deliberately "right-sized local port": a flat `Dictionary<string, (object? Value,
string Classification)>` detail bag, an unregistered classification fails closed, `Classification`
is the max of what's kept (`RbacAuditEventBuilder`, `ProductionAuditEventBuilder`,
`ReportAuditEventBuilder`, and my own `CardTemplateAuditEvent`'s producer, all independently
converged on this exact shape). Re-implementing the full recursive engine here, for records that
are ALREADY flat dictionaries by the time they reach this story, would be solving a problem this
codebase's own convention doesn't have.

What BIO-RPT-003 actually adds, and what no other feature needed before it: **a second, view-time
gate.** The producing feature's classification answers "is this safe to *capture* in the durable
record at all." This story's `AuditViewRedaction.Apply` answers a different question: "is this
safe to *show the person asking, right now*." A record classified `personal`/`sensitive` has its
`Detail` withheld from any viewer who doesn't hold `audit.view_sensitive_detail`
(`AuditPermissionKeys`, checked via `IAuditExportPermissionGateway` wrapping BIO-IDN-006's real
`PermissionService.HasPermissionAsync`); `Classification` itself always travels unmodified, even
when hidden, so a denied viewer still learns *that* the record is sensitive, never its contents
(ADR-0003: "classification is the maximum ... so a consumer can make a ... decision from the
envelope alone"). `secret`-classified detail (and an unregistered classification string, which
ADR-0003 says must be treated as the most restrictive) is hidden **unconditionally**, regardless of
permission — defence in depth, since no producer in this codebase ever registers `secret`.

### Which sources are wired, and why

`Domain/IAuditEventSource.cs` is a genuinely open extension point (`string SourceFeature { get; }`
+ `ListForTenant(tenantId)`); any future module can register another implementation into the same
DI collection without touching this story's files at all. Four adapters are wired today, in
`BioRpt003Module.cs`:

| Source | Adapter | Person id from | Device |
|---|---|---|---|
| `rbac` (BIO-IDN-006, declared dependency) | `RbacAuditEventSource` | `Detail["person_id"]` when the source event carries one (role-assign/-revoke do; permission-grant does not) | always `null` |
| `card_templates` (BIO-CRD-008, my own prior story) | `CardTemplateAuditEventSource` | `null` (no person concept in this feature) | always `null` |
| `report_builder` (BIO-RPT-002, my own prior story) | `ReportAuditEventSource` | `null` | always `null` |
| `card_production` (BIO-CRD-007, my own prior story) | `ProductionAuditEventSource` | parsed from the production-job target's own composite key (`"{personId}:{cardClass}:{sequenceNumber}[:{jobSequenceNumber}]"`) | always `null` |

Only Rbac is a **declared** story dependency (build-wave gating: BIO-IDN-006 must exist before this
story starts). The other three are already-accepted, already-built, earlier-wave features whose
own audit sinks were **already exposed for exactly this purpose** — two of their own doc comments
say so verbatim: `InMemoryProductionAuditSink.Events` ("exposed for tests and a future audit-read
endpoint") and `InMemoryReportAuditSink.EventsForTenant` ("exposed for tests and
`Api.ReportEndpoints`'s own audit-read endpoint"). Consuming an already-public, already-anticipated
read helper — composed only inside `Biolog.Api` (a shared entry point every story in this epic
already touches for its own module registration) — is not a new declared dependency in the
approval-tracking sense; it is zero new coupling to those features' own owned files. This is a
disclosed design choice, not silent scope creep: `BioRpt003Module.DependsOn` truthfully lists all
four (`Tenant`, `Rbac`, `CardTemplates`, `ReportBuilder`, `CardProduction`) as real DI-graph edges
that must be registered first, distinct from the story's own approved `Dependencies` field, which
governs wave-ordering and is fully satisfied by BIO-ARC-003/BIO-IDN-006 alone.

**Not wired**: the ~20 other features across the codebase that also carry an audit sink of their
own shape (BIO-CRD-005/006, BIO-IDN-001-005, BIO-ATT-001, BIO-PHO-001/002, BIO-TEN-001/002,
BIO-TRN-001/002, BIO-VIS-001, BIO-DOC-008/009, BIO-ACC-001, etc.). Wiring all of them in one story
would mean writing ~20 more adapters against types this story's author has not verified in detail,
a real correctness risk for marginal AC-coverage benefit (`IAuditEventSource`'s own contract is
demonstrated with four heterogeneous real sources already). Adding a fifth adapter is a
same-shaped, few-line change to `BioRpt003Module.cs` alone whenever a later story needs it — a
disclosed, intentionally-scoped boundary, not a silent gap.

### AC01's device filter: implemented and tested, honestly not yet exercised by any real source

Every existing audit-event builder in this codebase (`RbacAuditEventBuilder`,
`ProductionAuditEventBuilder`, `ReportAuditEventBuilder`, and `CardTemplateAuditEvent`'s own
producer) sets `Device = null`, always — verified directly, not assumed. ADR-0003 is explicit that
this is a real, honest answer ("no device" vs. "nobody recorded the device" must stay
distinguishable), not a bug. The `device_id` filter is fully implemented
(`AuditInvestigationService.Matches`) and unit-tested against a synthetic
`IAuditEventSource` fixture that DOES populate a real `AuditDeviceRef` (`AC01_filters_by_device_id_
against_a_synthetic_fixture`), proving the matching logic itself is correct; it will start
returning real matches automatically, with zero code change here, the moment any wired (or future)
source begins populating a real device value. The frontend's own SIMULATION fixture
(`web/features/bio-rpt-003/mock.ts`) deliberately includes one record with a real device value so
the filter is demonstrable end-to-end through the actual UI even though no LIVE source can
currently supply one.

### A latent gap found in the shared `.biolog-table` pattern, fixed locally, disclosed rather than silently patched upstream

`AuditInvestigationPanel`'s results table has seven columns — wider than any other consumer of the
shared `.biolog-table` CSS class in this codebase today. `.biolog-table` deliberately sets
`display: block; overflow-x: auto` on itself (a prior fix, see that rule's own comment) so a wide
table scrolls within its own bounds rather than overflowing the page. At this table's actual
column count, that makes it a genuinely *scrollable region* at common viewport widths — axe-core
correctly flagged `scrollable-region-focusable` (WCAG 2.1.1/2.1.3): a scrollable region needs to be
reachable by keyboard. No earlier consumer of `.biolog-table` was ever wide enough to trip this at
default viewport, so the gap was real but latent. Fixed here with `tabIndex={0}` on this story's own
`<table>` JSX element only — a purely local fix, not a change to `globals.css` (a shared
design-system file this story does not own). Worth a note to whoever owns `.biolog-table`
(Damilola): the same latent gap exists for any FUTURE consumer wide enough to overflow.

## Issues found and fixed during development (not assumed correct, verified)

1. **A missing `using` statement resolved the wrong extension method** — the third confirmed
   instance of this exact bug class in this codebase (`ToWireString`/`TryParse` defined on several
   same-shaped extension classes across namespaces; BIO-RPT-002 and BIO-CRD-008 each hit it once
   already). `Api/AuditInvestigationDtos.cs` needed `using Biolog.Features.AuditInvestigation.
   Audit;` alongside `...Domain;` for `ActorType`/`AuditOutcome`'s own `ToWireString` to resolve
   correctly instead of silently binding to `AuditClassificationExtensions.ToWireString`. Caught by
   the compiler, not a test.
2. **The same class of bug, a second time, across FOUR different sibling namespaces at once** —
   `BioRpt003Module.cs`'s four adapters each convert another feature's own `ActorType`/
   `AuditOutcome` (a same-shaped but distinct CLR type per namespace) into this story's own; since
   only this story's own `Audit` namespace is `using`'d (to avoid the four siblings' own types
   colliding unqualified), each cross-namespace `ToWireString()` call needed a fully-qualified
   static invocation (e.g. `Biolog.Features.Rbac.Audit.ActorTypeExtensions.ToWireString(...)`)
   rather than instance-call syntax. Caught by the compiler across eight call sites at once.
3. **A test bug, not a product bug**: the CSV-export test first asserted `per_alice` (a person's
   canonical id) was absent from a redacted export. It is not supposed to be — ADR-0003 explicitly
   treats a canonical identifier as a safe, correlatable reference distinct from the raw value it
   names ("it carries the canonical identifier of the record instead ... already non-secret").
   Fixed the test to assert the actual sensitive VALUE (a synthetic full name) is withheld while
   the safe identifier remains visible.
4. **A test bug, not a product bug**: the AC02 structural regression test expected `404 Not Found`
   for `PUT`/`PATCH`/`DELETE` under `/api/v1/audit-investigations`. ASP.NET Core's own routing
   correctly returns `405 Method Not Allowed` instead (the route template matches, the method
   doesn't) — arguably the stronger proof of "read-only," not a weaker one. Fixed the assertion.
5. **A harness-script bug, not a product bug**: an offline structural check searched
   `AuditInvestigationEndpoints.cs` for the literal strings `MapPut`/`MapPatch`/`MapDelete` to
   prove none exist — and matched its own doc comment, which names those exact strings while
   explaining their absence. Fixed by requiring the opening parenthesis (`MapPut\(` etc.), which
   only appears in a real method call, never in `<c>MapPut</c>` XML-doc prose.
6. **A Playwright strict-mode locator collision, self-caught** — `getByRole('heading', { name:
   'Audit' })` (no `exact: true`) also matched this story's own new `<h2>Investigate audit
   events</h2>` heading via Playwright's default substring match. The same class of bug BIO-RPT-002
   hit in its own suite. Fixed with `exact: true`.
7. **A test-authoring bug in the LIVE spec, self-caught**: the third LIVE test registered
   `page.waitForResponse(...)` *after* the click that produced the response it was waiting for,
   so it waited the full 30s timeout for a response that had already happened. Fixed by moving the
   listener registration into the same `Promise.all` as the triggering click, matching every other
   response-waiting test in this suite.

8. **A stale `next-server` process, self-caught, during evidence capture** — a second LIVE-mode
   evidence run reused port 3214 from an earlier attempt; `curl` silently succeeded against the
   OLD process instead of the freshly-built one, and the LIVE badge/panel appeared entirely
   absent (a false negative that looked like a real rendering regression). `lsof -i :3214` +
   confirming the process was genuinely `next-server` before killing it, then restarting cleanly,
   resolved it — the same "stale reused dev server" lesson BIO-CRD-008 already documented,
   confirmed a second time as a real, recurring risk specifically during evidence capture, not
   just first-draft test runs.

None of these were review-round findings — all eight were caught and fixed during my own
implementation pass, before any independent review.

## Test evidence

- **Backend unit/integration tests** (`dotnet test`, xunit): 37/37 passing
  (`tests/features/bio-rpt-003/`) — see `backend-unit-test-output.txt`. Covers AC01 (every filter
  dimension, pagination, ordering, tenant isolation), AC02 (no mutation method exists; a real-HTTP
  405 for every mutating verb under the prefix), AC03 (export forbidden without permission, export
  audited before returning, the filter/redaction gate applies identically to export and search),
  AC04 (the view-time redaction gate: public/internal never hidden, personal/sensitive
  gated by permission, secret always hidden, an unregistered classification treated as secret,
  producer-supplied `Withheld` preserved alongside view-time withholding).
- **API host build**: clean, 0 errors/warnings, `BioRpt003Module` registered and DI-validated at
  startup (booted the real host immediately after wiring the module, not saved for a final pass —
  applying the DI-lifetime lesson from BIO-RPT-002/BIO-CRD-008 proactively; no bug found this time)
  — see `api-host-build-output.txt`.
- **`tsc --noEmit` + `next build`**: PASS, zero errors — see `web-typecheck-output.txt`,
  `web-build-output.txt`.
- **Frontend unit tests** (vitest): 426/426 passing across 56 files, including 13 new
  `bio-rpt-003-audit-investigation-mock.test.ts` tests and the updated `bio-ux-005-feature-mode.
  test.ts` multi-port guard — see `frontend-unit-test-output.txt`.
- **Frontend e2e tests** (Playwright + axe-core), SIMULATION mode, single-worker: 418/419 passing
  (6 new `bio-rpt-003-audit-investigation.spec.ts` tests, 10 skipped-by-design LIVE-only tests
  across the repo); the one other failure (`bio-ux-012-human-test-console.spec.ts`, Document
  Verify evidence-state timing) is a pre-existing, unrelated flake — confirmed by rerunning it in
  isolation, where it passes — see `frontend-e2e-test-output.txt`.
- **AC05 LIVE-mode dual-server proof, actually run, not just asserted**: real `.NET` backend +
  LIVE-built Next.js server, a tenant domain registered and verified through BIO-TEN-002's own
  control plane. A real `card_templates` audit event was produced first (via BIO-CRD-008's own
  `POST /api/v1/card-templates`, an already-accepted endpoint, with no prerequisite person entity
  needed), then genuinely searched for and returned through this story's own real
  `/api/v1/audit-investigations` endpoint — proving the four-source fan-in is real, not asserted.
  3/3 Playwright cases passed (`frontend-live-e2e-test-output.txt`), plus direct `curl`
  verification of the full register-tenant → create-template → search (unfiltered and
  action-filtered) → export-forbidden-403 cycle against the real backend.

### Proving AC05

```bash
# 1. Real backend
dotnet build api/Biolog.Api/Biolog.Api.csproj --nologo
dotnet run --project api/Biolog.Api/Biolog.Api.csproj --no-build --urls http://127.0.0.1:5091 &

# 2. Register + verify a tenant domain for the host the test runs against (BIO-TEN-002)
curl -X POST http://127.0.0.1:5091/api/v1/control-plane/tenant-domains \
  -H "Content-Type: application/json" -H "X-Actor-Id: web-ui" \
  -d '{"hostname":"127.0.0.1","tenant_id":"ten_00000000-0000-7000-8000-0000000000f1"}'
curl -X PUT http://127.0.0.1:5091/api/v1/control-plane/tenant-domains/127.0.0.1/status \
  -H "Content-Type: application/json" -H "X-Actor-Id: web-ui" \
  -d '{"version":1,"status":"VERIFIED"}'

# 3. Produce a real, searchable audit event (any already-accepted feature's own mutating endpoint
#    works; BIO-CRD-008's card-template draft needs no prerequisite entity)
curl -X POST http://127.0.0.1:5091/api/v1/card-templates \
  -H "Content-Type: application/json" -H "X-Actor-Id: designer-1" -H "Host: 127.0.0.1" \
  -d '{"name":"LIVE Proof Template","snapshot":{"format_kind":"cr80","orientation":"portrait","front_elements":[],"back_elements":[],"watermark":"none","barcode":"none"}}'

# 4. LIVE-built Next.js server (NEXT_PUBLIC_* baked in at build time, not runtime)
(cd web && rm -rf .next && \
  NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_SEARCHAUDITEVENTS=LIVE \
  NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_EXPORTAUDITEVENTS=LIVE \
  BIOLOG_API_BASE_URL=http://127.0.0.1:5091 pnpm exec next build)
(cd web && BIOLOG_API_BASE_URL=http://127.0.0.1:5091 pnpm exec next start -p 3212 -H 127.0.0.1 &)

# 5. The real dual-server LIVE Playwright proof
(cd tests/frontend && BIOLOG_RPT003_LIVE_TESTS=1 BIOLOG_BASE_URL=http://127.0.0.1:3212 \
  pnpm exec playwright test bio-rpt-003-audit-investigation-live.spec.ts --workers=1)
```
