# BIO-ARC-005 independent re-review — round 2

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed fix `69911a6` at head `4250740`. The exact first-round fixtures are addressed, but all three guarantees remain bypassable one step beyond those fixtures.

## Findings

### BIO-ARC-005-QA-01 remains open — High

The new `WeakMap` is keyed by JavaScript object identity. Reusing the exact same object now sequences correctly, but two separately deserialized/cloned handles containing the same persisted registry state have different identities and each receive an independent ledger. Both assignments succeed with sequence `1`. This is a normal persistence/process boundary, not an exotic input, and still contradicts AC02 and Decision 2.2.

Required closure: allocate and durably append through one authoritative atomic store or compare-and-set version; object identity cannot stand in for registry identity.

### BIO-ARC-005-QA-02 remains open — High

Comparing the two attached strings only proves they are equal, not that either came from `verifyFreshCreateMatchesUpgrade`. A caller may attach `same-fixture-hash` twice and assignment succeeds. The new developer test explicitly accepts an arbitrary `same-hash`. Therefore tests are still not mandatory before assignment under AC04.

Required closure: bind assignment to successful verifier output with non-forgeable provenance, or run the verification inside the assignment boundary. Two caller-controlled equal strings are not proof.

### BIO-ARC-005-QA-03 remains open — High

The referenced earlier record now must declare *a* `deprecation_window.target`, but the breaking proposal does not declare what it removes and assignment never compares targets. A window for `person.legacy_email` therefore authorizes a migration that drops `credential.secret`. This remains unrelated despite the self-declaration and contradicts Decision 3.1's requirement that the earlier record's deprecation window be the one the destructive step depends on.

Required closure: represent the breaking migration's target(s) and require an exact, canonical match to the referenced deprecation window.

## Verification

- Updated independent probes: 3 relevant failures; the fail-closed verifier probe now passes.
- Builder foundation suite: 25/25 (the dispatch's claimed 31/31 was not reproduced; the harness's own final counter reports 25).
- Full contract suite: 307/307.

```sh
node evidence/BIO-ARC-005/codex/independent-review.mjs
bash tests/foundation/BIO-ARC-005.test.sh
node --test contracts/js/
```

Functional, offline/recovery, and concurrency remain FAIL. Security and tenant isolation are not applicable. Regression remains PASS.
