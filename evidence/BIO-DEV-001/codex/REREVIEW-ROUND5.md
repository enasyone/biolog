# BIO-DEV-001 remediation re-review — round 5

**Verdict: FAIL**

Reviewed QA-05 remediation `5821e2d` at evidence head `dc54385`.

## Closed portion

The cancellation-atomicity probe is repaired and passes with the supported `secretref://` fixture. QA-04 plaintext rejection remains closed.

## QA-05 remains open — secret scan still fails (High)

The remediation reworded the original validator comment, but added the same contiguous credential-bearing URL literal to its new retrospective section in `evidence/BIO-DEV-001/builder/SUMMARY.md`. The exact required command still exits nonzero:

```text
$ bash scripts/secret-scan.sh --all
323:   (a scheme, then a username, a colon, a password, and finally @host/..., written with
secret-scan: potential secret in evidence/BIO-DEV-001/builder/SUMMARY.md
```

The committed metadata claims this command passed at `5821e2d`; it is not reproducible at the delivered head.

## QA-06 — vulnerability audit fails open on advisory-source failure plus clean phrase (High)

The refreshed builder transcript records `NU1900: Error occurred while getting package vulnerability data` for all three projects, then records `DOTNET VULNERABILITY AUDIT: PASS`. Direct reproduction against the API project exits 0 with the same warning.

`scripts/check-dotnet-vulnerabilities.sh` rejects an advisory-source failure only when the ordinary “has no vulnerable packages” phrase is absent. Real `dotnet list` output contains both the `NU1900` failure and that phrase, so the wrapper accepts an audit that did not reliably obtain advisory data. This contradicts the script's fail-closed contract and makes the story's mandatory security evidence unreliable.

Required closure: reject `NU1900`/advisory-source failure output even when a clean phrase is also present, add that combined-output regression to the shared wrapper's tests, and rerun the audit successfully with advisory data available. Because the wrapper is shared BIO-FND-008 infrastructure, route its code change through the owning workflow rather than silently editing it in this feature review.

## Categories

- Functional: PASS.
- Security: FAIL.
- Tenant isolation: PASS.
- Offline: NOT APPLICABLE.
- Concurrency: PASS for the accepted singleton, same-process tier.
- Regression: FAIL.
