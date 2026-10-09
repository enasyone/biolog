// BIOLOG optimistic concurrency, idempotency and sync conflict rules — ADR-0001 Decision 3.

import { createHash } from 'node:crypto';

/** Aggregates where a version mismatch may never be auto-merged (ADR-0001 Decision 3.5). */
export const SECURITY_SENSITIVE_AGGREGATES = Object.freeze(['person', 'credential', 'biometric']);

/** Immutable or append-only records carry no version and reject updates (Decision 3.3). */
export const APPEND_ONLY_AGGREGATES = Object.freeze([
  'photo_master', 'card_template_version', 'document_template_version',
  'document_evidence', 'biometric_evidence', 'audit_event', 'event',
]);

export const INITIAL_VERSION = 1;

export class ConcurrencyError extends Error {
  constructor(message, code, status, detail = {}) {
    super(message);
    this.name = 'ConcurrencyError';
    this.code = code;
    this.status = status;
    this.detail = detail;
  }
}

export function nextVersion(version) {
  if (!Number.isInteger(version) || version < INITIAL_VERSION) {
    throw new ConcurrencyError(`version must be an integer >= ${INITIAL_VERSION}`, 'invalid_version', 500, { version });
  }
  return version + 1;
}

/**
 * Compare-and-set gate for every mutating write.
 * A missing precondition FAILS (428). It is deliberately not a last-write-wins fallback:
 * a client that cannot say what it believes it is overwriting does not get to overwrite.
 */
export function checkPrecondition({ storedVersion, expectedVersion }) {
  if (expectedVersion === undefined || expectedVersion === null) {
    throw new ConcurrencyError(
      'expected_version (or If-Match) is required on a mutating write',
      'precondition_required', 428, { current_version: storedVersion },
    );
  }
  if (!Number.isInteger(expectedVersion)) {
    throw new ConcurrencyError('expected_version must be an integer', 'invalid_version', 400, { expectedVersion });
  }
  if (expectedVersion !== storedVersion) {
    throw new ConcurrencyError(
      `version conflict: expected ${expectedVersion}, stored ${storedVersion}`,
      'concurrency_conflict', 409, { current_version: storedVersion },
    );
  }
  return { ok: true, next_version: nextVersion(storedVersion) };
}

/** Append-only records have no concurrency token because they are never updated. */
export function assertUpdatable(aggregateType) {
  if (APPEND_ONLY_AGGREGATES.includes(aggregateType)) {
    throw new ConcurrencyError(
      `${aggregateType} is append-only; supersede it with a new record instead of updating it`,
      'immutable_record', 409, { aggregate_type: aggregateType },
    );
  }
  return true;
}

/** Stable fingerprint of a request body, so replays can be told from key reuse. */
export function requestFingerprint(payload) {
  return createHash('sha256').update(canonicalJson(payload)).digest('hex');
}

function canonicalJson(value) {
  if (value === null || typeof value !== 'object') return JSON.stringify(value) ?? 'null';
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  return `{${Object.keys(value).sort().map((k) => `${JSON.stringify(k)}:${canonicalJson(value[k])}`).join(',')}}`;
}

/**
 * Idempotency gate for retry-prone writes (card activation, production completion,
 * offline sync intake, webhook intake).
 * Same key + same body replays the stored result. Same key + different body is a client
 * defect and is surfaced as 409, not absorbed — a retried activation must never mint a
 * second card, and a changed one must never hide behind an old key.
 */
export function resolveIdempotency({ storedRecord, fingerprint }) {
  if (!storedRecord) return { outcome: 'EXECUTE' };
  if (storedRecord.fingerprint === fingerprint) {
    return { outcome: 'REPLAY', result: storedRecord.result };
  }
  throw new ConcurrencyError(
    'idempotency key reused with a different request body',
    'idempotency_key_reuse', 409, { idempotency_key: storedRecord.key },
  );
}

/**
 * Classify an offline mutation on intake (Decision 3.5).
 * Security-sensitive aggregates never last-write-wins; without a written merge rule the
 * default for everything else is also CONFLICT, so silence is safe rather than lossy.
 */
export function classifySyncIntake({ aggregateType, baseVersion, storedVersion, mergeRule = null }) {
  if (baseVersion === storedVersion) return { outcome: 'APPLY', next_version: nextVersion(storedVersion) };
  if (SECURITY_SENSITIVE_AGGREGATES.includes(aggregateType)) {
    return { outcome: 'CONFLICT', sync_state: 'CONFLICT', queue: 'sync-resolution', reason: 'security_sensitive_version_mismatch' };
  }
  if (typeof mergeRule === 'function') {
    return { outcome: 'MERGE', sync_state: 'PENDING', merged: mergeRule({ baseVersion, storedVersion }) };
  }
  return { outcome: 'CONFLICT', sync_state: 'CONFLICT', queue: 'sync-resolution', reason: 'no_declared_merge_rule' };
}

/** Event payload shape version — unrelated to an aggregate's state version (Decision 3.6). */
export function isBreakingSchemaChange({ removedFields = [], retypedFields = [], addedOptionalFields = [] }) {
  void addedOptionalFields;
  return removedFields.length > 0 || retypedFields.length > 0;
}
