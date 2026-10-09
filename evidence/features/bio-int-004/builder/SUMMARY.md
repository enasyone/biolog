# BIO-INT-004 — Builder evidence

**Story:** Implement outbound webhooks
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-INT-004`, branch `feature/BIO-INT-004` off `develop`
**Dependencies:** BIO-ARC-003 (the event-envelope ADR this story's own wire payload mirrors),
BIO-INT-003 (this story's own ActorResolver/ActorProvenanceGate/IPermissionGateway precedent)
**Commit:** see `metadata.json` (original builder `ad5f854`, round 1 fix `911394e`, round 2 fix `01ec2af`, round 3 fix `ce59642`, round 4 fix `2ff9d1d`, acceptance-rejection fix `94c42be`, SY-INT004-04 fix `8f5fc00`)

## Remediation (post-acceptance re-review): SY-INT004-04 (Seyi, Medium — failure-path test proved only a client-side guard, never a real failed API call)

Seyi's re-review of the acceptance-rejection fix found the new Playwright suite's only
"failure" test asserted the create button's own client-side disabled state at zero event types
— it never invoked a genuinely failed create/toggle call or asserted a visible error/failure
message, so a silently swallowed error or an unhandled API 500 would have passed unchanged.

Fixed two ways. `WebhookSubscriptionsPanel.tsx`'s own `notice` state now carries an explicit
`isError` flag and renders `role="alert"` only when it is one, mirroring the same pattern this
panel's own `error`/`offline` load-failure states already use. A new Playwright test clears the
URL field (leaving an event type checked, so the client-side zero-event-types guard does not
intercept it) to genuinely reach `createWebhookSubscriptionMockPort`'s own pre-existing "A URL is
required (SIMULATION)" validation failure — the same realistic, already-present mock condition
`web/features/bio-crd-008/mock.ts` established as this codebase's convention, never an invented
magic-string trigger — then asserts `role="alert"`, the expected message, and that no row or
secret appeared. Full detail in `metadata.json` and `docs/features/bio-int-004/README.md`'s own
"AC05 — web wiring" section.

Verified: `npx tsc --noEmit` clean. 244/244 integration (one `ObservabilityTests` flake on the
first run, the known pre-existing `Console.Out`-redirect race against concurrent
`WebApplicationFactory` hosts, non-reproducing on immediate re-run). 210/210 unit, no regression.
Module-reference/secret-scan/traceability all PASS. `npx vitest run` 546/548 (the 2 failures are
the pre-existing, already-tracked `TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP`, unrelated).
`npx playwright test bio-int-004-webhooks`: 6/6 passed (the new failure-path test plus the
existing 5), re-run once more to confirm stability (both runs clean). Web-only change — no `.NET`
source files touched, so `dotnet build`/the two `.NET` test suites were re-run only to confirm no
regression, not because anything in them changed.

## Remediation (acceptance rejection): BIO-INT-004-QA-01 (Chioma, High — AC05 had no executable browser proof)

Chioma rejected acceptance: all six backend/security rounds confirmed closed, but AC05's own
evidence cited only `tsc` and manual code reading — no Playwright workflow existed, and
`/settings/authentication` was neither wired nor covered by a documented scope decision. Full
detail in `metadata.json` and `docs/features/bio-int-004/README.md`'s own "AC05 — web wiring"
section; condensed here:

- Added `tests/frontend/playwright/bio-int-004-webhooks.spec.ts` (SIMULATION mode, default
  suite) — proves the panel mounts correctly, the empty state, the full create workflow
  (including the one-time secret reveal), the create-button-disables-with-zero-selections guard,
  and both toggle directions, each with its own visible status transition.
- Added `data-testid` attributes throughout `WebhookSubscriptionsPanel.tsx` to make the proof
  reliable.
- Documented the `/settings/authentication` boundary explicitly — this story's own ports are
  never routed there, the identical disclosed boundary BIO-INT-003's own README already drew.

## Remediation (round 4): ZB-INT004-07/SY-INT004-03 + ZB-INT004-08 (Zainab+Seyi, Medium+High — concurrent audit-repair race + IPv6 not a true positive policy)

Both reviewers independently caught the first finding. Full detail in `metadata.json` and
`docs/features/bio-int-004/README.md`'s own "Round 4" section; condensed here:

- **ZB-INT004-07/SY-INT004-03**: round 3's own repair path (`ListForTenantAsync` then
  `RecordAsync`) was itself a check-then-write race — the same shape as the original send-side
  dedup race round 1 already fixed, now one layer up in the fix meant to close a different race.
  Fixed with `IWebhookDeliveryAuditSink.TryRecordOnceAsync`, an atomic create-if-absent keyed by
  a record's full structural identity — the same `GetOrAdd`-plus-`ReferenceEquals` pattern
  `IIdempotencyStore.Claim` already established. `CompleteTerminalAsync` and the dedup branch now
  share one `EnsureTerminalRecordAsync` method, retrying the atomic write twice before a recheck
  (preserving round 3's own fail-once self-healing property).
- **ZB-INT004-08**: the IPv6 policy was still a deny-list, never a true positive allow-list —
  round 3's table let `4000::1`, `6000::1`, `8000::1`, `a000::1` and `c000::1` all through despite
  being reserved. Fixed with a genuine positive gate requiring IPv6 membership in `2000::/3`
  (IANA's own currently-assigned global-unicast allocation), checked after the named deny-list so
  specific special-purpose ranges still get precise rejection reasons.

## Remediation (round 3): ZB-INT004-05/SY-INT004-01 + ZB-INT004-06/SY-INT004-02 (Zainab+Seyi, Medium+High — audit-trail convergence + remaining IPv6 gaps)

Both reviewers independently found the same two issues. Full detail in `metadata.json` and
`docs/features/bio-int-004/README.md`'s own "Round 3" section; condensed here:

- **ZB-INT004-05/SY-INT004-01**: round 2's audit recheck only ever distinguished "missing" from
  "already there" — it never actually retried the write, so a duplicate dispatch could write a
  bare `Deduplicated` record with no terminal record underneath it, forever. Fixed by storing a
  full `TerminalDeliveryResult` on the completed claim and sharing one `RepairTerminalRecordAsync`
  helper between the first-failure path and the dedup path — a single failure now self-heals
  within the same call; a repair failure still converges on a later duplicate dispatch.
- **ZB-INT004-06/SY-INT004-02**: the "complete" IANA IPv6 table was still missing
  `64:ff9b:1::/48` (local-use NAT64 — flagged as a live SSRF bypass, not just a gap),
  `100:0:0:1::/64`, `3fff::/20`, and `5f00::/16`. Did a full re-pass against the current registry
  (not just the four named items) and added
  `SpecialPurposeRangesLastVerifiedAgainstIanaRegistry` to disclose this is a checked-in snapshot
  with a known staleness date, not a self-proclaimed permanently-complete table.

## Remediation (round 2): ZB-INT004-03/04 (Zainab, High x2 — claim-release-on-audit-failure + destination policy gaps)

Full detail in `metadata.json` and `docs/features/bio-int-004/README.md`'s own "Round 2" section;
condensed here:

- **ZB-INT004-03**: round 1's `CancellationToken.None` fix only stopped caller cancellation from
  suppressing the terminal audit write — a genuine audit failure after a successful send still
  released the idempotency claim, letting a later dispatch redeliver the same event. Fixed by
  calling `IIdempotencyStore.Complete` BEFORE the terminal audit write, so idempotency safety no
  longer depends on that write's outcome; a recheck-on-throw (ADR-0003 Decision 7's own
  discipline) decides whether to surface a new `WebhookAuditUnavailableException`.
  `InMemoryIdempotencyStore.Release` is now a no-op against an already-`Completed` record,
  defense-in-depth.
- **ZB-INT004-04**: (1) plain HTTP was still accepted — fixed with a purpose-built
  `Webhooks:AllowInsecureHttp` config flag (default false), deliberately not an
  `IsDevelopment()` check (same lesson `BIOLOG_ALLOW_DEMO_SESSION_ISSUANCE` already taught this
  session). (2) the destination policy was a hand-enumerated blocklist missing real IANA ranges —
  rebuilt as the complete IANA IPv4/IPv6 Special-Purpose Address Registry, a positive "global
  unicast by construction" policy rather than a guessed subset.
- New regression test proves the sender is called exactly once across two dispatches despite a
  genuine audit failure after a successful send; new range/boundary/HTTPS tests.

## Remediation (round 1): ZB-INT004-01/02 (Zainab, High x2 — SSRF + idempotency-claim race)

Full detail in `metadata.json` and `docs/features/bio-int-004/README.md`'s own "Round 1" section;
condensed here:

- **ZB-INT004-01 (SSRF)**: subscription creation accepted any absolute HTTP(S) URL with no
  destination policy. Fixed with `WebhookDestinationPolicy` enforced at CONNECT time via
  `WebhookConnectCallback` (`SocketsHttpHandler.ConnectCallback`, wired into `HttpWebhookSender`)
  — covers every redirect hop and re-resolves DNS on every connection attempt, closing both the
  initial-URL gap and the DNS-rebinding/redirect bypass her review named explicitly. A
  `WebhookDestinationBlockedException` is classified `PermanentFailure`, never retried.
- **ZB-INT004-02 (idempotency-claim race)**: dedup was check-then-send-then-record with no
  atomic claim — the same shape as this session's BIO-INT-002/BIO-MOB-003/005/BIO-ACC-003/
  BIO-CRD-005/006/007 findings. Ported that established `IIdempotencyStore`/
  `IdempotencyClaim.IsNewClaim` fix verbatim, plus her explicit ask that every terminal audit
  write use `CancellationToken.None`, not the caller's own token.
- Test proof built to the mature `ClaimBarrierIdempotencyStore` + mutant-discrimination standard
  from the start (not the round-1-then-round-2 path BIO-CRD-005/006/007 took), per her own
  explicit request.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — tenant-scoped, filtered subscriptions | `WebhookSubscription`, `IWebhookSubscriptionRepository`/`InMemoryWebhookSubscriptionRepository`, `WebhookSubscriptionService` (permission-gated create/list/enable-disable) | `WebhookSubscriptionServiceTests.cs` (10 tests), `WebhookEndpointsTests.cs` (6 tests) |
| AC02 — versioned payloads | `WebhookEventEnvelope` (independent copy of the ADR-0003 event-envelope schema), `WebhookPayloadDto` (explicit `[JsonPropertyName]` snake_case) | `WebhookDispatchServiceTests.cs`'s own wire-shape test |
| AC03 — signed, replay-protected deliveries | `WebhookSigner` (`t={unixMs},v1={hex-hmac}`, timestamp bound into the signed message) | `WebhookSignerTests.cs` (5 tests, including an openssl-`dgst -sha256 -hmac`-cross-checked golden vector) |
| AC04 — auditable retry/dead-letter | `WebhookDispatchService.AttemptWithRetryAsync` (`MaxAttempts = 3`), `IWebhookDeliveryAuditSink`/`InMemoryWebhookDeliveryAuditSink` | `WebhookDispatchServiceTests.cs` (7 tests: delivered, permanent-failure-no-retry, transient-retry-then-dead-letter, transient-recovers, dedup, tenant+event-type matching, wire shape) |
| AC05 — `/administration/integrations` wired via `IntegrationService` | New additive `WebhookSubscriptionsPanel.tsx`, mounted alongside BIO-INT-002's `ImportConsole` and BIO-INT-003's `ServiceAccountsPanel`; three new independently mode-switchable port keys | `npx tsc --noEmit` clean; manual code read against the live BIO-INT-003 precedent it mirrors |

## Design decisions worth flagging

- **A research fork initially reported "no domain-event bus exists anywhere."** Verified
  directly before trusting that conclusion (same discipline TD-WEB-PROVENANCE-BRIDGE's own
  Host-rewrite verification used earlier this session) and found it was wrong:
  `IDomainEventBus`/`InMemoryDomainEventBus`
  (`api/features/bio-rpt-001/.../Domain/IDomainEventBus.cs`) is a real, already-built,
  already-registered in-process pub/sub bus, already used by BIO-RPT-001's own demo
  `/primary-action` endpoint. Its own doc comment explicitly names this exact situation: "A
  future story that publishes real events depends on `IDomainEventBus`... with no change
  needed here." Corrected the design before writing code — `BioInt004Module.cs` subscribes to
  the SAME real bus BIO-RPT-001's own demo event already fires through, not a second synthetic
  copy. The first REAL event publisher (a future story) triggers real webhook delivery with no
  change needed here; today's demo event already proves the full pipeline end to end.
- **Envelope translation is an explicit switch, never a numeric enum cast.**
  `TranslateAggregateType`/`TranslateClassification` in `BioInt004Module.cs` map each enum
  member by name rather than casting through the underlying `int` — the two `AggregateType`
  enums (`Biolog.Features.Notifications.AggregateType` and `Biolog.Features.Webhooks.
  AggregateType`) are independently declared, and nothing stops either one's member order
  drifting from the other; a numeric cast would silently mistranslate instead of failing to
  compile.
- **A webhook signing secret is OUR OWN key material, not hashed** — the opposite role
  BIO-INT-003's own `ServiceAccount.SecretHash` plays, and explicitly documented as such in
  `WebhookSubscription`'s own header: a service-account secret is a credential the CALLER
  presents to US (we only ever need to verify a hash of it); a webhook signing secret is
  something WE compute `hex(HMACSHA256(secret, body))` with on every outbound delivery — the
  same shared-secret role every `*ActorProvenanceSecret`/`WebSessionBridge:TokenSecret` config
  value already has in this codebase.
- **`IWebhookSubscriptionRepository` is public, not internal, from the start** — unlike
  BIO-INT-003's own `IServiceAccountRepository`, which started internal and needed a later
  disclosed test-only escape hatch. A deliberate choice here so `WebhookEndpointsTests.cs` can
  substitute it directly if a future round needs to, without an `InternalsVisibleTo` wall.
- **`admin` maps to `TENANT_ADMIN`, not `SUPER_ADMIN`**, wherever this story needed a worked
  privileged-role example (unit tests use a `FakePermissionGateway` that grants everything by
  default, so this choice is only visible in the docs/comments, not exercised by a real grant
  lookup here) — `SuperAdmin` bypasses the real `RolePermissionGrant` lookup entirely via its
  own short-circuit, which would prove nothing about the grant-lookup path a permission check
  exists to exercise.

## Disclosed test limitation (same shape as BIO-INT-003's own)

This project has no `InternalsVisibleTo` access to seed a real BIO-IDN-006 `RolePermissionGrant`
from any test project. `WebhookEndpointsTests.cs` therefore proves every mutating/listing
endpoint reaches the REAL permission check and is rejected for lack of authority (never a bare
HTTP success, never a privilege escalation) plus the two request-shape validations
(`invalid_url`/`unknown_event_type`) that run before the permission check in the create handler.
The AC01/AC02/AC03/AC04 positive paths (a management action actually succeeding) are fully
proven at the unit level via `WebhookSubscriptionServiceTests.cs`/`WebhookDispatchServiceTests.cs`,
each using a permission gateway/sender test double that grants/responds deterministically.

## Verification (acceptance-rejection fix)

- `dotnet build api/Biolog.Api/Biolog.Api.csproj` — clean, 0 warnings, 0 errors (no backend logic
  changed this round; rebuilt after merging `develop` to confirm the trivial module-list
  conflicts resolved cleanly).
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` — 244/244
  (was 241, +3 from the `develop` merge, no regression).
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` — 210/210, no regression.
- `npx tsc --noEmit` (web) — clean.
- `npx vitest run` (tests/frontend) — 546/548 (the 2 failures are the pre-existing, already-
  tracked `TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP`, unrelated).
- `npx playwright test bio-int-004-webhooks` — 5/5 passed, re-run twice to confirm stability.
- `bash scripts/check-module-project-references.sh` — PASS.
- `python3 scripts/check-traceability.py --all` — PASS (36 implemented stories).
- `bash scripts/secret-scan.sh` — clean.
- `bash scripts/check-dotnet-vulnerabilities.sh` (both new projects) — PASS, no vulnerable
  packages.
- `npx tsc --noEmit` (web, after `pnpm install` — `node_modules` was not yet installed in this
  worktree) — clean.
- `npx vitest run` (tests/frontend, after `pnpm install`) — 540/542 (the 2 failures are the
  pre-existing, already-tracked `TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP` — this story's new
  `IntegrationService` port keys were added to `MODULE_PORT_KEYS` the same way BIO-INT-003's own
  four keys already were, consistent with that existing precedent, not a new inconsistency;
  `web/lib/module-registry.ts` is owned by that separate TD, not touched here).

**Sandbox note**: the orchestrator sandbox's own `dotnet build`/`dotnet test` for the full
`Biolog.Api` host graph intermittently reports `Build FAILED` with `0 Warning(s)`/`0 Error(s)`
and exit code 0 — a known environment artifact (`TD-SANDBOX-FULLHOST-BUILD-HANG`), not a real
compile failure. Every command above was re-run with the sandbox disabled to get a trustworthy
result; all of them succeeded cleanly in 10-40 seconds once unblocked.
