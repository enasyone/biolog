// BIOLOG audit, classification/redaction and event idempotency — ADR-0003.
// Owner: Kemi (Chief Architect & Integrator). One audit shape for the whole platform:
// build events through here, never by assembling an object by hand.

import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { newId, parseId, isCanonicalId } from './identifiers.mjs';
import { nowUtc, isUtcInstant } from './clock.mjs';

const REGISTRY = JSON.parse(
  readFileSync(fileURLToPath(new URL('../schemas/data-classification.json', import.meta.url)), 'utf8'),
);

/** Classification levels, ordered. `rank` is what "highest" means (ADR-0003 Decision 3). */
export const CLASSIFICATIONS = Object.freeze(
  Object.fromEntries(REGISTRY.levels.map((l) => [l.level, Object.freeze(l)])),
);
/** Levels an envelope may carry at all. `secret` is absent from this list on purpose. */
export const ENVELOPE_CLASSIFICATIONS = Object.freeze(
  REGISTRY.levels.filter((l) => l.envelope !== 'never').map((l) => l.level),
);
/**
 * `unknown` is a real option, not a gap. A surface that genuinely does not learn what kind
 * of actor it refused must be able to record that, rather than assert `user` and put a
 * wrong fact in the one record that exists to be trusted.
 */
export const ACTOR_TYPES = Object.freeze(['user', 'service_account', 'device', 'system', 'anonymous', 'unknown']);
export const OUTCOMES = Object.freeze(['ALLOWED', 'DENIED', 'ERROR']);
export const AUDIT_SCHEMA_VERSION = 1;

export class AuditError extends Error {
  constructor(message, code, status = 500, detail = {}) {
    super(message);
    this.name = 'AuditError';
    this.code = code;
    this.status = status;
    this.detail = detail;
  }
}

/** Rank of a level; unknown levels are treated as the most restrictive, not the least. */
export function classificationRank(level) {
  return CLASSIFICATIONS[level]?.rank ?? CLASSIFICATIONS.secret.rank;
}

/** The highest classification among those given (ADR-0003 Decision 3). */
export function highestClassification(levels) {
  return [...levels].reduce(
    (highest, level) => (classificationRank(level) > classificationRank(highest) ? level : highest),
    'public',
  );
}

/** Keys that can reach Object.prototype. Never projected, whatever an allowlist says. */
const UNSAFE_KEYS = new Set(['__proto__', 'constructor', 'prototype']);

/** How deep a projection will descend before it gives up and withholds. */
export const MAX_REDACTION_DEPTH = 8;

const isPlainObject = (v) => typeof v === 'object' && v !== null && !Array.isArray(v);
const isPrimitive = (v) => v === null || (typeof v !== 'object' && typeof v !== 'function');

/**
 * Escape one raw key so it can be joined into a path with `.` as an unambiguous delimiter.
 *
 * QA-04 (Seyi re-review round 3): a source is caller-controlled, including its key names.
 * Joining raw keys with `.` let a flat, literally-dotted key such as `"profile.name"`
 * produce the identical path string as the nested traversal `profile` -> `name`. An
 * allowlist entry for one then silently permitted the other, and a caller who controls
 * the source can choose which one shows up — a value the classification map never
 * intended to admit rides in on a path string that was supposed to name something else.
 *
 * The fix is the one every path-encoding bug needs: escape the delimiter and the escape
 * character itself, in that order, so no raw key can produce a string containing an
 * unescaped `.`. `\` -> `\\` first, then `.` -> `\.`. A path is then unambiguous: an
 * unescaped `.` is always a real delimiter between two distinct keys, never part of one.
 */
export function escapeSegment(key) {
  return String(key).replace(/\\/g, '\\\\').replace(/\./g, '\\.');
}

/** Freeze a structure all the way down, so "append-only" holds below the first level. */
export function deepFreeze(value) {
  if (value === null || typeof value !== 'object' || Object.isFrozen(value)) return value;
  Object.freeze(value);
  for (const key of Object.keys(value)) deepFreeze(value[key]);
  return value;
}

/**
 * Project a source object through an explicit allowlist, recursively.
 *
 * An allowlist, never a denylist. The hostile question is "what would a caller have to
 * control for this rule to be false", and under a denylist the answer is "the field
 * names" — which the caller controls completely. A field added by a later story, a nested
 * object, or a near-miss spelling of a blocked name all walk straight through a denylist.
 *
 * The same question one level down is what a shallow allowlist misses: a caller nests the
 * secret. Permitting `card` does not permit `card.template`. So:
 *
 * - Allowlist entries are **paths**: `surname`, `card.sequence`. A raw key's own literal
 *   `.` and `\` characters are escaped (`escapeSegment`) before being joined, so a flat
 *   key such as `"profile.name"` can never alias the nested traversal `profile` ->
 *   `name` — every path string names exactly one location, however the source is shaped.
 * - **A raw dotted key is withheld outright if it also happens to alias a real nested
 *   value in the same source** (QA-04, Seyi re-review round 3): if a caller supplies
 *   both `{ "profile.name": X }` and `{ profile: { name: Y } }`, splitting the flat key
 *   on its literal dots resolves to an ACTUAL value at `profile.name` in the same object.
 *   A classification map can only ever have been written with one of those two locations
 *   in mind, and the source's shape — which the caller controls — decides which one a
 *   naive implementation would honour. Rather than guess, both the flat key and the
 *   nested value it aliases are withheld, exactly as a cycle or an unregistered
 *   classification is: an ambiguity a caller can manufacture is not a decision this
 *   contract makes for them. Escaping (above) still applies for the ordinary case where
 *   no such collision exists; this rule only fires when one actually does.
 * - A leaf is emitted only if its path is allowlisted **and** its classification is a
 *   registered level an envelope may carry. Missing, unregistered or `secret` is withheld.
 *   Unregistered fails closed *and* can never become the envelope's classification, which
 *   the published schema would reject anyway.
 * - A container is descended into only if its own path is allowlisted, and it confers no
 *   permission on its children. Every leaf is decided on its own path.
 * - An array containing anything but primitives is withheld outright: an object array in
 *   an audit detail is a shape nobody needs, and withholding it is the fail-closed answer.
 * - Arrays of primitives are copied, never referenced (see `buildAuditEvent`).
 * - Cycles, excessive depth and prototype-reaching keys are withheld.
 *
 * Returns the projection, the **paths** — never the values — of what was left out, and the
 * highest classification of what was kept.
 */
/**
 * Find raw keys containing a literal `.` that also resolve to a real nested value when
 * their own name is split on those dots and walked from the SAME node they live in.
 *
 * `"profile.name"` split on `.` is `["profile", "name"]`; if this same object also has
 * `node.profile.name`, the flat key aliases that nested value — the exact QA-04 collision.
 * Every step of the walk requires an actual OWN property (`hasOwnProperty`), so this can
 * never be tricked into resolving through an inherited or prototype value.
 *
 * Adds the escaped path of both the flat key and the nested value it aliases to
 * `collisions`, so the caller can force-withhold both without re-deriving the mapping.
 */
function collectDottedKeyCollisions(node, prefix, collisions, depth, seen) {
  if (!isPlainObject(node) || depth > MAX_REDACTION_DEPTH) return;
  for (const key of Object.keys(node)) {
    if (UNSAFE_KEYS.has(key)) continue;
    const path = prefix ? `${prefix}.${escapeSegment(key)}` : escapeSegment(key);

    if (key.includes('.')) {
      const segments = key.split('.');
      let cursor = node;
      let resolvable = true;
      for (const segment of segments) {
        if (UNSAFE_KEYS.has(segment) || !isPlainObject(cursor)
          || !Object.prototype.hasOwnProperty.call(cursor, segment)) {
          resolvable = false;
          break;
        }
        cursor = cursor[segment];
      }
      if (resolvable) {
        collisions.add(path); // the flat, literally-dotted key itself
        let nestedPath = prefix;
        let nestedCursor = node;
        for (const segment of segments) {
          nestedCursor = nestedCursor[segment];
          nestedPath = nestedPath ? `${nestedPath}.${escapeSegment(segment)}` : escapeSegment(segment);
        }
        collisions.add(nestedPath); // the real nested value it aliases
        void nestedCursor;
      }
    }

    const value = node[key];
    if (isPlainObject(value) && !seen.has(value)) {
      collectDottedKeyCollisions(value, path, collisions, depth + 1, new Set([...seen, value]));
    }
  }
}

export function redact(source, { allow = [], classifications = {} } = {}) {
  if (!isPlainObject(source)) {
    throw new AuditError('redaction source must be a plain object', 'invalid_redaction_source', 500);
  }
  const permitted = new Set(allow);
  const withheld = [];
  let level = 'public';

  // Pre-pass: find every raw dotted key that aliases a real nested value in this same
  // source, and force both escaped paths into withheld regardless of what the allowlist
  // or classification map says (QA-04). Run once over the whole tree up front, since the
  // flat key and its nested partner can live at different recursion depths from each
  // other's perspective, but this collision set is keyed by fully-qualified escaped path
  // so a single lookup works uniformly wherever the main walk encounters either side.
  const forcedWithhold = new Set();
  collectDottedKeyCollisions(source, '', forcedWithhold, 0, new Set([source]));

  const project = (node, prefix, depth, seen) => {
    const out = {};
    if (depth > MAX_REDACTION_DEPTH) {
      withheld.push(prefix || '<root>');
      return out;
    }
    for (const key of Object.keys(node)) {
      // Escaped, not the raw key: an unescaped `.` in the path is always a real delimiter
      // between two distinct keys, never a literal dot inside one (QA-04).
      const path = prefix ? `${prefix}.${escapeSegment(key)}` : escapeSegment(key);
      const value = node[key];

      if (UNSAFE_KEYS.has(key)) { withheld.push(path); continue; }
      if (forcedWithhold.has(path)) { withheld.push(path); continue; }
      if (!permitted.has(path)) { withheld.push(path); continue; }

      if (isPlainObject(value)) {
        if (seen.has(value)) { withheld.push(path); continue; } // cycle
        out[key] = project(value, path, depth + 1, new Set([...seen, value]));
        continue;
      }
      if (Array.isArray(value)) {
        if (!value.every(isPrimitive)) { withheld.push(path); continue; }
        const fieldLevel = classifications[path];
        if (!ENVELOPE_CLASSIFICATIONS.includes(fieldLevel)) { withheld.push(path); continue; }
        out[key] = [...value]; // copied, so a later push to the source cannot reach it
        level = highestClassification([level, fieldLevel]);
        continue;
      }
      if (!isPrimitive(value)) { withheld.push(path); continue; } // function, symbol, class
      const fieldLevel = classifications[path];
      // Unclassified is not "safe", and an unregistered level is not a level.
      if (!ENVELOPE_CLASSIFICATIONS.includes(fieldLevel)) { withheld.push(path); continue; }
      out[key] = value;
      level = highestClassification([level, fieldLevel]);
    }
    return out;
  };

  const detail = project(source, '', 0, new Set([source]));
  return {
    detail: deepFreeze(detail),
    withheld: deepFreeze(withheld.sort()),
    classification: level,
  };
}

/**
 * Reject hashing as a substitute for redacting low-entropy personal data.
 *
 * A hash of a date of birth, a national identifier, a staff number or a phone number is
 * reversible by anyone willing to enumerate the input space, and each of those spaces is
 * small. Publishing one in an audit trail is pseudonymisation dressed as redaction. Where
 * a record must be correlatable without the value, it carries the canonical identifier of
 * the record instead: already non-secret, already meaningless outside its tenant, and
 * already what the rest of the system joins on.
 */
export function correlatableReference(canonicalId) {
  if (!isCanonicalId(canonicalId)) {
    throw new AuditError(
      'a correlatable reference must be a canonical identifier; hashing a personal value is not redaction',
      'invalid_reference', 500, { value: typeof canonicalId },
    );
  }
  return canonicalId;
}

/**
 * Build an audit event. The only sanctioned way to make one.
 *
 * `detail` is produced by projection through `allow`, so an unredacted value never exists
 * on the object — there is no moment at which a caller could read it back out.
 */
export function buildAuditEvent({
  tenant_id = null, actor, device = null, action, target, outcome, reason_code,
  correlation_id, causation_id = null, occurred_at, source = {}, allow = [],
  classifications = {}, schema_version = AUDIT_SCHEMA_VERSION, min_classification = 'public',
} = {}) {
  if (tenant_id !== null) parseId(tenant_id, 'tenant');
  if (!actor || !ACTOR_TYPES.includes(actor.type) || typeof actor.id !== 'string' || actor.id.length === 0) {
    throw new AuditError(
      'actor must be { type, id }; a bare name cannot tell a user from a service account of the same name',
      'invalid_actor', 500, { actor: actor?.type ?? null },
    );
  }
  if (typeof action !== 'string' || !/^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$/.test(action)) {
    throw new AuditError(`action must be a namespaced verb, got ${JSON.stringify(action)}`, 'invalid_action', 500);
  }
  if (!target || typeof target.type !== 'string' || target.type.length === 0) {
    throw new AuditError('target must be { type, id }', 'invalid_target', 500);
  }
  if (!OUTCOMES.includes(outcome)) {
    throw new AuditError(`outcome must be one of ${OUTCOMES.join(', ')}`, 'invalid_outcome', 500, { outcome });
  }
  if (typeof reason_code !== 'string' || reason_code.length === 0) {
    // Required on success too: an audit that records only failures cannot prove that
    // something was permitted, and then "no record" is ambiguous.
    throw new AuditError('reason_code is required on every outcome, including ALLOWED', 'invalid_reason_code', 500);
  }
  if (!isCanonicalId(correlation_id) || !correlation_id.startsWith('evt_')) {
    throw new AuditError('correlation_id must be a canonical event identifier', 'invalid_correlation_id', 500);
  }
  // causation_id is required (present) but its value is null for a root event, so only a
  // non-null value is shape-checked — the same rule correlation_id gets, applied once
  // causation_id itself became a required field rather than an optional afterthought.
  if (causation_id !== null && (!isCanonicalId(causation_id) || !causation_id.startsWith('evt_'))) {
    throw new AuditError('causation_id must be null or a canonical event identifier', 'invalid_causation_id', 500);
  }
  const claimed = occurred_at ?? nowUtc();
  if (!isUtcInstant(claimed)) {
    throw new AuditError(`occurred_at must be a UTC instant, got ${JSON.stringify(occurred_at)}`, 'invalid_instant', 500);
  }

  const projected = redact(source, { allow, classifications });
  const { detail, withheld } = projected;
  // An envelope that names a person or credential identifier is not public, even when it
  // carries no fields: min_classification lets the caller state that floor.
  const classification = highestClassification([projected.classification, min_classification]);

  // Deep, not shallow: QA-02 showed a shallow freeze still let a caller mutate a nested
  // object after construction — changing an append-only record, injecting a field its
  // classification never saw, and racing whatever the async sink was about to persist.
  return deepFreeze({
    event_id: newId('event'),
    schema_version,
    tenant_id,
    actor: { type: actor.type, id: actor.id },
    device: device === null ? null : ({
      device_id: device.device_id ?? null,
      workstation: device.workstation ?? null,
    }),
    action,
    target: { type: target.type, id: target.id ?? null },
    outcome,
    reason_code,
    correlation_id,
    causation_id,
    occurred_at: claimed,
    received_at: nowUtc(),
    classification,
    withheld,
    detail,
  });
}

// ---------------------------------------------------------------------------
// Event idempotency — ADR-0003 Decision 4.
// ---------------------------------------------------------------------------

/** The dedupe key a consumer stores. Tenant-scoped, so ids cannot collide across tenants. */
export function dedupeKey(event) {
  if (!event || !isCanonicalId(event.event_id) || !event.event_id.startsWith('evt_')) {
    throw new AuditError('event_id must be a canonical event identifier', 'invalid_event_id', 400);
  }
  return `${event.tenant_id ?? 'control-plane'}:${event.event_id}`;
}

/**
 * Content fingerprint of an event.
 *
 * `received_at` is excluded because the server stamps it per delivery: including it would
 * make every redelivery look like new content and defeat the dedupe it exists to support.
 * Everything the producer asserted is included.
 */
export function eventFingerprint(event) {
  const { received_at: _ignored, ...asserted } = event ?? {};
  return createHash('sha256').update(canonicalJson(asserted)).digest('hex');
}

function canonicalJson(value) {
  if (value === null || typeof value !== 'object') return JSON.stringify(value) ?? 'null';
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  return `{${Object.keys(value).sort().map((k) => `${JSON.stringify(k)}:${canonicalJson(value[k])}`).join(',')}}`;
}

/**
 * Classify a delivery against what the consumer has already seen.
 *
 * The CONFLICT case is the point. `event_id` is minted by the producer, which at the edge
 * is a device. Under a key-only dedupe, a producer that reuses an identifier with
 * different content would have its second event silently dropped — a way to make a
 * revocation disappear behind a replayed id. Comparing content makes the collision loud.
 */
export function classifyDelivery(seen, event) {
  const key = dedupeKey(event);
  const fingerprint = eventFingerprint(event);
  const previous = seen instanceof Map ? seen.get(key) : seen?.[key];
  if (previous === undefined) return { outcome: 'FIRST', key, fingerprint };
  if (previous === fingerprint) return { outcome: 'DUPLICATE', key, fingerprint };
  return {
    outcome: 'CONFLICT', key, fingerprint,
    reason_code: 'event_id_reuse',
    detail: 'an event id was redelivered with different content; surface it, never absorb it',
  };
}

/** Apply a delivery to a Map of seen keys, returning the classification. */
export function recordDelivery(seen, event) {
  const result = classifyDelivery(seen, event);
  if (result.outcome === 'FIRST') seen.set(result.key, result.fingerprint);
  return result;
}
