# BIO-OPS-007 builder evidence

## Result

Local controlled hardening evidence is complete with synthetic data. This story adds no production
code, shared contract, migration, route, or UI change.

- AC01 adds a two-distinct-replacement activation race: exactly one succeeds and one card is active.
- AC02 binds overlapping reconnect passes to one upload per mutation.
- AC03 records 31 genuine loopback HTTP samples for named live API and approved UI endpoints,
  including raw values and median/p95/p99, in `latency-baseline.json`.
- AC04 executes repository-write, queue-append and network interruption/recovery proofs.
- AC05 documents bounded 16/32/64 GB laptop profiles.

The first unrestricted execution of the new AC01 test correctly found that two distinct
replacement activations could leave two active cards. Ngozi repaired the owning production service
at branch commit `a2537e9`. God then ran the exact test unrestricted five consecutive times; all five
runs passed. See `unrestricted-ac01-result.md`.

## Local results

- `pnpm install --offline --frozen-lockfile`: PASS.
- `pnpm build`: PASS; compile 3.1 s, 34 static routes generated in 364 ms.
- Local listener request probe: unavailable; Next correctly reported sandbox `listen EPERM`.
- Story-owned .NET project build: PASS with 0 warnings and 0 errors.
- Story-owned AC01 runtime test: PASS five consecutive times in god's unrestricted runner. The same
  command in Femi's managed sandbox compiles successfully, then VSTest aborts before execution on
  its known socket-bind denial.
- Remediation runtime: story-owned .NET suite **3/3 PASS**; selected Flutter reconnect/retry/network
  suite **15/15 PASS**.
- Live loopback latency, 31 samples after 5 warmups: API median/p95/p99
  0.880/1.380/1.825 ms; approved Replace Card page 1.627/2.466/2.502 ms.
- `bash tests/features/bio-ops-007/BIO-OPS-007.acceptance.sh`: see `acceptance-output.txt`.

The latency numbers are single-client loopback baselines for comparison on this laptop only, not
production service-level objectives or capacity claims.
