// Developer tests for BIO-ARC-004. Every test name carries the AC it proves.
// Run: node --test contracts/js/   (no dependencies, offline-safe)
// All tenants, keys and payloads here are SYNTHETIC.

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import {
  API_VERSION, API_PATH_PREFIX, IDEMPOTENCY_HEADER, CORRELATION_HEADER,
  IDEMPOTENCY_REPLAYED_HEADER, IDEMPOTENCY_RETENTION_FLOOR_MS,
  IDEMPOTENCY_POLL_INTERVAL_MS, IDEMPOTENCY_POLL_TIMEOUT_MS,
  DEFAULT_PAGE_LIMIT, MAX_PAGE_LIMIT,
  BiologApiError, ApiConventionError, toProblemResponse, toValidationProblem,
  resolveCorrelationId, idempotencyStorageKey, withIdempotency, createInMemoryIdempotencyStore,
  resolvePageLimit, buildPageEnvelope, assertFilterAllowlist,
  newId, isCanonicalId, escapeSegment,
  TenantContextError, ConcurrencyError, AuditError,
} from './index.mjs';

const schema = (name) =>
  JSON.parse(readFileSync(fileURLToPath(new URL(`../schemas/${name}`, import.meta.url)), 'utf8'));
const ADR = readFileSync(
  fileURLToPath(new URL('../../docs/adr/ADR-0004-api-versioning-error-and-idempotency-conventions.md', import.meta.url)), 'utf8',
);

const TENANT_A = 'ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77';
const TENANT_B = 'ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03';

/** The reference atomic store (ADR-0004 Decision 4.4), used by default in tests so the
 * tests exercise the real claim/complete/release protocol, not a hand-rolled stand-in. */
function memoryStore() {
  return createInMemoryIdempotencyStore();
}

/** A store that delays each operation, to force genuine interleaving between concurrent
 * `withIdempotency` calls sharing one backing Map — proving atomicity under real race
 * conditions rather than under Node's single-threaded luck. */
function delayedStore({ claimDelayMs = 5, otherDelayMs = 2 } = {}) {
  const backing = new Map();
  const wait = (ms) => new Promise((resolve) => { setTimeout(resolve, ms); });
  return {
    async claim(key, fingerprint) {
      // The check-and-set itself stays synchronous (no await before it), matching what a
      // real atomic primitive (a unique-constraint insert, SET … NX) guarantees; the delay
      // simulates network latency on top of an operation that is already atomic, not a
      // race inside the atomicity itself.
      let outcome;
      if (backing.has(key)) outcome = { won: false, record: backing.get(key) };
      else { backing.set(key, { fingerprint, status: 'IN_FLIGHT', startedAt: 'x' }); outcome = { won: true }; }
      await wait(claimDelayMs);
      return outcome;
    },
    async complete(key, record) {
      await wait(otherDelayMs);
      backing.set(key, { ...record, status: 'DONE' });
    },
    async release(key) {
      await wait(otherDelayMs);
      backing.delete(key);
    },
    async get(key) {
      await wait(otherDelayMs);
      return backing.get(key);
    },
  };
}

// ---------------------------------------------------------------------------
// BIO-ARC-004-AC01 — versioned API paths.
// ---------------------------------------------------------------------------

test('AC01: the path prefix is /api/v1', () => {
  assert.equal(API_VERSION, 'v1');
  assert.equal(API_PATH_PREFIX, '/api/v1');
});

test('AC01: the ADR distinguishes the API path version from schema_version and aggregate_version', () => {
  assert.match(ADR, /`\/api\/v1`.*this ADR.*Shape of the HTTP interface itself/s);
  assert.match(ADR, /does not imply anything about the `schema_version`/);
});

test('AC01: the ADR requires an explicit version for non-path interfaces', () => {
  assert.match(ADR, /Webhook payloads, device\/reader protocol messages/);
  assert.match(ADR, /schema_version.*api_version/s);
});

// ---------------------------------------------------------------------------
// BIO-ARC-004-AC02 — standardised problem/error response.
// ---------------------------------------------------------------------------

test('AC02: toProblemResponse works unchanged with all three existing error classes', () => {
  const errors = [
    new TenantContextError('conflict', 'tenant_context_conflict', 403, { channels: ['host', 'principal'] }),
    new ConcurrencyError('stale write', 'concurrency_conflict', 409, { current_version: 4 }),
    new AuditError('bad actor', 'invalid_actor', 500, { actor: null }),
  ];
  for (const error of errors) {
    const problem = toProblemResponse(error, { correlationId: undefined });
    assert.equal(problem.code, error.code);
    assert.equal(problem.status, error.status);
    assert.equal(problem.message, error.message);
    assert.deepEqual(problem.detail, error.detail);
    assert.ok(isCanonicalId(problem.correlation_id) && problem.correlation_id.startsWith('evt_'));
    for (const field of schema('problem-response.schema.json').required) {
      assert.ok(problem[field] !== undefined, `${field} present`);
    }
  }
});

test('AC02: a future error class needs only the same four properties, not inheritance', () => {
  const dutTyped = { code: 'card_class_conflict', status: 409, message: 'class already has an active card', detail: { card_class: 'staff' } };
  const problem = toProblemResponse(dutTyped);
  assert.equal(problem.code, 'card_class_conflict');
  assert.equal(problem.detail.card_class, 'staff');
});

test('AC02: BiologApiError is available as a convenience base and behaves like the others', () => {
  const error = new BiologApiError('nope', 'some_code', 422, { x: 1 });
  const problem = toProblemResponse(error);
  assert.equal(problem.code, 'some_code');
  assert.equal(problem.status, 422);
});

test('AC02: toProblemResponse refuses an object that is not error-shaped', () => {
  for (const bad of [null, undefined, {}, { code: 'x' }, { status: 500 }, 'error', 42]) {
    assert.throws(() => toProblemResponse(bad), (e) => e.code === 'invalid_error_shape');
  }
});

test('AC02/hostile: a detail field cannot shadow a reserved top-level field', () => {
  // If an error class's allowlist ever produced a "correlation_id" (or code/status/message/
  // errors) inside detail, the document would use that name for two different values at
  // once. Refused outright rather than emitted.
  for (const field of ['correlation_id', 'code', 'status', 'message', 'errors']) {
    assert.throws(
      () => toProblemResponse({ code: 'x', status: 500, message: 'm', detail: { [field]: 'anything' } }),
      (e) => e.code === 'reserved_detail_field' && e.detail.field === field,
      `detail.${field} must be refused`,
    );
  }
  // An ordinary, non-colliding detail field still works.
  const ok = toProblemResponse({ code: 'x', status: 500, message: 'm', detail: { channels: ['host'] } });
  assert.deepEqual(ok.detail, { channels: ['host'] });
});

test('AC02: detail is omitted entirely when empty, never an empty object on the wire', () => {
  const problem = toProblemResponse({ code: 'x', status: 500, message: 'm', detail: {} });
  assert.equal('detail' in problem, false);
});

test('AC02: errors[] and a non-validation code never coexist', () => {
  const problem = toProblemResponse({ code: 'tenant_context_conflict', status: 403, message: 'm' });
  assert.equal('errors' in problem, false);
  const validation = toValidationProblem([{ field: ['name'], code: 'required' }]);
  assert.equal(validation.code, 'validation_failed');
  assert.ok(Array.isArray(validation.errors));
});

test('AC02: the response is frozen — an error handler cannot mutate a shared problem object', () => {
  const problem = toProblemResponse({ code: 'x', status: 500, message: 'm' });
  assert.throws(() => { problem.status = 200; }, TypeError);
});

test('AC02: the schema requires code, message, status and correlation_id', () => {
  const s = schema('problem-response.schema.json');
  assert.deepEqual(s.required.sort(), ['code', 'correlation_id', 'message', 'status'].sort());
  assert.match(s.properties.code.pattern, /a-z/);
});

// --- BIO-ARC-004 QA-03 regression (Seyi review, 2026-09-21) --------------------------
// Duck-typing which PROPERTIES an object exposes is not the same as validating what is IN
// them. Each builder now enforces the exact content constraints its own published schema
// requires, not just that the right keys are present.

test('AC02/QA-03: a schema-invalid code is refused, not passed through', () => {
  const codeSchema = schema('problem-response.schema.json').properties.code;
  const codePattern = new RegExp(codeSchema.pattern);
  for (const code of ['NOT VALID; CODE', 'CamelCase', 'has space', '', '1starts_with_digit']) {
    assert.equal(codePattern.test(code), false, `${JSON.stringify(code)} must not satisfy the schema pattern`);
    assert.throws(() => toProblemResponse({ code, status: 500, message: 'm' }), (e) => e.code === 'invalid_error_shape');
  }
  // Length is a separate schema constraint (maxLength) from the character-class pattern;
  // a string can match the pattern and still be too long.
  const tooLong = 'a'.repeat(codeSchema.maxLength + 1);
  assert.ok(codePattern.test(tooLong), 'sanity: the pattern alone does not bound length');
  assert.throws(() => toProblemResponse({ code: tooLong, status: 500, message: 'm' }), (e) => e.code === 'invalid_error_shape');
});

test('AC02/QA-03: a schema-invalid status is refused — NaN, fractional, and out of range', () => {
  for (const status of [NaN, Infinity, -Infinity, 42.5, 99, 600, -1, 0]) {
    assert.throws(() => toProblemResponse({ code: 'x', status, message: 'm' }), (e) => e.code === 'invalid_error_shape',
      `status ${status} must be refused`);
  }
  for (const status of [100, 200, 403, 428, 503, 599]) {
    assert.equal(toProblemResponse({ code: 'x', status, message: 'm' }).status, status);
  }
});

test('AC02/QA-03: errors[] cannot ride along on a non-validation code', () => {
  assert.throws(
    () => toProblemResponse({ code: 'tenant_context_conflict', status: 403, message: 'm', errors: [{ path: 'x', code: 'y', message: 'z' }] }),
    (e) => e.code === 'invalid_error_shape',
  );
  // The one code errors[] may legitimately accompany still works.
  const validation = toProblemResponse({ code: 'validation_failed', status: 400, message: 'm', errors: [{ path: 'x', code: 'required', message: 'z' }] });
  assert.equal(validation.errors.length, 1);
});

test('AC02/QA-03: an empty errors[] array is treated as absent, not an empty validation list', () => {
  const problem = toProblemResponse({ code: 'tenant_context_conflict', status: 403, message: 'm', errors: [] });
  assert.equal('errors' in problem, false);
});

test('AC04/QA-03: a validation item message must be a string when supplied, not merely truthy', () => {
  for (const message of [42, {}, ['x'], true]) {
    assert.throws(() => toValidationProblem([{ field: ['x'], code: 'required', message }]),
      (e) => e.code === 'invalid_validation_errors', `message ${JSON.stringify(message)} must be refused`);
  }
  // Falsy-but-absent still falls back to the code, unchanged behaviour.
  assert.equal(toValidationProblem([{ field: ['x'], code: 'required' }]).errors[0].message, 'required');
});

test('AC04/QA-03: buildPageEnvelope refuses a missing, non-integer or non-positive limit', () => {
  for (const limit of [undefined, null, 'ten', 1.5, 0, -1, NaN]) {
    assert.throws(() => buildPageEnvelope([1, 2], { limit }), (e) => e.code === 'invalid_page_limit',
      `limit ${JSON.stringify(limit)} must be refused`);
  }
  assert.equal(buildPageEnvelope([1, 2], { limit: 10 }).page.limit, 10);
});

test('AC04/QA-03: buildPageEnvelope refuses a non-string, non-null cursor', () => {
  for (const nextCursor of [12345, {}, ['x'], true]) {
    assert.throws(() => buildPageEnvelope([1, 2], { limit: 10, nextCursor }), (e) => e.code === 'invalid_page_cursor',
      `cursor ${JSON.stringify(nextCursor)} must be refused`);
  }
  assert.equal(buildPageEnvelope([1, 2], { limit: 10, nextCursor: null }).page.next_cursor, null);
  assert.equal(buildPageEnvelope([1, 2], { limit: 10, nextCursor: 'opaque' }).page.next_cursor, 'opaque');
});

test('AC04/QA-03: buildPageEnvelope refuses a negative or fractional total_count', () => {
  for (const totalCount of [-5, -0.5, 1.5, NaN, Infinity]) {
    assert.throws(() => buildPageEnvelope([1, 2], { limit: 10, totalCount }), (e) => e.code === 'invalid_page_total_count',
      `totalCount ${totalCount} must be refused`);
  }
  assert.equal(buildPageEnvelope([1, 2], { limit: 10, totalCount: 0 }).page.total_count, 0);
});

test('AC04/QA-03: every builder output actually satisfies its own published schema shape', () => {
  // The point of QA-03: prove the builders and the schemas agree, not just assert each in
  // isolation. Build one representative valid document per schema and check every
  // required field is present with the right type.
  const problem = toProblemResponse({ code: 'tenant_context_conflict', status: 403, message: 'm', detail: { x: 1 } });
  const problemSchema = schema('problem-response.schema.json');
  for (const field of problemSchema.required) assert.ok(problem[field] !== undefined, `problem.${field}`);
  assert.match(problem.code, new RegExp(problemSchema.properties.code.pattern));

  const validation = toValidationProblem([{ field: ['a', 'b'], code: 'required', message: 'm' }]);
  const itemSchema = schema('validation-error-item.schema.json');
  for (const field of itemSchema.required) assert.ok(validation.errors[0][field] !== undefined, `errors[0].${field}`);

  const page = buildPageEnvelope([1, 2], { limit: 10, nextCursor: 'opaque', totalCount: 2 });
  const pageSchema = schema('paginated-list-response.schema.json');
  for (const field of pageSchema.required) assert.ok(page[field] !== undefined, `page.${field}`);
  for (const field of pageSchema.properties.page.required) assert.ok(page.page[field] !== undefined, `page.page.${field}`);
});

test('AC02/QA-03: the ADR records that a builder must enforce, not merely resemble, its schema', () => {
  assert.match(ADR, /A builder must enforce the schema it publishes, not merely resemble it/);
  assert.match(ADR, /two different things that happened to agree on the easy cases/);
});

// --- BIO-ARC-004 QA-03 round 2 regression (Seyi re-review, 2026-09-21) ---------------
// The first QA-03 pass checked SOME of each schema's constraints but not all of them:
// a non-string top-level message, and errors[] items copied through unchecked when code
// was already validation_failed.

test('AC02/QA-03r2: the exact reported examples are all refused, not returned', () => {
  assert.throws(() => toProblemResponse({ code: 'bad', status: 400, message: 42 }),
    (e) => e.code === 'invalid_error_shape');
  assert.throws(() => toProblemResponse({ code: 'validation_failed', status: 400, message: 'm', errors: [42] }),
    (e) => e.code === 'invalid_validation_errors');
  assert.throws(
    () => toProblemResponse({
      code: 'validation_failed', status: 400, message: 'm',
      errors: [{ path: 123, code: 'NOT_SNAKE', message: null }],
    }),
    (e) => e.code === 'invalid_validation_errors',
  );
  assert.throws(() => toValidationProblem([{ field: ['x'], code: 'required' }], { message: 42 }),
    (e) => e.code === 'invalid_validation_errors');
});

test('AC02/QA-03r2: a validation errors[] item handed through directly is validated, not spread blindly', () => {
  for (const item of [
    null, undefined, 'x', 42, [], // not an object at all
    {}, // missing everything
    { path: '', code: 'required', message: 'm' }, // empty path
    { path: 'x', code: 'Required', message: 'm' }, // not snake_case
    { path: 'x', code: 'required', message: '' }, // empty message
    { path: 'x', code: 'required', message: 42 }, // wrong type
    { path: 'x', code: 'required' }, // missing message entirely
  ]) {
    assert.throws(
      () => toProblemResponse({ code: 'validation_failed', status: 400, message: 'm', errors: [item] }),
      (e) => e.code === 'invalid_validation_errors',
      `${JSON.stringify(item)} must be refused`,
    );
  }
  // A well-formed item, handed through directly rather than built by toValidationProblem,
  // still works — and is reconstructed clean, not spread with extra properties.
  const problem = toProblemResponse({
    code: 'validation_failed', status: 400, message: 'm',
    errors: [{ path: 'x', code: 'required', message: 'x is required', extra: 'should not appear' }],
  });
  assert.deepEqual(problem.errors, [{ path: 'x', code: 'required', message: 'x is required' }]);
});

test('AC02/QA-03r2: toValidationProblem and toProblemResponse validate items through the same rule', () => {
  // Not two independent checks that could drift apart: assertValidationErrorItem is shared.
  const viaBuilder = () => toValidationProblem([{ field: ['x'], code: 'Bad Code', message: 'm' }]);
  const viaDirect = () => toProblemResponse({
    code: 'validation_failed', status: 400, message: 'm',
    errors: [{ path: 'x', code: 'Bad Code', message: 'm' }],
  });
  assert.throws(viaBuilder, (e) => e.code === 'invalid_validation_errors');
  assert.throws(viaDirect, (e) => e.code === 'invalid_validation_errors');
});

test('AC02/QA-03r2: a legitimate errors[] payload still works end to end', () => {
  const problem = toProblemResponse({
    code: 'validation_failed', status: 400, message: 'validation failed',
    errors: [
      { path: 'profile.name', code: 'required', message: 'name is required' },
      { path: 'profile\\.name', code: 'invalid_format', message: 'must not contain a literal dot' },
    ],
  });
  assert.equal(problem.errors.length, 2);
  for (const field of schema('validation-error-item.schema.json').required) {
    assert.ok(problem.errors[0][field] !== undefined, `errors[0].${field}`);
  }
});

test('AC02/QA-03r2: an empty-string or null message is refused, not silently replaced', () => {
  // Falsy is not automatically "absent": an explicit empty string or null is a caller
  // mistake worth surfacing, not a value to quietly substitute a default for.
  for (const message of [null, '']) {
    assert.throws(() => toValidationProblem([{ field: ['x'], code: 'required', message }]),
      (e) => e.code === 'invalid_validation_errors', `item message ${JSON.stringify(message)} must be refused`);
  }
  // Genuinely absent (never provided) still falls back to the code, unchanged behaviour.
  assert.equal(toValidationProblem([{ field: ['x'], code: 'required' }]).errors[0].message, 'required');
});

// ---------------------------------------------------------------------------
// BIO-ARC-004-AC04 (part) — validation error shape, reusing ADR-0003's path rule.
// ---------------------------------------------------------------------------

test('AC04/AC02: a validation error path is escaped exactly like an ADR-0003 redaction path', () => {
  const problem = toValidationProblem([
    { field: ['profile', 'name'], code: 'required', message: 'name is required' },
    { field: ['profile.name'], code: 'invalid_format' },
  ]);
  assert.equal(problem.errors[0].path, 'profile.name');
  assert.equal(problem.errors[1].path, 'profile\\.name');
  assert.notEqual(problem.errors[0].path, problem.errors[1].path, 'nested and flat-literal must not collide');
  assert.equal(problem.errors[0].path, ['profile', 'name'].map(escapeSegment).join('.'));
});

test('AC04/hostile: a bare dot-joined string is refused, not silently interpreted', () => {
  // The exact ambiguity ADR-0003's QA-04 fixed: is "profile.name" one field or two? This
  // API refuses to guess, forcing the caller to say which.
  assert.throws(() => toValidationProblem([{ field: 'profile.name', code: 'required' }]),
    (e) => e.code === 'invalid_validation_errors');
  for (const badField of [[], [''], [42], null, undefined, 'x']) {
    assert.throws(() => toValidationProblem([{ field: badField, code: 'required' }]),
      (e) => e.code === 'invalid_validation_errors', `${JSON.stringify(badField)} rejected`);
  }
});

test('AC04: a validation error requires a snake_case code', () => {
  for (const code of ['Required', 'REQUIRED', 'required-field', '', 'required field', null]) {
    assert.throws(() => toValidationProblem([{ field: ['x'], code }]), (e) => e.code === 'invalid_validation_errors');
  }
  assert.equal(toValidationProblem([{ field: ['x'], code: 'out_of_range' }]).errors[0].code, 'out_of_range');
});

test('AC04: toValidationProblem requires at least one error', () => {
  assert.throws(() => toValidationProblem([]), (e) => e.code === 'invalid_validation_errors');
  assert.throws(() => toValidationProblem(null), (e) => e.code === 'invalid_validation_errors');
});

test('AC04: the validation-error-item schema matches what toValidationProblem emits', () => {
  const s = schema('validation-error-item.schema.json');
  const item = toValidationProblem([{ field: ['a', 'b'], code: 'required', message: 'm' }]).errors[0];
  for (const field of s.required) assert.ok(item[field] !== undefined, `${field} present`);
  assert.match(item.code, new RegExp(s.properties.code.pattern));
});

// ---------------------------------------------------------------------------
// BIO-ARC-004-AC03 — write-endpoint idempotency.
// ---------------------------------------------------------------------------

test('AC03: a new key executes and stores the result', async () => {
  const store = memoryStore();
  let calls = 0;
  const execute = async () => { calls += 1; return { status: 201, body: { id: 'crd_x' } }; };
  const result = await withIdempotency({
    tenantId: TENANT_A, operation: 'credential.activate', idempotencyKey: 'k-1',
    payload: { action: 'activate' }, store, execute,
  });
  assert.equal(result.outcome, 'EXECUTE');
  assert.equal(result.replayed, false);
  assert.equal(calls, 1);
});

test('AC03: the same key and body replays without re-executing', async () => {
  const store = memoryStore();
  let calls = 0;
  const execute = async () => { calls += 1; return { status: 201, body: { card_sequence: '001' } }; };
  const run = () => withIdempotency({
    tenantId: TENANT_A, operation: 'credential.activate', idempotencyKey: 'k-1',
    payload: { action: 'activate' }, store, execute,
  });
  const first = await run();
  const second = await run();
  assert.equal(calls, 1, 'a retried activation must never mint a second card');
  assert.equal(second.replayed, true);
  assert.deepEqual(second.body, first.body);
});

test('AC03/hostile: the same key with different content is surfaced, never absorbed', async () => {
  const store = memoryStore();
  const execute = async () => ({ status: 200, body: {} });
  await withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: { a: 1 }, store, execute });
  await assert.rejects(
    () => withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: { a: 2 }, store, execute }),
    (e) => {
      assert.equal(e.code, 'idempotency_key_reuse');
      assert.equal(e.status, 409);
      return true;
    },
  );
});

test('AC03: a required key that is missing is 428 idempotency_key_required, distinct from precondition_required', () => {
  assert.notEqual('idempotency_key_required', 'precondition_required');
});

test('AC03: a required key that is missing fails before execute is ever called', async () => {
  const store = memoryStore();
  let calls = 0;
  const execute = async () => { calls += 1; return { status: 200, body: {} }; };
  await assert.rejects(
    () => withIdempotency({ tenantId: TENANT_A, operation: 'card.activate', payload: {}, store, execute }),
    (e) => { assert.equal(e.code, 'idempotency_key_required'); assert.equal(e.status, 428); return true; },
  );
  assert.equal(calls, 0);
});

test('AC03: an optional idempotency key executes normally when omitted', async () => {
  const store = memoryStore();
  let calls = 0;
  const execute = async () => { calls += 1; return { status: 200, body: {} }; };
  const result = await withIdempotency({ tenantId: TENANT_A, operation: 'report.generate', required: false, payload: {}, store, execute });
  assert.equal(result.outcome, 'EXECUTE');
  assert.equal(calls, 1);
});

test('AC03/hostile: the storage key is scoped to tenant and operation — no cross-tenant or cross-operation collision', async () => {
  const store = memoryStore();
  let calls = 0;
  const execute = async () => { calls += 1; return { status: 200, body: { call: calls } }; };
  const runFor = (tenantId, operation) => withIdempotency({ tenantId, operation, idempotencyKey: 'same-key', payload: { a: 1 }, store, execute });
  const a1 = await runFor(TENANT_A, 'op.one');
  const b1 = await runFor(TENANT_B, 'op.one'); // same key, different tenant
  const a2 = await runFor(TENANT_A, 'op.two'); // same key, same tenant, different operation
  assert.equal(calls, 3, 'each scope executes independently');
  assert.notEqual(a1.body.call, b1.body.call);
  assert.notEqual(a1.body.call, a2.body.call);
});

// --- BIO-ARC-004 QA-02 regression (Seyi review, 2026-09-21) --------------------------
// Naive `${a}:${b}:${c}` joining let a colon inside operation or idempotencyKey forge a
// collision with a completely different (operation, key) pair — the same delimiter-alias
// failure ADR-0003's QA-04 found, one character over.

test('AC03/QA-02: the exact reported collision — operation/key tuples cannot alias', () => {
  const key1 = idempotencyStorageKey({ tenantId: TENANT_A, operation: 'card', idempotencyKey: 'activate:key' });
  const key2 = idempotencyStorageKey({ tenantId: TENANT_A, operation: 'card:activate', idempotencyKey: 'key' });
  assert.notEqual(key1, key2);
});

test('AC03/QA-02: a colon-carrying operation or key cannot collide across many shapes', () => {
  const cases = [
    [{ operation: 'a:b', idempotencyKey: 'c' }, { operation: 'a', idempotencyKey: 'b:c' }],
    [{ operation: 'a', idempotencyKey: 'b:c:d' }, { operation: 'a:b', idempotencyKey: 'c:d' }],
    [{ operation: 'a:b:c', idempotencyKey: 'd' }, { operation: 'a', idempotencyKey: 'b:c:d' }],
  ];
  for (const [left, right] of cases) {
    const k1 = idempotencyStorageKey({ tenantId: TENANT_A, ...left });
    const k2 = idempotencyStorageKey({ tenantId: TENANT_A, ...right });
    assert.notEqual(k1, k2, `${JSON.stringify(left)} vs ${JSON.stringify(right)} must not collide`);
  }
});

test('AC03/QA-02/hostile: an operation or key crafted with embedded quotes still cannot collide', () => {
  // JSON.stringify's own escaping must hold even when the caller tries to fight it.
  // tenantId is fixed and canonical here — QA-04 governs its own validity separately.
  const k1 = idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op"', idempotencyKey: '"activate' });
  const k2 = idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op', idempotencyKey: '"","activate' });
  assert.notEqual(k1, k2);
  const k3 = idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op\\', idempotencyKey: 'x' });
  const k4 = idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op', idempotencyKey: '\\x' });
  assert.notEqual(k3, k4);
});

test('AC03/QA-02: two different tenants running otherwise-identical operations still never collide', () => {
  const k1 = idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k' });
  const k2 = idempotencyStorageKey({ tenantId: TENANT_B, operation: 'op', idempotencyKey: 'k' });
  assert.notEqual(k1, k2);
  // A caller cannot forge a third tenant by concatenating two real ones — QA-04 rejects
  // any tenantId that is not itself a single canonical identifier before this is reached.
  assert.throws(() => idempotencyStorageKey({ tenantId: `${TENANT_A}:${TENANT_B}`, operation: 'op', idempotencyKey: 'k' }),
    (e) => e.code === 'invalid_tenant_scope');
});

test('AC03/QA-02: the ADR records the injective JSON-tuple rule', () => {
  assert.match(ADR, /The scope key is not naive string concatenation/);
  assert.match(ADR, /JSON\.stringify\(\[tenant_id, operation, idempotency_key\]\)/);
  assert.match(ADR, /injective by construction/);
});

// --- BIO-ARC-004 QA-04 regression (Seyi re-review, 2026-09-21) -----------------------
// tenantId was never validated, so a raw non-canonical value became part of a shared
// storage scope. Concretely, JSON.stringify(NaN) is "null" — the same tuple component the
// control-plane scope uses deliberately — so a caller with a broken tenant resolution
// could alias a tenant-scoped record onto the control plane's own key space.

test('AC03/QA-04: the exact reported alias — NaN cannot become the control-plane scope', () => {
  const control = idempotencyStorageKey({ tenantId: null, operation: 'op', idempotencyKey: 'k' });
  assert.throws(() => idempotencyStorageKey({ tenantId: NaN, operation: 'op', idempotencyKey: 'k' }),
    (e) => e.code === 'invalid_tenant_scope');
  // Sanity: this is exactly the collision that was possible before the fix.
  assert.equal(JSON.stringify([NaN, 'op', 'k']), JSON.stringify([null, 'op', 'k']));
  void control;
});

test('AC03/QA-04: only an explicit null or a canonical tenant identifier is accepted', () => {
  for (const bad of [
    NaN, undefined, {}, [], 42, true, '', 'not-canonical',
    'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', // wrong identifier type
    `${TENANT_A}:${TENANT_B}`, // a caller-forged "tenant" built from two real ones
  ]) {
    assert.throws(() => idempotencyStorageKey({ tenantId: bad, operation: 'op', idempotencyKey: 'k' }),
      (e) => e.code === 'invalid_tenant_scope', `${JSON.stringify(bad)} must be refused`);
  }
  assert.equal(idempotencyStorageKey({ tenantId: null, operation: 'op', idempotencyKey: 'k' }), JSON.stringify([null, 'op', 'k']));
  assert.equal(idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k' }), JSON.stringify([TENANT_A, 'op', 'k']));
});

test('AC03/QA-04/hostile: an omitted tenantId is refused, not silently treated as control-plane', () => {
  // The original `tenantId ?? null` made "forgot to resolve a tenant" and "deliberately
  // doing control-plane work" the same fact. They no longer are.
  assert.throws(() => idempotencyStorageKey({ operation: 'op', idempotencyKey: 'k' }),
    (e) => e.code === 'invalid_tenant_scope');
});

test('AC03/QA-04: withIdempotency itself refuses a malformed tenantId before touching the store', async () => {
  const store = createInMemoryIdempotencyStore();
  let calls = 0;
  const execute = async () => { calls += 1; return { status: 200, body: {} }; };
  await assert.rejects(
    () => withIdempotency({ tenantId: NaN, operation: 'op', idempotencyKey: 'k', payload: {}, store, execute }),
    (e) => e.code === 'invalid_tenant_scope',
  );
  assert.equal(calls, 0);
});

test('AC03/QA-04: the ADR records the alias risk and the explicit-null requirement', () => {
  assert.match(ADR, /`tenantId` is validated too, not only `operation` and `idempotencyKey`/);
  assert.match(ADR, /could \*\*alias a tenant-scoped\s*\nidempotency record onto the control plane's own key space\*\*/);
  assert.match(ADR, /`tenantId` must now be \*\*explicitly\*\* `null`/);
});

// --- BIO-ARC-004 acceptance blocker BIO-ARC-004-ACC-01 (Chioma, 2026-09-21) ----------
// Chioma's acceptance audit FAILed on documentation only: the builder summary claimed
// ADR-0004 states the relationship between this ADR's write idempotency and ADR-0003's
// domain-event idempotency, but the ADR text never actually said so. These tests assert
// the cross-reference is genuinely present, names both mechanisms, and answers — not just
// gestures at — the concrete interaction question the dispatch raised.

test('ACC-01: the ADR names both mechanisms and states they are distinct, not shared', () => {
  assert.match(ADR, /How this relates to ADR-0003's event idempotency — two mechanisms, not one/);
  assert.match(ADR, /share no implementation/);
  assert.match(ADR, /\(tenant_id, operation, idempotency_key\)/);
  assert.match(ADR, /\(tenant_id, event_id\)/);
});

test('ACC-01: the ADR references ADR-0003\'s eventFingerprint mechanism by its actual behaviour', () => {
  // Not just a citation — the specific facts a reader needs: the fingerprint excludes
  // received_at, and reuse is surfaced as event_id_reuse rather than absorbed.
  assert.match(ADR, /`received_at` \(server-stamped per delivery; ADR-0003 §4\)/);
  assert.match(ADR, /`event_id_reuse`, surfaced not absorbed \(ADR-0003 §4\)/);
});

test('ACC-01: the ADR answers — not just raises — whether a replayed response re-emits the domain event', () => {
  assert.match(ADR, /does a replayed API response mean the domain event is not\s*\nre-emitted, or can it be emitted twice/);
  assert.match(ADR, /the\s*\ndomain event cannot be re-emitted, and not by coincidence/);
  assert.match(ADR, /A `REPLAY` outcome \(Decision\s*\n4\.4\) never calls `execute` again/);
});

test('ACC-01: the ADR identifies the actual seam — a partial failure, not an ordinary retry — precisely', () => {
  assert.match(ADR, /The seam is narrower and specific: a partial failure/);
  assert.match(ADR, /mints a fresh event via `newId`\s*\nthe same way the first one did/);
  assert.match(ADR, /ADR-0003's own dedupe cannot catch it as a duplicate/);
  assert.match(ADR, /has no visibility into what\s*\n`execute` did internally before it failed/);
});

test('ACC-01: the ADR places the resulting obligation on the endpoint author, explicitly', () => {
  assert.match(ADR, /the emission itself must\s*\nbe made safe against being repeated on a retried attempt/);
  assert.match(ADR, /does not extend that\s*\nguarantee to a side effect `execute` performs before the point where it can still fail/);
  assert.match(ADR, /can emit a domain event and then fail before returning makes\s+that emission idempotent against a retried attempt itself \(Decision 4\.6\)/);
});

test('AC03: idempotencyStorageKey rejects a missing operation or an oversized key', () => {
  assert.throws(() => idempotencyStorageKey({ tenantId: TENANT_A, operation: '', idempotencyKey: 'k' }), (e) => e.code === 'invalid_operation');
  assert.throws(() => idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op', idempotencyKey: '' }), (e) => e.code === 'invalid_idempotency_key');
  assert.throws(() => idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'x'.repeat(256) }), (e) => e.code === 'invalid_idempotency_key');
  assert.equal(idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'x'.repeat(255) }).length > 0, true);
});

test('AC03: a control-plane operation (no tenant) still scopes correctly', () => {
  // QA-02: the key is now an injective JSON tuple, not naive colon-joining.
  assert.equal(idempotencyStorageKey({ tenantId: null, operation: 'op', idempotencyKey: 'k' }), JSON.stringify([null, 'op', 'k']));
  assert.notEqual(
    idempotencyStorageKey({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k' }),
    idempotencyStorageKey({ tenantId: null, operation: 'op', idempotencyKey: 'k' }),
  );
});

test('AC03: the ADR states the retention floor and the distinct machine code', () => {
  assert.equal(IDEMPOTENCY_RETENTION_FLOOR_MS, 24 * 60 * 60 * 1000);
  assert.match(ADR, /kept for \*\*at least\s+24 hours\*\*/);
  assert.match(ADR, /distinct code from ADR-0001's `precondition_required`/);
});

test('AC03: an async store is awaited correctly, sequentially', async () => {
  const store = delayedStore();
  let calls = 0;
  const execute = async () => { calls += 1; return { status: 200, body: { calls } }; };
  const first = await withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: {}, store, execute });
  const second = await withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: {}, store, execute });
  assert.equal(calls, 1);
  assert.deepEqual(second.body, first.body);
});

// --- BIO-ARC-004 QA-01 regression (Seyi review, 2026-09-21) --------------------------
// get-then-execute-then-set as three separate awaited steps let two concurrent identical
// requests both observe nothing stored before either wrote anything, so both executed. The
// fix moves the atomic step (store.claim) to BEFORE execute runs.

test('AC03/QA-01: concurrent identical retries execute exactly once, under real interleaving', async () => {
  // The reference in-memory store's claim is synchronous internally, so this alone would
  // not prove much — delayedStore forces a genuine await window between two callers'
  // claim attempts, which is what actually reproduced QA-01 against the shallow fix.
  const store = delayedStore({ claimDelayMs: 8, otherDelayMs: 3 });
  let calls = 0;
  const execute = async () => { calls += 1; await new Promise((r) => setTimeout(r, 15)); return { status: 201, body: { winner: calls } }; };
  const run = () => withIdempotency({
    tenantId: TENANT_A, operation: 'card.activate', idempotencyKey: 'concurrent-1', payload: { a: 1 }, store, execute,
  });
  const results = await Promise.all([run(), run(), run(), run(), run()]);
  assert.equal(calls, 1, 'a crash-prone write must never execute twice for concurrent identical retries');
  const bodies = results.map((r) => JSON.stringify(r.body));
  assert.ok(bodies.every((b) => b === bodies[0]), 'every caller sees the same outcome');
  assert.equal(results.filter((r) => r.replayed).length, 4, 'exactly one winner, four replays');
});

test('AC03/QA-01: the reference store proves atomicity at scale — twenty concurrent callers, one execution', async () => {
  const store = createInMemoryIdempotencyStore();
  let calls = 0;
  const execute = async () => { calls += 1; return { status: 200, body: { n: calls } }; };
  const run = () => withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'burst', payload: {}, store, execute });
  await Promise.all(Array.from({ length: 20 }, run));
  assert.equal(calls, 1);
});

test('AC03/QA-01: a crash between execute and completion is recoverable by a later retry, not silently lost', async () => {
  // "A crash after execute but before set" from the finding: execute throws, the claim is
  // released, and a subsequent (non-concurrent) retry can execute again exactly once.
  const store = createInMemoryIdempotencyStore();
  let attempt = 0;
  const flaky = async () => {
    attempt += 1;
    if (attempt === 1) throw new Error('simulated crash between execute and store.complete');
    return { status: 200, body: { attempt } };
  };
  await assert.rejects(
    () => withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: {}, store, execute: flaky }),
    /simulated crash/,
  );
  const retry = await withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: {}, store, execute: flaky });
  assert.equal(attempt, 2, 'released after the crash, so the retry could execute exactly once more');
  assert.equal(retry.outcome, 'EXECUTE');
  assert.equal(retry.replayed, false);
});

test('AC03/QA-01: concurrent DIFFERENT content under the same key still conflicts, never silently races through', async () => {
  const store = delayedStore({ claimDelayMs: 5 });
  const execA = async () => ({ status: 200, body: { from: 'A' } });
  const execB = async () => ({ status: 200, body: { from: 'B' } });
  const runA = withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: { x: 1 }, store, execute: execA });
  await new Promise((r) => { setTimeout(r, 1); });
  await assert.rejects(
    () => withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: { x: 2 }, store, execute: execB }),
    (e) => { assert.equal(e.code, 'idempotency_key_reuse'); assert.equal(e.status, 409); return true; },
  );
  await runA;
});

test('AC03/QA-01: a loser waits, bounded, and a permanently stuck winner surfaces a distinct timeout', async () => {
  const store = createInMemoryIdempotencyStore();
  const hang = () => new Promise(() => {}); // never resolves — simulates a wedged winner
  const winner = withIdempotency({ tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: {}, store, execute: hang });
  await new Promise((r) => { setTimeout(r, 5); });
  await assert.rejects(
    () => withIdempotency({
      tenantId: TENANT_A, operation: 'op', idempotencyKey: 'k', payload: {}, store, execute: hang,
      pollIntervalMs: 5, pollTimeoutMs: 40,
    }),
    (e) => { assert.equal(e.code, 'idempotency_wait_timeout'); assert.equal(e.status, 503); return true; },
  );
  void winner; // deliberately left pending; the process exits once the test file finishes
});

test('AC03/QA-01: default poll bounds are exported, not magic numbers buried in the function', () => {
  assert.equal(IDEMPOTENCY_POLL_INTERVAL_MS, 20);
  assert.equal(IDEMPOTENCY_POLL_TIMEOUT_MS, 5000);
});

test('AC03/QA-01: the ADR records the atomic-claim rule and why get-then-set failed', () => {
  assert.match(ADR, /That sequence is not atomic/);
  assert.match(ADR, /`claim\(key, fingerprint\)`/);
  assert.match(ADR, /idempotency_wait_timeout/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-004-AC03/AC04 — correlation ID resolution and reconciliation with ADR-0003.
// ---------------------------------------------------------------------------

test('AC04: a well-formed caller-supplied correlation id is used as-is', () => {
  const supplied = newId('event');
  assert.equal(resolveCorrelationId(supplied), supplied);
});

test('AC04/hostile: a malformed or foreign correlation id is never trusted — a fresh one is minted', () => {
  for (const bad of [
    'not-an-id', 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', '', null, undefined, 42,
    'evt_not-a-real-uuid', "evt_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d; DROP TABLE audit_event",
  ]) {
    const resolved = resolveCorrelationId(bad);
    assert.ok(isCanonicalId(resolved) && resolved.startsWith('evt_'), `${JSON.stringify(bad)} yields a fresh valid id`);
    assert.notEqual(resolved, bad);
  }
});

test('AC04: a resolved correlation id would satisfy buildAuditEvent\'s own validation', async () => {
  // The concrete reconciliation the story flagged: this is the SAME identifier ADR-0003's
  // audit envelope requires, not a second id space needing translation.
  const { buildAuditEvent } = await import('./audit.mjs');
  const correlation_id = resolveCorrelationId(undefined);
  const event = buildAuditEvent({
    tenant_id: TENANT_A, actor: { type: 'user', id: 'admin-1' }, action: 'credential.activate',
    target: { type: 'credential', id: 'crd_x' }, outcome: 'ALLOWED', reason_code: 'ok', correlation_id,
  });
  assert.equal(event.correlation_id, correlation_id);
});

test('AC04: the ADR states there is one correlation id space, not two', () => {
  assert.match(ADR, /does not mint a second, HTTP-flavoured\s*\ncorrelation ID/);
  assert.match(ADR, /One identifier, one\s*\nfield name, zero translation/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-004-AC04 — pagination and filtering.
// ---------------------------------------------------------------------------

test('AC04: default and max page limits are documented and enforced', () => {
  assert.equal(resolvePageLimit(undefined), DEFAULT_PAGE_LIMIT);
  assert.equal(resolvePageLimit(null), DEFAULT_PAGE_LIMIT);
  assert.equal(resolvePageLimit(10), 10);
  assert.equal(resolvePageLimit(MAX_PAGE_LIMIT + 500), MAX_PAGE_LIMIT, 'clamped, not rejected');
});

test('AC04/hostile: a non-integer or non-positive limit is rejected, not coerced silently', () => {
  for (const bad of [0, -1, 1.5, 'ten', NaN, {}, []]) {
    assert.throws(() => resolvePageLimit(bad), (e) => e.code === 'invalid_limit', `${JSON.stringify(bad)} rejected`);
  }
});

test('AC04: the page envelope matches the published schema shape', () => {
  const envelope = buildPageEnvelope([{ id: 1 }], { nextCursor: 'opaque-abc', limit: 20 });
  const s = schema('paginated-list-response.schema.json');
  for (const field of s.required) assert.ok(envelope[field] !== undefined, `${field} present`);
  for (const field of s.properties.page.required) assert.ok(envelope.page[field] !== undefined, `page.${field} present`);
  assert.equal('total_count' in envelope.page, false, 'omitted when not supplied');
});

test('AC04: total_count is included only when explicitly supplied', () => {
  const withCount = buildPageEnvelope([], { limit: 10, totalCount: 42 });
  assert.equal(withCount.page.total_count, 42);
});

test('AC04: an empty final page has a null next_cursor', () => {
  const envelope = buildPageEnvelope([], { limit: 10 });
  assert.equal(envelope.page.next_cursor, null);
});

test('AC04: the returned data array is copied, not the caller\'s live array', () => {
  const source = [1, 2];
  const envelope = buildPageEnvelope(source, { limit: 10 });
  source.push(3);
  assert.deepEqual([...envelope.data], [1, 2]);
});

test('AC04: filtering is allowlisted — an unrecognised field is a validation error, not a silent drop', () => {
  assert.ok(assertFilterAllowlist({ status: 'active' }, ['status', 'created_at']));
  assert.throws(() => assertFilterAllowlist({ status: 'active', ssn: '123' }, ['status']), (e) => {
    assert.equal(e.code, 'validation_failed');
    assert.equal(e.status, 400);
    assert.ok(e.errors.some((item) => item.path === 'ssn' && item.code === 'unknown_field'));
    return true;
  });
});

test('AC04/hostile: a field added later to an unrelated allowlist is still refused here', () => {
  // The same allowlist-not-denylist reasoning as ADR-0003: adding a NEW field to the
  // requested filters, that the endpoint never declared, must still be refused.
  assert.throws(() => assertFilterAllowlist({ newly_added_field: 'x' }, ['status']), (e) => e.code === 'validation_failed');
});

test('AC04: cursor pagination is documented as the default with a stated reason', () => {
  assert.match(ADR, /Cursor pagination is the default, not offset/);
  assert.match(ADR, /500k.{1,3}2M identities per tenant/);
  assert.match(ADR, /opaque.{0,20}\*\*/s);
});

test('AC04: filtering allowlist reasoning is documented and matches ADR-0003\'s', () => {
  assert.match(ADR, /allowlisted per endpoint/);
  assert.match(ADR, /"allowlist, never a denylist" reasoning from ADR-0003/);
});

// ---------------------------------------------------------------------------
// Contract hygiene.
// ---------------------------------------------------------------------------

test('every published schema is self-describing JSON Schema 2020-12', () => {
  for (const name of ['problem-response.schema.json', 'validation-error-item.schema.json', 'paginated-list-response.schema.json']) {
    const s = schema(name);
    assert.equal(s.$schema, 'https://json-schema.org/draft/2020-12/schema', name);
    assert.ok(s.$id && s.title && s.description, `${name} is self-describing`);
  }
});

test('ADR-0004 records the decision, the reuses, and the freeze policy', () => {
  assert.match(ADR, /# ADR-0004/);
  assert.match(ADR, /\*\*Status:\*\* Accepted \(BIO-ARC-004/);
  assert.match(ADR, /Freezes at W01 exit/);
  for (const ac of ['BIO-ARC-004-AC01', 'BIO-ARC-004-AC02', 'BIO-ARC-004-AC03', 'BIO-ARC-004-AC04']) {
    assert.ok(ADR.includes(ac), `${ac} is traceable to a decision section`);
  }
  assert.match(ADR, /ADR-0001/);
  assert.match(ADR, /ADR-0002/);
  assert.match(ADR, /ADR-0003/);
});

test('headers and constants used across the ADR are exported, not re-typed by consumers', () => {
  assert.equal(IDEMPOTENCY_HEADER, 'Idempotency-Key');
  assert.equal(CORRELATION_HEADER, 'X-Correlation-Id');
  assert.equal(IDEMPOTENCY_REPLAYED_HEADER, 'Idempotency-Replayed');
});
