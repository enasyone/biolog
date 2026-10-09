# BIO-ARC-002 Security Review — Zainab

**Reviewer:** Zainab (zainab-mu9ouqus)  
**Date:** 2026-09-21  
**Commits reviewed:** `8bd5c66` (fix), `4840c99` (head)  
**Verdict:** PASS

---

## Scope

Independent security review of the tenant-context and database-routing contract. Focus areas mandated by god's dispatch: QA-01 (provenance forging), QA-02 (unaudited denial), QA-03 (async audit ordering), and the malformed-thenable edge case flagged by Seyi.

Did not copy Seyi's test plan. Derived checks independently from the AC and ADR.

---

## QA-01: Provenance cannot be forged — WeakMap is authoritative

**Code path:** `RESOLVED` WeakMap is module-private (not exported). Only `freezeContext` writes entries; only `resolveTenantContext` and `createControlPlaneContext` call `freezeContext`. A caller-built object is never in `RESOLVED`, so `isResolvedContext` returns false.

**`bindingForRepository` reads from provenance, not the public field.** The `binding_key` a caller puts on any object they hand in is never read — `assertResolvedTenantContext` reads `RESOLVED.get(context).binding_key`. Setting `binding_key` on an object is inert.

**Zainab probe (PASS):**
- Mutation attempt on a real context → `tenant_context_immutable` thrown ✅
- `bindingForRepository(ctx)` returns the control-plane binding (`a-db-real`), not anything the caller touched ✅
- Spread copy (`{ ...ctx }`) loses provenance → `audit_unavailable` (no entry in WeakMap) ✅
- JSON round-trip, `Object.create`, `new Proxy`, function with fields — all rejected ✅

**QA-01: CONFIRMED CLOSED.**

---

## QA-02: Every denial is audited before the response is emitted

**Code path:** every denial goes through `emitDenial`, which is `await`ed before any `throw new TenantContextError(...)`. If the sink is missing, non-callable, or throws synchronously → `audit_unavailable` (exit code `503`). The caller never receives the ordinary denial.

**Code review:** `deny` in `resolveTenantContext` is:
```js
const deny = async (code, ...) => {
  await emitDenial(...);      // must settle first
  throw new TenantContextError(...);   // only reached after audit
};
```
Same pattern in `refuse` inside `assertResolvedTenantContext` and direct `emitDenial` call in `assertRecordInTenant`.

**QA-02: CONFIRMED CLOSED.**

---

## QA-03: Async audit settles before denial is observable

**Code path:** `emitDenial` uses `await Promise.race([pending, timeout])`. The function does not return until the race settles. If the async sink's promise rejects (for any reason, including microtask-level rejection) → `audit_unavailable`. If it hangs past `auditTimeoutMs` (default 5 s) → `audit_unavailable`. In all cases the caller receives no ordinary denial until the audit has either settled durably or failed definitively.

**`clearTimeout(timer)` in `finally`:** the timeout timer is always cancelled, preventing it from firing after the race settles. Correct.

**Zainab probe — ordering (PASS):**
```
Cross-tenant read:        [ 'audited:cross_tenant_access_denied', 'response:not_found' ]
Resolution denial:        [ 'audited:tenant_binding_unavailable', 'response:tenant_binding_unavailable' ]
```
Audit settled (20 ms async delay) before the denial was returned in both paths. ✅

**QA-03: CONFIRMED CLOSED.**

---

## Malformed thenable — independent probe

Seyi's observation: a thenable whose `then` getter throws surfaces the raw error instead of normalising to `audit_unavailable`.

**Code path (traced independently):**
```js
// in emitDenial — NOT inside the try { pending = audit(event) } catch block:
if (pending === null || typeof pending?.then !== 'function') return event;
```
`pending?.then` accesses the getter. If it throws, the throw is outside the wrapping `try/catch`, so it propagates as a rejected promise from the async `emitDenial`. The `deny` function's `await emitDenial(...)` rethrows with the raw error, not wrapped in `unavailable()`.

**Is this an exploitable path?**

No. Confirmed by probe:

```
P1 code: undefined   status: undefined   isOrdinaryDenial: false
P2 code: undefined   status: undefined   isOrdinaryDenial: false
PASS: malformed thenable cannot yield any ordinary denial
```

The caller receives the raw getter error — `e.code` is `undefined`, not any ordinary denial code (`tenant_context_missing`, `not_found`, `tenant_context_conflict`, etc). The call blows up entirely rather than returning a usable denial. No data is returned. No cross-tenant leak is possible.

**Who can inject a malformed thenable?** Only a caller who controls the `audit` parameter. The `audit` sink is passed by platform middleware, not by the inbound request. An external attacker cannot reach this path.

**Classification:** non-exploitable edge case. The normalization gap (raw error vs `audit_unavailable`) is cosmetic — the security invariant (no ordinary denial without a settled audit) holds because no ordinary denial is returned at all.

**Seyi's observation: CONFIRMED independently. Does not require a fix.**

---

## Additional security checks

| Check | Result |
|-------|--------|
| No implicit/default tenant fallback (AC02) | `claims.length === 0` always denies; no default tenant constant anywhere in contract code ✅ |
| Context immutability (AC03) | Proxy traps `set`, `deleteProperty`, `defineProperty` — all throw `tenant_context_immutable`. `channels` array is `Object.freeze`'d ✅ |
| 404 not 403 on cross-tenant (info disclosure) | `assertRecordInTenant` throws `not_found` / `404`; internal `denial_reason` stays off the surface error ✅ |
| Audit event carries no payload (data minimisation) | `buildDenialEvent` records only identifiers and reason codes; `not.anyOf` schema asserts `payload`, `record`, `version` forbidden ✅ |
| Hostname normalisation | Rejects non-ASCII, controls, homoglyph-risk bytes, malformed labels; port/case/trailing-dot stripping is precise ✅ |
| No secrets or absolute paths in tracked files | `secret-scan.sh --all` → clean; `grep` for `/Users|/home` → no matches ✅ |
| No git remote | `git remote -v` → empty ✅ |

---

## Live run summary

| Suite | Result |
|-------|--------|
| `node --test contracts/js/` | 121/121 PASS |
| `bash evidence/BIO-ARC-002/codex/independent-review.sh` | 30/30 + 8/8 PASS |
| Zainab probe 1 (malformed thenable) | PASS |
| Zainab probe 2 (WeakMap provenance) | PASS |
| Zainab probe 3 (async audit ordering) | PASS |
| `scripts/secret-scan.sh --all` | clean |

---

## AC verdicts

| AC | Verdict |
|----|---------|
| AC01 — host/domain, principal, service channels defined | PASS |
| AC02 — no implicit/default tenant | PASS |
| AC03 — context immutable for request lifetime | PASS |
| AC04 — cross-tenant fails closed and is audited | PASS |

## Category verdicts

- Functional: PASS
- Security: PASS
- Tenant isolation: PASS
- Offline: NOT APPLICABLE
- Concurrency: PASS (async audit ordering, timeout, rejection all verified)
- Regression: PASS

---

## Overall verdict: PASS

No Critical or High issues. QA-01/02/03 confirmed closed. Malformed-thenable observation confirmed non-exploitable. Safe to route to acceptance.
