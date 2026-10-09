# BIO-MOB-002 Acceptance Audit — Round 2

**Decision:** PASS

**Reviewed head:** `dbcaa770550a7e54a0f787eb7d6fdda6c2d20dce`

**Remediation:** `14341f53901d30bf2147bb83e5efedefa2785a76`

## Result

`BIO-MOB-002-QA-02` is closed. Database startup now transactionally finds mutations left in `UPLOADING` and routes each through the same bounded failed-attempt accounting used by explicit upload failures. The interrupted attempt increments `retryCount`, becomes scheduled `PENDING`, or becomes terminal `FAILED` when the retry bound is exhausted.

The corrected acceptance restart probe reports `uploading_after_restart=0`, `due_after_restart=1`, exit 0. The original probe had an inverted membership assertion; that auditor-owned assertion is corrected in this round.

## Traceability

| Requirement | Evidence | Verdict |
|---|---|---|
| User story: durable operation through connectivity loss | Corrected restart probe; startup recovery regression; Seyi round three | PASS |
| AC01: identifiers, version, idempotency key | Foundation harness; Flutter tests; builder evidence | PASS |
| AC02: six states and guarded transitions | Foundation harness; QA-01 regressions; Seyi rounds two/three | PASS |
| AC03: exponential, bounded retry including interrupted uploads | Foundation 18/18; restart probe; Flutter 76/76 builder evidence | PASS |
| AC04: conflicts are not silently overwritten | Conflict regressions; Seyi round three | PASS |

## Commands

- `bash tests/foundation/BIO-MOB-002.test.sh` — PASS, 18/18.
- `python3 scripts/check-traceability.py governance/stories/BIO-MOB-002.json` — PASS, 4 ACs.
- `bash scripts/check-module-project-references.sh` — PASS.
- `/opt/homebrew/share/flutter/bin/cache/dart-sdk/bin/dart --packages=tests/flutter/.dart_tool/package_config.json evidence/BIO-MOB-002/acceptance/stranded_uploading_restart_probe.dart` — PASS.

Builder evidence records `flutter analyze` PASS and Flutter tests 76/76 at remediation `14341f5`. Seyi round-three verdict is PASS across Functional, Security, Tenant Isolation, Offline, Concurrency and Regression.

Security remains PASS. No security-ledger update is required.
