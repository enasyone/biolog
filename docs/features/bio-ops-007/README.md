# BIO-OPS-007 — local performance, concurrency and resilience hardening

This story records repeatable laptop-scale evidence. It is deliberately **not a production-scale SLO**
or a capacity claim. All fixtures are synthetic.

## Acceptance mapping

| AC | Controlled proof |
|---|---|
| AC01 | `ConcurrentReplacementHardeningTests` creates two distinct replacements, races their activation, and asserts exactly one activation succeeds and exactly one card remains active. |
| AC02 | `bio_mob_003_ac05_reconnect_storm_test.dart` overlaps two sync passes and asserts each queued mutation uploads once. The BIO-OPS-004 recovery test also coalesces a reconnect arriving during a failed request. |
| AC03 | `latency-probe.py` sends 31 measured HTTP requests after warmup to live `GET /health/ready` and approved `GET /cards/replace?as=admin`, retaining raw values plus median/p95/p99. |
| AC04 | Story-owned tests interrupt a repository create and event-queue append, then assert clean retry, integrity, no loss and no duplicate. The network test drops a request mid-flight and proves queued recovery. |
| AC05 | `RESOURCE-PROFILES.md` gives bounded 16/32/64 GB recommendations. |

## Repeatable commands

```sh
bash tests/features/bio-ops-007/BIO-OPS-007.acceptance.sh
dotnet test tests/features/bio-ops-007/Biolog.Features.PerformanceResilience.Tests.csproj
dotnet test tests/features/bio-mob-003/Biolog.Features.OfflineSync.Tests.csproj --filter OfflineSyncAuditAtomicityTests
flutter test tests/flutter/test/bio_mob_003_ac05_reconnect_storm_test.dart tests/flutter/test/bio_mob_002_ac03_retry_queue_test.dart tests/flutter/test/bio_ops_004_sy_ops004_01_access_cache_reconnect_test.dart
```

The last three commands exercise accepted production components. In this managed sandbox, .NET
restore stalls at dependency resolution and local HTTP listeners fail with `EPERM`; the frozen
source tests and prior unrestricted acceptance evidence remain the executable product proofs.

The story-owned AC01 project builds cleanly locally. Its first unrestricted run exposed a real
two-active-card race; production repair `a2537e9` closes it, and the exact test passed five
consecutive unrestricted reruns.
