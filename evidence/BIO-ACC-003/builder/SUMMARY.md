# BIO-ACC-003 — Builder evidence

**Story**: Implement offline deny-list and cached access decisions
**Owner**: Busola — Mobile & Edge Engineer
**Commit**: `66188f748a956717de77cb0ee1b77638372abe15`
**Data classification**: SYNTHETIC only

## Scope decisions made before coding (recorded here for the record)

1. **AC02/AC03 scope: identity-level revocation only.** The obvious server-side deny-list
   source (BIO-CRD-006's `GET /api/v1/card-lifecycle/deny-list`) is not a declared dependency of
   this story and its endpoint has no device/actor authentication of its own. god's ruling
   (2026-09-24, `conv-bio-acc-003-deny-list-source`): implement against BIO-ACC-001's identity-
   status pattern instead (a real, legitimate transitive dependency via PersonRegistry), and
   document card-level revocation as explicitly out of scope pending a declared BIO-CRD-006
   dependency in a future story/amendment. BIO-CRD-006's endpoint having no device auth was
   logged as its own follow-up, not something this story fixes.
2. **`ICrossTenantPersonRevocationLookup` added to PersonRegistry (bio-idn-001).** A device-
   authenticated request has no ambient tenant channel, so `PersonService`'s ambient-tenant-only
   surface (including BIO-ACC-001's own `IIdentityStatusGateway`) cannot be called from this
   story's revocation-delta endpoint. Approved by god (2026-09-24), mirroring the identical
   `ICrossTenantDeviceLookup` precedent Kemi ruled on for BIO-MOB-003/DeviceRegistry — narrow,
   `internal`, constructed via a manual factory lambda, never exposed on `PersonService`'s own
   public path.
3. **AC06**: `accessViewAccessEventsPort` (the "View access events" secondary action) is wired
   live against a dedicated demo-bridge route (`/api/v1/access/view-access-events`), mirroring
   BIO-ATT-001's/BIO-VIS-001's own `*DemoBridgeService` pattern exactly — the generic
   `createLiveAdapter` contract sends no headers, so a tenant/actor-gated route would be
   permanently unreachable from it. `accessPort` ("create policy") stays SIMULATION/documented:
   no story has built a live backend for it (BIO-ACC-002 does not exist yet).

## AC-to-evidence table

| AC | Requirement | Evidence |
|----|-------------|----------|
| AC01 | Edge cache is minimum necessary data | `RevocationEntry` (person id + revoked-at only); server-side `RevocationEntry` DTO maps down from `Person` at the gateway boundary. `tests/flutter/test/bio_acc_003_ac01_ac02_revocation_cache_test.dart`; `tests/foundation/BIO-ACC-003.test.sh` |
| AC02 | Revocation deltas have priority | Additive, monotonic local cache merge (`INSERT OR REPLACE`, no removal path — matches the one-way `REVOKED` transition); server delta feed is an inclusive, cursor-based, tenant-scoped read. `tests/flutter/test/bio_acc_003_ac01_ac02_revocation_cache_test.dart`; `tests/features/bio-acc-003/AccessCacheEndpointsIntegrationTests.cs` (`Revocation_delta_returns_entries_registered_for_this_devices_own_tenant_only`) |
| AC03 | Old card presentation logs locally and denies once revocation is cached | `AccessCacheService.checkPresentation` — no network call, always logs a `PresentationEvent` locally regardless of outcome. `tests/flutter/test/bio_acc_003_ac03_ac04_offline_decision_test.dart` |
| AC04 | Stale-cache policy is explicit | `stale_cache_policy.dart` — a documented 24h threshold (this story's own judgment call, no governing ADR/spec value exists, same position BIO-MOB-002's retry constants were in); staleness is recorded as an explicit, durable per-decision flag and never changes the allow/deny outcome. `tests/flutter/test/bio_acc_003_ac03_ac04_offline_decision_test.dart` (`staleness never changes the allow/deny outcome`) |
| AC05 | Reconnect sync uploads events exactly once | `AccessCacheSyncClient` uses each event's own id as the `Idempotency-Key`; server-side `AccessCacheService.IntakePresentationEventAsync` claims/completes an idempotency record before appending, rejecting a fingerprint mismatch on key reuse. `tests/flutter/test/bio_acc_003_ac05_sync_client_test.dart`; `tests/features/bio-acc-003/AccessCacheEndpointsIntegrationTests.cs` (`Replaying_the_identical_event_returns_the_original_result_not_a_duplicate`, `Reusing_the_same_event_id_with_different_content_is_rejected_not_recorded_twice`) |
| AC06 | AccessService connected live / documented HYBRID | `web/features/access/live.ts`'s pre-existing `accessViewAccessEventsLivePort` now has a real endpoint (`POST /api/v1/access/view-access-events`) plus the matching `web/next.config.mjs` rewrite; `accessPort` documented SIMULATION. `access-page.tsx` untouched — no redesign. `tests/features/bio-acc-003/AccessCacheEndpointsIntegrationTests.cs` (`View_access_events_bridge_*`) |

## Test results (real command output, this commit)

- `dotnet test tests/features/bio-acc-003/Biolog.Features.AccessCache.Tests.csproj`: **13/13 passed** — see `dotnet-test-access-cache.txt`.
- `dotnet test tests/features/bio-idn-001/Biolog.Features.PersonRegistry.Tests.csproj` (regression check on the `ICrossTenantPersonRevocationLookup` addition): **70/70 passed** — see `dotnet-test-person-registry-regression.txt`.
- `flutter test` (full suite, `tests/flutter/`): **119/119 passed**, no regressions — see `flutter-test-full-suite.txt`.
- `dotnet build api/Biolog.Api/Biolog.Api.csproj` (full 40-project host, including the new `BioAcc003Module`): succeeded, 0 warnings, 0 errors.
- `bash tests/foundation/BIO-ACC-003.test.sh`: **20/20 passed** — see `foundation-harness-output.txt`.
- `bash scripts/check-module-project-references.sh`: PASS (no feature project references `Biolog.Api.csproj`).
- `bash scripts/secret-scan.sh --all`: clean.
- `python3 scripts/check-traceability.py governance/stories/BIO-ACC-003.json`: run after this commit + evidence directory were both in place (see below).
- Full-repo `bash scripts/quality.sh --full` (every implemented story's own harness/tests plus frontend/Playwright/Flutter): kicked off against this exact commit; see `quality-full-run-tail.txt` for its outcome as captured before this evidence was finalized — a whole-repository run against ~50 implemented stories takes a long time end to end, so this file captures its state at evidence-capture time rather than blocking indefinitely on it, on top of (not instead of) the full, real, targeted verification above.

## Environment note

`tests/frontend/node_modules` was not installed in this worktree before this story (`pnpm install` had never been run here) — a pre-existing gap unrelated to this story's own changes (nothing here touches `web/package.json` or any frontend dependency). Ran `pnpm install` in `tests/frontend/` (805ms, lockfile already up to date) so `scripts/quality.sh --full`'s frontend/Playwright steps could run for real rather than failing on a missing toolchain.

## Deferred/out-of-scope, disclosed explicitly

- **Card-level revocation** (BIO-CRD-006's own deny-list) — out of scope pending a declared
  dependency; see scope decision 1 above.
- **Device provisioning** for this feature's own `AccessCache:DeviceCredentials` namespace has
  no production caller, mirroring BIO-MOB-003's own identical, already-accepted
  `DeviceCredentialStore.writeSecret` gap (device enrolment/pairing is a future story's concern).
- **No mobile UI screen** was added for the offline deny/allow check itself — `AccessCacheService`
  is a headless service other stories (a future edge-verification story) call; AC06 only mandates
  the web surface, and no mobile UI story exists yet for this capability (mirrors how BIO-MOB-003
  shipped sync plumbing with the `verify`/`scan` screens still placeholders).
