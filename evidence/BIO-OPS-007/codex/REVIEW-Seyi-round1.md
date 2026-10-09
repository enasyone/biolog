# BIO-OPS-007 independent review — round 1

**Verdict: FAIL** at builder commit `a12a65b`.

## Findings

### SY-OPS007-01 — High — AC03 records throughput proxies, not API/UI latency

`latency-baseline.json` labels the API measurement as the elapsed time of seven TestServer test
cases and explicitly says it includes runner overhead and is “not wire latency.” The UI value is
the static-route generation phase of `pnpm build` and explicitly says it is a build/render baseline;
no request or interaction was timed. Dividing those totals by case/route counts does not turn them
into API or UI latency. The acceptance gate validates only that both sample counts are positive, so
arbitrary positive values pass.

Reproduce:

```sh
python3 evidence/BIO-OPS-007/codex/adversarial-evidence-check.py
```

Required remediation: record repeatable latency for named API requests and named UI interactions or
page requests, including sample distribution (at least count and percentile/median), environment,
and command/raw output. Controlled local measurements are sufficient; production SLO claims are not
required.

### SY-OPS007-02 — High — AC04 does not inject database or queue interruptions

The mapped “database” proof injects an audit-sink failure into the offline-sync service; it does not
interrupt a database operation. The mapped “queue” proof directly invokes `recordUploadFailure` on
an in-memory fake; it does not interrupt a queue operation. Only the network case injects a real
mid-request failure. The story gate executes none of these tests and passes on source-text matches.

Required remediation: add executable, deterministic failure injection at each named boundary:
database operation interruption with recovery/data-integrity assertions, queue claim/ack or enqueue
interruption with retry/no-loss/no-duplicate assertions, and the existing network interruption. Bind
successful exact-commit results to the evidence.

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 concurrent replacement | PASS | Production fix rejects a lower sequence whenever a later live sibling exists; exact story test builds locally. God's unrestricted evidence records five passing executions. Local VSTest aborts before execution because socket bind is denied. |
| AC02 reconnect storm | PASS | Existing overlapping-pass and atomic-claim proofs assert one upload per mutation; recovery test coalesces the follow-up pass. |
| AC03 API/UI latency | FAIL | Values are test-runner duration and build-time route generation, not API/UI latency. |
| AC04 database/queue/network injection | FAIL | Network is injected; database and queue interruptions are not. |
| AC05 laptop profiles | PASS | Concrete bounded 16/32/64 GB profiles are documented. |

## Category verdicts

- Functional: FAIL (AC03, AC04)
- Security: NOT APPLICABLE
- Tenant isolation: NOT APPLICABLE
- Offline/resilience: FAIL (AC04)
- Concurrency: PASS
- Regression: PASS for reviewed card-replacement repair; runtime execution locally sandbox-blocked

## Commands

- `bash tests/features/bio-ops-007/BIO-OPS-007.acceptance.sh` — 13/13, demonstrating the builder gate's text-only false positive.
- `dotnet test ... --filter FullyQualifiedName~AC01_two_distinct` — compilation succeeds; VSTest aborts on sandbox socket-bind denial before execution.
- `python3 evidence/BIO-OPS-007/codex/adversarial-evidence-check.py` — 0 passed, 5 failed.
