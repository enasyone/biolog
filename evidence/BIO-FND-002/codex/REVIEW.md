# BIO-FND-002 Independent Review — Seyi

Date: 2026-09-20  
Reviewed commit: `29e57a1`  
Verdict: **FAIL**

## Acceptance criteria

| Criterion | Verdict | Evidence |
|---|---|---|
| AC01 | PASS | `toolchain/versions.json` covers .NET LTS, Node LTS, pnpm, Flutter, Docker Engine/Compose, and MySQL client. |
| AC02 | FAIL | Exact versions exist, but the version checker does not enforce consistency between the stated single source of truth and the repository-managed native pin files. |
| AC03 | FAIL | Missing web, mobile, and API lock files are rejected, but native toolchain pins can be upgraded or downgraded inside a feature change without detection. |
| AC04 | FAIL | Manifest drift and malformed/partial manifests fail, but conflicting native pins pass and the policy's explicitly unsupported MySQL 9.x is accepted. |

## Findings

### BIO-FND-002-QA-01 — native pin drift is not checked

Severity: High  
Files: `scripts/check-toolchain.sh`, `.nvmrc`, `global.json`, `package.json`

The policy calls `toolchain/versions.json` the single source of truth and says native pin files must stay synchronized. The checker reads installed binaries and `versions.json`, but never reads the native pins. In an isolated clone with every installed tool simulated at the manifest version, each of these mutations still produced `TOOLCHAIN CHECK: SUCCESS`:

- `.nvmrc`: `20.20.2` → `99.0.0`;
- `global.json` SDK: `10.0.201` → `9.9.999`;
- `package.json`: pnpm `9.12.0` → `8.0.0` and Node engine `20.20.2` → `18.0.0`.

This allows agents to resolve different toolchains while the mandated check passes, violating AC02–AC04 and the no-feature-story-upgrade policy.

### BIO-FND-002-QA-02 — unsupported MySQL major version passes

Severity: Medium  
File: `scripts/check-toolchain.sh`

The committed policy states a different MySQL major such as 9.x is unsupported. `check_min` performs an unrestricted numeric greater-than-or-equal comparison, so a simulated `mysql 9.0.0` passes against the `8.4.0` policy.

## Passing probes

- Fully matching synthetic toolchain: success.
- Tampered `toolchain/versions.json` pin: failure.
- Missing web, mobile, and API lock files: failure.
- Malformed and partial `versions.json`: failure.
- Real machine: pnpm and MySQL client reported `NOT INSTALLED` as expected; checker exited non-zero.
- Builder regression suite: 11/11 passed, but it does not exercise the two bypasses above.

## Category verdicts

- Functional: FAIL.
- Security: NOT APPLICABLE.
- Tenant isolation: NOT APPLICABLE.
- Offline/recovery: PASS; checks are local and malformed policy fails closed.
- Concurrency: NOT APPLICABLE.
- Regression: FAIL.

Reproduction:

```sh
bash evidence/BIO-FND-002/codex/independent-review.sh
```

Expected review exit status: `1` until both findings are fixed. No implementation files were changed during review.
