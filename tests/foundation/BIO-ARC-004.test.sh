#!/usr/bin/env bash
# Developer tests for BIO-ARC-004, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-ARC-004.test.sh
#
# Artifact and consistency checks here; behaviour is asserted by the node:test suite in
# contracts/js/api.test.mjs. Both are offline and dependency-free.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0

check() {
  local name="$1"; shift
  total=$((total + 1))
  if "$@"; then
    echo "PASS  $name"
  else
    echo "FAIL  $name"
    failed=$((failed + 1))
  fi
}

ADR=docs/adr/ADR-0004-api-versioning-error-and-idempotency-conventions.md

# --- AC01: versioned API paths -------------------------------------------------------
check "AC01 ADR-0004 exists and is accepted" \
  bash -c "test -f $ADR && grep -q '\*\*Status:\*\* Accepted (BIO-ARC-004' $ADR"
check "AC01 API_PATH_PREFIX is /api/v1" \
  bash -c 'node --input-type=module -e "
    import { API_PATH_PREFIX } from \"./contracts/js/index.mjs\";
    if (API_PATH_PREFIX !== \"/api/v1\") process.exit(1);
  "'
check "AC01 ADR distinguishes API path version from schema_version/aggregate_version" \
  bash -c "grep -q 'Shape of the HTTP interface itself' $ADR && grep -q 'aggregate_version' $ADR"

# --- AC02: standardised problem/error response ---------------------------------------
check "AC02 problem-response and validation-error schemas published" \
  bash -c "test -f contracts/schemas/problem-response.schema.json && test -f contracts/schemas/validation-error-item.schema.json"
check "AC02 api.mjs implementation published" test -f contracts/js/api.mjs
check "AC02 toProblemResponse serialises existing error classes unchanged" \
  bash -c 'node --input-type=module -e "
    import { toProblemResponse, TenantContextError } from \"./contracts/js/index.mjs\";
    const e = new TenantContextError(\"m\", \"tenant_context_conflict\", 403, { channels: [\"host\"] });
    const p = toProblemResponse(e);
    if (p.code !== \"tenant_context_conflict\" || p.status !== 403) process.exit(1);
  "'
check "AC02 a detail field cannot shadow a reserved top-level field" \
  bash -c 'node --input-type=module -e "
    import { toProblemResponse } from \"./contracts/js/index.mjs\";
    try { toProblemResponse({ code: \"x\", status: 500, message: \"m\", detail: { correlation_id: \"evt_fake\" } }); process.exit(1); }
    catch (e) { if (e.code !== \"reserved_detail_field\") process.exit(1); }
  "'
check "AC02 QA-03r2 a non-string message and malformed errors[] items are refused" \
  bash -c 'node --input-type=module -e "
    import { toProblemResponse } from \"./contracts/js/index.mjs\";
    try { toProblemResponse({ code: \"x\", status: 500, message: 42 }); process.exit(1); }
    catch (e) { if (e.code !== \"invalid_error_shape\") process.exit(1); }
    try {
      toProblemResponse({ code: \"validation_failed\", status: 400, message: \"m\", errors: [42] });
      process.exit(1);
    } catch (e) { if (e.code !== \"invalid_validation_errors\") process.exit(1); }
    try {
      toProblemResponse({ code: \"validation_failed\", status: 400, message: \"m\",
        errors: [{ path: 123, code: \"NOT_SNAKE\", message: null }] });
      process.exit(1);
    } catch (e) { if (e.code !== \"invalid_validation_errors\") process.exit(1); }
  "'

# --- AC03: write-endpoint idempotency -------------------------------------------------
check "AC03 ADR reuses ADR-0001 idempotency primitives rather than reimplementing" \
  bash -c "grep -q 'requestFingerprint.*resolveIdempotency' $ADR"
check "AC03 ADR states the 24-hour retention floor and the distinct machine code" \
  bash -c "grep -q '24 hours' $ADR && grep -q 'idempotency_key_required' $ADR"
check "AC03 a retried write replays without re-executing" \
  bash -c 'node --input-type=module -e "
    import { withIdempotency, createInMemoryIdempotencyStore } from \"./contracts/js/index.mjs\";
    const store = createInMemoryIdempotencyStore();
    let calls = 0;
    const execute = async () => { calls += 1; return { status: 201, body: { x: 1 } }; };
    const run = () => withIdempotency({ tenantId: \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\", operation: \"op\", idempotencyKey: \"k\", payload: {}, store, execute });
    await run(); await run();
    if (calls !== 1) process.exit(1);
  "'
check "AC03 concurrent identical retries execute exactly once (QA-01)" \
  bash -c 'node --input-type=module -e "
    import { withIdempotency, createInMemoryIdempotencyStore } from \"./contracts/js/index.mjs\";
    const store = createInMemoryIdempotencyStore();
    let calls = 0;
    const execute = async () => { calls += 1; return { status: 200, body: { n: calls } }; };
    const run = () => withIdempotency({ tenantId: \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\", operation: \"card.activate\", idempotencyKey: \"burst\", payload: {}, store, execute });
    await Promise.all([run(), run(), run(), run(), run(), run(), run(), run(), run(), run()]);
    if (calls !== 1) process.exit(1);
  "'
check "AC03 a required idempotency key that is missing fails closed before executing" \
  bash -c 'node --input-type=module -e "
    import { withIdempotency, createInMemoryIdempotencyStore } from \"./contracts/js/index.mjs\";
    let calls = 0;
    const execute = async () => { calls += 1; return { status: 200, body: {} }; };
    try {
      await withIdempotency({ tenantId: \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\", operation: \"op\", payload: {}, store: createInMemoryIdempotencyStore(), execute });
      process.exit(1);
    } catch (e) { if (e.code !== \"idempotency_key_required\" || e.status !== 428 || calls !== 0) process.exit(1); }
  "'
check "AC03 QA-04 a malformed tenantId cannot alias the control-plane scope" \
  bash -c 'node --input-type=module -e "
    import { idempotencyStorageKey } from \"./contracts/js/index.mjs\";
    const control = idempotencyStorageKey({ tenantId: null, operation: \"op\", idempotencyKey: \"k\" });
    for (const bad of [NaN, {}, \"not-canonical\", undefined]) {
      let threw = false;
      try { idempotencyStorageKey({ tenantId: bad, operation: \"op\", idempotencyKey: \"k\" }); }
      catch (e) { threw = e.code === \"invalid_tenant_scope\"; }
      if (!threw) process.exit(1);
    }
    const real = idempotencyStorageKey({
      tenantId: \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\", operation: \"op\", idempotencyKey: \"k\" });
    if (real === control) process.exit(1);
  "'

# --- AC04: pagination, filtering, correlation IDs, validation errors ------------------
check "AC04 ADR states cursor pagination is the default, with the capacity reason" \
  bash -c "grep -q 'Cursor pagination is the default' $ADR"
check "AC04 ADR reconciles the API correlation id with ADR-0003's audit correlation id" \
  bash -c "grep -q 'One identifier, one' $ADR"
check "ACC-01 ADR states the ADR-0003 event-idempotency relationship and answers the interaction question" \
  bash -c "grep -q 'two mechanisms, not one' $ADR \
    && grep -q 'share no implementation' $ADR \
    && grep -q 'The seam is narrower and specific: a partial failure' $ADR \
    && grep -q 'ADR-0003.s own dedupe cannot catch it as a duplicate' $ADR"
check "AC04 pagination schema published" test -f contracts/schemas/paginated-list-response.schema.json
check "AC04 validation error path reuses ADR-0003's escapeSegment, not a second scheme" \
  bash -c "grep -q 'field paths reuse ADR-0003' $ADR"
check "AC04 a validation error path from a nested field and a literal dotted field never collide" \
  bash -c 'node --input-type=module -e "
    import { toValidationProblem } from \"./contracts/js/index.mjs\";
    const p = toValidationProblem([
      { field: [\"profile\", \"name\"], code: \"required\" },
      { field: [\"profile.name\"], code: \"invalid_format\" },
    ]);
    if (p.errors[0].path === p.errors[1].path) process.exit(1);
  "'
check "AC04 an unrecognised filter field is a validation error, not a silent drop" \
  bash -c 'node --input-type=module -e "
    import { assertFilterAllowlist } from \"./contracts/js/index.mjs\";
    try { assertFilterAllowlist({ ssn: \"1\" }, [\"status\"]); process.exit(1); }
    catch (e) { if (e.code !== \"validation_failed\") process.exit(1); }
  "'

# --- shared-contract hygiene ---------------------------------------------------------
check "every published schema is valid JSON" \
  bash -c "python3 -c 'import json,glob;[json.load(open(f)) for f in glob.glob(\"contracts/schemas/*.json\")]'"
check "ADR index lists ADR-0004" bash -c "grep -q 'ADR-0004' docs/adr/README.md"
check "contracts README documents the API contract" bash -c "grep -q 'problem-response' contracts/README.md"
check "fixtures are marked synthetic" bash -c "grep -q SYNTHETIC contracts/js/api.test.mjs"
check "BIO-ARC-001 contracts still green (no regression)" bash tests/foundation/BIO-ARC-001.test.sh
check "BIO-ARC-002 contracts still green (no regression)" bash tests/foundation/BIO-ARC-002.test.sh
check "BIO-ARC-003 contracts still green (no regression)" bash tests/foundation/BIO-ARC-003.test.sh
check "contract behaviour suite passes" node --test contracts/js/

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
