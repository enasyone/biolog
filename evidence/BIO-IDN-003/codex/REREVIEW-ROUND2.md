# BIO-IDN-003 remediation re-review — round 2

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed remediation commit `82248f0` at builder evidence head `460adf5`.

## Prior finding status

The concurrent two-person assignment half of QA-01 is closed: `AssignAsync` now takes the shared `(TenantId, Type, CustomLabel)` policy-scope lock outside the per-person lock. `IdentifierUniquenessRuleService.SetRuleAsync` uses that same lock registry, so in-flight assignment and rule writes are serialized.

## BIO-IDN-003-QA-01 remains High — uniqueness can be enabled over violating data

The round-1 required closure also required enabling enforcement to reject or reconcile existing duplicates. `SetRuleAsync` still never inspects institutional identifier data. A tenant can disable uniqueness, assign the same value to two different people, then enable uniqueness. The call succeeds, records `Enforced = true`, and writes an `ALLOWED / uniqueness_rule_set` audit while both duplicate values remain.

The result is a configured rule whose protected dataset already violates it. Serialization prevents a new race during the transition but does not validate the state being transitioned.

### Reproduction

```sh
dotnet run --project evidence/BIO-IDN-003/codex/RuleEnableProbe/RuleEnableProbe.csproj
```

Observed:

```text
REPRODUCED: uniqueness enabled while pre-existing duplicate values remain
```

Required closure: while holding the shared policy-scope lock, `SetRuleAsync(enforced: true)` must atomically inspect the complete `(TenantId, Type, CustomLabel)` value population and reject with a stable conflict if duplicates exist, or perform an explicitly specified reconciliation before committing/auditing the enabled rule. Add tests for active and retired duplicate histories and prove a rejected enablement leaves both the previous rule and audit trail unchanged.

## Regression evidence

- BIO-IDN-003 structural suite: 60/60 PASS, but it checks lock sharing only and has no enable-over-duplicates case.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- Builder evidence records API build clean, feature tests 53/53, unit tests 154/154, integration tests 64/64, and the non-full quality gate PASS.

Functional FAIL; Security PASS; Tenant Isolation PASS; Offline/Recovery NOT APPLICABLE; Concurrency PASS for assignment serialization; Regression FAIL.
