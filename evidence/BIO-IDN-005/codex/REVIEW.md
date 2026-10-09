# BIO-IDN-005 independent review

**Verdict: FAIL / CHANGES REQUIRED**

Reviewed implementation commit `bcda47d` at builder evidence head `0fa6a2e`.

## BIO-IDN-005-QA-01 — High — AC01 contact-data signal is absent

AC01 separately requires candidate duplicates to arise from identifiers, contact data, and later biometric signals. The implementation provides an institutional-identifier signal and reserves a biometric enum value, but its alleged contact-data signal is `NAME_AND_DATE_OF_BIRTH` over first name, last name, and birth date. Those are identity/demographic attributes, not contact information. No email, phone, address, contact point, or other contact-data field exists in `PersonAttributes`, `DuplicateSignalSource`, or the comparison service.

The feature README explicitly redefines “contact data” to mean name and date of birth because BIO-IDN-001 currently lacks contact fields. A lower-level implementation note cannot rewrite the approved acceptance criterion. If the dependency cannot yet supply contact information, the criterion is blocked or needs an approved scope change; it is not implemented by relabeling different data.

### Reproduction

```sh
evidence/BIO-IDN-005/codex/contact-signal-probe.sh
```

Observed:

```text
FAIL: duplicate detection has no contact-data field or signal; name and date of birth are identity attributes, not contact data
```

Required closure: add a real tenant-scoped contact-data source and signal path that can independently raise/augment a candidate, with tests for positive matching, nonmatching values, cross-tenant exclusion, normalization boundaries, and audit privacy. If contact data is intentionally unavailable in W04, obtain an approved acceptance-criterion change or dependency decision rather than claiming this AC passes.

## Other verification

- Identifier-value matching and the name/date-of-birth signal are implemented.
- Human decisions require actor, reason, optimistic version, and append-only audit before commit.
- No automatic merge surface exists.
- Tenant-scoped gateways and packaged-host coverage support AC04.
- BIO-IDN-005 structural suite: 58/58 PASS, but its “contact-data signal” check matches explanatory comment text rather than a contact-data field or code path.
- Traceability inventory: PASS (19 implemented stories).
- Module project-reference direction: PASS.
- Tracked secret scan: clean.
- Builder evidence records feature tests 49/49, integration tests 75/75, unit tests 160/160, structural tests 58/58, and quality gate PASS.

Functional FAIL; Security PASS; Tenant Isolation PASS; Offline/Recovery NOT APPLICABLE; Concurrency PASS; Regression PASS.
