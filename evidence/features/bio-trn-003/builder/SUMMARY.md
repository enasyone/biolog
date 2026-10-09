# BIO-TRN-003 — Builder evidence

**Story:** Implement parent/system transport notifications
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-TRN-003`, branch `feature/BIO-TRN-003` off `develop`
**Dependencies:** BIO-TRN-002 (real board/alight/missing-alight-exception source data this story
reads), BIO-RPT-001 (named in the approved story; see `docs/features/bio-trn-003/README.md`'s own
"Why BIO-RPT-001's own dispatch pipeline is not reused" for why this story builds its own domain
instead of extending that one directly)
**Commit:** see `metadata.json` (first pass `cf932ec`, SY-TRN003-01 fix `15184ef`)

## Remediation: SY-TRN003-01 (Seyi, High — lost-update race on concurrent rule edits)

`InMemoryTransportNotificationRuleRepository.UpdateAsync` was a blind dictionary assignment: the
version check in `SetEnabledAsync` and the write that followed it were not one atomic step, so two
callers racing from the SAME stale read could both pass the check and both write, the second
silently clobbering the first. Fixed by replacing `UpdateAsync` with
`TryReplaceAsync(expectedCurrent, updated)`, backed by `ConcurrentDictionary.TryUpdate`'s own
atomic compare-and-swap — the loser of a genuine race now throws `RuleConcurrencyConflictException`
with the real current version instead of being silently overwritten. Full detail in
`metadata.json` and `docs/features/bio-trn-003/README.md`'s own "SY-TRN003-01 fix" section.

Seyi's own reviewer-provided `SeyiRuleConcurrencyProbe.cs` was adapted (not replaced) to the new
interface shape, preserving her exact read-gating coordination strategy. A second,
independently-authored barrier-gated test proves the same invariant from scratch. Also flagged to
god as an FYI: BIO-INT-004's own already-accepted `InMemoryWebhookSubscriptionRepository.UpdateAsync`
has the identical blind-assignment shape — not fixed here, out of this story's own owned paths.

## First builder pass

## Summary

First builder pass. Full detail in `metadata.json` and `docs/features/bio-trn-003/README.md`.

- **AC01** — `TransportNotificationRule` selects a non-empty set of board/alight/missed-
  alight/exception event types; RBAC-gated create/list/toggle.
- **AC02** — `TransportNotificationPayloadRedactor` produces a structurally-minimized payload
  (no `Location`/`VehicleId` property exists on the delivered type at all).
- **AC03** — bounded-retry dispatch with visible `Delivered`/`DeadLettered` outcomes, atomic
  claim-based dedup.
- **AC04** — the cross-story feed gateway onto bio-trn-002 has no write member at all.
- **AC05** — a new "Transport notifications" panel mounted additively onto `/transport`, proven
  via SIMULATION-mode Playwright (6 tests) and a real end-to-end `.NET` integration test
  (`BioTrn003EndpointsTests.cs`) that captures a genuine board event through bio-trn-002's own
  existing demo bridge and shows it delivered through this story's own real backend.

Also disclosed explicitly (not silently assumed): external parent/guardian (SMS/email/push)
delivery is out of scope — no guardian-contact model exists anywhere in this codebase (`Person`
carries no phone/email at all). This story builds the real, buildable half (an in-app notification
feed) with the channel abstraction left open for a future contact-channel story to extend.

Verified (first pass): `dotnet build` clean. 248/248 integration (244 pre-existing + 4 new, no
regression; one `ObservabilityTests` flake on an earlier run, the known pre-existing
`Console.Out`-redirect race, confirmed non-reproducing). 210/210 unit, no regression. 19/19 new
feature-unit tests. Module-reference/secret-scan/traceability all PASS. `npx tsc --noEmit` clean.
`npx vitest run` 546/548 (the 2 failures are the pre-existing, already-tracked
`TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP` for `IntegrationService`, unrelated — this story's own
new `TransportService` ports are correctly fixture-covered via an update to
`bio-ux-010-module-ports.test.ts`). `npx playwright test bio-trn-003-transport-notifications`:
6/6 passed, re-run twice more to confirm stability (two earlier attempts timed out under heavy
concurrent host load from other sessions' processes — a cold Turbopack first compile, not a code
defect; see the README's own Verification section for the pre-warmed-server confirmation).

Verified (SY-TRN003-01 remediation, backend-only — web layer untouched, not re-run): `dotnet build`
clean. 248/248 integration, no regression. 210/210 unit, no regression. 21/21 feature-unit tests
(19 original + Seyi's own adapted probe + 1 new concurrency test), re-run 3x to confirm the two
concurrency tests are not flaky — all three runs clean. Module-reference/secret-scan/traceability
all PASS.
