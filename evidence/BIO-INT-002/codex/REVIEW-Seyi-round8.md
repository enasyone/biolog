# BIO-INT-002 round-eight review

Verdict: PASS

Reviewed builder `9c00ffc` and evidence `66d5dff` against BIO-INT-002 AC01–AC06, with round-seven F07 as the remediation focus.

## Independent verification

- `ComputeFingerprint` now serializes an explicit object graph containing `mappingId`, sorted rows, each row's external key, and sorted key/value field entries before SHA-256 hashing.
- JSON string escaping and explicit arrays/objects remove the old delimiter ambiguity for external keys, field names, values, nulls, and row boundaries.
- Sorting preserves order-independent fingerprints for row and dictionary enumeration without discarding content.
- Direct regressions cover both known collision shapes: one field embedding another field entry, and one row value embedding another row.
- The end-to-end regression forces a post-provenance link failure, verifies claim release, retries with formerly colliding but different content, and observes fresh conflict classification with no link and no second PERSON.
- Existing same-content replay and ordinary different-content protections remain present.
- The implementation delta is limited to the fingerprint helper, tests, documentation, and structural harness. No recovery-chain ordering or shared-contract change was introduced.

## Results

- Structural harness: 76 passed, 0 failed.
- Focused test assembly: compiled successfully, 0 errors. Existing xUnit analyzer warnings remain.
- Local focused VSTest execution: blocked by sandbox loopback socket denial (`SocketException (13): Permission denied`). Builder evidence reports feature tests 43/43 and integration tests 192/192.
- Functional: PASS.
- Security/data integrity: PASS.
- Tenant isolation: PASS; tenant behavior is unchanged and the fingerprint binds mapping/request content.
- Offline/recovery: PASS; released-attempt formerly colliding reuse is routed through fresh classification.
- Concurrency/idempotency: PASS; no regression found in the existing claim/replay path.
- Regression: PASS subject to the stated local runner limitation.

No blocking findings.
