# BIO-RPT-001 — Builder evidence

**Story:** Implement notification event/rule foundation
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-RPT-001`, branch `feature/BIO-RPT-001` off `develop`
**Dependencies:** BIO-ARC-003 (domain-event envelope contract, accepted — `contracts/schemas/event-envelope.schema.json`)
**Commit:** `99964c6`

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — rules subscribe to versioned domain events | `IDomainEventBus`/`InMemoryDomainEventBus`; `NotificationRule.MinSchemaVersion`/`MaxSchemaVersion` actually enforced by `INotificationRuleRepository.ListMatchingAsync`; `NotificationDispatchService` subscribed to the bus at host startup | `RuleMatchingAndEventBusTests.cs` (11/11), `NotificationEndpointsTests.cs` (real host) |
| AC02 — channel adapters are separated | `INotificationChannelAdapter` + 5 adapter classes (Email/Sms/Push/Teams/Webhook); dispatcher never branches on channel type, only dictionary-dispatches | `ChannelAdapterMinimizationTests.cs`, structural harness |
| AC03 — retry/deduplication and delivery audit exist | `INotificationDeliveryAuditSink` records every attempt; bounded retry (3 attempts, transient only); dedup key `(tenant_id, event_id, rule_id, channel)` extending ADR-0003 Decision 4's own base pair | `RetryDedupAuditTests.cs` (6/6) |
| AC04 — sensitive information is minimized per channel | `PayloadAllowlist.Project` (allowlist, per ADR-0003 Decision 3); 5 distinct per-channel allowlists; Personal/Sensitive events get no payload fields on any channel | `ChannelAdapterMinimizationTests.cs` |
| AC05 — `/reports`/`/audit`/`/dashboard` connected via `ReportingService` or documented HYBRID | Real, live `POST /api/v1/reporting/primary-action` and `/view-audit-trail` at the exact paths `web/features/reporting/live.ts` already targets; zero changes to BIO-UX-005/011's owned frontend files | `NotificationEndpointsTests.cs`, `docs/features/bio-rpt-001/README.md`'s "AC05" section |

## Developer tests

- `tests/features/bio-rpt-001/RuleMatchingAndEventBusTests.cs` — 11 tests (AC01: schema-version-range matching, tenant/aggregate-type isolation, disabled-rule exclusion, bus pub/sub semantics including subscriber-failure isolation and unsubscribe, end-to-end bus→dispatch integration).
- `tests/features/bio-rpt-001/ChannelAdapterMinimizationTests.cs` — 10 tests (AC02: each adapter's own channel identity; AC04: per-classification and per-channel minimization, including a hostile "unallowlisted field must never leak" case; deterministic simulated delivery outcomes for every adapter).
- `tests/features/bio-rpt-001/RetryDedupAuditTests.cs` — 6 tests (AC03: permanent-failure-never-retried, transient-failure-retried-to-bound with every attempt individually audited, successful-delivery-recorded-once, dedup on repeat dispatch, dedup scoped per rule+channel not just per event, dedup scoped per tenant).
- `tests/dotnet/Biolog.IntegrationTests/NotificationEndpointsTests.cs` — 6 tests against the real composed host: real rule registration → real primary-action dispatch → real delivery, real audit-trail summary reflecting real records (not synthetic UI data), tenant scoping, tenant fail-closed, optimistic-concurrency conflict on rule status change, a disabled rule producing zero deliveries.

Reproduce with:
```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj
dotnet test tests/features/bio-rpt-001/Biolog.Features.Notifications.Tests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj
bash tests/features/bio-rpt-001/BIO-RPT-001.test.sh
bash scripts/check-module-project-references.sh
python3 scripts/check-traceability.py --all
bash scripts/secret-scan.sh --all
```
(exact commands in `metadata.json`'s `test_command`.)

## Results

- `dotnet build api/Biolog.Api/Biolog.Api.csproj`: PASS, 0 warnings, 0 errors — full host (all 39
  feature projects) builds clean with the new project wired in.
- `Biolog.Features.Notifications.Tests`: 31/31 PASS.
- `Biolog.IntegrationTests` (full suite, not just this story's new file): 146/146 PASS — 0
  pre-existing failures reintroduced.
- `Biolog.UnitTests` (full suite): 200/200 PASS.
- `BIO-RPT-001.test.sh`: 23/23 PASS.
- `check-module-project-references.sh`: PASS.
- `check-traceability.py --all`: PASS (31 implemented stories) — no `tests/foundation/BIO-RPT-001.test.sh`
  or `governance/stories/BIO-RPT-001.json` needed, matching the established precedent for every
  other `api/features/`-convention story with no mobile component.
- `check-dotnet-vulnerabilities.sh` (both new projects): PASS, no vulnerable packages.
- `secret-scan.sh --all`: clean.

## No domain-event publisher exists yet — confirmed by search, not assumed

Searched the codebase for any existing code constructing an object matching
`contracts/schemas/event-envelope.schema.json` (BIO-ARC-003's domain-event envelope) at
runtime: none exists. `Biolog.Api.Audit.AuditEvent` is a different, already-pervasive envelope
(`audit-event.schema.json`). This story therefore builds both the subscription mechanism
(`IDomainEventBus`) and, for end-to-end demonstration, its own first synthetic producer (the
AC05 endpoints) — a future real publisher depends on the same bus with no change here.

## AC01 proven, not just present: the schema-version range is actually enforced

A rule's `MinSchemaVersion`/`MaxSchemaVersion` is checked by
`INotificationRuleRepository.ListMatchingAsync` before a rule is ever handed to the
dispatcher — `RuleMatchingAndEventBusTests.publishing_an_unsupported_schema_version_dispatches_to_zero_rules_not_processed_blindly`
publishes a real envelope with an unsupported `SchemaVersion` and confirms zero rules matched
and zero deliveries occurred, not merely that the field exists on the record.

## AC03: dedup key deliberately extends ADR-0003 Decision 4, doesn't reinvent it

ADR-0003 Decision 4 already defines a domain event's own idempotency key as
`(tenant_id, event_id)`. This story's delivery layer needs two more dimensions (one event fans
out to several independent rule+channel deliveries), so `INotificationDeliveryAuditSink`'s own
doc comment records this extension explicitly rather than silently diverging from the ADR's own
base key. `RetryDedupAuditTests.deduplication_is_scoped_per_rule_and_channel_not_just_per_event`
proves the extension is load-bearing (two rules for the same event, different channels, both
still deliver — a naive event-only dedup key would have wrongly suppressed the second).

## AC04: allowlist enforced per ADR-0003 Decision 3, verified against a hostile input

`PayloadAllowlist.Project` copies only explicitly-named keys — never the raw payload object.
`AC04_webhook_never_leaks_a_key_outside_its_own_declared_allowlist_plus_metadata` injects a
payload field named to look like it might be sensitive/interesting
(`not_allowlisted_secret_looking_field`) and confirms it never appears in the rendered
notification's fields OR its summary text — the adversarial case ADR-0003 Decision 3 itself
names ("what would a caller have to control for this to be false?").

## AC05: a real live connection, built for exactly this seam, with an honest stated limit

`web/features/reporting/live.ts` (BIO-UX-005) already targets
`/api/v1/reporting/primary-action` and `/view-audit-trail` — the `/reports` page's own doc
comment names this exact gap ("No `BIO-RPT-*` backend story is built yet... not a gap"). Built
both endpoints for real, backed by the actual AC01-04 mechanism (not a hardcoded response),
with ZERO changes to `web/features/reporting/**` or `web/app/{reports,audit}/**` — confirmed by
the structural harness's own `git diff` check against `develop`. Honestly documented limit
(not hidden): the reports/audit PAGES themselves still render client-side synthetic data
regardless of LIVE/SIMULATION mode (`createLiveAdapter`'s contract only returns a `{message}`
string, never row data) — the same limit every other `ModulePort` integration in this codebase
has today, not a new gap this story introduces.

## Scope boundaries, documented rather than silently assumed

1. **No changes to `web/features/reporting/**`/`web/app/{reports,audit}/**`** (BIO-UX-005/011's
   own owned files) — the live backend was built to the ALREADY-EXISTING contract those files
   define, not the other way around.
2. **No new domain-event publishers wired into other stories.** `DeviceService.ChangeStatusAsync`
   and similar mutation methods elsewhere in this codebase do not yet publish through
   `IDomainEventBus` — adding that is each owning story's own future work, not retrofitted here.
3. **Capture/verify/print/encode operations themselves remain out of scope** — this story
   reports and routes notifications about domain events, never performs the operations those
   events describe (that is the `BIO-BIO-*`/`BIO-CRD-*`/`BIO-DEV-*` epics' own territory).
