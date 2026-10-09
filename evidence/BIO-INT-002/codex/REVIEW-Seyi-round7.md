# BIO-INT-002 round-seven review

Verdict: FAIL — round-cap redesign escalation required

Reviewed `8ab0c83` / `ffa6a10`.

- Each durable row-provenance entry now carries the whole-batch fingerprint that produced it.
- The replay path trusts a surviving entry only when its fingerprint matches the current request. A mismatch re-enters normal classification and the existing content-aware orphan-adoption path, so changed content becomes a conflict rather than silently linking the old PERSON.
- A matching fingerprint preserves legitimate resume-after-failure behavior, including idempotent completion of a missing link.
- Direct regressions cover both changed-content and same-content reuse after claim release.
- No separate ledger or shared idempotency-store contract change was introduced.
- Independent structural harness: 72 passed, 0 failed.
- Focused tests compiled; local execution remained blocked by the known VSTest loopback-socket restriction. Builder evidence reports 40/40 feature and 192/192 integration tests.

## Correction after cross-review convergence

Zainab identified a valid F07 in the exact capped recovery chain: `ComputeFingerprint` concatenates unescaped delimiters, so distinct field maps can produce the same canonical string. For example, `{a: "b,c=d"}` and `{a: "b", c: "d"}` both serialize their field segment as `a=b,c=d`. Their hashes match, so released-key changed content can still trust stale provenance and bypass fresh conflict classification.

This invalidates my provisional PASS. Because round seven found another distinct gap in the same chain, the recorded cap requires a from-scratch redesign decision with Kemi rather than another local patch.
