// Developer tests for BIO-ARC-003. Every test name carries the AC it proves.
// Run: node --test contracts/js/   (no dependencies, offline-safe)
// All tenants, actors, records and values here are SYNTHETIC.

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import {
  CLASSIFICATIONS, ENVELOPE_CLASSIFICATIONS, ACTOR_TYPES, OUTCOMES, AUDIT_SCHEMA_VERSION,
  classificationRank, highestClassification, redact, correlatableReference, buildAuditEvent,
  dedupeKey, eventFingerprint, classifyDelivery, recordDelivery, AuditError,
  deepFreeze, MAX_REDACTION_DEPTH, escapeSegment,
  buildDenialEvent, newId, isUtcInstant, isCanonicalId,
} from './index.mjs';

const schema = (name) =>
  JSON.parse(readFileSync(fileURLToPath(new URL(`../schemas/${name}`, import.meta.url)), 'utf8'));
const ADR = readFileSync(
  fileURLToPath(new URL('../../docs/adr/ADR-0003-audit-evidence-and-event-envelopes.md', import.meta.url)), 'utf8',
);

const TENANT_A = 'ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77';
const TENANT_B = 'ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03';
const PERSON = 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d';
const DEVICE = 'dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03';

const audit = (over = {}) => buildAuditEvent({
  tenant_id: TENANT_A, actor: { type: 'user', id: 'admin-1' }, action: 'credential.activate',
  target: { type: 'credential', id: 'crd_0199e0c2-0a13-7f64-8b25-3d8e5f7a1c92' },
  outcome: 'ALLOWED', reason_code: 'policy_satisfied', correlation_id: newId('event'), ...over,
});

// ---------------------------------------------------------------------------
// BIO-ARC-003-AC01 — audit envelope: tenant, actor, device, action, target,
// reason, correlation, time.
// ---------------------------------------------------------------------------

test('AC01: the envelope carries every field the criterion names', () => {
  const event = audit({ device: { device_id: DEVICE, workstation: 'ENROL-STATION-07' } });
  assert.equal(event.tenant_id, TENANT_A);
  assert.deepEqual({ ...event.actor }, { type: 'user', id: 'admin-1' });
  assert.deepEqual({ ...event.device }, { device_id: DEVICE, workstation: 'ENROL-STATION-07' });
  assert.equal(event.action, 'credential.activate');
  assert.equal(event.target.type, 'credential');
  assert.equal(event.reason_code, 'policy_satisfied');
  assert.ok(isCanonicalId(event.correlation_id));
  assert.ok(isUtcInstant(event.occurred_at) && isUtcInstant(event.received_at));
  for (const field of schema('audit-event.schema.json').required) {
    assert.ok(event[field] !== undefined, `${field} present`);
  }
});

test('AC01: an actor is structured, never a bare string', () => {
  assert.throws(() => audit({ actor: 'admin-1' }), (e) => e.code === 'invalid_actor');
  assert.throws(() => audit({ actor: { id: 'admin-1' } }), (e) => e.code === 'invalid_actor');
  assert.throws(() => audit({ actor: { type: 'root', id: 'x' } }), (e) => e.code === 'invalid_actor');
  assert.throws(() => audit({ actor: { type: 'user', id: '' } }), (e) => e.code === 'invalid_actor');
  for (const type of ACTOR_TYPES) assert.equal(audit({ actor: { type, id: 'a' } }).actor.type, type);
  assert.ok(ACTOR_TYPES.includes('unknown'), 'a surface may record that it does not know');
  assert.deepEqual(schema('audit-event.schema.json').properties.actor.properties.type.enum, [...ACTOR_TYPES]);
});

test('AC01: outcome and reason_code are required on success, not only on failure', () => {
  // Without them, "no record" means either "did not happen" or "happened and was allowed".
  assert.throws(() => audit({ outcome: undefined }), (e) => e.code === 'invalid_outcome');
  assert.throws(() => audit({ outcome: 'ok' }), (e) => e.code === 'invalid_outcome');
  assert.throws(() => audit({ reason_code: undefined }), (e) => e.code === 'invalid_reason_code');
  assert.throws(() => audit({ reason_code: '' }), (e) => e.code === 'invalid_reason_code');
  for (const outcome of OUTCOMES) assert.equal(audit({ outcome }).outcome, outcome);
});

test('AC01: an action must be a namespaced verb', () => {
  for (const action of ['activate', 'Credential.Activate', 'credential activate', 'credential.', '.activate', '', 'credential..activate', null]) {
    assert.throws(() => audit({ action }), (e) => e.code === 'invalid_action', `${JSON.stringify(action)} rejected`);
  }
  for (const action of ['credential.activate', 'tenant.access', 'person.export', 'document_session.review.approve']) {
    assert.equal(audit({ action }).action, action);
  }
});

test('AC01: a control-plane audit has a null tenant, and a bad tenant is refused', () => {
  assert.equal(audit({ tenant_id: null }).tenant_id, null);
  for (const tenant_id of ['ten_nope', PERSON, '', 'null']) {
    assert.throws(() => audit({ tenant_id }), Error, `${tenant_id} rejected`);
  }
});

test('AC01: an audit event is append-only and carries no payload or evidence', () => {
  const event = audit();
  assert.ok(Object.isFrozen(event));
  for (const forbidden of ['payload', 'record', 'version', 'evidence']) {
    assert.equal(event[forbidden], undefined, `must not carry ${forbidden}`);
  }
  assert.deepEqual(schema('audit-event.schema.json').not.anyOf.map((o) => o.required[0]).sort(),
    ['evidence', 'payload', 'record', 'version'].sort());
});

test('AC01: the times follow ADR-0001 — a claim and an authority', () => {
  const claimed = '2026-09-20T21:05:00.000Z';
  const event = audit({ occurred_at: claimed });
  assert.equal(event.occurred_at, claimed);
  assert.ok(isUtcInstant(event.received_at));
  assert.throws(() => audit({ occurred_at: '2026-02-30T12:00:00.000Z' }), (e) => e.code === 'invalid_instant');
  assert.throws(() => audit({ occurred_at: '2026-09-20T21:05:00Z' }), (e) => e.code === 'invalid_instant');
});

test('AC01: the correlation id ties the record to its request', () => {
  const correlation_id = newId('event');
  assert.equal(audit({ correlation_id }).correlation_id, correlation_id);
  for (const bad of [undefined, 'evt_nope', PERSON, '']) {
    assert.throws(() => audit({ correlation_id: bad }), (e) => e.code === 'invalid_correlation_id');
  }
});

test('AC01: the ARC-002 denial event is a conforming audit event', () => {
  // The alignment the dispatch required: one shape, so "everything that touched this
  // tenant" is a single query rather than a union of formats.
  const denial = buildDenialEvent({
    context_tenant_id: TENANT_A, attempted_tenant_id: TENANT_B, principal: 'reader-01',
    resource_type: 'person', resource_id: PERSON,
    reason_code: 'cross_tenant_access_denied', correlation_id: newId('event'),
  });
  for (const field of schema('audit-event.schema.json').required) {
    assert.ok(denial[field] !== undefined, `denial carries the audit field ${field}`);
  }
  assert.equal(denial.outcome, 'DENIED');
  assert.equal(denial.action, 'tenant.access');
  assert.equal(denial.target.id, PERSON);
  assert.equal(denial.attempted_tenant_id, TENANT_B, 'plus the one denial-specific field');
  assert.equal(denial.classification, 'internal', 'naming a person id is not a public record');

  const resolution = buildDenialEvent({
    context_tenant_id: null, attempted_tenant_id: null, principal: 'anonymous',
    resource_type: 'tenant_context', reason_code: 'tenant_context_missing', correlation_id: newId('event'),
  });
  assert.equal(resolution.action, 'tenant.resolve');
  assert.equal(resolution.tenant_id, null);
});

// --- causation_id alignment (god request, 2026-09-21) -------------------------------
// Leaving the audit envelope's own causation_id optional while the domain-event's was
// required+nullable was the same "enforced one step short" pattern as QA-01..03, one
// field over from where it had just been closed. Both envelopes now hold it identically.

test('AC01: causation_id is required on the audit envelope and explicitly nullable', () => {
  const s = schema('audit-event.schema.json');
  assert.ok(s.required.includes('causation_id'));
  assert.deepEqual(s.properties.causation_id.type, ['string', 'null']);
  const event = audit();
  assert.equal(event.causation_id, null, 'explicitly null, present either way');
  assert.ok('causation_id' in event);
});

test('AC01: a document missing causation_id fails the audit schema required list, same as device', () => {
  const auditRequired = schema('audit-event.schema.json').required;
  const { causation_id: _c, ...withoutCausation } = audit();
  assert.deepEqual(auditRequired.filter((f) => !(f in withoutCausation)), ['causation_id']);
});

test('AC01: causation_id accepts a canonical event id and rejects garbage, like correlation_id does', () => {
  const cause = newId('event');
  assert.equal(audit({ causation_id: cause }).causation_id, cause);
  for (const bad of ['not-an-id', PERSON, '', 42, {}]) {
    assert.throws(() => audit({ causation_id: bad }), (e) => e.code === 'invalid_causation_id',
      `${JSON.stringify(bad)} must be rejected`);
  }
  // null remains the sanctioned "no cause" value and is never shape-checked as an id.
  assert.equal(audit({ causation_id: null }).causation_id, null);
});

test('AC01/AC02: both envelopes hold causation_id to the identical rule', () => {
  const auditRule = schema('audit-event.schema.json').properties.causation_id;
  const eventRule = schema('event-envelope.schema.json').properties.causation_id;
  assert.ok(schema('audit-event.schema.json').required.includes('causation_id'));
  assert.ok(schema('event-envelope.schema.json').required.includes('causation_id'));
  const auditAllowsNull = auditRule.type?.includes('null');
  const eventAllowsNull = eventRule.anyOf?.some((o) => o.type === 'null');
  assert.ok(auditAllowsNull && eventAllowsNull, 'both are explicitly nullable, not just optional');
});

test('the ADR states the alignment is deliberate, not an inconsistency', () => {
  assert.match(ADR, /required here too, for the same reason it is required on the domain/);
  assert.match(ADR, /hold the field to the identical rule/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-003-AC02 — domain event envelope.
// ---------------------------------------------------------------------------

test('AC02: the envelope requires every field the criterion names', () => {
  const required = schema('event-envelope.schema.json').required;
  for (const field of ['event_id', 'aggregate_version', 'aggregate_type', 'aggregate_id', 'tenant_id',
    'correlation_id', 'occurred_at', 'schema_version']) {
    assert.ok(required.includes(field), `${field} is required`);
  }
  assert.ok('causation_id' in schema('event-envelope.schema.json').properties);
});

test('AC02: "version" means the aggregate version, and a bare version is still refused', () => {
  // The apparent conflict with ADR-0001 section 3.6, resolved in ADR-0003 Decision 2.
  const s = schema('event-envelope.schema.json');
  assert.deepEqual(s.not.required, ['version'], 'an event still has no version of its own');
  assert.equal(s.properties.aggregate_version.type, 'integer');
  assert.equal(s.properties.aggregate_version.minimum, 1);
  assert.match(ADR, /not a version \*of the event\*/);
});

test('AC02: an event may order against the aggregate version rather than a clock', () => {
  // ADR-0001 section 2.4 forbids wall-clock ordering for security-relevant decisions.
  const events = [3, 1, 2].map((aggregate_version) => ({
    aggregate_id: PERSON, aggregate_version, occurred_at: '2026-09-20T21:05:00.000Z',
  }));
  assert.deepEqual(events.slice().sort((a, b) => a.aggregate_version - b.aggregate_version)
    .map((e) => e.aggregate_version), [1, 2, 3], 'orderable with identical timestamps');
});

test('AC02: the idempotency key is optional and bounded', () => {
  const key = schema('event-envelope.schema.json').properties.idempotency_key;
  assert.equal(key.maxLength, 255);
  assert.equal(schema('event-envelope.schema.json').required.includes('idempotency_key'), false);
});

// ---------------------------------------------------------------------------
// BIO-ARC-003-AC03 — sensitive fields classified, redaction rules defined.
// ---------------------------------------------------------------------------

test('AC03: five levels are registered and ranked', () => {
  assert.deepEqual(Object.keys(CLASSIFICATIONS), ['public', 'internal', 'personal', 'sensitive', 'secret']);
  assert.deepEqual([...ENVELOPE_CLASSIFICATIONS], ['public', 'internal', 'personal', 'sensitive']);
  assert.equal(ENVELOPE_CLASSIFICATIONS.includes('secret'), false, 'secret may never appear');
  assert.deepEqual(schema('audit-event.schema.json').properties.classification.enum, [...ENVELOPE_CLASSIFICATIONS]);
  assert.ok(classificationRank('secret') > classificationRank('sensitive'));
  assert.ok(classificationRank('sensitive') > classificationRank('personal'));
  assert.ok(classificationRank('personal') > classificationRank('internal'));
});

test('AC03: an unknown classification is treated as the most restrictive, not the least', () => {
  assert.equal(classificationRank('made_up'), classificationRank('secret'));
  assert.equal(classificationRank(undefined), classificationRank('secret'));
  assert.equal(highestClassification(['public', 'made_up']), 'made_up', 'an unknown level wins');
});

test('AC03: redaction is an allowlist — an unlisted field never appears', () => {
  const source = { surname: 'Okafor', card_sequence: '001', notes: 'synthetic note', template: 'AAAA' };
  const { detail, withheld } = redact(source, {
    allow: ['surname', 'card_sequence'],
    classifications: { surname: 'personal', card_sequence: 'internal', notes: 'personal', template: 'secret' },
  });
  assert.deepEqual({ ...detail }, { surname: 'Okafor', card_sequence: '001' });
  assert.deepEqual([...withheld], ['notes', 'template']);
});

test('AC03/hostile: a field added later is withheld by default, which a denylist could not do', () => {
  // The hostile question for AC03: what would a caller have to control for the rule to be
  // false? Under a denylist, the field names — which the caller controls completely.
  const beforeSchemaChange = { surname: 'Okafor' };
  const afterSomeoneAddsAField = { surname: 'Okafor', national_id: '12345678901', ssn_like: 'X' };
  const allow = ['surname'];
  const classifications = { surname: 'personal' };
  const first = redact(beforeSchemaChange, { allow, classifications });
  const second = redact(afterSomeoneAddsAField, { allow, classifications });
  assert.deepEqual({ ...first.detail }, { ...second.detail }, 'new fields change nothing that is emitted');
  assert.deepEqual([...second.withheld], ['national_id', 'ssn_like']);
  for (const value of Object.values(second.detail)) {
    assert.notEqual(value, '12345678901', 'the new field never leaks');
  }
});

test('AC03/hostile: an unclassified field is withheld — unclassified is not "safe"', () => {
  const { detail, withheld } = redact({ mystery: 'value' }, { allow: ['mystery'], classifications: {} });
  assert.deepEqual({ ...detail }, {}, 'allowlisted but unclassified is still withheld');
  assert.deepEqual([...withheld], ['mystery']);
});

test('AC03/hostile: an allowlist cannot promote a secret into an envelope', () => {
  // Even a caller who names the field explicitly does not get it.
  const { detail, withheld } = redact(
    { biometric_template: 'AAECAwQ=', card_key: 'K', password: 'p' },
    {
      allow: ['biometric_template', 'card_key', 'password'],
      // Keys quoted so the repository secret scanner does not read a classification map
      // as a credential assignment. The scanner is right to be blunt about that shape.
      classifications: { 'biometric_template': 'secret', 'card_key': 'secret', 'password': 'secret' },
    },
  );
  assert.deepEqual({ ...detail }, {});
  assert.deepEqual([...withheld], ['biometric_template', 'card_key', 'password']);
});

test('AC03: a secret is absent, and only its name is recorded', () => {
  const event = audit({
    source: { template: 'AAECAwQ=', card_sequence: '001' },
    allow: ['card_sequence'],
    classifications: { template: 'secret', card_sequence: 'internal' },
  });
  assert.deepEqual([...event.withheld], ['template']);
  const serialised = JSON.stringify(event);
  assert.equal(serialised.includes('AAECAwQ='), false, 'the value appears nowhere in the record');
  assert.ok(serialised.includes('template'), 'but the omission is honest');
});

test('AC03: classification is the maximum of everything the envelope names', () => {
  const classifications = { a: 'public', b: 'internal', c: 'personal', d: 'sensitive' };
  assert.equal(redact({ a: 1 }, { allow: ['a'], classifications }).classification, 'public');
  assert.equal(redact({ a: 1, b: 2 }, { allow: ['a', 'b'], classifications }).classification, 'internal');
  assert.equal(redact({ a: 1, c: 3 }, { allow: ['a', 'c'], classifications }).classification, 'personal');
  assert.equal(redact({ a: 1, d: 4 }, { allow: ['a', 'd'], classifications }).classification, 'sensitive');
  assert.equal(highestClassification(['public', 'sensitive', 'internal']), 'sensitive');
});

test('AC03: an envelope naming an identifier can declare a classification floor', () => {
  const event = audit({ source: {}, allow: [], min_classification: 'internal' });
  assert.equal(event.classification, 'internal', 'not "public" merely because it carries no fields');
  assert.equal(audit({ source: { c: 1 }, allow: ['c'], classifications: { c: 'sensitive' }, min_classification: 'internal' })
    .classification, 'sensitive', 'the floor never lowers a higher value');
});

test('AC03: hashing a low-entropy personal value is refused as a redaction substitute', () => {
  for (const value of ['1990-01-01', '12345678901', 'STAFF-00042', '+2348012345678', '', null, undefined, 42]) {
    assert.throws(() => correlatableReference(value), (e) => {
      assert.ok(e instanceof AuditError);
      assert.equal(e.code, 'invalid_reference');
      return true;
    }, `${JSON.stringify(value)} is not a correlatable reference`);
  }
  assert.equal(correlatableReference(PERSON), PERSON, 'the canonical identifier is the join key');
  assert.match(ADR, /reversible by anyone willing to enumerate/);
});

test('AC03: redaction refuses a non-object source rather than guessing', () => {
  for (const source of [null, 'string', 42, ['a'], undefined]) {
    if (source === undefined) continue; // defaults to {}
    assert.throws(() => redact(source, { allow: [] }), (e) => e.code === 'invalid_redaction_source');
  }
  assert.deepEqual({ ...redact({}, { allow: [] }).detail }, {});
});

test('AC03: the registry records the rules, so they cannot be softened silently', () => {
  const registry = JSON.parse(readFileSync(
    fileURLToPath(new URL('../schemas/data-classification.json', import.meta.url)), 'utf8'));
  for (const rule of ['secrets_are_absent_not_redacted', 'allowlist_not_denylist',
    'no_hashing_low_entropy', 'classification_is_the_maximum']) {
    assert.ok(registry.rules[rule], `${rule} is recorded`);
  }
  assert.equal(registry.levels.find((l) => l.level === 'secret').envelope, 'never');
});

// --- BIO-ARC-003 QA-01/02/03 regression (Seyi review, 2026-09-21) -------------------
// QA-01: the allowlist was shallow, so a caller could nest the secret; and an
// unregistered classification copied the value and emitted a level the schema rejects.
// QA-02: the envelope was shallow-frozen, so a retained nested reference let a built,
// append-only event be changed after the fact.
// QA-03: two AC-required slots were defined but not schema-required.

test('AC03/QA-01: permitting a container does not permit what is nested inside it', () => {
  // The attack: nest the secret under an allowlisted object.
  const { detail, withheld, classification } = redact(
    { card: { sequence: '001', template: 'SECRET-TEMPLATE' } },
    { allow: ['card', 'card.sequence'], classifications: { 'card.sequence': 'internal', 'card.template': 'secret' } },
  );
  assert.deepEqual(JSON.parse(JSON.stringify(detail)), { card: { sequence: '001' } });
  assert.deepEqual([...withheld], ['card.template']);
  assert.equal(classification, 'internal');
  assert.equal(JSON.stringify(detail).includes('SECRET-TEMPLATE'), false);
});

test('AC03/QA-01: a field added later beneath an allowed container is withheld', () => {
  const allow = ['card', 'card.sequence'];
  const classifications = { 'card.sequence': 'internal' };
  const before = redact({ card: { sequence: '001' } }, { allow, classifications });
  const after = redact({ card: { sequence: '001', national_id: '12345678901' } }, { allow, classifications });
  assert.deepEqual(JSON.parse(JSON.stringify(before.detail)), JSON.parse(JSON.stringify(after.detail)));
  assert.deepEqual([...after.withheld], ['card.national_id']);
});

test('AC03/QA-01: nesting fails closed at every depth, not just the first', () => {
  const source = { a: { b: { c: { secret: 'LEAK', kept: 'ok' } } } };
  const { detail, withheld } = redact(source, {
    allow: ['a', 'a.b', 'a.b.c', 'a.b.c.kept'],
    classifications: { 'a.b.c.kept': 'internal', 'a.b.c.secret': 'secret' },
  });
  assert.deepEqual(JSON.parse(JSON.stringify(detail)), { a: { b: { c: { kept: 'ok' } } } });
  assert.deepEqual([...withheld], ['a.b.c.secret']);
  assert.equal(JSON.stringify(detail).includes('LEAK'), false);
});

test('AC03/QA-01: an unregistered classification withholds the value and never becomes the level', () => {
  for (const level of ['made_up', 'SECRET', 'Public', '', null, 0, undefined]) {
    const { detail, withheld, classification } = redact({ f: 'v' }, { allow: ['f'], classifications: { f: level } });
    assert.deepEqual({ ...detail }, {}, `${JSON.stringify(level)} must not emit the value`);
    assert.deepEqual([...withheld], ['f']);
    assert.ok(ENVELOPE_CLASSIFICATIONS.includes(classification),
      `envelope level ${classification} must be one the schema accepts`);
  }
});

test('AC03/QA-01: a built event can never carry an out-of-schema classification', () => {
  const event = audit({ source: { f: 'v' }, allow: ['f'], classifications: { f: 'made_up' } });
  assert.ok(schema('audit-event.schema.json').properties.classification.enum.includes(event.classification));
  assert.deepEqual({ ...event.detail }, {});
});

test('AC03/QA-01: awkward shapes fail closed — object arrays, cycles, depth, prototype keys', () => {
  const objectArray = redact({ items: [{ secret: 'LEAK' }] },
    { allow: ['items'], classifications: { items: 'internal' } });
  assert.deepEqual({ ...objectArray.detail }, {}, 'an array of objects is withheld outright');
  assert.deepEqual([...objectArray.withheld], ['items']);

  const cyclic = { name: 'x' };
  cyclic.self = cyclic;
  const cycleResult = redact(cyclic, { allow: ['name', 'self'], classifications: { name: 'internal' } });
  assert.deepEqual({ ...cycleResult.detail }, { name: 'x' });
  assert.ok(cycleResult.withheld.includes('self'));

  // Allowlist EVERY path down the chain, so the depth limit is what stops it and not a
  // missing entry — otherwise this test would pass for the wrong reason.
  const levels = MAX_REDACTION_DEPTH + 2;
  let deep = { leaf: 'v' };
  for (let i = levels; i > 0; i -= 1) deep = { [`n${i}`]: deep };
  const deepAllow = [];
  for (let i = 1; i <= levels; i += 1) deepAllow.push(Array.from({ length: i }, (_v, j) => `n${j + 1}`).join('.'));
  const leafPath = `${deepAllow[deepAllow.length - 1]}.leaf`;
  deepAllow.push(leafPath);
  const deepResult = redact(deep, { allow: deepAllow, classifications: { [leafPath]: 'internal' } });
  assert.equal(JSON.stringify(deepResult.detail).includes('"v"'), false, 'the depth limit stops it');
  assert.ok(deepResult.withheld.some((w) => w.startsWith('n1.n2')), 'and says where it gave up');

  // The same chain one level shallower does reach its leaf, proving the bound is the cause.
  let shallow = { leaf: 'v' };
  for (let i = MAX_REDACTION_DEPTH - 1; i > 0; i -= 1) shallow = { [`n${i}`]: shallow };
  const shallowAllow = [];
  for (let i = 1; i <= MAX_REDACTION_DEPTH - 1; i += 1) {
    shallowAllow.push(Array.from({ length: i }, (_v, j) => `n${j + 1}`).join('.'));
  }
  const shallowLeaf = `${shallowAllow[shallowAllow.length - 1]}.leaf`;
  shallowAllow.push(shallowLeaf);
  const shallowResult = redact(shallow, { allow: shallowAllow, classifications: { [shallowLeaf]: 'internal' } });
  assert.ok(JSON.stringify(shallowResult.detail).includes('"v"'), 'a shallower chain still projects');

  const polluted = JSON.parse('{"__proto__": {"admin": true}, "ok": "v"}');
  const protoResult = redact(polluted, { allow: ['__proto__', 'ok'], classifications: { ok: 'internal' } });
  assert.deepEqual({ ...protoResult.detail }, { ok: 'v' });
  assert.equal({}.admin, undefined, 'nothing reached Object.prototype');
});

test('AC03/QA-01: an array of primitives is copied, not referenced', () => {
  const source = { tags: ['a', 'b'] };
  const { detail } = redact(source, { allow: ['tags'], classifications: { tags: 'internal' } });
  source.tags.push('LEAK');
  assert.deepEqual([...detail.tags], ['a', 'b']);
});

test('AC03/QA-02: mutating the source after construction cannot change a built event', () => {
  // The attack: keep a reference to the object you handed in, then edit it while the
  // asynchronous sink is still persisting.
  const source = { card: { sequence: '001' } };
  const event = audit({
    source, allow: ['card', 'card.sequence'], classifications: { 'card.sequence': 'internal' },
  });
  const persisted = JSON.stringify(event);

  source.card.sequence = '999';
  source.card.injected = 'LEAK';
  source.card = { replaced: true };

  assert.equal(event.detail.card.sequence, '001', 'the value is the one that was approved');
  assert.equal(event.detail.card.injected, undefined, 'no field appears that classification never saw');
  assert.equal(JSON.stringify(event), persisted, 'the whole event is byte-identical');
});

test('AC03/QA-02: a built event is frozen at every level', () => {
  const event = audit({
    source: { card: { sequence: '001' }, tags: ['a'] },
    allow: ['card', 'card.sequence', 'tags'],
    classifications: { 'card.sequence': 'internal', tags: 'internal' },
  });
  for (const node of [event, event.actor, event.target, event.detail, event.detail.card, event.detail.tags, event.withheld]) {
    assert.ok(Object.isFrozen(node), 'frozen');
  }
  assert.throws(() => { event.detail.card.sequence = 'X'; }, TypeError);
  assert.throws(() => { event.detail.card.added = 'X'; }, TypeError);
  assert.throws(() => { event.detail.tags.push('X'); }, TypeError);
  assert.throws(() => { event.actor.id = 'someone-else'; }, TypeError);
  assert.equal(event.detail.card.sequence, '001');
});

test('AC03/QA-02: a denial event is deeply immutable too', () => {
  const denial = buildDenialEvent({
    context_tenant_id: TENANT_A, attempted_tenant_id: TENANT_B, principal: 'p',
    resource_type: 'person', resource_id: PERSON,
    reason_code: 'cross_tenant_access_denied', correlation_id: newId('event'),
  });
  assert.ok(Object.isFrozen(denial) && Object.isFrozen(denial.actor) && Object.isFrozen(denial.target));
  assert.throws(() => { denial.actor.id = 'x'; }, TypeError);
  assert.throws(() => { denial.attempted_tenant_id = TENANT_A; }, TypeError);
});

test('AC03/QA-02: deepFreeze handles nesting, arrays and already-frozen values', () => {
  const value = deepFreeze({ a: { b: [{ c: 1 }] } });
  assert.ok(Object.isFrozen(value.a) && Object.isFrozen(value.a.b) && Object.isFrozen(value.a.b[0]));
  assert.equal(deepFreeze(null), null);
  assert.equal(deepFreeze(7), 7);
});

test('AC01/QA-03: the audit schema requires the device slot', () => {
  // AC01 names device/workstation, so it may not be omitted: "no device" and "nobody
  // recorded the device" have to stay different answers.
  const s = schema('audit-event.schema.json');
  assert.ok(s.required.includes('device'));
  assert.deepEqual(s.properties.device.type, ['object', 'null']);
  const event = audit();
  assert.equal(event.device, null, 'explicitly null, present either way');
  assert.ok('device' in event);
  const withDevice = audit({ device: { device_id: DEVICE, workstation: 'ENROL-07' } });
  assert.equal(withDevice.device.workstation, 'ENROL-07');
});

test('AC02/QA-03: the event envelope requires causation_id, nullable for a root event', () => {
  const s = schema('event-envelope.schema.json');
  assert.ok(s.required.includes('causation_id'));
  assert.ok(s.properties.causation_id.anyOf.some((o) => o.type === 'null'), 'null for a root event');
  assert.ok(s.properties.causation_id.anyOf.some((o) => o.$ref === 'canonical-id.schema.json'));
});

test('AC01/AC02/QA-03: a document missing either field fails its schema required list', () => {
  // Asserted against the published required lists, which is what a validator enforces.
  const auditRequired = schema('audit-event.schema.json').required;
  const { device: _d, ...withoutDevice } = audit();
  assert.ok(auditRequired.some((f) => !(f in withoutDevice)), 'device omission is detectable');
  assert.equal(auditRequired.filter((f) => !(f in withoutDevice)).join(), 'device');

  const eventRequired = schema('event-envelope.schema.json').required;
  const rootEvent = {
    event_id: newId('event'), schema_version: 1, tenant_id: TENANT_A, aggregate_type: 'person',
    aggregate_id: PERSON, aggregate_version: 1, correlation_id: newId('event'),
    occurred_at: '2026-09-21T00:00:00.000Z', received_at: '2026-09-21T00:00:00.000Z',
    producer: 'enrolment-service', payload_classification: 'personal', payload: {},
  };
  assert.deepEqual(eventRequired.filter((f) => !(f in rootEvent)), ['causation_id']);
  assert.deepEqual(eventRequired.filter((f) => !(f in { ...rootEvent, causation_id: null })), []);
});

test('QA-01/02/03: the ADR records all three rules so they cannot be softened', () => {
  assert.match(ADR, /list of paths, and it projects recursively/);
  assert.match(ADR, /An unregistered classification is not a classification/);
  assert.match(ADR, /An audit event is immutable all the way down/);
  assert.match(ADR, /\*\*Required\*\*, and explicitly `null` otherwise/);
  assert.match(ADR, /`causation_id` is \*\*required\*\*/);
});

// --- BIO-ARC-003 QA-04 regression (Seyi re-review round 3, 2026-09-21) --------------
// A path was built by joining raw keys with `.`, so a flat key that itself contained a
// literal dot could alias the identical string as a nested traversal — an allowlist entry
// or classification meant for one silently governed the other, and the caller, who
// controls the source's shape, decided which one showed up.

test('AC03/QA-04: the exact reported collision — flat "profile.name" beside nested profile.name', () => {
  const secret = 'synthetic-sensitive-value';
  const result = redact(
    { profile: { name: secret }, 'profile.name': 'approved display value' },
    { allow: ['profile', 'profile.name'], classifications: { 'profile.name': 'personal' } },
  );
  // Neither side is trusted to resolve the ambiguity: both are withheld.
  assert.notEqual(result.detail.profile?.name, secret);
  assert.equal(Object.keys(result.detail.profile ?? {}).length, 0);
  assert.equal('profile.name' in result.detail, false);
  assert.deepEqual([...result.withheld].sort(), ['profile.name', 'profile\\.name']);
});

test('AC03/QA-04: escapeSegment gives flat and nested paths distinct, non-colliding strings', () => {
  assert.equal(escapeSegment('name'), 'name');
  assert.equal(escapeSegment('profile.name'), 'profile\\.name');
  assert.notEqual(escapeSegment('profile.name'), 'profile' + '.' + escapeSegment('name'));
  // Escaping order (\ before .) prevents a crafted backslash from forging a collision: the
  // raw key containing a REAL backslash-dot ('profile\.name', 13 chars) must not escape to
  // the same string as the raw key with a plain dot ('profile.name', 12 chars).
  assert.notEqual(escapeSegment('profile.name'), escapeSegment('profile\\.name'));
});

test('AC03/QA-04: a flat dotted key with no competing nested structure still needs its escaped form', () => {
  // The collision rule only fires when a real collision exists; it does not relax the
  // ordinary escaping requirement for the non-colliding case.
  const flatOnly = { 'profile.name': 'flat-value' };
  const unescaped = redact(flatOnly, { allow: ['profile.name'], classifications: { 'profile.name': 'internal' } });
  assert.deepEqual({ ...unescaped.detail }, {}, 'the unescaped entry still means "nested", which does not exist here');

  const escapedPath = escapeSegment('profile.name');
  const escaped = redact(flatOnly, { allow: [escapedPath], classifications: { [escapedPath]: 'internal' } });
  assert.deepEqual({ ...escaped.detail }, { 'profile.name': 'flat-value' });
});

test('AC03/QA-04: a partial match is not a collision — no competing value, no false withhold beyond policy', () => {
  // profile exists but has no "name" property: splitting "profile.name" cannot resolve to
  // a real value, so there is no ambiguity to protect against.
  const source = { profile: { other: 'x' }, 'profile.name': 'flat' };
  const result = redact(source, { allow: ['profile', 'profile.other', 'profile.name'], classifications: { 'profile.other': 'internal' } });
  assert.deepEqual({ ...result.detail }, { profile: { other: 'x' } });
  // The flat key is withheld only because its escaped form was never allowlisted — the
  // ordinary rule, not the collision rule.
  assert.ok(result.withheld.includes('profile\\.name'));
});

test('AC03/QA-04: the collision is detected at any depth, not only at the root', () => {
  const source = { a: { profile: { name: 'NESTED-LEAK' }, 'profile.name': 'flat' } };
  const result = redact(source, {
    allow: ['a', 'a.profile', 'a.profile.name', `a.${escapeSegment('profile.name')}`],
    classifications: { 'a.profile.name': 'personal', [`a.${escapeSegment('profile.name')}`]: 'personal' },
  });
  assert.deepEqual({ ...result.detail.a.profile }, {});
  assert.equal(JSON.stringify(result.detail).includes('NESTED-LEAK'), false);
});

test('AC03/QA-04: a three-level dotted key aliasing a deep nested value is also caught', () => {
  const source = { a: { b: { c: 'DEEP-LEAK' } }, 'a.b.c': 'flat' };
  const result = redact(source, { allow: ['a', 'a.b', 'a.b.c'], classifications: { 'a.b.c': 'internal' } });
  assert.deepEqual({ ...result.detail.a.b }, {});
  assert.equal(JSON.stringify(result.detail).includes('DEEP-LEAK'), false);
  assert.ok(result.withheld.includes('a.b.c'));
  // Written via escapeSegment rather than a hand-typed backslash literal, so this
  // assertion cannot fall prey to the same string-escaping mistake it is checking for.
  assert.ok(result.withheld.includes(escapeSegment('a.b.c')));
  assert.equal(escapeSegment('a.b.c'), 'a\\.b\\.c');
});

test('AC03/QA-04: collision detection cannot be used to walk through the prototype chain', () => {
  const polluted = JSON.parse('{"__proto__.x": "y", "__proto__": {"x": "z"}}');
  const result = redact(polluted, { allow: ['__proto__', '__proto__.x'], classifications: { '__proto__.x': 'internal' } });
  assert.deepEqual({ ...result.detail }, {});
  assert.equal(({}).x, undefined, 'Object.prototype is untouched');
});

test('AC03/QA-04: the ordinary nested case, with no colliding flat key, is unaffected', () => {
  // No regression: this is the exact scenario AC01 tests already cover, re-asserted here
  // to prove the collision detector does not fire when there is nothing to detect.
  const result = redact({ card: { sequence: '001' } }, {
    allow: ['card', 'card.sequence'], classifications: { 'card.sequence': 'internal' },
  });
  assert.deepEqual({ ...result.detail }, { card: { sequence: '001' } });
  assert.deepEqual([...result.withheld], []);
});

test('AC03/QA-04: the ADR records the collision rule and when it does and does not fire', () => {
  assert.match(ADR, /A dot-joined path is a caller-controlled string, and strings can be forged to collide/);
  assert.match(ADR, /withheld outright, on both sides, regardless of the allowlist/);
  assert.match(ADR, /only fires when a real collision exists/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-003-AC04 — events are idempotency-capable.
// ---------------------------------------------------------------------------

const delivery = (over = {}) => ({
  event_id: newId('event'), tenant_id: TENANT_A, aggregate_id: PERSON, aggregate_version: 1,
  payload: { state: 'ACTIVE' }, received_at: '2026-09-21T00:00:00.000Z', ...over,
});

test('AC04: the dedupe key is tenant plus event id', () => {
  const event = delivery();
  assert.equal(dedupeKey(event), `${TENANT_A}:${event.event_id}`);
  assert.notEqual(dedupeKey(event), dedupeKey({ ...event, tenant_id: TENANT_B }),
    'the same id in two tenants is two events');
  assert.equal(dedupeKey({ ...event, tenant_id: null }), `control-plane:${event.event_id}`);
  for (const event_id of [undefined, 'nope', PERSON, '']) {
    assert.throws(() => dedupeKey({ ...event, event_id }), (e) => e.code === 'invalid_event_id');
  }
});

test('AC04: a redelivery of the same event is a DUPLICATE', () => {
  const seen = new Map();
  const event = delivery();
  assert.equal(recordDelivery(seen, event).outcome, 'FIRST');
  assert.equal(recordDelivery(seen, event).outcome, 'DUPLICATE');
  assert.equal(recordDelivery(seen, event).outcome, 'DUPLICATE');
  assert.equal(seen.size, 1);
});

test('AC04: received_at is excluded from the fingerprint, or dedupe would never fire', () => {
  // The server stamps it per delivery. Including it would make every redelivery look new.
  const event = delivery();
  assert.equal(
    eventFingerprint(event),
    eventFingerprint({ ...event, received_at: '2026-09-21T09:99:99.999Z'.replace('99:99', '59:59') }),
  );
  const seen = new Map();
  recordDelivery(seen, event);
  assert.equal(recordDelivery(seen, { ...event, received_at: '2026-09-21T00:00:30.000Z' }).outcome, 'DUPLICATE');
});

test('AC04/hostile: a reused event id with different content is a CONFLICT, never absorbed', () => {
  // event_id is minted by the producer, which at the edge is a device. Under a key-only
  // dedupe, a second different event would be silently dropped — a way to make a
  // revocation vanish behind a replayed identifier.
  const seen = new Map();
  const original = delivery({ payload: { state: 'REVOKED' } });
  assert.equal(recordDelivery(seen, original).outcome, 'FIRST');

  const impostor = { ...original, payload: { state: 'ACTIVE' } };
  const result = recordDelivery(seen, impostor);
  assert.equal(result.outcome, 'CONFLICT');
  assert.equal(result.reason_code, 'event_id_reuse');
  assert.equal(seen.get(result.key), eventFingerprint(original), 'the first event is not overwritten');
});

test('AC04/hostile: every asserted field is in the fingerprint, so nothing can be edited quietly', () => {
  const event = delivery();
  for (const [field, value] of [
    ['aggregate_version', 2], ['aggregate_id', 'per_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14'],
    ['tenant_id', TENANT_B], ['payload', { state: 'REVOKED' }], ['schema_version', 2],
  ]) {
    assert.notEqual(eventFingerprint(event), eventFingerprint({ ...event, [field]: value }),
      `${field} is covered by the fingerprint`);
  }
});

test('AC04: the fingerprint ignores key order but not values', () => {
  assert.equal(eventFingerprint({ a: 1, b: 2 }), eventFingerprint({ b: 2, a: 1 }));
  assert.notEqual(eventFingerprint({ a: 1 }), eventFingerprint({ a: '1' }));
  assert.notEqual(eventFingerprint({ a: { b: 1 } }), eventFingerprint({ a: { b: 2 } }));
});

test('AC04: classification works against a plain object store as well as a Map', () => {
  const event = delivery();
  const store = { [dedupeKey(event)]: eventFingerprint(event) };
  assert.equal(classifyDelivery(store, event).outcome, 'DUPLICATE');
  assert.equal(classifyDelivery({}, event).outcome, 'FIRST');
});

test('AC04: two different events are both applied', () => {
  const seen = new Map();
  assert.equal(recordDelivery(seen, delivery()).outcome, 'FIRST');
  assert.equal(recordDelivery(seen, delivery()).outcome, 'FIRST');
  assert.equal(seen.size, 2);
});

// ---------------------------------------------------------------------------
// Contract hygiene.
// ---------------------------------------------------------------------------

test('AC01/AC02: schemas are self-describing JSON Schema 2020-12', () => {
  for (const name of ['audit-event.schema.json', 'tenant-access-denial-event.schema.json', 'event-envelope.schema.json']) {
    const s = schema(name);
    assert.equal(s.$schema, 'https://json-schema.org/draft/2020-12/schema', name);
    assert.ok(s.$id && s.title && s.description, `${name} is self-describing`);
  }
  assert.equal(AUDIT_SCHEMA_VERSION, 1);
});

test('ADR-0003 records the decisions, the amendments and the freeze policy', () => {
  assert.match(ADR, /# ADR-0003/);
  assert.match(ADR, /\*\*Status:\*\* Accepted \(BIO-ARC-003/);
  assert.match(ADR, /Freezes at W01 exit/);
  for (const ac of ['BIO-ARC-003-AC01', 'BIO-ARC-003-AC02', 'BIO-ARC-003-AC03', 'BIO-ARC-003-AC04']) {
    assert.ok(ADR.includes(ac), `${ac} is traceable to a decision section`);
  }
  assert.match(ADR, /\*\*Amends:\*\* ADR-0001 §3\.6 .* and ADR-0002 §5/s, 'both amendments are declared');
  assert.match(ADR, /allowlist, never a denylist/i);
  assert.match(ADR, /never absorb/i);
});
