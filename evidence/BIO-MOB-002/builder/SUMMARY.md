# BIO-MOB-002 — Builder evidence

**Story:** Implement offline event queue and idempotency foundation
**Owner:** Busola — Mobile & Edge Engineer
**Worktree:** `biolog-worktrees/BIO-MOB-002`, branch `feature/BIO-MOB-002`
**Dependencies:** BIO-MOB-001 (DONE, commit `bb890397435bbce02057b89da12eb3d17d29db1a` — Flutter Android shell, encrypted local store, offline-mutation model), BIO-ARC-003 (DONE — ADR-0003, audit/domain-event envelopes and the idempotency dedupe model this story's `resolveEnqueue`, from BIO-MOB-001, already implements)
**Conflict group:** `offline-core`
**Reviewed commit:** `14341f53901d30bf2147bb83e5efedefa2785a76`

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — queued records have global IDs, device IDs, version and idempotency keys | Already delivered by BIO-MOB-001's `OfflineMutation` (`mutationId`, `deviceId`, `baseVersion`, `idempotencyKey` — all canonical-id/schema validated in its constructor). This story adds `retryCount`/`nextRetryAt` as **local-only** bookkeeping (not part of the frozen `offline-mutation.schema.json` wire envelope — `toEnvelopeJson()` still emits exactly the original 7 fields) and validates them the same way every other field is validated. | `bio_mob_002_ac01_retry_fields_test.dart` (7 tests: defaults, negative-retryCount/malformed-nextRetryAt rejection, `copyWith` clearing semantics, envelope non-leakage), `foundation-harness-output.txt`. |
| AC02 — states include pending/uploading/acknowledged/synced/conflict/failed | The six-state enum was already delivered by BIO-MOB-001. This story adds the **transition methods** that actually move a mutation between states for the first time: `markUploading` (pending → uploading), `recordUploadFailure` (→ pending-with-backoff or → failed), `recordConflict` (→ conflict). `acknowledged`/`synced` transitions are out of this story's scope (no networking dependency exists yet — see Known gaps). | `bio_mob_002_ac03_retry_queue_test.dart` (`markUploading` test), `foundation-harness-output.txt`. |
| AC03 — retry is exponential and bounded | `mobile/lib/core/offline/retry_policy.dart`: `RetryPolicy` implements AWS's published "Full Jitter" algorithm (`sleep = random(0, min(cap, base·2^attempt))`) — bounded exponential backoff with per-attempt randomised jitter, chosen specifically because it is the documented answer to `OFFLINE-EDGE-ARCHITECTURE.md`'s "reconnect storms must be rate-controlled without losing or duplicating events" (no fixed schedule would satisfy that; see Decisions below for what was checked and found absent). `AppDatabase.recordUploadFailure` increments `retryCount` and either reschedules (`nextRetryAt`, back to `pending`) while attempts remain, or moves to the terminal `failed` state once the bound (`maxAttempts`) is exhausted — never retries forever. `dueForRetry(now)` returns only `pending` mutations whose backoff window has elapsed, so a scheduling caller never busy-polls a mutation that isn't due yet. `offline_mutations` gets an in-place `v1 → v2` schema migration (`onUpgrade`, `ALTER TABLE ADD COLUMN`, not a drop/recreate) for the two new columns — real user data from a BIO-MOB-001 install must survive the upgrade. | `bio_mob_002_ac03_retry_policy_test.dart` (7 tests: bound enforcement, jitter bounds, exponential growth via a fixed-jitter double, cap behaviour, full-jitter can return ~0), `bio_mob_002_ac03_retry_queue_test.dart` (5 tests: immediate first-attempt due-ness, scheduled-retry not due immediately then due after elapsing, bounded exhaustion → `failed`, `retryCount` incrementing), `emulator-schema-migration-boot-screenshot.png`/`emulator-migration-logcat.txt` — **real** upgrade-in-place verification: installed BIO-MOB-001's actual last debug APK (schema v1) on a clean emulator, launched it (creating a real v1 database file), then installed this story's v2 build directly over it (`adb install -r`, preserving app data) and confirmed a clean launch with no `SQLiteException`/`FATAL EXCEPTION`, `Fully drawn`. |
| AC04 — security-sensitive conflicts are never silently overwritten | Per ADR-0001 §3.5 ("Offline sync conflicts"): on a `base_version` mismatch, identity/credential/biometric aggregates always move to `CONFLICT`, no silent last-write-wins; for any other aggregate a deterministic merge is permitted **only** where the owning feature story defines the merge rule in writing — absent one (true of every aggregate today), the default is also `CONFLICT`. `AppDatabase.recordConflict` therefore applies unconditionally: every conflict → `conflict`, uniformly, with no merge logic anywhere. A conflicted mutation is cleared out of the retry cycle (`nextRetryAt` reset to null, excluded from `dueForRetry`) and surfaced via `conflictedMutations()` for the not-yet-built human Sync Resolution Queue — there is deliberately no automatic resolution path in this story. | `bio_mob_002_ac04_conflict_test.dart` (4 tests: conflict removes a mutation from the retry cycle, `conflictedMutations()` surfaces it with its original unmodified payload, a mutation already mid-retry-schedule is cleared on conflict, no resolution path exists), `foundation-harness-output.txt`. |

## CHANGES_REQUIRED round 1 — BIO-MOB-002-QA-01 (fixed)

Seyi's independent review (`evidence/BIO-MOB-002/codex/REVIEW-Seyi-round1.md`, verdict FAIL)
found two real defects, now fixed in this commit:

- **State transitions were not guarded (High).** `markUploading` updated by `mutation_id` alone,
  with no requirement that the current state actually be `pending` — a mutation already resolved
  to the terminal `failed` state, or to human-owned `conflict`, could be resurrected to
  `uploading` by a stale or out-of-order caller. Repro: enqueue, `recordConflict`, then
  `markUploading` — the row became `uploading` and `conflictedMutations` no longer returned it.
  **Fix:** every transition method (`markUploading`, `recordUploadFailure`, `recordConflict`) now
  guards its `UPDATE` with a `WHERE ... AND sync_state = ?` on the expected source state, matching
  the real intended flow (`pending -> uploading -> {pending-with-backoff, failed, conflict}`); a
  mismatched guard is a silent no-op, never a state-machine violation. `FakeAppDatabase` mirrors
  the identical guards.
- **`recordUploadFailure` was an untransactional read-then-write (Concurrency FAIL).** Two
  concurrent failures for the same mutation could both read the same stale `retryCount` and both
  write the same increment, undercounting attempts and weakening AC03's "bounded" guarantee — the
  same class of bug already fixed once in `enqueueMutation` for BIO-MOB-001-F01. **Fix:** wrapped
  the whole read-decide-write sequence in `_requireOpen.transaction(...)`, identical to that
  earlier fix.
- Updated the AC03/AC04 tests that previously called `recordUploadFailure`/`recordConflict`
  directly on a still-`pending` mutation (a shortcut that no longer matches the corrected state
  machine) to drive `markUploading` first, and added two dedicated regressions reproducing Seyi's
  exact repro (`markUploading` after `recordConflict`, and after exhausting retries to `failed`).
- Re-ran the full suite after the fix: `flutter analyze` clean, `flutter test` 75/75 (3 net-new),
  `tests/foundation/BIO-MOB-002.test.sh` 17/17 (2 net-new checks), `scripts/check-traceability.py`
  PASS, `scripts/secret-scan.sh --all` clean — all captured fresh in this evidence directory,
  superseding the round-1 output.

## CHANGES_REQUIRED (acceptance round 1) — BIO-MOB-002-QA-02 (fixed)

Chioma's acceptance review (`evidence/BIO-MOB-002/acceptance/REPORT.md`, verdict FAIL) found one
real defect surviving the QA-01 fix:

- **A mutation stranded in `uploading` by a process restart was never recovered (High,
  Functional/Offline/Regression).** `markUploading` writes a durable row to `uploading`, but if the
  app process ends before the matching `recordUploadFailure`/`recordConflict` call — the normal
  shape of "the device loses power or is killed mid-upload," squarely inside this story's own
  degraded-connectivity remit — the row stayed `uploading` forever. It is excluded from
  `dueForRetry` (which only selects `pending`) and from every other transition method's guard
  (which all expect the state they're leaving, not `uploading` as a starting point after a fresh
  `open()`), so the mutation was permanently unretriable and undetectable short of a manual DB
  inspection. Chioma's repro:
  `evidence/BIO-MOB-002/acceptance/stranded_uploading_restart_probe.dart` — `markUploading`, then a
  fresh `open()` standing in for the restart.
  **Fix:** `SqlCipherAppDatabase.open()` now calls a new `_recoverStrandedUploads()` immediately
  after `openDatabase(...)`, inside the same transaction pattern used elsewhere: it finds every row
  still `uploading` and applies the identical accounting a real failed upload would get, via a
  shared `_applyFailedAttempt(txn, mutation)` helper factored out of `recordUploadFailure` (no
  behavioural duplication between the explicit-failure path and the restart-recovery path). The
  interrupted attempt counts against the bounded-retry budget — `retryCount` increments exactly as
  it would for an explicit failure — and is rescheduled through the same `RetryPolicy` (or moved to
  `failed` if the bound is already exhausted), rather than reset to a fresh `pending` state or given
  an unearned immediate retry. `FakeAppDatabase.open()` mirrors this with the equivalent recovery
  loop, sharing its own `_applyFailedAttempt` with `recordUploadFailure`.
- New regression: `bio_mob_002_ac03_retry_queue_test.dart` — "a mutation stranded in uploading by a
  process restart recovers to retryable pending" — `markUploading`, then `close()`/`open()`
  (simulating the restart), asserts zero mutations remain `uploading`, the mutation is back in
  `pendingMutations()` with `retryCount == 1`, and is `dueForRetry` within a generous window.
  `tests/foundation/BIO-MOB-002.test.sh` gained a matching structural check.
- **Evidence transparency note on Chioma's probe script:** re-running
  `stranded_uploading_restart_probe.dart` against this fix still exits non-zero (captured as-is in
  `qa02-chioma-probe-post-fix-output.txt`), but the probe's own instrumentation shows the fix is
  correct: `uploading_after_restart=0` and `due_after_restart=1` — exactly what the remediation
  asked for ("the record becomes retryable"). The probe's guard is
  `if (uploading != 0 || due.any(...)) throw ...` — i.e. it throws precisely when the mutation *is*
  in `due`, the opposite of its own stated intent and of the remediation text. I verified this is
  the probe's own logic, not a misunderstanding on my part, with a standalone Dart truth-table
  script reproducing both the literal condition (throws on this exact, correct fix) and the negated
  form matching the error message's stated intent (does not throw). I did not edit Chioma's
  acceptance-evidence file — that isn't mine to change — so this is flagged here with the raw output
  kept for the record; the new first-party regression test above asserts the correct (non-inverted)
  condition directly.
- Re-ran the full suite after the fix: `flutter analyze` clean, `flutter test` 76/76 (1 net-new),
  `tests/foundation/BIO-MOB-002.test.sh` 18/18 (1 net-new check), `scripts/check-traceability.py`
  PASS, `scripts/secret-scan.sh --all` clean — all captured fresh in this evidence directory,
  superseding the prior round's output.

## Developer tests (76/76 passing, one Flutter suite — 26 net-new for this story)

- `tests/flutter/test/bio_mob_002_ac01_retry_fields_test.dart` (7) — AC01: `OfflineMutation`'s new local-only fields.
- `tests/flutter/test/bio_mob_002_ac03_retry_policy_test.dart` (6) — AC03: the pure `RetryPolicy` algorithm.
- `tests/flutter/test/bio_mob_002_ac03_retry_queue_test.dart` (9) — AC02/AC03: the queue's retry state machine end to end, via `FakeAppDatabase` (implements the identical `AppDatabase` contract the real `SqlCipherAppDatabase` does), including the round-1 `markUploading`-resurrection/no-op-when-not-uploading regressions and the acceptance-round-1 stranded-uploading-recovers-on-restart regression.
- `tests/flutter/test/bio_mob_002_ac04_conflict_test.dart` (4) — AC04: conflict handling (now driving `markUploading` first, matching the corrected state machine).
- All 50 pre-existing BIO-MOB-001 tests (`bio_mob_001_*`) — unaffected, still passing, confirming this story's changes are additive.

All 76 pass together via `cd tests/flutter && flutter test` (`flutter-test-output.txt`), and `mobile/` analyzes clean (`flutter-analyze-output.txt`). `tests/foundation/BIO-MOB-002.test.sh` (18 offline-safe structural checks, `foundation-harness-output.txt`) is wired into `scripts/quality.sh`'s base tier.

## Real-device verification (beyond what `flutter test` can prove)

The one thing a host-run test genuinely cannot prove is that an **existing on-device database file, created by the previously-accepted BIO-MOB-001 build, survives the upgrade to this build's new schema** — `flutter test` never touches SQLite/SQLCipher's real `onUpgrade` path at all (platform-channel only). So this was verified for real:

1. Installed BIO-MOB-001's actual last accepted debug APK (`biolog-worktrees/BIO-MOB-001/mobile/build/app/outputs/flutter-apk/app-debug.apk`, schema version 1) on a clean Android emulator (API 36, arm64) and launched it — creating a real `offline_mutations` table in the v1 shape (no `retry_count`/`next_retry_at` columns).
2. Force-stopped it, then installed this story's build directly over it (`adb install -r`, which preserves app data — the same mechanism a real user's Play Store update would use) and launched it again.
3. `openDatabase(..., version: 2, onUpgrade: ...)` therefore ran against the real, existing v1 file and executed the two `ALTER TABLE ADD COLUMN` statements for real, not against a synthetic in-memory database.
4. Confirmed via `logcat`: no `SQLiteException`, no `FATAL EXCEPTION`/`AndroidRuntime` crash, `ActivityTaskManager: ... Fully drawn` (`emulator-migration-logcat.txt`), and a clean render of the (unrelated, BIO-MOB-001) tenant-select screen (`emulator-schema-migration-boot-screenshot.png`) proving the app booted all the way through `SessionController.bootstrap()`, which opens the database as its very first step.

## Decisions and assumptions

- **No concrete retry-backoff numbers exist anywhere in the accepted contracts.** Checked ADR-0001 (§3.4/3.5), ADR-0003 (Decision 4, the idempotency dedupe model — informs `resolveEnqueue`, not backoff timing), ADR-0004 (caller-retry/`Idempotency-Key` conventions, a different concern from device-side scheduling), `contracts/js/*.mjs`, `02-ARCHITECTURE/OFFLINE-EDGE-ARCHITECTURE.md` and `EVENT-ARCHITECTURE.md`: none name max attempts, base delay or jitter. The only directive is `OFFLINE-EDGE-ARCHITECTURE.md`'s "reconnect storms must be rate-controlled without losing or duplicating events." `RetryPolicy`'s constants (`baseDelay: 30s`, `maxDelay: 30min`, `maxAttempts: 8`) and algorithm (AWS's named, published "Full Jitter" — chosen over a fixed or additive-jitter schedule specifically because full jitter is the documented answer to "rate-controlled... without... a reconnect storm") are this story's own explicit choice, not a value read from a contract. Flagged here rather than silently implemented, in case a later story supplies an authoritative number.
- **AC04's conflict rule is unconditional, not per-aggregate, because ADR-0001 §3.5's own default is unconditional today.** The ADR distinguishes identity/credential/biometric aggregates (always `CONFLICT`, no exception) from "other" aggregates (a deterministic merge is allowed only if the owning feature story writes the rule down) — but no story anywhere in this codebase has written such a rule yet, so §3.5's own fallback ("absent such a rule, the default is CONFLICT") applies to every aggregate this queue currently handles. `recordConflict` therefore does not branch on aggregate type at all; a future story that defines a real merge rule for a specific aggregate type would add that branch then, not before.
- **`acknowledged`/`synced` have no transition methods yet.** Both states were already modelled by BIO-MOB-001's enum, but nothing in this repository calls a networking layer to actually upload a mutation — BIO-MOB-002's own dependencies are only BIO-MOB-001 and BIO-ARC-003, neither of which is a sync-intake API. Adding transition methods for states nothing can currently reach would be speculative; a future sync-execution story adds them together with the HTTP call that drives them.
- **The human "Sync Resolution Queue" itself (a UI/workflow for a person to act on a `conflict` record) is not part of this story's scope.** `conflictedMutations()` is the data-access seam a future story's UI would consume; building that UI here would be scope creep beyond "offline event queue and idempotency **foundation**."
- **Schema version bumped 1 → 2 via `onUpgrade`, not a fresh `onCreate` rewrite**, specifically because BIO-MOB-001 already shipped and was accepted — a real device could already have a v1 database file with real (synthetic, in this environment, but structurally real) queued mutations in it. `onCreate` was also updated to include the new columns directly, so a genuinely fresh install never needs the upgrade path at all; both paths converge on the identical final table shape.

## Known gaps / deferred to later stories (not blocking)

- Actual network upload/sync execution (calling a server, handling its `409 concurrency_conflict`/`428 precondition_required` responses per ADR-0004, transitioning to `uploading` → `acknowledged`/`synced` for real) is not in this story's dependency scope — a future sync-execution story (mobile-side) plus whatever server-side offline-intake API it depends on.
- The human Sync Resolution Queue UI (letting a person actually resolve a `conflict` record) does not exist yet; this story only provides `conflictedMutations()` for it to be built against.
- Revocation-delta priority ordering (ADR-0001 §3.5: "Revocation deltas ... take a priority path") is not implemented — `dueForRetry`/`pendingMutations` order by `occurred_at ASC` only. No story yet defines what a revocation-delta mutation looks like in this queue, so there is nothing to prioritise against today.
