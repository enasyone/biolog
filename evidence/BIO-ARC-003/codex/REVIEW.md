# BIO-ARC-003 independent adversarial review

- Reviewer: Seyi
- Implementation commit: `8622cd8`
- Reviewed head: `1a4d617`
- Verdict: **FAIL**

## Findings

### BIO-ARC-003-QA-01 — High — shallow allowlisting leaks nested and unregistered data

`redact` classifies and allowlists only the top-level property. If an allowed property is
an object, every nested property is copied unchanged. A field added later beneath that
object, including an unclassified secret, therefore enters the audit event. This violates
ADR-0003 Decision 3: new fields must fail closed and secret values must never be copied.

An unregistered classification also does not fail closed. `classificationRank` ranks it
like `secret`, but `redact` still copies its value and emits the unregistered string as the
event classification. That value is rejected by the published audit schema.

Reproduction:

```sh
bash evidence/BIO-ARC-003/codex/independent-review.sh
```

Failing probes:

- `AC03 a nested unclassified secret cannot cross an allowlist boundary`
- `AC03 unknown classification cannot produce an invalid envelope level`

### BIO-ARC-003-QA-02 — High — audit events are mutable through retained nested references

`buildAuditEvent` freezes the envelope and the top-level `detail`, but retains references
to allowed nested objects. Mutating the source object after construction changes the
already-built audit event and can inject new fields without updating its classification.
An asynchronous sink can therefore persist content different from the content approved at
construction. This violates the append-only audit decision and undermines audit integrity.

Failing probe: `AC03 an audit event cannot change after it is built`.

### BIO-ARC-003-QA-03 — High — schemas permit omission of AC-required fields

The audit schema defines `device` but does not require it, although AC01 requires the
device/workstation slot and ADR-0003 defines it as an object or `null`. The domain-event
schema defines `causation_id` but does not require it, although AC02 explicitly requires
correlation/causation. A document missing either field is accepted by the corresponding
contract schema, so independent producers can omit required envelope data.

Failing probes:

- `AC01 audit schema requires the device/workstation slot`
- `AC02 domain event schema requires causation`

## Verified behavior

- `aggregate_version` is required and a bare event `version` remains forbidden.
- The ARC-002 denial event uses the unified shape; tenant pairs, no-payload behavior,
  audit-before-response ordering, and missing/rejected/timed-out sink failures remain
  covered and green.
- Low-entropy personal values are refused as correlatable references.
- Reused `(tenant_id, event_id)` with changed content produces `CONFLICT` and
  `event_id_reuse`; `received_at` alone does not change the fingerprint.
- Builder acceptance and regression harness: 24/24. Contract suite: 156/156.

## Category verdicts

| Category | Verdict | Reason |
|---|---|---|
| Functional | FAIL | Required device and causation fields are optional in published schemas. |
| Security | FAIL | Nested and unregistered values can bypass the redaction boundary. |
| Tenant isolation | PASS | ARC-002 tenant-pair and fail-closed guarantees remain green. |
| Offline | PASS | Replay fingerprint behavior handles redelivery and ID reuse as specified. |
| Concurrency | FAIL | Nested source mutation can alter an event while an asynchronous sink persists it. |
| Regression | PASS | ARC-001, ARC-002, and the full contract suite pass. |
