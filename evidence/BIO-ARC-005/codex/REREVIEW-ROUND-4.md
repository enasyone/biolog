# BIO-ARC-005 independent re-review — round 4

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed fix `0b80d69` at head `8b224df`. QA-03 remains closed. QA-01 and QA-02 remain open at the next caller-controlled boundary.

## Findings

### BIO-ARC-005-QA-01 remains open — High

The `Map` enforces one handle per caller-supplied `registryId`, but `createMigrationRegistry` accepts arbitrary IDs and `assignSequence` accepts every resulting handle. Two handles seeded from the same persisted records under `migration-core` and `migration-core-alias` each become an authority and each allocate sequence `1`. The ADR says production must use exactly `SEQUENCING_CONFLICT_GROUP`, but code does not enforce that rule.

Required closure: the production API must expose exactly one migration registry authority, not a caller-named namespace. Test isolation needs an internal/reset mechanism that cannot create additional production authorities.

### BIO-ARC-005-QA-02 remains open — High

Fusing verification into assignment does not make it authoritative while `applyUp` and `hashState` remain caller-supplied. Passing `applyUp: state => state` causes assignment to “verify” without interpreting or executing the candidate's `up` body at all. With matching trivial hashes, a proposal whose migration body is `THIS MIGRATION BODY IS NEVER EXECUTED` receives a sequence number. This directly disproves the ADR's claim that callers cannot pre-arrange the inputs to lie.

Required closure: assignment must invoke a trusted migration runner/state inspector selected by the contract or authority, not arbitrary functions supplied alongside the assignment request. At minimum, the verifier must prove the candidate migration was actually applied on both paths.

## Closed finding

QA-03 remains closed: mismatched destructive and deprecation targets are refused.

## Verification

- Independent probes: QA-01 FAIL, QA-02 FAIL, fail-closed verifier PASS, QA-03 PASS.
- Foundation baseline: 32/32.
- Full contract baseline: 319/319.

```sh
node evidence/BIO-ARC-005/codex/independent-review.mjs
bash tests/foundation/BIO-ARC-005.test.sh
node --test contracts/js/
```

Functional and concurrency remain FAIL. Security and tenant isolation are not applicable. Offline/recovery PASS and regression PASS.
