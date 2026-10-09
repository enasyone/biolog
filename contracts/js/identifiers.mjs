// BIOLOG canonical identifiers — ADR-0001 Decision 1.
// Owner: Kemi (Chief Architect & Integrator). Shared contract: do not fork this logic
// into a feature module. Registered types live in ../schemas/identifier-registry.json.

import { randomBytes } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const REGISTRY = JSON.parse(
  readFileSync(fileURLToPath(new URL('../schemas/identifier-registry.json', import.meta.url)), 'utf8'),
);

/** Registered entity type -> registry entry. */
export const TYPES = Object.freeze(Object.fromEntries(REGISTRY.types.map((t) => [t.type, Object.freeze(t)])));
const BY_PREFIX = Object.freeze(Object.fromEntries(REGISTRY.types.map((t) => [t.prefix, t])));

// UUIDv7, lowercase, dashed. The `7` and the `[89ab]` pin version and RFC 9562 variant:
// a v4 value sneaking in is a contract violation, not a harmless variation.
const UUID_V7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const CANONICAL = new RegExp(`^(${Object.keys(BY_PREFIX).join('|')})_${UUID_V7.source.slice(1, -1)}$`);

export class IdentifierError extends Error {
  constructor(message, code) {
    super(message);
    this.name = 'IdentifierError';
    this.code = code;
  }
}

/** True when `value` is a well-formed canonical identifier of any registered type. */
export function isCanonicalId(value) {
  return typeof value === 'string' && CANONICAL.test(value);
}

/**
 * Parse a canonical identifier, optionally asserting its type.
 * Passing `expectedType` is how a service boundary catches a credential id used where a
 * person id was expected — before it reaches a repository.
 */
export function parseId(value, expectedType) {
  if (typeof value !== 'string' || !CANONICAL.test(value)) {
    throw new IdentifierError(`not a canonical BIOLOG identifier: ${JSON.stringify(value)}`, 'invalid_identifier');
  }
  const [prefix, uuid] = [value.slice(0, value.indexOf('_')), value.slice(value.indexOf('_') + 1)];
  const type = BY_PREFIX[prefix].type;
  if (expectedType !== undefined && type !== expectedType) {
    throw new IdentifierError(`expected a ${expectedType} identifier, got ${type}`, 'identifier_type_mismatch');
  }
  return { id: value, type, prefix, uuid, plane: BY_PREFIX[prefix].plane };
}

/** Mint a new identifier. Safe offline: no coordination, no server round trip. */
export function newId(type) {
  const entry = TYPES[type];
  if (!entry) throw new IdentifierError(`unregistered identifier type: ${JSON.stringify(type)}`, 'unregistered_type');
  return `${entry.prefix}_${uuidV7()}`;
}

/** RFC 9562 UUIDv7: 48-bit Unix ms, version 7, variant 0b10, remainder random. */
export function uuidV7(nowMs = Date.now()) {
  const b = randomBytes(16);
  const ms = BigInt(nowMs);
  for (let i = 0; i < 6; i += 1) b[i] = Number((ms >> BigInt(8 * (5 - i))) & 0xffn);
  b[6] = (b[6] & 0x0f) | 0x70; // version 7
  b[8] = (b[8] & 0x3f) | 0x80; // variant 0b10
  const hex = b.toString('hex');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** The 16 raw bytes to store: MySQL BINARY(16), SQLite BLOB. The prefix is not stored. */
export function toStorageBytes(id) {
  return Buffer.from(parseId(id).uuid.replaceAll('-', ''), 'hex');
}

/** Rebuild the wire form from stored bytes plus the column's known type. */
export function fromStorageBytes(type, bytes) {
  const entry = TYPES[type];
  if (!entry) throw new IdentifierError(`unregistered identifier type: ${JSON.stringify(type)}`, 'unregistered_type');
  const buf = Buffer.from(bytes);
  if (buf.length !== 16) {
    throw new IdentifierError(`identifier storage form must be 16 bytes, got ${buf.length}`, 'invalid_identifier');
  }
  const h = buf.toString('hex');
  const id = `${entry.prefix}_${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
  return parseId(id, type).id;
}

/**
 * Embedded creation time, for index-locality reasoning and debugging only.
 * ADR-0001 Decision 1.3: this is NOT a business timestamp. At the edge it comes from a
 * device clock. Never read it as occurred_at and never order security decisions by it.
 */
export function orderingTimestampMs(id) {
  return Number(BigInt('0x' + parseId(id).uuid.replaceAll('-', '').slice(0, 12)));
}
