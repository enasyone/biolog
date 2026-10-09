# BIO-ARC-002 Independent Review

Date: 2026-09-21  
Reviewed implementation commit: `7b135f2`  
Reviewed branch head: `52ade1b`  
Verdict: **FAIL**

## Blocking findings

### BIO-ARC-002-QA-01 — a forged context can select an arbitrary database binding

Severity: Critical  
File: `contracts/js/tenant-context.mjs`

`assertResolvedTenantContext()` claims to reject hand-made contexts, but it accepts any object with `kind: "tenant"`, a canonical tenant identifier, and a non-empty `binding_key`. Consequently:

```js
bindingForRepository({
  kind: "tenant",
  tenant_id: tenantA,
  binding_key: "caller-chosen-tenant-b-db",
})
```

returns `caller-chosen-tenant-b-db`. The object did not come from `resolveTenantContext()` and its binding was never looked up in the control plane. This directly violates the rules that repositories require a resolved context and callers can never name the database binding. A context needs unforgeable provenance, and regression tests must include a structurally valid forged object.

### BIO-ARC-002-QA-02 — audited denial is optional and absent on required paths

Severity: High  
File: `contracts/js/tenant-context.mjs`

`emitDenial()` returns without writing anything when `audit` is missing. Both resolution failures and cross-tenant record denials then throw their normal response errors, including a caller-visible `404 not_found`, with no audit event. The audit dependency is therefore optional even though ADR-0002 requires every denial to be written before response.

Control-plane misuse is also impossible to audit through the public API: `bindingForRepository()` and `assertResolvedTenantContext()` accept no audit dependency. Passing an audit option as an extra argument is ignored; `control_plane_context_misuse` is thrown with zero events.

The denial interface must fail closed when durable audit cannot be performed, and every named audited denial path must receive the required sink. Tests must cover missing/unavailable audit, not only a supplied in-memory callback.

## Acceptance criteria

| Criterion | Verdict |
|---|---|
| AC01 resolution and routing rules | FAIL — forged context bypasses resolution/binding lookup |
| AC02 no default tenant | PASS |
| AC03 immutable request context | PASS for legitimately resolved contexts |
| AC04 cross-tenant denial and audit | FAIL |

## Regression summary

- Builder foundation suite: 22/22 passed.
- Contract behavior suite: 101/101 passed.
- Independent channel-conflict/unanimity probe passed.
- Hostname homoglyph rejection, 404 behavior, null-context rejection, and no-default behavior passed in the supplied suite.
- The supplied tests cover malformed forged contexts but omit a structurally valid forged context and denial without an audit sink.

## Category verdicts

- Functional: FAIL.
- Security: FAIL.
- Tenant isolation: FAIL.
- Offline/recovery: FAIL — background/service routing uses the same forgeable context contract.
- Concurrency: PASS.
- Regression: FAIL.

Reproduction:

```sh
bash evidence/BIO-ARC-002/codex/independent-review.sh
```

Expected result: the forged-binding and mandatory-audit probes fail; the script exits `1`. No implementation files were changed.
