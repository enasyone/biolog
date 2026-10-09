# BIO-ARC-002 Independent Re-review — Round 2

Date: 2026-09-21  
Reviewed fix commit: `823bca8`  
Reviewed branch head: `d3edab2`  
Verdict: **FAIL**

## Prior findings

- QA-01: **CLOSED**. Module-private `WeakMap` provenance now distinguishes legitimately resolved contexts from forged, copied, proxied, or reconstructed objects. Repository routing reads the authoritative binding from that provenance rather than caller-visible fields.
- QA-02 original reproductions: **CLOSED**. Missing, non-callable, or synchronously failing audit sinks no longer allow a normal denial response. Control-plane misuse now accepts and writes through the audit dependency.

## New blocking finding

### BIO-ARC-002-QA-03 — asynchronous audit is not awaited before denial response

Severity: High  
File: `contracts/js/tenant-context.mjs`

`emitDenial()` calls `audit(event)` but ignores its return value. When the sink is asynchronous, the access function throws the ordinary caller response immediately while the audit promise is still pending. A controlled sink that persists after one microtask produced this order:

1. `assertRecordInTenant()` returned `404 not_found`.
2. At return time, the event was not persisted.
3. The audit completed only afterward.

If that promise later rejects, the caller has already received the denial and the write failure cannot replace it with `audit_unavailable`. This violates the explicit audit-written-before-response contract and is the asynchronous form of QA-02.

The interface must either await an asynchronous durable sink through an asynchronous denial path or explicitly require and enforce a synchronous durable sink. Regression tests must cover promise-returning success and rejection, not only synchronous callbacks.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 resolution and routing rules | PASS |
| AC02 no default tenant | PASS |
| AC03 immutable request context | PASS |
| AC04 cross-tenant denial and audit | FAIL |

## Regression summary

- Builder foundation suite: 26/26 passed.
- Contract behavior suite: 113/113 passed.
- Original independent forged-context, missing-audit, control-plane audit, and unanimity probes passed.
- The new asynchronous persistence-order probe fails.

## Category verdicts

- Functional: FAIL.
- Security: FAIL.
- Tenant isolation: PASS for denial/routing; required audit durability fails.
- Offline/recovery: FAIL.
- Concurrency: FAIL — asynchronous audit completion races the response.
- Regression: FAIL.

Reproduction:

```sh
bash evidence/BIO-ARC-002/codex/independent-review.sh
```

Expected result: all original checks pass, the asynchronous audit ordering check fails, and the script exits `1`. No implementation files were changed.
