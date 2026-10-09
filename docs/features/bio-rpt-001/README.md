# BIO-RPT-001 — Implement notification event/rule foundation

As tenants, we need configurable notification rules for email/SMS/push/Teams/webhook and
future channels.

**Feature project:** `api/features/bio-rpt-001/Biolog.Features.Notifications` (namespace
`Biolog.Features.Notifications`), the same separate-project, one-way-dependency convention
`BIO-BIO-001`/`BIO-MOB-003`/`BIO-DEV-003` already established. Registered into the host via
`api/Biolog.Api/Modules/BioRpt001Module.cs`.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — rules subscribe to versioned domain events | `IDomainEventBus`/`InMemoryDomainEventBus` (the subscription mechanism); `NotificationRule.MinSchemaVersion`/`MaxSchemaVersion` + `INotificationRuleRepository.ListMatchingAsync` (a rule declares which schema versions it understands, and is excluded for one it does not); `NotificationDispatchService` subscribed at host startup | `RuleMatchingAndEventBusTests.cs` (11 tests), `NotificationEndpointsTests.cs` (real host) |
| AC02 — channel adapters are separated | `INotificationChannelAdapter` + one adapter class per channel (`Adapters/*.cs`); `NotificationDispatchService` never branches on `NotificationChannel` itself, only dictionary-dispatches to the matching adapter | `ChannelAdapterMinimizationTests.cs`, structural harness |
| AC03 — retry/deduplication and delivery audit exist | `INotificationDeliveryAuditSink` records every attempt (not just the final outcome); bounded retry (`MaxAttempts = 3`) for transient failures, never for permanent ones; dedup key `(tenant_id, event_id, rule_id, channel)` | `RetryDedupAuditTests.cs` (6 tests) |
| AC04 — sensitive information is minimized per channel | `PayloadAllowlist.Project` (allowlist, never a denylist, per ADR-0003 Decision 3); each adapter declares its own, differently-sized allowlist; `Personal`/`Sensitive`-classified events carry no payload fields on any channel | `ChannelAdapterMinimizationTests.cs` |
| AC05 — `/reports`/`/audit`/`/dashboard` connected via `ReportingService` or documented HYBRID | Real, live `POST /api/v1/reporting/primary-action` and `POST /api/v1/reporting/view-audit-trail` — the exact two paths `web/features/reporting/live.ts` already targets | See "AC05: what 'live' means here" below, `NotificationEndpointsTests.cs` |

## Why a separate feature project

Mirrors `BIO-DEV-003`'s own reasoning (see `docs/features/bio-dev-003/README.md`): this
project cannot reference `Biolog.Api.csproj` (one-way dependency,
`scripts/check-module-project-references.sh`), so it never resolves a tenant itself - every
method that needs one takes `string tenantId` as a plain parameter, and
`BioRpt001Module` resolves the real `ITenantContextAccessor` and passes it down. This is a
narrower seam than `BIO-DEV-003`'s `ICertificationGate` or `BIO-MOB-003`'s
`IDeviceTenantResolutionGateway` needed (those crossed the boundary with a whole object;
this only ever needs an already-resolved string).

## No domain-event publisher exists yet - confirmed, not assumed

Before designing this story, the codebase was searched for any code already constructing an
object matching `contracts/schemas/event-envelope.schema.json` (the BIO-ARC-003 domain-event
envelope) at runtime - none exists. `Biolog.Api.Audit.AuditEvent` is a DIFFERENT envelope
(`contracts/schemas/audit-event.schema.json`, ADR-0003 Decision 1's audit trail), used
pervasively, but no story yet publishes a real DOMAIN event. This story therefore builds both
halves: the subscription mechanism (`IDomainEventBus`) AND, for end-to-end demonstration, a
synthetic event constructed by the AC05 "reporting" endpoints themselves - a future story that
publishes a real domain event (e.g. `DeviceService.ChangeStatusAsync` publishing a real
`device.status_changed` event) depends on `IDomainEventBus` the same way this one's own
`NotificationDispatchService` already does, with no change needed here.

## Domain-event envelope: this project's own copy, not a shared type

`Domain/DomainEventEnvelope.cs` mirrors `contracts/schemas/event-envelope.schema.json`
field-for-field (see the type's own doc comment for why it cannot be the SAME C# type any
future in-host publisher would use). `AggregateType`'s five values match the schema's exact
enum (`tenant`/`person`/`credential`/`device`/`document_session`) - no sixth value was
invented for "notification" itself; the AC05 demonstration event reuses `device` (an existing,
valid choice) rather than requiring a schema change.

## AC01: a rule's own declared schema-version range is enforced, not just present

`NotificationRule.SupportsSchemaVersion(int)` and
`INotificationRuleRepository.ListMatchingAsync` exclude a rule whose
`MinSchemaVersion`/`MaxSchemaVersion` does not cover the event's `SchemaVersion` -
`RuleMatchingAndEventBusTests.a_rule_does_not_match_a_schema_version_outside_its_declared_range`
and `publishing_an_unsupported_schema_version_dispatches_to_zero_rules_not_processed_blindly`
prove this is enforced, not merely a field that exists on the record but is never read.

## AC03: dedup key extends ADR-0003 Decision 4's own `(tenant_id, event_id)`, doesn't invent one

ADR-0003 Decision 4 already defines the domain event's own idempotency key as
`(tenant_id, event_id)`. This story's delivery layer extends it with `rule_id` and `channel`,
documented on `INotificationDeliveryAuditSink` itself: one event can fan out to several
independent rule+channel deliveries, each needing its own dedup entry, so the base pair alone
is not enough for THIS layer - but it is not replaced, only extended.
`RetryDedupAuditTests.deduplication_is_scoped_per_rule_and_channel_not_just_per_event` and
`dedup_is_tenant_scoped_a_different_tenants_identical_event_id_is_not_conflated` prove both
halves of the extension are actually load-bearing, not just documented.

Retry is bounded (`MaxAttempts = 3`): a transient failure is retried until the bound, then
dead-lettered; a permanent failure is dead-lettered on its first attempt, never retried.
`RetryDedupAuditTests.a_transient_failure_is_retried_up_to_the_bound_then_dead_lettered_with_every_attempt_audited`
asserts every one of the three attempts is its own audit record (attempt numbers 1, 2, 3),
not just the final outcome - the whole retry history is reconstructable.

## AC04: allowlist, not denylist - and personal/sensitive get nothing by default

`PayloadAllowlist.Project` implements ADR-0003 Decision 3's own rule at this project's layer:
a channel names exactly which payload keys it may carry; anything not named is withheld. Five
channels, five different allowlists (SMS narrowest - `reference` only; Email/Teams/Webhook
widest - `summary`/`action`/`reference` plus channel-appropriate metadata; Push in between).
`Personal`/`Sensitive`-classified events short-circuit to an empty projection on EVERY
channel regardless of that channel's own allowlist - this foundation story does not define a
per-field allowlist for personal/sensitive data (ADR-0003: those levels "may appear... only
through an allowlist"; absent one, the fail-closed default is none of it).
`AC04_a_sensitive_classified_event_carries_no_payload_fields_on_any_channel` and the
`personal` counterpart prove this holds across every adapter, not just one.

## AC05: what "live" means here, and its limits

`web/features/reporting/live.ts` (built by BIO-UX-005) already targets
`/api/v1/reporting/primary-action` and `/api/v1/reporting/view-audit-trail` - the reports page's
own doc comment says so explicitly: *"No `BIO-RPT-*` backend story is built yet... stays
SIMULATION-only by design, not a gap."* This story implements exactly those two paths for
real, in `BioRpt001Module`, **without changing any file under `web/features/reporting/**` or
`web/app/{reports,audit}/**`** (confirmed by the structural harness's own `git diff` check) -
the seam was already built for a story like this one to fill.

- `POST /api/v1/reporting/primary-action` publishes a synthetic demonstration `device` event
  through the REAL `IDomainEventBus`/`NotificationDispatchService` pipeline (not a hardcoded
  response) and reports how many delivery attempts that produced.
- `POST /api/v1/reporting/view-audit-trail` summarizes this tenant's REAL delivery audit trail
  (AC03), not synthetic UI data.

**Limit, stated plainly**: the `/reports`/`/audit` PAGES themselves still render their own
client-side synthetic dataset (`lib/report-rows.ts`/`lib/audit-rows.ts`) regardless of which
mode `ReportingService` resolves to - `createLiveAdapter`'s own contract (`web/lib/module-port.ts`)
only ever returns a `{message}` string that drives the button's success/failure feedback, not
the table's row data; no existing BIOLOG story's `ModulePort` integration renders live data
into a table today (the same limit `BIO-DEV-003`'s devices page had). Flipping
`NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE`/`..._VIEWAUDITTRAIL` from `SIMULATION` to `LIVE`
today correctly and fully activates against this story's real implementation (proven end to
end in `NotificationEndpointsTests.cs`); this story does not flip that default itself
(an ops/deployment decision, not a builder one), and real tenant resolution still requires a
resolved `Host`-header tenant like every other tenant-plane endpoint in this API - a browser
session without one sees the same fail-closed `tenant_context_missing` any other LIVE module
shows today, which is this codebase's current, accepted maturity level, not a new problem this
story introduces or is expected to solve.

## Local development / verification

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj
dotnet test tests/features/bio-rpt-001/Biolog.Features.Notifications.Tests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj --filter NotificationEndpointsTests
bash tests/features/bio-rpt-001/BIO-RPT-001.test.sh
```

## Scope boundaries

- No changes to `web/features/reporting/**` or `web/app/{reports,audit}/**` (BIO-UX-005/011's
  own owned files) - see "AC05" above.
- No changes to any other story's owned backend files.
- Actually capturing a fingerprint, matching a face, printing a card, etc. - and publishing a
  REAL domain event for any of those actions - belongs to the owning epic (`BIO-BIO-*`,
  `BIO-CRD-*`, `BIO-DEV-*`); this story only builds the rule/dispatch/channel/audit mechanism
  those future publishers plug into.
