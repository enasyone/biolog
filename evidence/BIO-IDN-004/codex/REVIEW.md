# BIO-IDN-004 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation commit `d2c3ede` at builder evidence head `6935c23`.

## BIO-IDN-004-QA-01 — High — suspension can commit without credential/access revocation

AC02 requires emergency suspension to emit credential/access revocation events. `PersonLifecycleService.TransitionAsync` first commits `ACTIVE -> SUSPENDED` through `gateway.ChangeStatusAsync`, then writes the supplementary lifecycle audit, and only then calls the revocation sink. If that sink is unavailable and `ExistsAsync` cannot confirm an event, the request returns `audit_unavailable`, but the PERSON remains `SUSPENDED` with no revocation event. Credentials or access can therefore remain enabled even though the authoritative identity state says emergency suspension completed.

This is not an ambiguous theoretical window: the builder test `When_the_revocation_sink_fails_the_ordinary_lifecycle_event_was_already_recorded` explicitly asserts the split state. The independent probe derives the opposite security invariant from AC02 and reproduces the violation.

### Reproduction

```sh
dotnet run --project evidence/BIO-IDN-004/codex/RevocationAtomicityProbe/RevocationAtomicityProbe.csproj
```

Observed:

```text
FAIL: person is SUSPENDED but no credential/access revocation event exists
```

Required closure: make the suspension state change and guaranteed revocation work atomic from the caller/consumer perspective. A durable transactional outbox with the status mutation, or an explicitly modeled non-final state plus mandatory idempotent reconciliation, can satisfy this. Merely reversing the two independent writes would create the inverse false-revocation failure if the status commit then fails.

## Other verification

- BIO-IDN-004 structural suite: 45/45 PASS, but it checks that publication is called, not the failure invariant.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- The feature test assembly compiled independently. The local test runner then aborted because the sandbox denied its loopback socket, so no independent xUnit execution is claimed.
- Builder evidence records API build clean, feature tests 65/65, integration tests 70/70, unit tests 160/160, structural tests 45/45, and quality gate PASS.

Functional FAIL; Security FAIL; Tenant Isolation PASS; Offline/Recovery FAIL; Concurrency PASS; Regression PASS.
