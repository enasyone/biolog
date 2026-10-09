# BIO-ARC-005 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation `fd63e82` at head `d4dcda0` against BIO-ARC-005 AC01–AC04.

## Findings

### BIO-ARC-005-QA-01 — High — Sequence assignment is still raceable

`assignSequence` accepts the caller-controlled string `lockHeldBy: 'Kemi'`, reads an immutable registry snapshot, and returns a new registry. Two assignments using the same valid snapshot both succeed with sequence `1`. The supplied race test avoids the race by passing the first call's updated registry to the second call. The orchestration lock limits active stories, but it does not make two assignment calls inside the holder atomic. This contradicts Decision 2.2 and AC02.

Required closure: sequence allocation and durable append must be one atomic operation against authoritative registry state; a stale snapshot must not be able to win twice. The lock assertion must be backed by non-forgeable/current lock state or an equivalent compare-and-set boundary.

### BIO-ARC-005-QA-02 — High — Mandatory migration proofs are unverified strings

`proposeMigration` and `assignSequence` accept any two non-empty proof hashes, including unequal fabricated values. `verifyFreshCreateMatchesUpgrade` returns `matches: false` but does not fail, and assignment is not connected to its result. Therefore a migration with a failed or never-run upgrade proof is eligible for numbering, contrary to Decision 4.1/4.2 and AC04.

Required closure: assignment must consume authenticated verifier output or independently require a successful equality result; unequal or unverified proofs must fail closed.

### BIO-ARC-005-QA-03 — High — Any earlier record satisfies the breaking-change deprecation rule

For a breaking proposal, `assignSequence` checks only that some record has the referenced sequence. Records carry no declaration identifying a deprecation window or the shape being deprecated. An unrelated additive migration at sequence 1 therefore authorizes a destructive migration at sequence 2. This contradicts Decision 3.1 and AC03's documented forward-safe rule.

Required closure: represent the deprecation contract in the record and verify that a breaking migration references a real, relevant earlier deprecation record/window.

## Category verdicts

| Category | Verdict |
|---|---|
| Functional | FAIL — QA-02 and QA-03 |
| Security | NOT APPLICABLE |
| Tenant isolation | NOT APPLICABLE |
| Offline/recovery | FAIL — failed upgrade proof can be assigned |
| Concurrency | FAIL — QA-01 |
| Regression | PASS — builder foundation and contract suites remain green |

## Reproduction

```sh
node evidence/BIO-ARC-005/codex/independent-review.mjs
bash tests/foundation/BIO-ARC-005.test.sh
node --test contracts/js/
```

The independent script must exit non-zero with all four probes reported as `FAIL` on the reviewed implementation.
