# BIO-PHO-003 security review — Zainab round 5

**Verdict: FAIL**  
**Reviewed head:** `02c6c9c`  
**Remediation:** `d730efa`

## Closed

The new lookup no longer trusts an audit candidate whose claimed PhotoMaster version does not
exist. The focused regression correctly covers a failed first commit followed directly by this
same attempt's successful retry.

## Blocking finding

### ZB-PHO003-04 — Medium — version existence does not bind the version to the attempt

`FindPromotedVersionAsync` matches the attempt ID only in the pre-commit audit, extracts its
claimed version N, then treats the candidate as confirmed whenever `GetVersionAsync(person, N)`
returns any master. `PhotoMaster` itself carries no attempt/correlation ID.

Interleaving:

1. Approval attempt A writes its `photo.master.append` audit claiming version N.
2. A's repository append fails, so A created no master.
3. An unrelated workflow B appends next and legitimately commits version N under another
   correlation ID.
4. A retries. Its stale audit candidate still claims N; the repository now contains B's N, so
   A falsely adopts B's master, writes an approval audit, and finalizes the capture without ever
   performing its own promotion.

The new test omits step 3, so it cannot distinguish “my claimed version now exists” from “my
attempt created that version.” This reintroduces the operation-identity problem under a different
signal.

**Required remediation:** bind the stable attempt ID atomically to the committed PhotoMaster or
to an idempotency record committed with it, and query that binding directly. Existence by version
alone is insufficient. Add the exact failed-A/unrelated-B-same-version/retry-A interleaving and
prove A neither claims B's master nor creates duplicates.

## Verification

- Inspected the complete `3090e78..02c6c9c` production and test delta.
- Traced the audit candidate set, claimed version extraction and repository confirmation.
- Confirmed `PhotoMaster` has no correlation/attempt field, so repository lookup cannot validate
  candidate ownership.
- Builder evidence records 41/41 feature and 250/250 integration tests, but no intervening
  unrelated commit at the failed candidate's claimed version.
