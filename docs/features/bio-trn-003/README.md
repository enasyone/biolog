# BIO-TRN-003 — Implement parent/system transport notifications

**Status: Seyi FAILed the first builder pass (SY-TRN003-01, High - lost-update race on concurrent
rule edits), fixed with an atomic repository-level compare-and-swap. Ready for fresh review.**

User story: "As configured school tenants, I need notifications on boarding/alighting and exceptions."

- **AC01** — Notification rules can select board/alight/missed/exception events.
- **AC02** — PII is minimized.
- **AC03** — Delivery failure is visible.
- **AC04** — Notifications do not change transport event truth.
- **AC05** — The approved web surface (`/transport`) is connected through `TransportService` to
  this story's live implementation or an explicitly documented HYBRID state, and the mapped
  end-to-end human workflow passes without an unapproved UI redesign.

Dependencies: BIO-TRN-002 (the real board/alight/missing-alight-exception source data this story
reads), BIO-RPT-001 (named in the approved story, but see "Why BIO-RPT-001's own dispatch
pipeline is not reused" below for why this story builds its own domain instead of extending that
one directly). Conflict group: `transport-notify`.

## What's built

New, self-contained project: `api/features/bio-trn-003/Biolog.Features.TransportNotifications.csproj`
(mirrors bio-trn-002's own flat `Domain`/`Api` layout; cannot reference any sibling feature
project — see the csproj's own comment).

- **`Domain/TransportNotificationEventType.cs`** — the four AC01 categories: `Board`/`Alight`
  (bio-trn-002's own `TransportEventMode`), `MissedAlight` (bio-trn-002's own already-computed
  `DerivedPresenceStatus.MissingAlightException`, read via its own disclosed
  `ITriggeredNotificationLog` seam) and `Exception` — this story's own new, additive
  classification: a captured event where every evidence entry is unverified, derived read-only
  from fields bio-trn-002's own `TransportEvent.Evidence` already exposes.
- **`Domain/TransportNotificationRule.cs`** — tenant-scoped, CAS-versioned rule selecting a
  non-empty set of event types; `ITransportNotificationRuleRepository` (in-memory).
- **`Domain/TransportNotificationRuleService.cs`** — RBAC-gated create/list/toggle
  (`transport_notifications.manage`/`transport_notifications.read`), the same
  actor+permission-gate shape BIO-INT-004's own `WebhookSubscriptionService` already established.
  Unlike bio-trn-002's own `TransportNotificationConfig` (deliberately left open - it only writes
  an internal log record), this story's own rules control a real delivery decision, so they are a
  genuinely different, gated class of decision.
- **`Domain/TransportFeedItem.cs`** / **`ITransportEventFeedGateway`** — the read-only cross-story
  seam onto bio-trn-002. No write member exists on this interface at all, so AC04 ("notifications
  do not change transport event truth") holds structurally, not merely by discipline.
- **`Domain/TransportNotificationPayloadRedactor.cs`** — AC02's own minimized payload: an explicit
  allowlist (`TenantId`/`PersonId`/`RouteId`/`EventType`/`OccurredAt`) that deliberately drops the
  source fact's own richer `Location`/`VehicleId` fields — `TransportNotificationPayload` has no
  property for either, a structural guarantee, not merely a runtime check.
- **`Domain/IInAppNotificationSender.cs`** / **`ITransportNotificationDeliveryAuditSink.cs`** /
  **`TransportNotificationDispatchLedger.cs`** — the send/audit/claim split BIO-INT-004's own
  webhook pipeline already established, re-derived here for this story's own domain: bounded
  retry (`MaxAttempts = 3`), visible `Delivered`/`DeadLettered` outcomes (AC03), atomic
  claim-based dedup so one rule delivers at most once per source event even when that event
  independently qualifies under more than one of the rule's own selected event types (see
  `TransportNotificationDispatchService`'s own header).
- **`Domain/TransportNotificationDispatchService.cs`** — `DispatchAndListAsync` is both the write
  path (runs any newly-matching rule against the current bio-trn-002 feed) and the read path (the
  full durable trail) in one call — the same "recompute/catch-up at read time" discipline
  bio-trn-002's own `GetPresenceStatusAsync` already establishes, so the web panel's own "load"
  needs no separate trigger/sync button.
- **`Api/ActorResolver.cs`** / **`Api/ActorProvenanceGate.cs`** — the same ADR-0006 Decision 3
  actor+provenance seam every sibling RBAC-gated story in this codebase already establishes
  (`TransportNotifications:ActorProvenanceSecret` configuration key).
- **`api/Biolog.Api/Modules/BioTrn003Module.cs`** — the translation boundary.
  `TransportEventFeedGatewayAdapter` wraps bio-trn-002's own already-registered
  `ITransportEventRepository` (internal, reachable here only because that project's own csproj
  grants this assembly internals visibility) and `ITriggeredNotificationLog` (public) directly,
  classifying each raw fact into this story's own `TransportFeedItem`. `Configure` seeds this
  story's own two `RolePermissionGrant` rows for `TenantAdmin`, the same "seed data, not
  provisioning logic" shape `DemoSessionIdentitySeed`/`BioAtt003Module` already established —
  fail-closed/inert unless `WebSessionBridge:SyntheticTenantId` is configured.
  Endpoints: `POST/GET /api/v1/transport-notifications/rules`,
  `PUT /api/v1/transport-notifications/rules/{id}/status`,
  `GET /api/v1/transport-notifications/deliveries`.

## Why BIO-RPT-001's own dispatch pipeline is not reused

BIO-RPT-001's own `NotificationDispatchService` matches rules by `(tenantId, AggregateType,
schemaVersion)` only — no sub-event-type filter — so a transport-specific rule would also match
every other `Person`-aggregate notification rule in the tenant. There is also no
`AggregateType.Transport` value, and none can be added without Kemi's approval (the same frozen
`contracts/schemas/event-envelope.schema.json` constraint bio-trn-002's own README already
documents for AC04). The same reasoning that led BIO-INT-004 to build its own
`WebhookSubscription`/`EventTypes` filter rather than reusing `NotificationRule` directly applies
here: this story builds its own small, self-contained rule/delivery domain instead.

## Why "parent" delivery is a disclosed boundary, not a silent gap

The story title names "parent/system transport notifications," but no guardian/parent
contact-channel model exists anywhere in this codebase: `Person` (`api/features/bio-idn-001/`)
carries no phone/email/address field at all, and `ProfileType.Parent` is only a label a `Person`
can carry, never a guardian-of relationship with a notification channel. Introducing one would be
a shared-contract change to `Person` this story does not own, and a materially larger surface than
"wire up notification rules." None of this story's own five acceptance criteria require an actual
external (SMS/email/push) delivery target — they ask for selectable rules, minimized content,
visible delivery failure, and read-only behaviour.

This story therefore implements the real, buildable half: a tenant-visible, in-app notification
feed (`IInAppNotificationSender`'s own real `InMemoryInAppNotificationSender`), with the channel
abstraction and retry/audit machinery built so a future guardian-contact story can plug in a real
external channel without redesigning this one. External parent delivery is a disclosed,
out-of-scope boundary pending that future contact-channel model, not something silently assumed
or left unaddressed.

## SY-TRN003-01 fix: rule updates are an atomic compare-and-swap, never a blind assignment

Seyi's first-pass review found `InMemoryTransportNotificationRuleRepository.UpdateAsync` (now
removed) was a plain dictionary assignment: `TransportNotificationRuleService.SetEnabledAsync`
read the current rule, checked `expectedVersion`, computed `version + 1`, then wrote - three
separate steps with no atomicity between the read and the write. Two callers racing from the
SAME stale read could both pass the version check and both write their own "version 2" record;
the second write silently clobbered the first instead of being rejected.

Fixed by replacing `UpdateAsync` with `ITransportNotificationRuleRepository.TryReplaceAsync(expectedCurrent,
updated)`, backed by `ConcurrentDictionary<TKey,TValue>.TryUpdate` - the BCL's own atomic
compare-and-swap. It only commits when the stored value still equals `expectedCurrent` at that
exact instant; `SetEnabledAsync` now re-reads and throws `RuleConcurrencyConflictException` with
the real current version when it loses that race, instead of silently losing a write. The fix
relies on `GetAsync` always returning the dictionary's own live reference (never a copy), so the
no-conflict case short-circuits on reference equality inside the record's generated `Equals`, and
the conflict case always differs on `Version` alone regardless of any other field's equality
semantics (see `TryReplaceAsync`'s own doc comment for the full reasoning) - the identical
"ownership via identity, never content inspection" discipline this session's `IIdempotencyStore.Claim`/
`IWebhookDeliveryAuditSink.TryRecordOnceAsync` already established, applied here to a record
REPLACE rather than a create.

Proven by two tests: Seyi's own reviewer-provided `SeyiRuleConcurrencyProbe.cs` (adapted to the
new `TryReplaceAsync` shape, same gating strategy - both reads block until both have read the
identical stale snapshot before either is released into its own write), and a second,
independently-authored barrier-gated test
(`TransportNotificationRuleServiceTests.Two_genuinely_concurrent_toggles_from_the_same_stale_read_commit_exactly_once`)
asserting exactly one of two racing toggles commits and the other is rejected as a conflict.

(This same blind-assignment shape exists in BIO-INT-004's own already-accepted
`InMemoryWebhookSubscriptionRepository.UpdateAsync` - flagged to god as an FYI, not fixed here:
that story is out of this story's own owned paths and already merged/accepted.)

## Design decisions worth flagging

- **Interpreting "missed" and "exception" as two distinct categories.** bio-trn-002 only computes
  one derived exception state (`MissingAlightException`). This story maps `MissedAlight` to that
  state directly, and introduces `Exception` as its own new, additive classification (an
  all-evidence-unverified capture) — a genuinely different condition (an anomalous/low-confidence
  capture vs. a missed alight), both legitimately deserving their own selectable rule category per
  AC01's own four-category wording.
- **One rule delivers at most once per underlying source event**, even when that event
  independently qualifies under more than one of the rule's own selected event types (e.g. a
  Board event with unverified evidence is both Board and Exception) — `ITransportNotificationDispatchLedger`'s
  claim key is `(tenantId, ruleId, sourceEventId)`, not `(..., eventType)`. Deliberate: a tenant
  configuring a rule for both categories should see one notification about one physical
  happening, not two.
- **No concurrency barrier test for the dispatch ledger** (unlike BIO-INT-004's own round-4
  concurrency test). This story's own `InMemoryTransportNotificationDispatchLedger.Claim` is a
  single synchronous `ConcurrentDictionary.GetOrAdd` call that resolves BEFORE any send is ever
  attempted — of two genuinely concurrent callers, only the winner's thread ever reaches the
  sender, so a barrier gating the sender (BIO-INT-004's own shape) would deadlock here. The
  dictionary's own documented single-writer guarantee is proven functionally instead, by two
  SEQUENTIAL dispatch calls (`TransportNotificationDispatchServiceTests.cs`'s own
  `Dispatching_twice_for_the_same_tenant_never_delivers_the_same_source_event_a_second_time`) —
  see that test file's own comment for the full reasoning.
- **No new `DeliveryAttemptOutcome.Deduplicated` value**, unlike BIO-RPT-001's/BIO-INT-004's own
  vocabulary. This story's own dispatch pass runs on every read (`DispatchAndListAsync`), so an
  already-processed source event is already durably visible once from its first pass — recording
  a fresh "deduplicated" row on every later page load would make the delivery trail grow without
  bound purely from repeated reads, never from a new underlying fact.

## AC05 — web wiring

New `web/features/bio-trn-003/` (`ports.ts`/`mock.ts`/`live.ts`/`index.ts`, the same
`resolvePort`/`createMockAdapter` pattern every module in this epic uses) plus a new
`TransportNotificationsPanel.tsx`, mounted on `/transport` ALONGSIDE (never replacing) the
existing boarding/alighting log and route-assignment sections
(`web/features/transport/transport-page.tsx`). Four new, independently mode-switchable
`TransportService` port keys: `listTransportNotificationRules`/`createTransportNotificationRule`/
`setTransportNotificationRuleEnabled`/`listTransportNotificationDeliveries`
(`web/lib/feature-mode.ts`), registered in `web/lib/module-registry.ts` and covered by
`tests/frontend/unit/bio-ux-010-module-ports.test.ts`'s own fixture (updated as part of this
story, not left as a fresh instance of the known `TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP`).

**The live adapter's own actor header is a disclosed boundary, the same one BIO-INT-004's own live
adapter already established.** `live.ts` sends a static, unauthenticated `{'X-Actor-Id': 'web-ui',
'X-Actor-Type': 'user'}` header pair, with no provenance proof — a real browser call against this
story's RBAC/provenance-gated backend would currently receive a 401/403. This is the SAME state
BIO-INT-004's own live adapter already shipped and was accepted in: the default browser Playwright
suite proves the mapped workflow against SIMULATION mode only (the bar every sibling story's own
AC05 section already sets for a panel with no real backend wired into that default suite), while
this story's real HTTP endpoints — RBAC/provenance enforcement included — are proven directly
against the real backend by `tests/dotnet/Biolog.IntegrationTests/BioTrn003EndpointsTests.cs`
(a genuine captured board event via bio-trn-002's own demo bridge, a real created rule, and a
real, minimized, visible delivery, all through the real packaged host).

**Executable proof, not just `tsc` and manual code reading.**
`tests/frontend/playwright/bio-trn-003-transport-notifications.spec.ts` (SIMULATION mode, runs in
the default `pnpm --dir tests/frontend test:e2e` suite) proves: the panel mounts alongside the two
existing sections, an empty tenant sees an explicit empty state, the full create workflow (fill
the name, select board and exception, submit, see the new rule, see a matching visible DELIVERED
row and a matching visible, accessible DEAD-LETTERED row), the create button itself disables with
zero event types selected, a genuinely failed create call (an empty name — a real validation
condition `createTransportNotificationRuleMockPort` already returns, not a client-side guard —
learning BIO-INT-004's own SY-INT004-04 lesson from the start) renders a `role="alert"` error, and
both toggle directions each show their own distinct visible status transition.

## Verification

- `dotnet build api/Biolog.Api/Biolog.Api.csproj` — clean.
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` — 248/248
  (244 pre-existing + 4 new `BioTrn003EndpointsTests`), one `ObservabilityTests` flake on an
  earlier run (the known, pre-existing `Console.Out`-redirect race against concurrent
  `WebApplicationFactory` hosts), confirmed non-reproducing on immediate re-run.
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` — 210/210, no regression.
- `dotnet test tests/features/bio-trn-003/Biolog.Features.TransportNotifications.Tests.csproj` —
  21/21 feature-unit tests (19 original + Seyi's own adapted concurrency probe + one new
  independently-authored barrier test for SY-TRN003-01), re-run 3x to confirm the two concurrency
  tests are not flaky.
- `bash scripts/check-module-project-references.sh` / `python3 scripts/check-traceability.py --all`
  / `bash scripts/secret-scan.sh` — all PASS.
- `npx tsc --noEmit` (web) — clean.
- `npx vitest run` (tests/frontend) — 546/548 (the 2 failures are the pre-existing, already-tracked
  `TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP` for `IntegrationService`, unrelated to this story;
  this story's own new `TransportService` ports are correctly covered, confirmed by updating and
  passing `bio-ux-010-module-ports.test.ts`'s own fixture).
- `npx playwright test bio-trn-003-transport-notifications` — 6/6 passed, re-run once more to
  confirm stability (both runs clean). Two earlier attempts on this same worktree timed out on
  every test waiting for the panel's own elements — not a code defect: this machine was running
  several other concurrent sessions' `dotnet test`/`next dev` processes at the time, and a brand
  new, un-pre-warmed Turbopack dev server's first compile of `/transport` exceeded the suite's
  30s per-test timeout under that load. Confirmed by pre-starting `pnpm dev -p 3000` in this same
  worktree, waiting for it to answer `/transport` with `200` once, then re-running the suite
  (`playwright.config.mjs`'s own `reuseExistingServer: !process.env.CI` picks it up) — clean on
  the very same code, twice.
