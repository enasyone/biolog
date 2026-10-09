# BIO-ARC-006 — acceptance-fail remediation evidence

- **Fix commit:** `8139b7eb705261d79145d8e4f8713089dfc6818c`
- **Finding:** tamper-rejection test could target the real worktree if `mktemp -d` failed.

## Closure

The probe now stops immediately when `mktemp -d` fails, verifies the returned path is
non-empty and is a directory before installing cleanup or writing files, and writes a real
newline to the cloned manifest. A restricted-`TMPDIR` replay exits non-zero while the real
v1.0.0 manifest remains byte-for-byte unchanged and valid JSON.

## Verification

- Restricted `TMPDIR` replay — expected non-zero; manifest SHA-256 unchanged
- `bash tests/foundation/BIO-ARC-006.test.sh` — 17/17 PASS
- Embedded contract behavior suite — 322/322 PASS
- Embedded tracked secret scan — clean
- `bash scripts/quality.sh` — QUALITY: PASS

Seyi's reviewer-owned evidence remains unchanged.
