# BIO-ARC-006 independent re-review — round 3

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed acceptance remediation `8139b7e` at head `674aca2`.

The reported destructive-target risk is closed: failure of `mktemp -d` now exits before cleanup or writes can target an unintended path. However, the tamper probe still has a false-success path.

## Finding

### BIO-ARC-006-QA-04 — High — Tamper probe passes when clone/setup fails

After creating the temporary directory, the test does not check `git clone`, the contract mutation, or the manifest rewrite. Because the final command is `! check-contract-freeze.py`, any earlier setup failure that leaves an incomplete clone makes that checker fail, and negation turns the result into test success.

Reproduction places a `git` shim first on `PATH` that fails only `git clone` while delegating every other Git command. The full BIO-ARC-006 foundation suite still exits zero and reports the AC04 tamper check as PASS even though no cloned contract was mutated or checked. This is a false proof on the exact immutable-freeze boundary AC04 depends on.

Required closure: every fixture setup/mutation step must be chained fail-closed (`|| exit 1` or `set -e` with deliberate handling), and the test must prove the clone, target mutation and manifest rewrite occurred before expecting the checker rejection.

## Prior findings

QA-01, QA-02 and QA-03 remain closed under the independent Git-clone probes.

## Reproduction

```sh
python3 evidence/BIO-ARC-006/codex/independent-review.py
bash tests/foundation/BIO-ARC-006.test.sh
node --test contracts/js/
```

Baseline foundation 17/17 and contracts 322/322 pass normally. Functional and offline/recovery FAIL due to QA-04. Security PASS, tenant isolation and concurrency not applicable. Regression PASS.
