# BIO-INT-004 — Implement outbound webhooks

**Status: Chioma REJECTED acceptance (BIO-INT-004-QA-01, High - AC05 had no executable browser
proof, only `tsc`/manual code reading), fixed with a real Playwright workflow spec and an
explicit `/settings/authentication` scope disclosure. Seyi's post-acceptance re-review then FAILed
that fix (SY-INT004-04, Medium - the only "failure" test proved a client-side guard, never a real
failed API call with a visible error state); fixed with a genuine failure-path test plus a
role="alert" success/failure distinction on the panel's own notice. Ready for fresh Seyi/Zainab
review on the web-only change.**

User story: "As tenants, we need signed, retryable outbound webhooks for selected events."

- **AC01** — Subscriptions are tenant-scoped and filtered by event type.
- **AC02** — Delivered payloads are versioned (the ADR-0003 event-envelope shape, on the wire).
- **AC03** — Deliveries are signed and protected against replay.
- **AC04** — Retry/dead-letter behavior is auditable.
- **AC05** — `/administration/integrations` is wired to a real `IntegrationService` surface for
  subscription management.

Dependencies: BIO-ARC-003 (the event-envelope ADR this story's own wire payload mirrors),
BIO-INT-003 (this story's own ActorResolver/ActorProvenanceGate/IPermissionGateway precedent).
Conflict group: `webhooks`.

## What's built

New, self-contained project: `api/features/bio-int-004/Biolog.Features.Webhooks.csproj`
(mirrors BIO-INT-003's own `Domain`/`Api` layout; cannot reference any sibling feature
project — see the csproj's own comment).

- **`Domain/WebhookEventEnvelope.cs`** — `AggregateType`/`EventClassification` enums plus
  `WebhookEventEnvelope`, this project's OWN independent copy of the BIO-ARC-003 event-envelope
  schema (`contracts/schemas/event-envelope.schema.json`), field-for-field identical to
  BIO-RPT-001's own `DomainEventEnvelope` — a third independent duplicate, not a shared type
  (neither feature project can reference the other or `Biolog.Api`).
- **`Domain/WebhookSubscription.cs`** — tenant-scoped subscription aggregate (AC01): a URL, a
  non-empty set of `AggregateType` filters, enabled flag, a cryptographically random signing
  secret, optimistic-concurrency version. `IWebhookSubscriptionRepository` (public — unlike
  BIO-INT-003's own internal `IServiceAccountRepository`, deliberately, so an integration test
  CAN substitute it directly, same as that story's own disclosed workaround) +
  `InMemoryWebhookSubscriptionRepository`.
- **`Domain/WebhookSigner.cs`** — AC03: `t={unixMs},v1={hex-hmac}` header format, Stripe/GitHub-
  style. The HMAC covers `"{timestampMs}.{body}"` — the timestamp is bound INTO the signed
  message, so a captured valid signature cannot be replayed with a swapped or absent timestamp.
  Delivery-side dedup (never redelivering the same event to the same subscription twice, via the
  audit sink's `HasSucceededAsync`) is the other half of replay protection.
- **`Domain/IWebhookSender.cs`** — the actual HTTP transport, separated from the dispatch
  service (BIO-RPT-001's own channel-adapter separation, applied here): 2xx → `Delivered`; 429
  or any 5xx, or a network-level failure → `TransientFailure`; any other 4xx (an unambiguous
  rejection) → `PermanentFailure`, never retried.
- **`Domain/IWebhookDeliveryAuditSink.cs`** — AC04: one row per delivery ATTEMPT (not per
  event-subscription pair), so a retried delivery's full history is reconstructable.
- **`Domain/WebhookDispatchService.cs`** — ties AC01 (subscription matching)/AC02 (versioned
  wire payload)/AC03 (signing)/AC04 (retry/dedup/audit) together, mirroring
  `NotificationDispatchService`'s own shape almost line-for-line: `MaxAttempts = 3` bounded
  retry, dedup checked BEFORE attempting delivery, `PermanentFailure` → dead-lettered
  immediately, `TransientFailure` → retry unless the last attempt. `WebhookPayloadDto` uses
  explicit `[JsonPropertyName]` snake_case attributes, not an automatic naming policy — citing
  BIO-INT-003's own `OAuthTokenRequest` bug (the host's default camelCase binding silently
  mismatched a real external wire contract) as the reason never to rely on automatic conversion
  for a wire boundary.
- **`Domain/WebhookSubscriptionService.cs`** — AC01's create/list/enable-disable, gated by
  `webhooks.subscription_manage`/`webhooks.subscription_read` (mirrors BIO-INT-003's
  `ManagePermission`/`ReadPermission` naming), checked via `IPermissionGateway`.
- **`Api/ActorResolver.cs`/`Api/ActorProvenanceGate.cs`** — copied from BIO-INT-003 (this
  project cannot reference that sibling feature project either), config key
  `Webhooks:ActorProvenanceSecret`.
- **`api/Biolog.Api/Modules/BioInt004Module.cs`** — the host module. Registers everything above,
  including `services.AddHttpClient<IWebhookSender, HttpWebhookSender>()` for a real HttpClient-
  backed sender. In `Configure`, resolves the REAL, shared, already-built
  `Biolog.Features.Notifications.IDomainEventBus` singleton (confirmed to exist and already be
  wired for BIO-RPT-001's own demo event — see "Design decisions" below) and subscribes, with an
  inline explicit-switch translation from `Notifications.DomainEventEnvelope` →
  `Webhooks.WebhookEventEnvelope` (never a numeric enum cast — see `TranslateAggregateType`'s own
  comment for why). Maps `POST /api/v1/integrations/webhooks/`,
  `GET /api/v1/integrations/webhooks/`, `PUT /api/v1/integrations/webhooks/{id}/status`.

## AC05 — web wiring

New `web/features/bio-int-004/` (`ports.ts`/`mock.ts`/`live.ts`/`index.ts`, the same
`resolvePort`/`createMockAdapter` pattern every module in this epic uses) plus a new
`WebhookSubscriptionsPanel.tsx`, mounted on `/administration/integrations` ALONGSIDE (never
replacing) BIO-INT-002's `ImportConsole` and BIO-INT-003's `ServiceAccountsPanel`. Three new,
independently mode-switchable `IntegrationService` port keys:
`listWebhookSubscriptions`/`createWebhookSubscription`/`setWebhookSubscriptionEnabled`
(`web/lib/feature-mode.ts`).

**`/settings/authentication` is deliberately out of scope - a disclosed boundary, not a silent
gap.** BIO-INT-004-QA-01 (Chioma, High - acceptance) asked this to be stated explicitly for every
story that adds `IntegrationService` port keys, not assumed. This story's own three new ports are
never routed to `/settings/authentication`; that page's own primary actions remain BIO-INT-001's
sign-in/session-policy domain, the identical boundary BIO-INT-003's own README already drew for
its own `IntegrationService` ports ("its own primary actions are BIO-INT-001's own sign-in/
session-policy domain ... not this story's API service-account concern"). Webhook subscription
management has no relationship to authentication settings, so there is nothing for this story to
wire there.

**Executable proof, not just `tsc` and manual code reading.** BIO-INT-004-QA-01 also required a
real browser-level proof of the mapped end-to-end workflow with visible success/failure states -
`tests/frontend/playwright/bio-int-004-webhooks.spec.ts` (SIMULATION mode, runs in the default
`pnpm --dir tests/frontend test:e2e` suite, the same bar `bio-rpt-004-operational-health.spec.ts`
already sets for a panel with no real backend wired into that default suite) proves: the panel
mounts alongside the two existing panels, an empty tenant sees an explicit empty state, the full
create workflow (fill the form, submit, see the one-time signing secret, see the new row appear
enabled), the create button itself disables with zero event types selected (never relying on the
backend alone to reject an empty selection), and both toggle directions (enable→disable→enable)
each show their own distinct visible status transition. `WebhookSubscriptionsPanel.tsx` gained
`data-testid` attributes throughout specifically to make this proof reliable rather than relying
on brittle text-content locators.

**SY-INT004-04 (Seyi, post-acceptance re-review): a disabled-button assertion is not a failure
proof.** The zero-event-types test above proves a client-side guard, not a failed API call - a
silently-swallowed error or a backend 500 that renders nothing would have passed it unchanged.
Fixed two ways. First, the panel's own `notice` state (`web/features/bio-int-004/
WebhookSubscriptionsPanel.tsx`) now carries an explicit `isError` flag alongside its message and
renders with `role="alert"` only when it is one - the same pattern this panel's own
`error`/`offline` load-failure states, and every other panel in this codebase, already use;
before this fix a failed create/toggle rendered through the identical plain `<p>` a successful one
did, indistinguishable to an assistive-technology user. Second, a new test clears the URL field
(leaving an event type checked, so the client-side guard does not intercept it) and genuinely
reaches `createWebhookSubscriptionMockPort`'s own pre-existing "A URL is required (SIMULATION)"
validation failure - the same realistic, already-present mock condition
`web/features/bio-crd-008/mock.ts` established as this codebase's convention for triggering a
failure path, never an invented magic-string trigger - then asserts the notice carries
`role="alert"`, the expected message, and that no row or secret appeared.

## Design decisions worth flagging

- **A research fork initially reported "no domain-event bus exists anywhere."** Verified
  directly before trusting that conclusion (same discipline TD-WEB-PROVENANCE-BRIDGE's own
  Host-rewrite verification used) and found it was wrong: `IDomainEventBus`/
  `InMemoryDomainEventBus` (`api/features/bio-rpt-001/.../Domain/IDomainEventBus.cs`) is a real,
  already-built, already-registered in-process pub/sub bus, already used by BIO-RPT-001's own
  demo `/primary-action` endpoint. Its own doc comment explicitly names this exact situation: "A
  future story that publishes real events depends on `IDomainEventBus`... with no change needed
  here." Corrected the design before writing code — webhooks subscribe to the SAME real bus
  BIO-RPT-001's own demo event already fires through, not a second synthetic copy. The first REAL
  event publisher (a future story) triggers real webhook delivery with no change needed here;
  today's demo event already proves the full pipeline end to end.
- **A webhook signing secret is OUR OWN key material, not hashed** — the opposite role BIO-INT-
  003's own `ServiceAccount.SecretHash` plays. A service-account secret is a credential the
  CALLER presents to US (we only ever need to verify a hash of it); a webhook signing secret is
  something WE compute `hex(HMACSHA256(secret, body))` with on every outbound delivery — the
  same shared-secret role every `*ActorProvenanceSecret`/`WebSessionBridge:TokenSecret` config
  value already has in this codebase. Stored plaintext-equivalent (hex), crypto-randomly
  generated, returned to the caller exactly once at creation.
- **`admin` maps to `TENANT_ADMIN`, deliberately not `SUPER_ADMIN`**, everywhere this story
  needed a worked example of a privileged demo role (see TD-WEB-PROVENANCE-BRIDGE round 6's own
  reuse of this reasoning) — `SuperAdmin` bypasses the real `RolePermissionGrant` lookup
  entirely via its own short-circuit, which would prove nothing about the grant-lookup path a
  permission check exists to exercise.
- **`IWebhookSubscriptionRepository` is public, not internal** (unlike BIO-INT-003's own
  `IServiceAccountRepository`, which started internal and was later found to need a test-only
  escape hatch) — a deliberate, disclosed choice from the start, so an integration test can
  substitute it directly for AC02/AC03/AC04 proofs without needing `InternalsVisibleTo` access
  this project's test assembly would otherwise lack for internal types.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — tenant-scoped, filtered subscriptions | `WebhookSubscription`, `IWebhookSubscriptionRepository`/`InMemoryWebhookSubscriptionRepository`, `WebhookSubscriptionService` | `WebhookSubscriptionServiceTests.cs` (12 tests), `WebhookEndpointsTests.cs` (6 tests) |
| AC02 — versioned payloads | `WebhookEventEnvelope` (ADR-0003 schema mirror), `WebhookPayloadDto` (explicit snake_case) | `WebhookDispatchServiceTests.cs` (the wire-shape test) |
| AC03 — signed, replay-protected deliveries | `WebhookSigner` (`t=...,v1=...`, timestamp bound into the signed message) | `WebhookSignerTests.cs` (5 tests, incl. an openssl-cross-checked golden vector) |
| AC04 — auditable retry/dead-letter | `WebhookDispatchService.AttemptWithRetryAsync`, `IWebhookDeliveryAuditSink` | `WebhookDispatchServiceTests.cs` (7 tests: delivered/permanent-failure/transient-retry/recovers/dedup/tenant+type matching) |
| AC05 — `/administration/integrations` wired via `IntegrationService` | `WebhookSubscriptionsPanel.tsx`, three new mode-switchable port keys | Mounted alongside existing panels; `npx tsc --noEmit` clean |

## Verification

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj                                           # clean
dotnet test tests/features/bio-int-004/Biolog.Features.Webhooks.Tests.csproj            # 22/22
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj          # 226/227
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj                        # 210/210
npx tsc --noEmit (web)                                                                   # clean
npx vitest run (tests/frontend)                                                          # 540/542
bash scripts/check-module-project-references.sh                                         # PASS
bash scripts/secret-scan.sh                                                             # clean
```

The 1 integration failure (`ObservabilityTests.AC01`) is a known, pre-existing `Console.Out`-
redirect race against concurrent `WebApplicationFactory` hosts (confirmed non-reproducing in
isolation). The 2 frontend failures (`bio-ux-005-feature-mode`/`bio-ux-005-module-registry`) are
the pre-existing, already-tracked `TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP` — this story's new
`IntegrationService` port keys were added to `MODULE_PORT_KEYS` the same way BIO-INT-003's own
four keys already were (consistent with that existing precedent, not a new inconsistency);
`web/lib/module-registry.ts` is owned by that separate TD, not touched here.

All test data is synthetic (`example-synthetic.test` URLs, `per_test_*`/`ten_*` fixture ids);
no production credentials, PII or biometric data anywhere in this story.

## Round 1 — Zainab security review FAIL, fixed (2026-10-07)

Two High findings, both confirmed dispositive from code inspection (her own feature-test replay
couldn't start in her environment - a known VSTest loopback-bind sandbox limitation, unrelated to
either finding).

**ZB-INT004-01 (SSRF).** Subscription creation accepted any absolute HTTP(S) URL with no
destination policy at all - `HttpWebhookSender`'s plain `HttpClient` would happily deliver an
event payload (which can carry personal/sensitive data, AC02's own `payload_classification`) to
loopback, an RFC 1918 private range, a link-local address (including the well-known cloud-
metadata address `169.254.169.254`), or wherever else a subscriber chose to point it. Fixed with
a real destination policy, enforced at the right layer:

- **`WebhookDestinationPolicy.IsAllowed`** - the single predicate (loopback/unspecified, RFC
  1918 private ranges, link-local/metadata, RFC 6598 shared address space, multicast/reserved,
  and the IPv6 equivalents: link-local, site-local, multicast, unique-local).
- **`WebhookConnectCallback`**, wired into `HttpWebhookSender`'s own
  `SocketsHttpHandler.ConnectCallback` (`BioInt004Module.RegisterServices`) - the authoritative
  enforcement point, deliberately NOT a URL-parse-time check: a hostname's DNS resolution can
  change between subscription creation and delivery (DNS rebinding), and a redirect's `Location`
  header is not known until delivery time at all. Every socket `HttpClient` ever opens for one
  logical request - the initial connection AND every redirect hop, since each is its own fresh
  TCP connection through the same callback - resolves DNS itself and is checked before connecting;
  a disallowed resolved address is never connected to, even if a legitimate multi-record DNS
  answer also contains an allowed one.
- A `WebhookDestinationBlockedException` thrown by the callback is classified
  `PermanentFailure` in `HttpWebhookSender` (never retried - the URL resolves to the same
  disallowed address every time retrying would be pointless).
- `WebhookSubscription`'s own constructor ALSO rejects a literal loopback/private/link-local IP
  address in the URL at creation time - explicitly documented as a best-effort, NON-authoritative
  fast-fail courtesy (no DNS lookup happens here), never a substitute for the connect-time check.

**Also considered, deliberately deferred rather than silently skipped**: Zainab's own
remediation paragraph also mentions requiring HTTPS in non-development environments, rejecting
URL userinfo, and restricting destination ports. None of these enable the demonstrated SSRF
itself (reaching an internal service by address) the way an unvalidated destination IP does, and
restricting ports risks rejecting a legitimate subscriber's own non-standard API port. Left as a
named, disclosed follow-up rather than built speculatively here - happy to add any of them if
round 2 still wants them.

**ZB-INT004-02 (idempotency-claim race).** The original dedup was check-then-send-then-record
(`IWebhookDeliveryAuditSink.HasSucceededAsync` -> send -> `RecordAsync`), with no atomic claim
between the check and the send - the exact same shape as this session's BIO-INT-002/BIO-MOB-003/
BIO-MOB-005/BIO-ACC-003/BIO-CRD-005/006/007 findings. Ported that established fix verbatim rather
than reinventing it:

- New `IIdempotencyStore`/`IdempotencyClaim`/`InMemoryIdempotencyStore` (this project's own
  independent copy - cannot reference any sibling feature project), scoped to
  `(tenantId, subscriptionId, eventId)` - the natural per-delivery key, since one subscription's
  own deliveries are independent of every other subscription's.
- `WebhookDispatchService.DeliverToSubscriptionAsync` claims ownership BEFORE sending via
  `IdempotencyClaim.IsNewClaim` (never the record's own content/state alone, which is identical
  for a genuine replay either way) - a bounded wait (`idempotencyWaitTimeout`, default 5s,
  injectable for tests) for a claim-loser, dead-lettering if the owner never finishes in time
  rather than waiting forever. `IWebhookDeliveryAuditSink` is now the per-attempt audit TRAIL
  only (AC04's own "auditable" requirement); it is no longer the dedup DECISION.
- `Release` on an unexpected exception mid-attempt, so a legitimate later delivery is never
  permanently blocked by an abandoned claim; `Complete` on either terminal outcome (Delivered OR
  DeadLettered - both are "this subscription has had its one real chance at this event").
- Every terminal audit-trail write (Delivered, DeadLettered, Deduplicated) now uses
  `CancellationToken.None`, not the caller's own token - Zainab's own explicit ask ("do not use
  the caller cancellation token for post-send completion recording"), the same ADR-0003
  Decision 7 discipline every other audit-atomicity site in this codebase already applies: once a
  real send has happened (or a dedup/timeout decision is final), recording it is the point of no
  return - a receiver that already accepted an event must never go unrecorded because the
  caller's own token happened to cancel in the narrow window right after. Only the non-terminal
  `Retrying` record (nothing irreversible decided yet) still uses the live token.

**Test proof, built to the mature standard from the start** (not the round-1-then-round-2 path
BIO-CRD-005/006/007 took, per Zainab's own explicit ask): `ClaimBarrierIdempotencyStore`
(`WebhookDispatchServiceTests.cs`) forces the real contested sequence deterministically - the
winning caller is held immediately after its claim becomes durably `InFlight`, until the test
confirms the losing caller has itself observed that SAME still-`InFlight` record, before
releasing the winner. A separate `Mutant_discrimination_*` test proves this harness shape
genuinely discriminates: the real `IsNewClaim`-based decision converges to exactly one send under
a `Barrier`-forced simultaneous-observation race, while the ORIGINAL defective
`HasSucceededAsync`-shaped decision does not. One representative bounded-timeout test reuses the
same barrier store (never releasing the owner).

### Verification (round 1 fix)

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj                                           # clean
dotnet test tests/features/bio-int-004/Biolog.Features.Webhooks.Tests.csproj            # 53/53 (was 22, +31 new: SSRF policy/connect-callback classification, literal-IP creation-time rejection, claim-barrier race, mutant-discrimination, bounded-timeout)
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj          # 236/236
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj                        # 210/210
bash scripts/check-module-project-references.sh                                         # PASS
bash scripts/secret-scan.sh                                                             # clean
python3 scripts/check-traceability.py --all                                             # PASS
```

The new concurrency tests (`HandleAsync_the_loser_must_genuinely_observe_the_winners_InFlight_claim...`,
`Mutant_discrimination_*`, the bounded-timeout test) were re-run 3x in a row with no flakiness.

## Round 2 — Zainab security review FAIL, fixed (2026-10-07)

Confirmed round 1's three fixes closed (SSRF connect-time/per-redirect enforcement, the atomic
claim, cancellation suppression on terminal audit). Two new High findings.

**ZB-INT004-03 (send-success/audit-failure still releases the claim and redelivers).** Round 1's
`CancellationToken.None` fix only stopped the CALLER's own cancellation from suppressing the
terminal audit write - it never made that write infallible. If the audit sink throws for any
OTHER reason after a successful send, the old code's outer `catch` released the idempotency
claim anyway, so a later dispatch of the same event would win a fresh claim and deliver it to
the same subscriber a SECOND time - the exact receiver-accepted/completion-write-failed
ambiguity round 1 was supposed to close.

Fixed by reordering, not by trying to make the audit write itself infallible (which isn't
possible in general): `WebhookDispatchService.CompleteTerminalAsync` now calls
`IIdempotencyStore.Complete` FIRST, before the terminal audit write is even attempted - so by
the time the write could throw, idempotency safety no longer depends on its outcome at all. If
the write does throw, a recheck (`auditSink.ListForTenantAsync`, the identical "ExistsAsync
verification on an ambiguous write throw" discipline ADR-0003 Decision 7 already applies
everywhere else in this codebase) distinguishes "didn't actually land" from "happened despite
throwing" before deciding whether to surface `WebhookAuditUnavailableException` - but either way
the delivery decision itself is already durable. `DeliverToSubscriptionAsync`'s own outer
`catch` only ever reaches `idempotencyStore.Release` for an exception that happened BEFORE any
terminal outcome was reached; as defense-in-depth, `InMemoryIdempotencyStore.Release` is now
also a no-op against an already-`Completed` record (an atomic conditional remove), so even a
mis-ordered future `Release` call can never resurrect a claim a `Complete` already closed.

New regression test (`A_genuine_audit_failure_after_a_successful_send_does_not_cause_a_duplicate_delivery_on_the_next_dispatch`):
a `FlakyAuditSink` fails the FIRST terminal audit write (genuine failure, not cancellation) right
after a successful send; the first `HandleAsync` call throws `WebhookAuditUnavailableException`
but the sender was still only called once; a SECOND `HandleAsync` call for the identical event is
deduplicated, proving the sender is never called twice regardless of the audit failure. A direct
unit test on the primitive (`Release_is_a_no_op_against_an_already_completed_claim`) proves the
defense-in-depth layer independently.

**ZB-INT004-04 (outbound confidentiality + destination policy completeness).** Two parts:

1. **Plaintext HTTP was still accepted.** HMAC signing proves integrity/authenticity, never
   confidentiality - a network observer could read a `personal`/`sensitive` event payload in
   transit, or capture a valid signed request. Fixed: `WebhookSubscriptionService.CreateAsync`
   now requires HTTPS unless a purpose-built, config-sourced `Webhooks:AllowInsecureHttp` flag is
   explicitly set (default `false`, fail-closed). Deliberately NOT an
   `ASPNETCORE_ENVIRONMENT`/`IsDevelopment()` check - the same lesson
   `BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE` already taught this session (TD-WEB-PROVENANCE-BRIDGE):
   this app is a demo/showcase product that runs in a real deployed "production" environment by
   design, so an environment-based gate would either never activate there or have to be disabled
   for the one environment HTTP subscriptions are actually wanted for (local testing) - a
   purpose-built flag names the real intent directly.
2. **The destination policy was a hand-enumerated blocklist, and it was missing real ranges.**
   Round 1's blocklist missed `198.18.0.0/15` (benchmarking), `192.0.0.0/24` (IETF protocol
   assignments), `192.0.2.0/24`/`198.51.100.0/24`/`203.0.113.0/24` (documentation/TEST-NET), and
   several special-purpose IPv6 ranges - exactly the "incomplete blocklist" failure mode
   enumerating ranges by hand invites. Reframed and rebuilt as a positive policy:
   `WebhookDestinationPolicy`'s `SpecialPurposeRanges` table is now the COMPLETE IANA IPv4/IPv6
   Special-Purpose Address Registry (RFC 6890 and its updates) plus the multicast classes, and
   `IsAllowed` allows an address only if it falls in NONE of them - "global unicast" by
   construction, not by omission. A future IANA addition is a one-line table entry, not a new
   blocklist condition someone has to remember to add reactively. The connect-time/per-redirect
   enforcement point itself (round 1's `WebhookConnectCallback`) is unchanged - only the
   predicate it consults got more complete.

New test vectors (`WebhookDestinationPolicyTests.cs`): every range the review named explicitly,
plus a dedicated boundary test (`Allows_addresses_immediately_outside_an_odd_prefix_length_boundary`)
proving the bit-level CIDR match in `SpecialRange.Contains` is correct exactly at a non-byte-
aligned prefix boundary (`/15` and `/23` are the two odd prefix lengths in the table), not merely
comfortably inside each range. New HTTPS-enforcement tests
(`WebhookSubscriptionServiceTests.cs`): rejects plain HTTP by default, accepts it only when
`allowInsecureHttp` is explicitly set, and HTTPS always succeeds regardless of the flag.

### Verification (round 2 fix)

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj                                           # clean
dotnet test tests/features/bio-int-004/Biolog.Features.Webhooks.Tests.csproj            # 81/81 (was 54, +27 new)
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj          # 241/241
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj                        # 210/210
bash scripts/check-module-project-references.sh                                         # PASS
bash scripts/secret-scan.sh                                                             # clean
python3 scripts/check-traceability.py --all                                             # PASS
bash scripts/check-dotnet-vulnerabilities.sh (both projects)                             # PASS
```

Re-ran the full feature-unit suite 3x in a row with no flakiness.

## Round 3 — Zainab AND Seyi converge on the same two gaps, fixed (2026-10-07)

Both reviewers independently found the identical two issues (ZB-INT004-05/SY-INT004-01,
ZB-INT004-06/SY-INT004-02).

**ZB-INT004-05/SY-INT004-01 (Medium) — audit failure made non-replayable but never repaired.**
Round 2's `CompleteTerminalAsync` completed the idempotency claim, attempted the terminal audit
write, rechecked once on failure, and threw `WebhookAuditUnavailableException` if the record was
genuinely absent — but the recheck only ever DISTINGUISHED "missing" from "already there"; it
never actually RETRIED the write. A later duplicate dispatch for the same event saw the claim
already `Completed` and wrote only a bare `Deduplicated` record, with nothing underneath it —
the trail could say "deduplicated" while showing zero evidence the event was ever delivered or
dead-lettered at all, violating AC04's own auditability requirement and round 2's own
convergence promise.

Fixed by storing enough to reconstruct the EXACT terminal record on the claim itself
(`TerminalDeliveryResult(Outcome, AttemptNumber, Detail)`, not just the bare outcome enum), and
introducing a shared `RepairTerminalRecordAsync` that both `CompleteTerminalAsync`'s own
first-failure path AND the dedup branch call: check whether the terminal record already exists:
if not, actually WRITE it (not merely note its absence) before giving up. A single transient
failure now self-heals within the SAME dispatch call (the common case — no second dispatch
needed); if that immediate repair also fails, every SUBSEQUENT duplicate dispatch for the same
event gets another chance to converge it, writing the real recovered outcome before adding its
own `Deduplicated` observation.

**ZB-INT004-06/SY-INT004-02 (High) — the "complete" IANA table was still incomplete, again.**
Still missing: `64:ff9b:1::/48` (local-use NAT64 — Zainab's own specific warning: this prefix can
translate an apparently-allowed IPv6 destination into IPv4 space the policy already blocks, a
live bypass vector, not merely a completeness gap), `100:0:0:1::/64` (the Dummy IPv6 Prefix,
distinct from the Discard-Only block `100::/64` already present), `3fff::/20` (documentation, RFC
9637, 2024) and `5f00::/16` (Segment Routing IPv6 SIDs, RFC 9602, 2024).

Per Zainab's own explicit point — "the table also calls itself complete and assumes every
address outside it is global unicast, which is not a safe invariant as the registry evolves" —
this is now disclosed honestly rather than re-asserted: added the four ranges above after a
full, careful re-pass against the complete current IANA IPv6 Special-Purpose Address Registry
(not just the four named gaps), and added
`WebhookDestinationPolicy.SpecialPurposeRangesLastVerifiedAgainstIanaRegistry` — a plain date
constant naming when this checked-in snapshot was last hand-verified against the live registry,
since no live-validation or registry-pinning infrastructure exists in this codebase to build
that properly (a named, disclosed follow-up, not a silently-assumed solved problem). The table's
own doc comment now also explicitly lists which narrower IANA sub-allocations (several /128s and
small blocks inside `2001::/23`) are deliberately NOT repeated as their own rows because a
broader entry already covers them — making the "why isn't X listed separately" reasoning
auditable rather than requiring a reader to trust it.

### Verification (round 3 fix)

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj                                           # clean
dotnet test tests/features/bio-int-004/Biolog.Features.Webhooks.Tests.csproj            # 91/91 (was 81, +10 new)
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj          # 241/241
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj                        # 210/210
bash scripts/check-module-project-references.sh                                         # PASS
bash scripts/secret-scan.sh                                                             # clean
python3 scripts/check-traceability.py --all                                             # PASS
bash scripts/check-dotnet-vulnerabilities.sh (both projects)                             # PASS
```

Re-ran the full feature-unit suite 3x in a row with no flakiness.

## Round 4 — both reviewers find a concurrent-repair race and an incomplete positive policy, fixed (2026-10-07)

**ZB-INT004-07/SY-INT004-03 (Medium, both reviewers independently caught this).** Round 3's own
`RepairTerminalRecordAsync` was itself a check-then-write race:
`auditSink.ListForTenantAsync` then `RecordAsync`, nothing atomic between them - the exact same
shape as the ORIGINAL send-side dedup race round 1 already fixed, now one layer up, in the repair
path that exists to fix a DIFFERENT race. Two genuinely concurrent repair attempts for the same
already-completed claim (two overlapping duplicate dispatches) could both observe the terminal
record absent and both append their own row, corrupting AC04's one-row-per-attempt trail.

Fixed by giving the audit sink itself an atomic create-if-absent operation,
`IWebhookDeliveryAuditSink.TryRecordOnceAsync`, keyed by a record's full structural identity
(tenant/event/subscription/attempt/outcome) - the identical `ConcurrentDictionary.GetOrAdd`-plus-
`ReferenceEquals` ownership ritual `IIdempotencyStore.Claim` already established, applied to the
audit trail instead of the delivery decision. `CompleteTerminalAsync` and the dedup branch now
both funnel through one `EnsureTerminalRecordAsync` method (renamed from round 3's
`RepairTerminalRecordAsync`, since it's now the SOLE mechanism for both the first write and any
later repair) that attempts the atomic write twice before falling back to a recheck - preserving
round 3's own "a single transient failure self-heals within the same dispatch" property on top of
the new atomicity.

New test: `EnsureTerminalRecordAsync_two_genuinely_concurrent_repairs_create_exactly_one_terminal_row`
- starts from a claim pre-completed with no terminal record at all (the exact starting state a
repair exists to converge), fires two genuinely concurrent dispatches via `Task.Run` (required,
not optional - everything under `HandleAsync` completes synchronously today, so without forcing
each call onto its own thread the first call's own blocking `Barrier.SignalAndWait` would
deadlock the test thread against itself before the second call is ever started), barrier-forces
both to reach the real atomic create-if-absent at the same instant, and proves exactly one
terminal row results.

**ZB-INT004-08 (High) — the IPv6 policy was still a deny-list, never a true positive allow-list.**
Round 2 rebuilt the IPv4 policy as "global unicast by construction" (allowed only if NOT in the
deny-list), but IPv4's deny-list - given its own `240.0.0.0/4` catch-all already accounts for the
entire remainder of the address space above the allocated unicast range - is ALREADY equivalent
to a positive policy in effect. IPv6 is different: the vast majority of its address space is
currently UNASSIGNED/reserved for future allocation, and the special-purpose registry only lists
exceptions IANA has already carved out, not "everything that isn't currently routable." Round 3's
policy let `4000::1`, `6000::1`, `8000::1`, `a000::1` and `c000::1` all through despite being
reserved and potentially internally routed - the review's own examples.

Fixed with a genuine positive gate: IANA's own IPv6 Address Space registry names `2000::/3` as
the ENTIRE currently assigned global-unicast allocation. `IsAllowed` now requires IPv6 membership
in `2000::/3`, checked AFTER the named deny-list (so a specific special-purpose range inside
`2000::/3` - documentation, 6to4 - still gets its own precise rejection reason, not a generic
one). New tests cover every reserved top-level block the review named, plus a dedicated boundary
test at the `2000::/3` edge itself (`1fff:...` just below, blocked; `2000::` the first address,
allowed; `3fff:ffff:...` the last address - and outside the narrower `3fff::/20` special-purpose
sub-block - allowed; `4000::` just above, blocked).

### Verification (round 4 fix)

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj                                           # clean
dotnet test tests/features/bio-int-004/Biolog.Features.Webhooks.Tests.csproj            # 103/103 (was 91, +12 new)
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj          # 241/241
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj                        # 210/210
bash scripts/check-module-project-references.sh                                         # PASS
bash scripts/secret-scan.sh                                                             # clean
python3 scripts/check-traceability.py --all                                             # PASS
bash scripts/check-dotnet-vulnerabilities.sh (both projects)                             # PASS
```

Re-ran the full feature-unit suite 3x in a row with no flakiness.
