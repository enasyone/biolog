# BIO-DEV-001 remediation re-review — round 4

**Verdict: PASS**

Reviewed acceptance remediation `a0a764b` at evidence head `60c201c`.

## QA-04 closure

The plaintext-credential finding is closed at both boundaries Chioma identified:

- `DeviceService.RegisterAsync` validates before calling the repository.
- `InMemoryDeviceRepository.CreateAsync` validates independently, so bypassing the service cannot store plaintext.
- Only `vault://`, `kv://`, `kms://`, and `secretref://` references matching the constrained opaque-reference grammar are accepted. Raw plaintext, empty/whitespace values, control characters, embedded-userinfo URLs, arbitrary schemes, and spaces are rejected.
- The composed endpoint maps `InvalidCredentialReferenceException` to HTTP 400 without echoing the supplied value.

The original acceptance probe now terminates with `InvalidCredentialReferenceException` at the repository boundary before creating a device. Compiled unit tests pass 100/100, including service, repository-bypass, supported-reference, and malformed-reference cases. Structural checks pass 47/47.

The compiled integration runner was independently started but stalled after test discovery in this sandbox. Builder evidence records 23/23 integration tests passing, including the composed-host plaintext rejection and valid-reference positive path; no independent execution claim is made for that runner.

## Decision 7 regression

Prior QA-01 through QA-03 remain closed. The added `ExistsAsync` path is accepted only under ADR-0003 v1.1.4's narrow current-tier wording: it is a useful ambiguity check, not a general proof of atomicity for independently durable stores. The registered audit sink and repository remain singleton, same-process, and volatile; durability/remoting still reopens the outbox/transaction obligation.

## Acceptance criteria and categories

- AC01 required device fields: PASS.
- AC02 credential references only: PASS.
- AC03 audited status changes: PASS within the explicit in-memory/single-process boundary.
- AC04 tenant isolation: PASS under the accepted W03/BIO-TEN-002 follow-up boundary.
- Functional: PASS.
- Security: PASS.
- Tenant isolation: PASS.
- Offline: NOT APPLICABLE.
- Concurrency: PASS for the accepted singleton, same-process tier.
- Regression: PASS.
