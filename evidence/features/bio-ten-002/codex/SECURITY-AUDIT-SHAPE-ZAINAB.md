# BIO-OPS-002 / BIO-TEN-002 Audit-Shape Security Review — Zainab

**Verdict:** FAIL  
**Reviewed fix:** `d3c6d95`  
**Builder evidence:** `8d4645b`

## Finding

### ZB-OPS002-01 — Medium — `previous_actor_id` bypasses the non-secret detail allowlist

The replacement event now uses the shared `AuditEvent` envelope and fixes the frozen-envelope fields, classification, device value, and correlation identifier shape. However, the endpoint accepts `previous_actor_id` as any caller-controlled string and writes it directly to `AuditEvent.Detail["previous_actor_id"]` with classification `internal`.

That is not the prescribed allowlisted-value shape. A holder of any valid synthetic demo-session token can persist arbitrary personal data, secret text, or an oversized value in the audit trail. The other previous-context fields are closed allowlists, and the previous role already deterministically maps to one fixed canonical demo person identifier, so this field does not need to remain open.

Reproduction by inspection:

1. Present any valid demo-session token.
2. Submit `previous_tenant_id = "alpha"`, `previous_role_id = "viewer"`, and an arbitrary `previous_actor_id` such as a secret or non-canonical string.
3. The request passes validation and the arbitrary string is enqueued under `Detail` as `internal`.

Required closure:

- Require `previous_actor_id` to equal the fixed `ActorIdByRole[previous_role_id]` value (which also guarantees the canonical `per_` identifier shape), rejecting mismatches before audit construction.
- Add a negative regression for an arbitrary/mismatched actor identifier.
- Add audit-shape assertions against the concrete sink covering the exact detail-key set, `Device = null`, `Classification = "internal"`, and replacement of an invalid client correlation identifier with a fresh canonical `evt_` identifier. The current nine tests assert only HTTP behavior and do not inspect the rewritten audit envelope.

## Confirmed fixes

- Bespoke event and sink types were removed.
- The shared structured actor, target, outcome, reason, causation, device, and detail fields are used.
- `new_tenant_id` comes from the verified session token's configured tenant mapping.
- Invalid correlation identifiers are discarded in code rather than passed through.
- Dependency-injection registration avoids shadowing the existing shared `IAuditSink` registration.
