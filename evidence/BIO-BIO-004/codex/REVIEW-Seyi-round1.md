# BIO-BIO-004 independent review — Seyi

Date: 2026-09-23
Verdict: FAIL

## Finding BIO-BIO-004-F01 — provider acquisition failures are only partially contained (High)

`FingerprintVerificationService.VerifyAsync` catches only `ArgumentException` around
`IFingerprintProvider.ExtractTemplateAsync`. The provider contract is an external adapter seam and
can fail with `InvalidOperationException`, `TimeoutException`, `OperationCanceledException` (when
not caller cancellation), or vendor-specific exceptions. Those failures propagate without a
`FingerprintVerificationRecord`, violating AC03's requirement that extraction failure is recorded
as the distinct `AcquisitionFailed` outcome rather than left to propagate. A provider outage can
therefore turn into an unhandled request and lose the audit/decision record.

### Reproduction

1. Supply a provider implementation whose `ExtractTemplateAsync` throws `InvalidOperationException("device offline")`.
2. Call `VerifyAsync` for a claimed identity with an enrolled reference.
3. The exception escapes and the record store remains empty; no `AcquisitionFailed` record is written.

The service should distinguish caller cancellation from provider failure, contain provider/vendor
failures, and persist the required acquisition-failed outcome (with cancellation semantics documented).

## Checks

- Structural suite: 35 passed, 0 failed.
- Build completed without reported errors.

Category verdicts: Functional FAIL (AC03); Security FAIL (unhandled provider outage); Tenant Isolation PASS; Offline FAIL; Concurrency PASS; Regression FAIL pending mitigation.
