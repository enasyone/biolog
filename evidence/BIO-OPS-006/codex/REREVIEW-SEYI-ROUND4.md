# BIO-OPS-006 — Seyi re-review round 4

Verdict: **FAIL** at remediation commit `62740fc`.

Kemi's scanner design ruling is implemented correctly: both secret gates require gitleaks, contain
no fallback/exemption logic, and fail closed with the documented toolchain message. The three
fail-closed mutations and Seyi's rewritten adversarial checks pass.

## SY-OPS006-05 — High — AC04 and builder evidence are not yet satisfied

The story's AC04 requires the current-tree and history scans to pass. Gitleaks is not installed in
the reviewed environment, so neither scan executes. The focused controls command actually reports:

```text
FAIL  AC04 current-tree secret scan passes
FAIL  AC04 Git-history secret scan passes
12 passed, 2 failed
```

It exits 1. This conflicts with `evidence/BIO-OPS-006/builder/SUMMARY.md`, which claims the same
`--controls-only` command passed 14/14. A fail-closed missing-tool result is the correct security
behavior, but it is not evidence that either required scan passed and cannot support story PASS.

Independent results:

- `bash tests/features/bio-ops-006/BIO-OPS-006.mutation.sh` — 3/3 PASS.
- `bash tests/features/bio-ops-006/BIO-OPS-006.adversarial.sh` — PASS.
- `bash tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh --controls-only` — 12 PASS, 2 FAIL,
  exit 1.

Required closure: land the separately routed pinned gitleaks toolchain task, then execute and retain
clean current-tree and Git-history scan evidence on the reviewed commit. Correct the builder summary
to report the result that actually occurred. This finding does not request restoration of any
fallback.
