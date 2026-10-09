# Round-1 remediation runtime results

God ran the exact commands unrestricted against commit `93bd24a` plus the generated latency output.

- Story-owned .NET project: **3/3 PASS** — replacement concurrency, database repository-create
  interruption/recovery/integrity, and queue append interruption/retry/no-loss/no-duplicate.
- Selected Flutter suite: **15/15 PASS** — reconnect duplicate suppression, retry queue behavior,
  and the injected mid-request network drop/recovery path.
- Real API: `dotnet run`, `http://127.0.0.1:5080/health/ready`.
- Real production UI: `pnpm build && pnpm start -p 3100`,
  `http://127.0.0.1:3100/cards/replace?as=admin`.
- Latency probe: 31 retained samples per endpoint after 5 warmups; validator PASS. API median/p95/p99
  0.880/1.380/1.825 ms; UI median/p95/p99 1.627/2.466/2.502 ms.

Both servers stopped cleanly after measurement. These are single-client local loopback baselines,
not production capacity or service-level objective claims.
