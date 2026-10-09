# BIO-MOB-003 independent performance/resilience review — Femi round 1

Reviewed commit: `a69aadb69bb73381d0460b1159d876e71a16243b`

## Verdict: FAIL

## Blocking finding

### BIO-MOB-003-FEMI-01 — central/offline sync has no executable production path

Severity: High  
Acceptance criteria: AC01 and AC05  
Categories: Functional, Offline, Regression

The isolated `SyncEngine` and fake-transport tests pass, but the live mobile application cannot perform the story's central/offline synchronization:

- `HttpSyncTransport.upload()` unconditionally throws `UnimplementedError` for every mutation.
- No code outside the sync package constructs `SyncEngine` or `HttpSyncTransport`, or calls `SyncEngine.attachTo(...)` from app/session bootstrap.
- Therefore additions, updates, and revocations cannot reach a central service in production, and real reconnects cannot initiate synchronization. The fake transport demonstrates component behavior only; it does not satisfy the approved user story or AC01's delta-sync capability.

The builder evidence explicitly defers both the transport and live wiring to a future story. No such dependency is named in BIO-MOB-003; its only declared dependency is BIO-MOB-002. Deferring the capability means this story is not complete.

## Reproduction

Run from the repository root:

```sh
bash evidence/BIO-MOB-003/codex/independent-review-femi.sh
```

Expected current result: component harness `15 passed, 0 failed`, followed by failures for production transport implementation and live-app wiring; overall exit status `1`.

The attempted focused Flutter suite could not run in this sandbox because Flutter tried to write `/opt/homebrew/share/flutter/bin/cache/engine.stamp.tmp.*` and received `Operation not permitted`. The committed builder transcript reports 93/93 passing; that does not affect this live-path finding.

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 — delta sync additions/updates/revocations | FAIL | Models and fake-driven engine exist, but the only production transport always throws and the engine is not wired into the app. |
| AC02 — server acknowledgements idempotent | PASS (component) | Replay and fresh acknowledgements follow the same guarded transition in `SyncEngine`; structural harness passes. |
| AC03 — critical conflicts enter queue | PASS (component) | Conflict acknowledgements unconditionally call `recordConflict`; conflicted rows remain queryable. |
| AC04 — no last-write-wins for sensitive data | PASS (component) | No merge path exists; conflict payload remains unchanged. |
| AC05 — reconnect storms create no duplicates | FAIL (live capability) | Single-flight and claim guards pass in isolation, but no production reconnect listener is attached, so the acceptance behavior is not delivered by the app. |

## Category verdicts

- Functional: FAIL.
- Security: PASS for the reviewed component behavior.
- Tenant isolation: PASS; persisted mutations retain tenant identity and the engine does not rewrite it.
- Offline: FAIL; no live central synchronization or reconnect path exists.
- Concurrency: PASS for isolated single-flight and database-claim behavior.
- Regression: FAIL because the story's production path is absent despite green fake-based tests.

No source repair was made.
