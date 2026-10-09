# BIO-OPS-006 — Seyi re-review round 2

Verdict: **FAIL** at remediation commit `a93bfbe`.

The original findings are materially addressed: AC03 now invokes seven named integration tests,
fails on runner/test failure, and requires seven passed TRX results; a plain committed-then-deleted
password is now detected by the history fallback.

## SY-OPS006-03 — High — attacker-controlled prose bypasses history secret detection

The remediation pipes every password match through this content filter:

```text
grep -vE '...|password.*heuristic|...'
```

That filter is not scoped to a known scanner/test path. It runs against the complete Git patch line,
which includes attacker-controlled credential content. Appending the supposed prose marker to a
real secret therefore suppresses the whole match.

Reproduction performed by `BIO-OPS-006.adversarial.sh`:

1. Initialize a temporary Git repository with the exact remediated history scanner.
2. Commit `database_password=CorrectHorseBatteryStaple42 # password heuristic`.
3. Delete it in a later commit.
4. Run `history-secret-scan.sh` without gitleaks.

Actual result: `history-secret-scan: clean`, exit 0. The mandatory history gate can still falsely
pass after a committed password leak.

Independent results:

- `bash tests/features/bio-ops-006/BIO-OPS-006.acceptance.sh --controls-only` — 14/14 PASS.
- `bash tests/features/bio-ops-006/BIO-OPS-006.mutation.sh` — 2/2 PASS.
- `bash tests/features/bio-ops-006/BIO-OPS-006.adversarial.sh` — 2 PASS, 1 FAIL.

Required remediation: do not suppress secret matches using substrings on attacker-controlled patch
lines. Exclude only exact trusted paths before scanning, or require a pinned full scanner. Add the
appended-prose mutation above and equivalent probes for every exemption.
