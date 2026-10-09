#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

failed=0
check() {
  local name="$1"
  shift
  if "$@"; then printf 'PASS %s\n' "$name"; else printf 'FAIL %s\n' "$name"; failed=$((failed + 1)); fi
}

check "builder acceptance and regression suite" bash tests/foundation/BIO-ARC-004.test.sh

check "AC01 public HTTP prefix is /api/v1" node --input-type=module -e '
  import { API_PATH_PREFIX } from "./contracts/js/index.mjs";
  if (API_PATH_PREFIX !== "/api/v1") process.exit(1);
'

check "AC02 existing contract errors serialize with one correlation id" node --input-type=module -e '
  import { TenantContextError, ConcurrencyError, AuditError, toProblemResponse, newId } from "./contracts/js/index.mjs";
  const id = newId("event");
  for (const ErrorType of [TenantContextError, ConcurrencyError, AuditError]) {
    const body = toProblemResponse(new ErrorType("denied", "test_code", 409, { safe: true }), { correlationId: id });
    if (body.code !== "test_code" || body.status !== 409 || body.correlation_id !== id) process.exit(1);
  }
'

check "AC02 problem builder cannot emit a schema-invalid code or status" node --input-type=module -e '
  import { toProblemResponse } from "./contracts/js/index.mjs";
  for (const error of [
    { code: "Bad Code", status: 400, message: "x", detail: {} },
    { code: "bad", status: NaN, message: "x", detail: {} },
    { code: "bad", status: 99, message: "x", detail: {} },
    { code: "bad", status: 200.5, message: "x", detail: {} },
  ]) {
    try { toProblemResponse(error); process.exit(1); } catch {}
  }
'

check "AC02 non-validation problems cannot carry validation errors" node --input-type=module -e '
  import { toProblemResponse } from "./contracts/js/index.mjs";
  try {
    toProblemResponse({ code: "tenant_context_missing", status: 400, message: "missing", detail: {},
      errors: [{ path: "x", code: "required", message: "required" }] });
    process.exit(1);
  } catch (e) { if (e.code !== "invalid_error_shape") process.exit(1); }
'

check "AC02 problem builder validates message and validation-item schema" node --input-type=module -e '
  import { toProblemResponse } from "./contracts/js/index.mjs";
  for (const error of [
    { code: "bad", status: 400, message: 42 },
    { code: "validation_failed", status: 400, message: "bad", errors: [42] },
    { code: "validation_failed", status: 400, message: "bad", errors: [{ path: 42, code: "Bad Code", message: null }] },
  ]) {
    try { toProblemResponse(error); process.exit(1); } catch {}
  }
'

check "AC03 concurrent identical retries execute exactly once" node --input-type=module -e '
  import { withIdempotency, createInMemoryIdempotencyStore } from "./contracts/js/index.mjs";
  let executions = 0;
  const store = createInMemoryIdempotencyStore();
  const input = {
    tenantId: "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77", operation: "card.activate",
    idempotencyKey: "retry-1", payload: { card: "synthetic" }, store,
    execute: async () => { executions += 1; return { status: 200, body: { ok: true } }; },
  };
  const first = withIdempotency(input);
  const retry = withIdempotency(input);
  await Promise.all([first, retry]);
  if (executions !== 1) process.exit(1);
'

check "AC03 operation and idempotency-key tuples cannot collide" node --input-type=module -e '
  import { idempotencyStorageKey } from "./contracts/js/index.mjs";
  const tenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
  const a = idempotencyStorageKey({ tenantId, operation: "card", idempotencyKey: "activate:key" });
  const b = idempotencyStorageKey({ tenantId, operation: "card:activate", idempotencyKey: "key" });
  if (a === b) process.exit(1);
'

check "AC03 malformed tenant ids cannot alias the control-plane scope" node --input-type=module -e '
  import { idempotencyStorageKey } from "./contracts/js/index.mjs";
  for (const tenantId of [NaN, {}, [], "not-a-tenant"]) {
    try { idempotencyStorageKey({ tenantId, operation: "op", idempotencyKey: "key" }); process.exit(1); } catch {}
  }
'

check "AC04 correlation id round-trips into an audit event" node --input-type=module -e '
  import { resolveCorrelationId, buildAuditEvent } from "./contracts/js/index.mjs";
  const id = resolveCorrelationId("evt_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14");
  const event = buildAuditEvent({ tenant_id: null, actor: { type: "system", id: "qa" },
    action: "qa.verify", target: { type: "contract", id: null }, outcome: "ALLOWED",
    reason_code: "verified", correlation_id: id });
  if (event.correlation_id !== id) process.exit(1);
'

check "AC04 validation and page builders cannot emit schema-invalid values" node --input-type=module -e '
  import { toValidationProblem, buildPageEnvelope } from "./contracts/js/index.mjs";
  try { toValidationProblem([{ field: ["x"], code: "required", message: 42 }]); process.exit(1); } catch {}
  try { toValidationProblem([{ field: ["x"], code: "required" }], { message: 42 }); process.exit(1); } catch {}
  for (const options of [
    { nextCursor: 42, limit: 50 }, { nextCursor: null, limit: undefined },
    { nextCursor: null, limit: 50, totalCount: -1 }, { nextCursor: null, limit: 50, totalCount: 1.5 },
  ]) {
    try { buildPageEnvelope([], options); process.exit(1); } catch {}
  }
'

exit "$([ "$failed" -eq 0 ] && echo 0 || echo 1)"
