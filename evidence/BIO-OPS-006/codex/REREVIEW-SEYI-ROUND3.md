# BIO-OPS-006 — Seyi re-review round 3

Verdict: **FAIL** at remediation commit `53d55bb`.

SY-OPS006-03's attacker-controlled line suppression is fixed, and all nine builder mutations pass.
However, the replacement exact-path allowlist contains deployable production source.

## SY-OPS006-04 — High — production source paths are omitted from history scanning

`history-secret-scan.sh` excludes these complete paths before constructing the Git patch stream:

- `mobile/lib/core/db/sqlcipher_app_database.dart`
- `api/Biolog.Api/Features/DeviceRegistry/CredentialReferenceValidator.cs`

These are production code, not scanner definitions or inert fixtures. Any credential ever committed
to either file is invisible to every fallback pattern. The exclusion also remains in force for
future commits, so a later leak is silently accepted without another review.

Independent reproduction:

1. Initialize a temporary repository with the exact remediated scanner.
2. Commit `database_password=CorrectHorseBatteryStaple42` at the excluded mobile production path.
3. Delete the file in a later commit.
4. Run the fallback.

Actual result: `history-secret-scan: clean`, exit 0.

Independent adversarial result: 3 PASS, 1 FAIL.

Required remediation: never exclude deployable source, configuration or data paths from secret
history scanning. Move inert examples out of production paths or narrow patterns without hiding the
entire file. Add mutations at every excluded path and require production paths to be detected.
