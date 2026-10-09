// BIOLOG API versioning, error, idempotency and pagination conventions — ADR-0004.
// Owner: Kemi (Chief Architect & Integrator). Deliberately thin: it reuses ADR-0001's
// idempotency primitives and ADR-0003's redaction/identifier machinery rather than
// re-deriving either. Feature endpoints build their HTTP responses through this module.

import { newId, isCanonicalId } from './identifiers.mjs';
import { nowUtc } from './clock.mjs';
import { requestFingerprint, resolveIdempotency, ConcurrencyError } from './concurrency.mjs';
import { redact, escapeSegment } from './audit.mjs';

/** `/api/v1`. Bump only on a breaking change (ADR-0004 Decision 1.3); additive changes stay here. */
export const API_VERSION = 'v1';
export const API_PATH_PREFIX = `/api/${API_VERSION}`;

/** Distinct from ADR-0001's `precondition_required`, though both may carry HTTP 428. */
export const IDEMPOTENCY_HEADER = 'Idempotency-Key';
export const CORRELATION_HEADER = 'X-Correlation-Id';
export const IDEMPOTENCY_REPLAYED_HEADER = 'Idempotency-Replayed';

/** Stored idempotency records are kept at least this long (ADR-0004 Decision 4.3). */
export const IDEMPOTENCY_RETENTION_FLOOR_MS = 24 * 60 * 60 * 1000;

/** Bounds on how long a concurrent identical retry waits for the winner to finish (Decision 4.4). */
export const IDEMPOTENCY_POLL_INTERVAL_MS = 20;
export const IDEMPOTENCY_POLL_TIMEOUT_MS = 5000;

export const DEFAULT_PAGE_LIMIT = 50;
export const MAX_PAGE_LIMIT = 200;

/**
 * Optional shared base for a new domain error class. `toProblemResponse` does not require
 * it — it duck-types on `.code`/`.status`/`.message`/`.detail`, which `TenantContextError`,
 * `ConcurrencyError` and `AuditError` already expose without extending this or one another.
 * Those three are deliberately left unchanged by this story; this base is for new code.
 */
export class BiologApiError extends Error {
  constructor(message, code, status = 500, detail = {}) {
    super(message);
    this.name = 'BiologApiError';
    this.code = code;
    this.status = status;
    this.detail = detail;
  }
}

export class ApiConventionError extends BiologApiError {}

/**
 * Serialise ANY error exposing `.code`, `.status`, `.message` (duck-typed, not by
 * inheritance) into the one platform problem-response shape.
 *
 * `detail` on the input error is used as-is: an error class is responsible for having
 * already built it through `redact()` at the point it threw, exactly as an audit event's
 * `detail` is built at the point it is emitted (ADR-0004 Decision 2.3). This function does
 * not re-redact, because it cannot know the allowlist an error's author intended — trying
 * to guess one here would be worse than requiring the caller to have gotten it right.
 */
/** Top-level fields of the envelope. A `detail` entry sharing one of these names would
 * shadow the real value in the same document and mislead whatever parses it. */
const RESERVED_PROBLEM_FIELDS = new Set(['code', 'message', 'status', 'correlation_id', 'detail', 'errors']);

const PROBLEM_CODE_PATTERN = /^[a-z][a-z0-9_]*$/;

/**
 * Validate one `{ path, code, message }` item against exactly what
 * `validation-error-item.schema.json` requires. Shared by `toValidationProblem` (which
 * builds items from raw segments) and `toProblemResponse` (which may receive already-built
 * items directly on `error.errors`) so the two never drift into checking different things.
 * Returns a clean `{ path, code, message }` — never a spread of whatever extra properties
 * the input happened to carry.
 */
function assertValidationErrorItem(item, { fieldName = 'errors[]' } = {}) {
  if (!item || typeof item !== 'object' || Array.isArray(item)) {
    throw new ApiConventionError(
      `${fieldName} item must be an object, got ${JSON.stringify(item)}`,
      'invalid_validation_errors', 500,
    );
  }
  if (typeof item.path !== 'string' || item.path.length === 0) {
    throw new ApiConventionError(`${fieldName} item.path must be a non-empty string`, 'invalid_validation_errors', 500);
  }
  if (typeof item.code !== 'string' || !PROBLEM_CODE_PATTERN.test(item.code)) {
    throw new ApiConventionError(
      `${fieldName} item.code must be snake_case, got ${JSON.stringify(item.code)}`,
      'invalid_validation_errors', 500,
    );
  }
  if (typeof item.message !== 'string' || item.message.length === 0) {
    throw new ApiConventionError(`${fieldName} item.message must be a non-empty string`, 'invalid_validation_errors', 500);
  }
  return { path: item.path, code: item.code, message: item.message };
}

export function toProblemResponse(error, { correlationId } = {}) {
  if (!error || typeof error.code !== 'string' || typeof error.status !== 'number') {
    throw new ApiConventionError(
      'toProblemResponse requires an error exposing string .code and numeric .status',
      'invalid_error_shape', 500, { received: typeof error },
    );
  }
  // QA-03 (Seyi review): duck-typing on the SHAPE of .code/.status is not the same as
  // validating their CONTENT. An error carrying a malformed code or an out-of-range status
  // was passed straight through, producing a document the platform's own published schema
  // would reject — a builder is not "the format" if it can emit something the format
  // forbids. Every constraint checked here mirrors problem-response.schema.json exactly.
  if (!PROBLEM_CODE_PATTERN.test(error.code) || error.code.length > 64) {
    throw new ApiConventionError(
      `error.code must be snake_case, got ${JSON.stringify(error.code)}`,
      'invalid_error_shape', 500, { field: 'code' },
    );
  }
  if (!Number.isInteger(error.status) || error.status < 100 || error.status > 599) {
    throw new ApiConventionError(
      `error.status must be an HTTP status integer (100-599), got ${JSON.stringify(error.status)}`,
      'invalid_error_shape', 500, { field: 'status' },
    );
  }
  if (Array.isArray(error.errors) && error.errors.length > 0 && error.code !== 'validation_failed') {
    // ADR-0004 Decision 5.3/2.1: errors[] appears only alongside validation_failed. An
    // error class attaching it to any other code would produce a document this platform's
    // own convention says cannot exist.
    throw new ApiConventionError(
      `errors[] may only accompany code "validation_failed", not ${JSON.stringify(error.code)}`,
      'invalid_error_shape', 500, { field: 'errors' },
    );
  }
  // QA-03 round 2: items handed straight through on error.errors were never checked
  // against the same shape toValidationProblem enforces when it builds them itself — a
  // primitive array entry, or a malformed path/code/message, passed unchanged. Every item
  // is now validated (and reconstructed clean, never a blind spread of extra fields) with
  // the identical rule wherever it comes from.
  const checkedErrors = Array.isArray(error.errors) && error.errors.length > 0
    ? error.errors.map((item) => assertValidationErrorItem(item))
    : undefined;
  if (typeof error.message !== 'string' || error.message.length === 0) {
    if (error.message !== undefined && error.message !== null && error.message !== '') {
      // Present but the wrong type: a caller error, not something to coerce quietly.
      throw new ApiConventionError(
        `error.message must be a string when provided, got ${typeof error.message}`,
        'invalid_error_shape', 500, { field: 'message' },
      );
    }
  }
  if (error.detail && typeof error.detail === 'object') {
    const collision = Object.keys(error.detail).find((key) => RESERVED_PROBLEM_FIELDS.has(key));
    // A detail object built through redact() with the wrong allowlist could still, in
    // principle, land on a name like "correlation_id" — the same word this envelope
    // already uses at the top level for something else entirely. Refuse rather than emit
    // a document where a consumer parsing "correlation_id" could get either value
    // depending on how it reads the object.
    if (collision) {
      throw new ApiConventionError(
        `detail must not contain a field named "${collision}"; it collides with a reserved problem-response field`,
        'reserved_detail_field', 500, { field: collision },
      );
    }
  }
  const body = {
    code: error.code,
    message: error.message || 'an error occurred',
    status: error.status,
    correlation_id: resolveCorrelationId(correlationId),
  };
  if (error.detail && Object.keys(error.detail).length > 0) body.detail = { ...error.detail };
  if (checkedErrors) body.errors = checkedErrors;
  return Object.freeze(body);
}

/**
 * Build a validation failure's problem response. `errors` entries take a raw `field` path
 * (an array of raw key segments, or a single raw key) which is escaped and joined here —
 * callers never hand-join a path themselves, so they cannot reintroduce ADR-0003's QA-04
 * collision in a second place.
 */
export function toValidationProblem(errors, { correlationId, message = 'validation failed' } = {}) {
  if (!Array.isArray(errors) || errors.length === 0) {
    throw new ApiConventionError('a validation problem requires at least one error entry', 'invalid_validation_errors', 500);
  }
  // QA-03 round 2: the top-level message option was never checked — only each item's was.
  if (typeof message !== 'string' || message.length === 0) {
    throw new ApiConventionError(
      `validation problem message must be a non-empty string, got ${JSON.stringify(message)}`,
      'invalid_validation_errors', 500,
    );
  }
  const items = errors.map(({ field, code, message: itemMessage }) => {
    // `field` must be an array of raw segments, never a bare string. A bare string is
    // exactly the shape that caused ADR-0003's QA-04: is "profile.name" one literal key or
    // two nested ones? This API refuses to guess — the caller states the real structure as
    // ["profile", "name"] or the single-element ["profile.name"], and escaping+joining
    // happens here, once, the same way for everyone.
    if (!Array.isArray(field) || field.length === 0 || field.some((s) => typeof s !== 'string' || s.length === 0)) {
      throw new ApiConventionError(
        'each validation error needs field as a non-empty array of raw segments, not a dot-joined string',
        'invalid_validation_errors', 500, { field },
      );
    }
    // Built here, then validated through the SAME check toProblemResponse applies to
    // items it receives directly — one rule, not two definitions that could drift apart.
    return assertValidationErrorItem(
      { path: field.map(escapeSegment).join('.'), code, message: (itemMessage !== undefined ? itemMessage : code) },
      { fieldName: 'validation error' },
    );
  });
  return Object.freeze({
    code: 'validation_failed',
    message,
    status: 400,
    correlation_id: resolveCorrelationId(correlationId),
    errors: items,
  });
}

/**
 * Resolve the request correlation id (ADR-0004 Decision 3). A caller-supplied value is
 * used only if it is a well-formed canonical evt_ identifier — the same fail-closed
 * posture as every typed identifier elsewhere: a malformed value never reaches downstream
 * code, including `buildAuditEvent`'s own validation, which would otherwise reject it
 * after the request has already started doing work.
 */
export function resolveCorrelationId(headerValue) {
  if (typeof headerValue === 'string' && isCanonicalId(headerValue) && headerValue.startsWith('evt_')) {
    return headerValue;
  }
  return newId('event');
}

// ---------------------------------------------------------------------------
// Write-endpoint idempotency (ADR-0004 Decision 4). Wraps ADR-0001's primitives; does not
// reimplement fingerprint comparison or the reuse-conflict rule.
// ---------------------------------------------------------------------------

/**
 * The tenant + operation + key scope an idempotency record is stored under.
 *
 * QA-02 (Seyi review): naive `${a}:${b}:${c}` concatenation is not injective when `b` and
 * `c` are both caller-controlled strings that may themselves contain `:` — the exact
 * failure ADR-0003's QA-04 found for `.`-joined redaction paths, one delimiter over. Rather
 * than invent a second custom escaping scheme, this key is `JSON.stringify` of the
 * three-element tuple: JSON's grammar delimits array elements with commas OUTSIDE quoted
 * strings, so a `:` (or any other character) inside `operation` or `idempotencyKey` can
 * never be mistaken for a boundary between them — the encoding is injective by
 * construction, using a well-tested primitive instead of a fifth hand-rolled one.
 */
export function idempotencyStorageKey({ tenantId, operation, idempotencyKey }) {
  // QA-04 (Seyi review): tenantId was never validated, so a raw, untrusted, non-canonical
  // value could become part of a shared storage scope. Concretely, `JSON.stringify(NaN)`
  // is `"null"` — the identical tuple component the control-plane scope uses deliberately
  // — so a caller who fails to resolve a real tenant (a bug, or a bypass) could alias a
  // tenant-scoped idempotency record onto the control plane's own key space. tenantId must
  // now be EXPLICITLY `null` (a deliberate control-plane operation, ADR-0002 §1.4) or a
  // canonical `ten_` identifier — never a raw value passed through unchecked, the same
  // fail-closed posture ADR-0002 already applies to every other tenant claim.
  if (tenantId !== null && (typeof tenantId !== 'string' || !isCanonicalId(tenantId) || !tenantId.startsWith('ten_'))) {
    throw new ApiConventionError(
      `tenantId must be a canonical tenant identifier or explicitly null for a control-plane operation, got ${JSON.stringify(tenantId)}`,
      'invalid_tenant_scope', 500,
    );
  }
  if (typeof operation !== 'string' || operation.length === 0) {
    throw new ApiConventionError('operation is required to scope an idempotency key', 'invalid_operation', 500);
  }
  if (typeof idempotencyKey !== 'string' || idempotencyKey.length === 0 || idempotencyKey.length > 255) {
    throw new ApiConventionError(
      'Idempotency-Key must be a non-empty string of at most 255 characters',
      'invalid_idempotency_key', 400,
    );
  }
  return JSON.stringify([tenantId, operation, idempotencyKey]);
}

/**
 * Gate a retry-prone write against a store exposing `claim`, `complete`, `release` and
 * `get` (ADR-0004 Decision 4.4). `execute` runs only for the request that wins the claim;
 * its resolved `{ status, body }` is what gets replayed on retry.
 *
 * QA-01 (Seyi review): a `get`-then-`execute`-then-`set` sequence, as three separate
 * awaited steps, is not atomic. Two requests arriving close together both `get` before
 * either `set`s, both see nothing stored, and both execute — the exact failure this
 * convention exists to prevent, on exactly the writes the spec names as retry-prone. A
 * crash after `execute` but before `set` has the identical effect on a later, sequential
 * retry: nothing was ever recorded, so the retry executes again.
 *
 * The fix moves the atomic operation to BEFORE `execute` runs, not after: `store.claim`
 * must atomically create an `IN_FLIGHT` marker only if none exists for the key — the same
 * pattern a real store implements with a unique-constraint insert or a `SET … NX`. The
 * request that wins the claim executes and then calls `complete` (recording the result) or
 * `release` (on an unexpected failure, so a later retry is not blocked forever by a claim
 * nobody will ever finish). A request that loses the claim never executes: if the existing
 * record is already `DONE`, its result is replayed (via `resolveIdempotency`, reused
 * unchanged from ADR-0001 — same-fingerprint replay, different-fingerprint conflict); if it
 * is still `IN_FLIGHT` with the same fingerprint, this is a genuine concurrent identical
 * retry, and it waits, bounded, for the winner to finish rather than executing a second
 * time. A wait that exceeds `pollTimeoutMs` is a visible, distinct failure
 * (`idempotency_wait_timeout`, 503) — never a silent second execution.
 *
 * A required-but-missing key is `428 idempotency_key_required` — deliberately a different
 * machine code from ADR-0001's `precondition_required`, even though both may carry the
 * same HTTP status (ADR-0004 Decision 4.2).
 */
export async function withIdempotency({
  tenantId, operation, idempotencyKey, required = true, payload, store, execute,
  pollIntervalMs = IDEMPOTENCY_POLL_INTERVAL_MS, pollTimeoutMs = IDEMPOTENCY_POLL_TIMEOUT_MS,
}) {
  if (idempotencyKey === undefined || idempotencyKey === null) {
    if (required) {
      throw new ApiConventionError(
        `${operation} requires an ${IDEMPOTENCY_HEADER} header for retry safety`,
        'idempotency_key_required', 428, { operation },
      );
    }
    const result = await execute();
    return { outcome: 'EXECUTE', replayed: false, ...result };
  }

  const key = idempotencyStorageKey({ tenantId, operation, idempotencyKey });
  const fingerprint = requestFingerprint(payload ?? {});

  const claim = await store.claim(key, fingerprint);
  if (claim.won) {
    return runClaimedWrite({ store, key, idempotencyKey, fingerprint, execute });
  }
  return waitForIdempotentOutcome({
    store, key, idempotencyKey, fingerprint, execute, record: claim.record, pollIntervalMs, pollTimeoutMs,
  });
}

/** Runs `execute` for the request that won the claim, then durably records the outcome. */
async function runClaimedWrite({ store, key, idempotencyKey, fingerprint, execute }) {
  let result;
  try {
    result = await execute();
  } catch (error) {
    // An unexpected (infrastructure-level) failure, not a business-outcome response — free
    // the claim so a genuine retry is not blocked forever by a claim nobody will finish. A
    // business failure the caller wants replayed consistently is instead a normal *result*
    // from `execute` (e.g. { status: 422, body: {...} }), captured below like any success.
    await store.release(key);
    throw error;
  }
  await store.complete(key, { key: idempotencyKey, fingerprint, result, storedAt: nowUtc() });
  return { outcome: 'EXECUTE', replayed: false, ...result };
}

/**
 * Resolves the outcome for a request that lost the claim race. `execute` is threaded
 * through and used only if the winner's claim is later found released (its own attempt
 * failed): every concurrent caller for the same key represents the same logical write —
 * that is what "identical fingerprint" already established — so a caller that reclaims an
 * abandoned slot runs the same operation the original winner would have.
 */
async function waitForIdempotentOutcome({ store, key, idempotencyKey, fingerprint, execute, record, pollIntervalMs, pollTimeoutMs }) {
  const deadline = Date.now() + pollTimeoutMs;
  let current = record;
  for (;;) {
    if (current && current.status === 'DONE') {
      // Reuses ADR-0001's fingerprint comparison unchanged: same content replays, a
      // different one is surfaced as idempotency_key_reuse, never silently absorbed.
      let resolution;
      try {
        resolution = resolveIdempotency({ storedRecord: current, fingerprint });
      } catch (error) {
        if (error instanceof ConcurrencyError) {
          throw new ApiConventionError(error.message, error.code, error.status, { ...error.detail, idempotency_key: idempotencyKey });
        }
        throw error;
      }
      return { outcome: 'REPLAY', replayed: true, ...resolution.result };
    }
    if (current && current.fingerprint !== fingerprint) {
      throw new ApiConventionError(
        'idempotency key reused with a different request body',
        'idempotency_key_reuse', 409, { idempotency_key: idempotencyKey },
      );
    }
    if (Date.now() >= deadline) {
      throw new ApiConventionError(
        'timed out waiting for a concurrent identical request to complete',
        'idempotency_wait_timeout', 503, { idempotency_key: idempotencyKey },
      );
    }
    await new Promise((resolve) => { setTimeout(resolve, pollIntervalMs); });
    current = await store.get(key);
    if (!current) {
      // The winner released its claim (its execute threw): the slot is open again. Try to
      // claim it ourselves rather than looping forever against a key nobody holds.
      const reclaim = await store.claim(key, fingerprint);
      if (reclaim.won) return runClaimedWrite({ store, key, idempotencyKey, fingerprint, execute });
      current = reclaim.record;
    }
  }
}

/**
 * Reference in-memory idempotency store (ADR-0004 Decision 4.4), demonstrating a store
 * that satisfies `withIdempotency`'s atomicity requirement. `claim` has no `await` inside
 * its body, so Node's single-threaded event loop cannot interleave two concurrent callers
 * between its check and its write — the same guarantee a real store must provide through
 * its own infrastructure (a unique-constraint insert, `SET key value NX`, …).
 */
export function createInMemoryIdempotencyStore() {
  const backing = new Map();
  return {
    // eslint-disable-next-line require-await
    async claim(key, fingerprint) {
      if (backing.has(key)) return { won: false, record: backing.get(key) };
      backing.set(key, { fingerprint, status: 'IN_FLIGHT', startedAt: nowUtc() });
      return { won: true };
    },
    async complete(key, record) {
      backing.set(key, { ...record, status: 'DONE' });
    },
    async release(key) {
      backing.delete(key);
    },
    async get(key) {
      return backing.get(key);
    },
  };
}

// ---------------------------------------------------------------------------
// Pagination (ADR-0004 Decision 5.1).
// ---------------------------------------------------------------------------

/** Clamp a caller-supplied limit into the endpoint's allowed range, never trusting it as-is. */
export function resolvePageLimit(requested, { defaultLimit = DEFAULT_PAGE_LIMIT, maxLimit = MAX_PAGE_LIMIT } = {}) {
  if (requested === undefined || requested === null) return defaultLimit;
  const n = Number(requested);
  if (!Number.isInteger(n) || n < 1) {
    throw new ApiConventionError(`limit must be a positive integer, got ${JSON.stringify(requested)}`, 'invalid_limit', 400);
  }
  return Math.min(n, maxLimit);
}

/**
 * Build the response envelope. `nextCursor` is opaque; callers never construct one by hand.
 *
 * QA-03: every field here is checked against the exact constraints
 * `paginated-list-response.schema.json` publishes, not just "does it look plausible" —
 * `limit` was previously allowed to be missing (the schema requires it), `next_cursor`
 * accepted any type (the schema requires string-or-null), and `total_count` accepted a
 * negative or fractional number (the schema requires a non-negative integer).
 */
export function buildPageEnvelope(data, { nextCursor = null, limit, totalCount } = {}) {
  if (!Array.isArray(data)) {
    throw new ApiConventionError('buildPageEnvelope requires data as an array', 'invalid_page_data', 500);
  }
  if (nextCursor !== null && typeof nextCursor !== 'string') {
    throw new ApiConventionError(
      `nextCursor must be a string or null, got ${typeof nextCursor}`, 'invalid_page_cursor', 500,
    );
  }
  if (!Number.isInteger(limit) || limit < 1) {
    throw new ApiConventionError(
      `limit is required and must be a positive integer, got ${JSON.stringify(limit)}`,
      'invalid_page_limit', 500,
    );
  }
  const page = { next_cursor: nextCursor, limit };
  if (totalCount !== undefined) {
    if (!Number.isInteger(totalCount) || totalCount < 0) {
      throw new ApiConventionError(
        `totalCount must be a non-negative integer when provided, got ${JSON.stringify(totalCount)}`,
        'invalid_page_total_count', 500,
      );
    }
    page.total_count = totalCount;
  }
  return Object.freeze({ data: [...data], page });
}

/**
 * Validate a filter/sort request against an endpoint's explicit allowlist (ADR-0004
 * Decision 5.2). An unrecognised key is a validation error, never silently dropped — a
 * dropped filter leaves the caller believing something was applied that was not.
 */
export function assertFilterAllowlist(requested, allow) {
  const permitted = new Set(allow);
  const unknown = Object.keys(requested ?? {}).filter((key) => !permitted.has(key));
  if (unknown.length > 0) {
    throw toValidationProblemThrow(unknown.map((field) => ({ field: [field], code: 'unknown_field', message: `${field} is not filterable/sortable on this endpoint` })));
  }
  return true;
}

function toValidationProblemThrow(errors) {
  const body = toValidationProblem(errors);
  const error = new ApiConventionError(body.message, body.code, body.status, {});
  error.errors = body.errors;
  return error;
}
