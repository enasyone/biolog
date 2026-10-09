# BIO-ARC-005 independent re-review — round 3

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed fix `bc5810c` at head `89d97e1`. QA-03 is closed. QA-01 and QA-02 remain open.

## Findings

### BIO-ARC-005-QA-01 remains open — High

`createMigrationRegistry(initialRecords)` is exported and may mint any number of independently branded authoritative handles from the same persisted records. Two handles minted from the same empty persisted state each pass `assignSequence` and each allocate sequence `1`. Branding rejects plain snapshots but does not establish a unique registry lineage or atomic durable compare-and-set.

Required closure: one durable authoritative registry must atomically allocate and append. A public factory must not create multiple independent authorities over the same history.

### BIO-ARC-005-QA-02 remains open — High

The new verifier-shaped check remains completely caller-forgeable. A proposal carrying `matches:true`, three arbitrary 64-character hexadecimal strings, and equal fresh/upgrade hashes is accepted without `verifyFreshCreateMatchesUpgrade` ever running. The ADR explicitly concedes this. That is shape validation, not enforcement of AC04's mandatory tests.

Required closure: use non-forgeable in-module provenance (for example, an opaque successful-proof token/object tracked by the module) or run verification inside the assignment boundary. The feature-agent caller is precisely the party whose unverified assertion must not be trusted.

## Closed finding

QA-03 is closed: `forward_safety.breaks` is compared exactly with the referenced `deprecation_window.target`; the prior cross-target reproduction is refused.

## Verification

- Independent probes: QA-01 FAIL, QA-02 FAIL, fail-closed verifier PASS, QA-03 PASS.
- Foundation baseline: 30/30.
- Full contract baseline: 315/315.

```sh
node evidence/BIO-ARC-005/codex/independent-review.mjs
bash tests/foundation/BIO-ARC-005.test.sh
node --test contracts/js/
```

Functional and concurrency remain FAIL. Offline/recovery passes for the now-closed mismatch and target checks. Security and tenant isolation are not applicable. Regression PASS.
