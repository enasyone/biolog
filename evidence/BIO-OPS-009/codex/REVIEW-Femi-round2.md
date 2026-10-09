# BIO-OPS-009 — Femi performance/resilience re-review round 2

**Target implementation:** `2e34975`  
**Evidence:** `6ca0e00`  
**Verdict:** FAIL

`FEMI-OPS009-02` is closed. The result now records per-store manifest timestamps and calculated RPO
durations, with exact lost data retained as impact evidence. Three live runs report MySQL RPO values
of 11–13 seconds, Azurite RPO of 1 second, and RTO of 31–32 seconds, all explicitly local/non-SLO.

`FEMI-OPS009-01` is improved but not fully closed. An ordinary nonzero archive command now reaches the
restart path and the live injected archive failure proves that case. However, the original finding also
covered interruption. `backup-azurite.sh` still installs no `EXIT`, `INT`, or `TERM` trap after stopping
the source container. If the backup shell receives SIGTERM, or is otherwise terminated between
`docker stop` and `docker start`, it exits without attempting the restart. The new test only causes
`mkdir`/archive to return nonzero; it does not interrupt the backup process.

## High — FEMI-OPS009-01 remains open for interrupted backups

Required remediation: after determining the container was running and before stopping it, install a
finally-style exit/signal handler that attempts restart and confirms readiness. Disarm it only after
the normal restart succeeds. Add a live failure-injection test that terminates the backup process after
the stop and proves the source container becomes healthy again.

Independent checks:

- Updated structural/regression suite: 16/16 PASS; three Docker tests skipped in the managed sandbox.
- Seyi adversarial checker: 5/5 PASS.
- Orchestrator unrestricted suite: 19/19 PASS, including ordinary archive-failure restart.
- Three round-two rehearsals: verification PASS; RTO 31/32/32 seconds.

AC04 now passes. Interruption resilience remains a High blocker.
