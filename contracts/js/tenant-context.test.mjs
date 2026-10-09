// Developer tests for BIO-ARC-002. Every test name carries the AC it proves.
// Run: node --test contracts/js/   (no dependencies, offline-safe)
// All tenants, hostnames, principals and records here are SYNTHETIC.

import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import {
  TENANT_CHANNELS, RESOLVING_DOMAIN_STATUS, ROUTING_BINDING_STATUS,
  normaliseHostname, lookupTenantDomain, collectTenantClaims, resolveTenantContext,
  createControlPlaneContext, bindingForRepository, assertRecordInTenant, buildDenialEvent,
  isResolvedContext, assertResolvedTenantContext, AUDIT_TIMEOUT_MS,
  TenantContextError, newId, isUtcInstant,
} from './index.mjs';

/** A sink that records events, for the paths that are now required to write one. */
const sink = () => { const events = []; const fn = (e) => events.push(e); fn.events = events; return fn; };

const schema = (name) =>
  JSON.parse(readFileSync(fileURLToPath(new URL(`../schemas/${name}`, import.meta.url)), 'utf8'));

const ADR = readFileSync(
  fileURLToPath(new URL('../../docs/adr/ADR-0002-tenant-context-and-database-routing.md', import.meta.url)),
  'utf8',
);

// --- SYNTHETIC fixtures --------------------------------------------------------------
const TENANT_A = 'ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77';
const TENANT_B = 'ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03';
const TENANT_C = 'ten_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14';

const DOMAINS = [
  { hostname: 'portal.a.example.edu.ng', tenant_id: TENANT_A, status: 'VERIFIED', is_primary: true },
  { hostname: 'portal.b.example.edu.ng', tenant_id: TENANT_B, status: 'VERIFIED', is_primary: true },
  { hostname: 'pending.example.edu.ng', tenant_id: TENANT_A, status: 'PENDING_VERIFICATION', is_primary: false },
  { hostname: 'suspended.example.edu.ng', tenant_id: TENANT_A, status: 'SUSPENDED', is_primary: false },
  { hostname: 'retired.example.edu.ng', tenant_id: TENANT_A, status: 'RETIRED', is_primary: false },
];

const BINDINGS = [
  { tenant_id: TENANT_A, binding_key: 'tenant-a-db', status: 'ACTIVE' },
  { tenant_id: TENANT_B, binding_key: 'tenant-b-db', status: 'ACTIVE' },
  { tenant_id: TENANT_C, binding_key: 'tenant-c-db', status: 'PROVISIONING' },
];

const HOST_OF = { [TENANT_A]: 'portal.a.example.edu.ng', [TENANT_B]: 'portal.b.example.edu.ng' };

async function resolve(overrides = {}) {
  const audited = [];
  const context = await resolveTenantContext({
    domainRegistry: DOMAINS, bindingRegistry: BINDINGS, audit: (e) => audited.push(e), ...overrides,
  });
  return { context, audited };
}

async function attempt(overrides = {}) {
  const audited = [];
  try {
    const context = await resolveTenantContext({
      domainRegistry: DOMAINS, bindingRegistry: BINDINGS, audit: (e) => audited.push(e), ...overrides,
    });
    return { ok: true, context, audited };
  } catch (error) {
    return { ok: false, error, audited };
  }
}

// ---------------------------------------------------------------------------
// BIO-ARC-002-AC01 — host, authenticated tenant and explicit service context.
// ---------------------------------------------------------------------------

test('AC01: there are exactly three channels and they are the documented ones', async () => {
  assert.deepEqual([...TENANT_CHANNELS], ['host', 'principal', 'service']);
  assert.deepEqual(schema('tenant-context.schema.json').properties.channels.items.enum, [...TENANT_CHANNELS]);
  for (const channel of TENANT_CHANNELS) assert.ok(ADR.includes(`\`${channel}\``), `ADR documents ${channel}`);
});

test('AC01: each channel alone resolves a context and records which channel it came from', async () => {
  const viaHost = await resolve({ host: HOST_OF[TENANT_A] });
  assert.equal(viaHost.context.tenant_id, TENANT_A);
  assert.deepEqual([...viaHost.context.channels], ['host']);

  const viaPrincipal = await resolve({ principal: { tenant_id: TENANT_A, name: 'svc-enrolment' } });
  assert.deepEqual([...viaPrincipal.context.channels], ['principal']);

  const viaService = await resolve({ serviceContext: { tenant_id: TENANT_A, name: 'sync-worker' } });
  assert.deepEqual([...viaService.context.channels], ['service']);
});

test('AC01: agreeing channels all appear in the resolved context', async () => {
  const { context } = await resolve({
    host: HOST_OF[TENANT_A],
    principal: { tenant_id: TENANT_A, name: 'admin' },
    serviceContext: { tenant_id: TENANT_A, name: 'worker' },
  });
  assert.deepEqual([...context.channels], ['host', 'principal', 'service']);
  assert.equal(context.binding_key, 'tenant-a-db');
});

test('AC01: a tenant id in a request field is not a channel', async () => {
  // The cross-tenant identifier substitution in the threat model, written as a feature.
  for (const field of ['tenant_id', 'tenantId', 'x_tenant_id', 'query', 'body', 'headers']) {
    const probe = await attempt({ [field]: TENANT_A });
    assert.equal(probe.ok, false, `${field} must not resolve a tenant`);
    assert.equal(probe.error.code, 'tenant_context_missing');
  }
});

test('AC01: hostname normalisation handles case, port and trailing dot', async () => {
  for (const host of [
    'portal.a.example.edu.ng', 'PORTAL.A.EXAMPLE.EDU.NG', 'Portal.A.Example.Edu.Ng',
    'portal.a.example.edu.ng.', 'portal.a.example.edu.ng:443', 'portal.a.example.edu.ng.:8443',
    '  portal.a.example.edu.ng  ',
  ]) {
    assert.equal(normaliseHostname(host), 'portal.a.example.edu.ng', `normalises ${JSON.stringify(host)}`);
    assert.equal((await resolve({ host })).context.tenant_id, TENANT_A);
  }
});

test('AC01: a non-ASCII hostname is rejected, not converted', async () => {
  // "pаypal" with a Cyrillic а renders identically to "paypal" and is a different byte
  // string. Folding them together here would build homoglyph confusion into the boundary.
  for (const host of [
    'portal.а.example.edu.ng', 'pаypal.example', 'xn--80ak6aa92e.example\u0000',
    'portal.a.example.edu.ng​', 'пример.example',
  ]) {
    assert.equal(normaliseHostname(host), null, `${JSON.stringify(host)} must not normalise`);
  }
  assert.equal(normaliseHostname('xn--80ak6aa92e.example'), 'xn--80ak6aa92e.example', 'already-punycode passes');
});

test('AC01: malformed hostnames do not resolve', async () => {
  for (const host of [
    '', '   ', '.', '..', '-leading.example', 'trailing-.example', 'a..b.example',
    'portal.a.example.edu.ng/../other', 'portal.a.example.edu.ng%00', "portal'; DROP TABLE tenant_domain--",
    `${'a'.repeat(254)}.example`, null, undefined, 42, {}, ['portal.a.example.edu.ng'],
  ]) {
    assert.equal(normaliseHostname(host), null, `${JSON.stringify(host)} must not normalise`);
    assert.equal(lookupTenantDomain(DOMAINS, host), null);
  }
});

test('AC01: only a VERIFIED domain resolves — every other status fails closed', async () => {
  // Exhaustive over the status enum, so a new status cannot default to resolving.
  const statuses = schema('tenant-domain.schema.json').properties.status.enum;
  assert.ok(statuses.includes(RESOLVING_DOMAIN_STATUS));
  for (const status of statuses) {
    const registry = [{ hostname: 'probe.example.edu.ng', tenant_id: TENANT_A, status, is_primary: false }];
    const found = lookupTenantDomain(registry, 'probe.example.edu.ng');
    assert.equal(found !== null, status === RESOLVING_DOMAIN_STATUS, `status ${status}`);
  }
  for (const host of ['pending.example.edu.ng', 'suspended.example.edu.ng', 'retired.example.edu.ng']) {
    const probe = await attempt({ host });
    assert.equal(probe.ok, false, `${host} must not resolve`);
    assert.equal(probe.error.code, 'host_not_recognised');
  }
});

test('AC01: a platform host that maps to no tenant still works when the token carries one', async () => {
  // Mobile and service traffic hit a platform hostname; the tenant comes from the token.
  const { context } = await resolve({ host: 'api.biolog.example', principal: { tenant_id: TENANT_B, name: 'mobile' } });
  assert.equal(context.tenant_id, TENANT_B);
  assert.deepEqual([...context.channels], ['principal']);
});

test('AC01: control-plane context is a distinct kind carrying no tenant', async () => {
  const control = createControlPlaneContext({ principal: { name: 'platform-admin' } });
  assert.equal(control.kind, 'control_plane');
  assert.equal(control.tenant_id, undefined, 'not a tenant context with a null tenant');
  assert.equal('tenant_id' in control, false);
});

test('AC01: claims are collected only from the three channels', async () => {
  const claims = collectTenantClaims({
    host: HOST_OF[TENANT_A], domainRegistry: DOMAINS,
    principal: { tenant_id: TENANT_A }, serviceContext: { tenant_id: TENANT_A },
    tenant_id: TENANT_B, headers: { 'x-tenant-id': TENANT_B },
  });
  assert.deepEqual(claims.map((c) => c.channel), ['host', 'principal', 'service']);
  assert.deepEqual([...new Set(claims.map((c) => c.tenant_id))], [TENANT_A]);
});

test('AC01/AC02: every combination of the three channels resolves exactly as specified', async () => {
  // Exhaustive: 3 channels x {absent, tenant A, tenant B} = 27 cases, asserted against
  // the rule rather than against a sample. Sampling is what let two boundary defects
  // through BIO-ARC-001.
  const values = [null, TENANT_A, TENANT_B];
  let checked = 0;
  for (const hostTenant of values) {
    for (const principalTenant of values) {
      for (const serviceTenant of values) {
        checked += 1;
        const probe = await attempt({
          host: hostTenant ? HOST_OF[hostTenant] : undefined,
          principal: principalTenant ? { tenant_id: principalTenant, name: 'p' } : undefined,
          serviceContext: serviceTenant ? { tenant_id: serviceTenant, name: 's' } : undefined,
        });
        const supplied = [hostTenant, principalTenant, serviceTenant].filter(Boolean);
        const distinct = new Set(supplied);

        if (supplied.length === 0) {
          assert.equal(probe.ok, false, 'no channel supplied a tenant');
          assert.equal(probe.error.code, 'tenant_context_missing');
        } else if (distinct.size > 1) {
          assert.equal(probe.ok, false, `channels disagree: ${supplied.join(',')}`);
          assert.equal(probe.error.code, 'tenant_context_conflict');
          assert.equal(probe.error.status, 403);
          assert.equal(probe.audited.length, 1, 'a conflict is audited');
        } else {
          assert.equal(probe.ok, true, `unanimous ${supplied.join(',')} must resolve`);
          assert.equal(probe.context.tenant_id, supplied[0]);
          assert.equal(probe.audited.length, 0, 'a successful resolution writes no denial');
        }
      }
    }
  }
  assert.equal(checked, 27);
});

test('AC01: channels are not ranked — no channel can overrule another', async () => {
  // If any precedence existed, one of these would resolve instead of failing.
  const hostWins = await attempt({ host: HOST_OF[TENANT_A], principal: { tenant_id: TENANT_B, name: 'p' } });
  const tokenWins = await attempt({ host: HOST_OF[TENANT_B], principal: { tenant_id: TENANT_A, name: 'p' } });
  for (const probe of [hostWins, tokenWins]) {
    assert.equal(probe.ok, false);
    assert.equal(probe.error.code, 'tenant_context_conflict');
  }
  assert.match(ADR, /no precedence order/);
});

// ---------------------------------------------------------------------------
// BIO-ARC-002-AC02 — no implicit or default tenant.
// ---------------------------------------------------------------------------

test('AC02: a request with no channel at all is refused, not defaulted', async () => {
  const probe = await attempt({});
  assert.equal(probe.ok, false);
  assert.equal(probe.error.code, 'tenant_context_missing');
  assert.equal(probe.error.status, 400);
  assert.equal(probe.audited.length, 1, 'the refusal is audited');
});

test('AC02: an unrecognised host alone answers 404, never 403', async () => {
  // 403 would confirm a hostname exists but is unverified — an enumeration oracle.
  for (const host of ['unknown.example.edu.ng', 'pending.example.edu.ng', 'a.example']) {
    const probe = await attempt({ host });
    assert.equal(probe.ok, false);
    assert.equal(probe.error.code, 'host_not_recognised');
    assert.equal(probe.error.status, 404);
    assert.notEqual(probe.error.status, 403);
  }
});

test('AC02: an empty or absent host is not treated as a wildcard', async () => {
  for (const host of ['', '   ', null]) {
    const probe = await attempt({ host });
    assert.equal(probe.ok, false, `${JSON.stringify(host)} must not resolve`);
    assert.ok(['host_not_recognised', 'tenant_context_missing'].includes(probe.error.code));
  }
});

test('AC02: a non-canonical tenant claim in a token is refused', async () => {
  for (const tenant_id of [
    'ten_not-a-uuid', 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', '', '*', 'null',
    '0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77', 'ten_0199e0c1-8b2f-4c31-9a4e-2f6b1d0c5a77',
  ]) {
    const probe = await attempt({ principal: { tenant_id, name: 'p' } });
    assert.equal(probe.ok, false, `${JSON.stringify(tenant_id)} must not resolve`);
    assert.equal(probe.error.code, 'tenant_context_missing');
  }
});

test('AC02: a tenant with no ACTIVE binding fails closed instead of sharing a database', async () => {
  const probe = await attempt({ principal: { tenant_id: TENANT_C, name: 'p' } });
  assert.equal(probe.ok, false);
  assert.equal(probe.error.code, 'tenant_binding_unavailable');
  assert.equal(probe.audited[0].attempted_tenant_id, TENANT_C);

  const unknown = await attempt({ principal: { tenant_id: 'ten_0199e0c3-3d46-72a7-be58-6a1b8c0d4f25', name: 'p' } });
  assert.equal(unknown.error.code, 'tenant_binding_unavailable');
});

test('AC02: only an ACTIVE binding routes — every other status fails closed', async () => {
  const statuses = schema('tenant-data-binding.schema.json').properties.status.enum;
  assert.ok(statuses.includes(ROUTING_BINDING_STATUS));
  for (const status of statuses) {
    const probe = await attempt({
      principal: { tenant_id: TENANT_A, name: 'p' },
      bindingRegistry: [{ tenant_id: TENANT_A, binding_key: 'k', status }],
    });
    assert.equal(probe.ok, status === ROUTING_BINDING_STATUS, `binding status ${status}`);
  }
});

test('AC02: the ADR forbids a development-only default tenant explicitly', async () => {
  assert.match(ADR, /no default tenant/);
  assert.match(ADR, /a fallback that exists in development is a fallback that ships/);
});

test('AC02: the binding is looked up, never supplied by the caller', async () => {
  // A caller-named database makes the tenant boundary whatever the caller says it is.
  const probe = await attempt({
    principal: { tenant_id: TENANT_A, name: 'p' },
    binding_key: 'tenant-b-db', connection_string: 'mysql://attacker/tenant_b', database: 'tenant_b',
  });
  assert.equal(probe.ok, true);
  assert.equal(probe.context.binding_key, 'tenant-a-db', 'the control-plane binding wins');
  const binding = schema('tenant-data-binding.schema.json');
  assert.deepEqual(binding.not.anyOf.map((o) => o.required[0]).sort(),
    ['connection_string', 'database_host', 'password'].sort());
});

// ---------------------------------------------------------------------------
// BIO-ARC-002-AC03 — immutable for the lifetime of the request.
// ---------------------------------------------------------------------------

test('AC03: a resolved context cannot be reassigned, deleted or redefined', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  for (const mutate of [
    () => { context.tenant_id = TENANT_B; },
    () => { context.binding_key = 'tenant-b-db'; },
    () => { context.kind = 'control_plane'; },
    () => { delete context.tenant_id; },
    () => { Object.defineProperty(context, 'tenant_id', { value: TENANT_B }); },
    () => { context.injected = TENANT_B; },
  ]) {
    assert.throws(mutate, (e) => {
      assert.ok(e instanceof TenantContextError);
      assert.equal(e.code, 'tenant_context_immutable');
      return true;
    });
  }
  assert.equal(context.tenant_id, TENANT_A, 'unchanged after every attempt');
});

test('AC03: the channel list cannot be mutated either', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A], principal: { tenant_id: TENANT_A, name: 'p' } });
  assert.ok(Object.isFrozen(context.channels));
  assert.throws(() => { context.channels.push('service'); }, TypeError);
  assert.deepEqual([...context.channels], ['host', 'principal']);
});

test('AC03: a control-plane context is immutable too and cannot grow a tenant', async () => {
  const control = createControlPlaneContext({ principal: { name: 'admin' } });
  assert.throws(() => { control.tenant_id = TENANT_A; }, (e) => e.code === 'tenant_context_immutable');
  assert.throws(() => { control.kind = 'tenant'; }, (e) => e.code === 'tenant_context_immutable');
});

test('AC03: nested and asynchronous work sees the same frozen object, not a copy', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const seen = await Promise.all([
    Promise.resolve().then(() => context),
    new Promise((r) => { setTimeout(() => r(context), 0); }),
  ]);
  for (const inherited of seen) {
    assert.equal(inherited, context, 'same object identity');
    assert.throws(() => { inherited.tenant_id = TENANT_B; }, (e) => e.code === 'tenant_context_immutable');
  }
});

test('AC03: re-resolving produces a separate context and never mutates the first', async () => {
  const first = (await resolve({ host: HOST_OF[TENANT_A] })).context;
  const second = (await resolve({ host: HOST_OF[TENANT_B] })).context;
  assert.equal(first.tenant_id, TENANT_A);
  assert.equal(second.tenant_id, TENANT_B);
  assert.notEqual(first.correlation_id, second.correlation_id);
});

test('AC03: a resolved context carries a valid correlation id and UTC resolution instant', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  assert.match(context.correlation_id, /^evt_/);
  assert.ok(isUtcInstant(context.resolved_at), 'ADR-0001 instant format');
  const s = schema('tenant-context.schema.json');
  for (const field of s.required) assert.ok(context[field] !== undefined, `${field} present`);
  assert.match(context.tenant_id, new RegExp(s.properties.tenant_id.pattern));
  assert.match(context.correlation_id, new RegExp(s.properties.correlation_id.pattern));
});

// ---------------------------------------------------------------------------
// BIO-ARC-002-AC04 — cross-tenant access fails closed and is audited.
// ---------------------------------------------------------------------------

test('AC04: a record from another tenant is denied', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const audited = [];
  const foreign = { id: 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', tenant_id: TENANT_B };
  await assert.rejects(
    () => assertRecordInTenant(context, foreign, { resourceType: 'person', audit: (e) => audited.push(e), principal: 'admin' }),
    (e) => {
      assert.ok(e instanceof TenantContextError);
      assert.equal(e.status, 404);
      return true;
    },
  );
  assert.equal(audited.length, 1);
  assert.equal(audited[0].reason_code, 'cross_tenant_access_denied');
});

test('AC04: the caller is told "not found", never "forbidden"', async () => {
  // ADR-0001 section 1.5: a 403 turns any identifier into a probe for another tenant.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const audited = [];
  try {
    await assertRecordInTenant(context, { id: 'crd_0199e0c2-0a13-7f64-8b25-3d8e5f7a1c92', tenant_id: TENANT_B },
      { resourceType: 'credential', audit: (e) => audited.push(e) });
    assert.fail('must deny');
  } catch (error) {
    assert.equal(error.status, 404);
    assert.equal(error.code, 'not_found');
    assert.match(error.message, /not found/);
    assert.equal(/forbidden|cross.tenant|other tenant/i.test(error.message), false,
      'the message must not disclose why');
    assert.equal(error.detail.denial_reason, 'cross_tenant_access_denied', 'the real reason stays internal');
  }
  assert.equal(audited[0].reason_code, 'cross_tenant_access_denied', 'and reaches the audit');
});

test('AC04: the audit records both tenants, which is the whole signal', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const audited = [];
  await assert.rejects(() => assertRecordInTenant(context, { id: 'dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03', tenant_id: TENANT_B },
    { resourceType: 'device', audit: (e) => audited.push(e), principal: 'reader-01' }), TenantContextError);
  // Field names are the canonical audit ones since ADR-0003 Decision 6.2; the guarantees
  // are unchanged — both tenants, the actor, the target and the correlation.
  const event = audited[0];
  assert.equal(event.tenant_id, TENANT_A);
  assert.equal(event.attempted_tenant_id, TENANT_B);
  assert.deepEqual({ ...event.actor }, { type: 'unknown', id: 'reader-01' });
  assert.equal(event.target.type, 'device');
  assert.equal(event.outcome, 'DENIED');
  assert.equal(event.action, 'tenant.access');
  assert.equal(event.correlation_id, context.correlation_id, 'ties the denial to the request');
  assert.ok(isUtcInstant(event.received_at));
});

test('AC04: the audit event carries no payload and no version', async () => {
  const event = buildDenialEvent({
    context_tenant_id: TENANT_A, attempted_tenant_id: TENANT_B, principal: 'p',
    resource_type: 'person', resource_id: 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d',
    reason_code: 'cross_tenant_access_denied', correlation_id: newId('event'),
  });
  for (const forbidden of ['payload', 'record', 'version', 'data', 'body', 'evidence']) {
    assert.equal(event[forbidden], undefined, `denial events must not carry ${forbidden}`);
  }
  const s = schema('tenant-access-denial-event.schema.json');
  assert.deepEqual(s.not.anyOf.map((o) => o.required[0]).sort(),
    ['evidence', 'payload', 'record', 'version'].sort());
  for (const field of s.required) assert.ok(event[field] !== undefined && event[field] !== '', `${field} present`);
  assert.ok(Object.isFrozen(event), 'append-only');
  // ADR-0003 Decision 6.2: it is a conforming audit event, not a shape of its own.
  assert.deepEqual(s.allOf, [{ $ref: 'audit-event.schema.json' }]);
  assert.equal(s.properties.outcome.const, 'DENIED');
});

test('AC04: the audit is written before the denial is returned', async () => {
  // Ordering matters: if the audit came after, a throw between the two would lose it.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const order = [];
  try {
    await assertRecordInTenant(context, { id: 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', tenant_id: TENANT_B },
      { resourceType: 'person', audit: () => order.push('audit') });
  } catch {
    order.push('throw');
  }
  assert.deepEqual(order, ['audit', 'throw']);

  // QA-03: the same ordering must hold when the sink is asynchronous. "Written" means the
  // sink's promise settled, not that the call was made.
  const asyncOrder = [];
  const durable = (event) => new Promise((r) => setTimeout(() => { asyncOrder.push(`persisted:${event.reason_code}`); r(); }, 5));
  try {
    await assertRecordInTenant(context, { id: 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', tenant_id: TENANT_B },
      { resourceType: 'person', audit: durable });
  } catch (error) {
    asyncOrder.push(`response:${error.code}`);
  }
  assert.deepEqual(asyncOrder, ['persisted:cross_tenant_access_denied', 'response:not_found']);
});

test('AC04: every resolution failure is audited, not just cross-tenant reads', async () => {
  const cases = [
    [{}, 'tenant_context_missing'],
    [{ host: 'unknown.example.edu.ng' }, 'host_not_recognised'],
    [{ host: HOST_OF[TENANT_A], principal: { tenant_id: TENANT_B, name: 'p' } }, 'tenant_context_conflict'],
    [{ principal: { tenant_id: TENANT_C, name: 'p' } }, 'tenant_binding_unavailable'],
  ];
  for (const [input, expected] of cases) {
    const probe = await attempt(input);
    assert.equal(probe.ok, false, expected);
    assert.equal(probe.audited.length, 1, `${expected} is audited exactly once`);
    assert.equal(probe.audited[0].reason_code, expected);
  }
});

test('AC04: a forged or unresolved context cannot admit a record', async () => {
  // A malformed context whose tenant_id is null must not compare equal to a record whose
  // tenant is also null. Two absent tenants are not a match.
  for (const forged of [
    { kind: 'tenant' },
    { kind: 'tenant', tenant_id: null, binding_key: 'k' },
    { kind: 'tenant', tenant_id: undefined, binding_key: 'k' },
    { kind: 'tenant', tenant_id: '', binding_key: 'k' },
    { kind: 'tenant', tenant_id: TENANT_A },
  ]) {
    for (const record of [{ id: 'x' }, { id: 'x', tenant_id: null }, { id: 'x', tenant_id: TENANT_A }]) {
      await assert.rejects(() => assertRecordInTenant(forged, record, { resourceType: 'person', audit: sink() }),
        TenantContextError, `${JSON.stringify(forged)} must not admit ${JSON.stringify(record)}`);
    }
  }
});

test('AC04: a record with a missing or null tenant is denied, not waved through', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  for (const record of [{ id: 'x' }, { id: 'x', tenant_id: null }, { id: 'x', tenant_id: undefined }, {}, null]) {
    const audit = sink();
    await assert.rejects(() => assertRecordInTenant(context, record, { resourceType: 'person', audit }),
      (e) => e.status === 404, `${JSON.stringify(record)} must be denied`);
    assert.equal(audit.events.length, 1, 'and audited');
  }
});

test('AC04: a record from the resolved tenant is allowed', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const audited = [];
  assert.equal(
    await assertRecordInTenant(context, { id: 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', tenant_id: TENANT_A },
      { resourceType: 'person', audit: (e) => audited.push(e) }),
    true,
  );
  assert.equal(audited.length, 0, 'an allowed read writes no denial event');
});

test('AC04: a control-plane context cannot open a tenant repository or pass a tenant check', async () => {
  const control = createControlPlaneContext({ principal: { name: 'admin' } });
  const a = sink();
  await assert.rejects(() => bindingForRepository(control, { audit: a, principal: 'admin' }), (e) => {
    assert.equal(e.code, 'control_plane_context_misuse');
    assert.equal(e.status, 403);
    return true;
  });
  const b = sink();
  await assert.rejects(() => assertRecordInTenant(control, { id: 'x', tenant_id: TENANT_A }, { resourceType: 'person', audit: b }),
    (e) => e.code === 'control_plane_context_misuse');
  // QA-02: control-plane misuse must be reachable by the audit, not invisible to it.
  for (const s of [a, b]) {
    assert.equal(s.events.length, 1, 'control-plane misuse is audited');
    assert.equal(s.events[0].reason_code, 'control_plane_context_misuse');
  }
});

test('AC04: a repository cannot be opened without a resolved context', async () => {
  for (const bad of [
    undefined, null, {}, 'tenant', { tenant_id: TENANT_A },
    { kind: 'tenant' },                                     // claims the kind, carries nothing
    { kind: 'tenant', tenant_id: TENANT_A },                // no binding
    { kind: 'tenant', tenant_id: TENANT_A, binding_key: '' },
    { kind: 'tenant', tenant_id: null, binding_key: 'k' },
    { kind: 'tenant', tenant_id: 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', binding_key: 'k' },
  ]) {
    await assert.rejects(() => bindingForRepository(bad, { audit: sink() }), TenantContextError,
      `${JSON.stringify(bad)} must not bind`);
  }
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  assert.equal(await bindingForRepository(context), 'tenant-a-db');
});

// ---------------------------------------------------------------------------
// Code / schema parity — the class of defect that blocked BIO-ARC-001 twice.
// ---------------------------------------------------------------------------

test('AC04: every reason code the implementation can emit is in the schema enum', async () => {
  const declared = new Set(schema('tenant-access-denial-event.schema.json').properties.reason_code.enum);
  const emitted = new Set();
  const probes = [
    {}, { host: 'unknown.example.edu.ng' },
    { host: HOST_OF[TENANT_A], principal: { tenant_id: TENANT_B, name: 'p' } },
    { principal: { tenant_id: TENANT_C, name: 'p' } },
  ];
  for (const input of probes) for (const e of (await attempt(input)).audited) emitted.add(e.reason_code);
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  try {
    await assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B },
      { resourceType: 'person', audit: (e) => emitted.add(e.reason_code) });
  } catch { /* expected */ }
  for (const code of emitted) assert.ok(declared.has(code), `${code} must be declared in the schema`);
  // And the reverse: no declared code is dead, so the enum documents real behaviour.
  for (const code of ['control_plane_context_misuse', 'tenant_context_immutable', 'tenant_context_not_resolved']) {
    assert.ok(declared.has(code), `${code} declared`);
  }
  assert.equal(declared.size, 8);
  // audit_unavailable names the failure to write an event, so no event may carry it.
  assert.equal(declared.has('audit_unavailable'), false);
});

test('AC04: the hostname schema pattern and normaliseHostname agree', async () => {
  const pattern = new RegExp(schema('tenant-domain.schema.json').properties.hostname.pattern);
  const probes = [
    'portal.a.example.edu.ng', 'a.example', 'xn--80ak6aa92e.example', 'a', '1.2.3.4',
    '-leading.example', 'trailing-.example', 'a..b.example', '', '.', 'UPPER.example',
    'portal.a.example.edu.ng.', 'portal.a.example.edu.ng:443', 'пример.example',
  ];
  for (const host of probes) {
    const normalised = normaliseHostname(host);
    if (normalised !== null) {
      assert.match(normalised, pattern, `normalised ${JSON.stringify(host)} must satisfy the schema`);
    }
    // A value that is already in stored form must be classified identically by both.
    if (host === normalised) assert.equal(pattern.test(host), normalised !== null, host);
    if (normalised === null && pattern.test(host)) {
      assert.fail(`${JSON.stringify(host)} satisfies the schema but the code refuses it`);
    }
  }
});

// --- BIO-ARC-002-QA-01 regression (Seyi review, 2026-09-21) --------------------------
// Validation used to accept any object that LOOKED like a context, so a hand-built one
// carrying its own binding_key selected its own database. Field shape is what a forged
// object imitates best, so it is no longer what grants access: provenance is.

test('AC01/QA-01: a structurally perfect forged context is refused', async () => {
  // Right kind, canonical tenant, non-empty binding — everything the old check asked for.
  const forged = { kind: 'tenant', tenant_id: TENANT_A, binding_key: 'tenant-a-db', channels: ['host'],
    correlation_id: newId('event'), resolved_at: '2026-09-20T21:05:00.000Z' };
  assert.equal(isResolvedContext(forged), false);
  const audit = sink();
  await assert.rejects(() => bindingForRepository(forged, { audit, principal: 'attacker' }), (e) => {
    assert.equal(e.code, 'tenant_context_not_resolved');
    assert.equal(e.status, 403);
    return true;
  });
  assert.equal(audit.events[0].reason_code, 'tenant_context_not_resolved', 'and it is audited');
});

test('AC01/QA-01: a caller-chosen binding_key cannot select a database', async () => {
  // The finding as filed: bindingForRepository returned "caller-chosen-tenant-b-db".
  for (const binding_key of ['tenant-b-db', 'caller-chosen-tenant-b-db', 'ANY-DB-I-LIKE', '../../tenant-b']) {
    await assert.rejects(
      () => bindingForRepository({ kind: 'tenant', tenant_id: TENANT_A, binding_key }, { audit: sink() }),
      (e) => e.code === 'tenant_context_not_resolved', `binding_key ${binding_key} must not route`,
    );
  }
});

test('AC01/QA-01: a binding_key set on a real context is inert, not authoritative', async () => {
  // Belt and braces: the binding comes from provenance, so even if the object could be
  // mutated the routed database would not change.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  assert.throws(() => { context.binding_key = 'tenant-b-db'; }, (e) => e.code === 'tenant_context_immutable');
  assert.equal(await bindingForRepository(context), 'tenant-a-db');

  // A copy carries the fields but not the provenance, so it routes nothing.
  const copy = { ...context };
  assert.equal(copy.binding_key, 'tenant-a-db');
  assert.equal(isResolvedContext(copy), false);
  await assert.rejects(() => bindingForRepository(copy, { audit: sink() }), (e) => e.code === 'tenant_context_not_resolved');
});

test('AC01/QA-01: provenance survives neither serialisation nor reconstruction', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const rebuilt = JSON.parse(JSON.stringify({ ...context }));
  assert.equal(rebuilt.tenant_id, TENANT_A, 'the fields round-trip');
  assert.equal(isResolvedContext(rebuilt), false, 'the provenance does not');
  await assert.rejects(() => bindingForRepository(rebuilt, { audit: sink() }),
    (e) => e.code === 'tenant_context_not_resolved');
  for (const impostor of [Object.create(context), new Proxy({ ...context }, {}), Object.assign(() => {}, { ...context })]) {
    assert.equal(isResolvedContext(impostor), false);
    await assert.rejects(() => bindingForRepository(impostor, { audit: sink() }), TenantContextError);
  }
});

test('AC01/QA-01: a forged context cannot pass the cross-tenant check either', async () => {
  const forged = { kind: 'tenant', tenant_id: TENANT_A, binding_key: 'tenant-a-db' };
  const audit = sink();
  await assert.rejects(
    () => assertRecordInTenant(forged, { id: 'x', tenant_id: TENANT_A }, { resourceType: 'person', audit }),
    (e) => e.code === 'tenant_context_not_resolved',
    'a forged context must not admit even a same-tenant record',
  );
  assert.equal(audit.events.length, 1);
});

test('AC01/QA-01: a legitimately resolved context still works end to end', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_B], principal: { tenant_id: TENANT_B, name: 'p' } });
  assert.ok(isResolvedContext(context));
  assert.equal(await bindingForRepository(context), 'tenant-b-db');
  assert.equal((await assertResolvedTenantContext(context)).tenant_id, TENANT_B);
  assert.equal(
    await assertRecordInTenant(context, { id: 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', tenant_id: TENANT_B },
      { resourceType: 'person' }),
    true,
  );
});

// --- BIO-ARC-002-QA-02 regression (Seyi review, 2026-09-21) --------------------------
// emitDenial used to return quietly when no sink was supplied, so a denial could be
// returned with nothing written. An unaudited denial is worse than the access it
// refused: the caller is stopped and nobody learns it happened.

test('AC04/QA-02: a cross-tenant denial without an audit sink fails closed', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  await assert.rejects(
    () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B }, { resourceType: 'person' }),
    (e) => {
      assert.equal(e.code, 'audit_unavailable');
      assert.equal(e.status, 503);
      assert.notEqual(e.status, 404, 'the caller must not receive the ordinary denial');
      return true;
    },
  );
});

test('AC04/QA-02: every denial path refuses to proceed without a sink', async () => {
  const cases = [
    () => resolveTenantContext({ domainRegistry: DOMAINS, bindingRegistry: BINDINGS }),
    () => resolveTenantContext({ host: 'unknown.example.edu.ng', domainRegistry: DOMAINS, bindingRegistry: BINDINGS }),
    () => resolveTenantContext({
      host: HOST_OF[TENANT_A], principal: { tenant_id: TENANT_B, name: 'p' },
      domainRegistry: DOMAINS, bindingRegistry: BINDINGS,
    }),
    () => resolveTenantContext({ principal: { tenant_id: TENANT_C, name: 'p' }, domainRegistry: DOMAINS, bindingRegistry: BINDINGS }),
    () => bindingForRepository({ kind: 'tenant', tenant_id: TENANT_A, binding_key: 'k' }),
    () => bindingForRepository(createControlPlaneContext()),
  ];
  for (const [index, run] of cases.entries()) {
    await assert.rejects(run, (e) => {
      assert.equal(e.code, 'audit_unavailable', `case ${index} must fail closed on the missing sink`);
      return true;
    });
  }
});

test('AC04/QA-02: a sink that is not callable is treated as unavailable', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  for (const audit of [null, undefined, 'audit', 42, {}, [], Symbol('audit')]) {
    await assert.rejects(
      () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B }, { resourceType: 'person', audit }),
      (e) => e.code === 'audit_unavailable', `${String(audit)} is not a sink`,
    );
  }
});

test('AC04/QA-02: a sink that throws is a failure to audit, not a swallowed error', async () => {
  // Changed in round 3: every way of failing to persist now reports the same machine code
  // so a caller has one case to handle, and the original failure survives on `cause`.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const broken = () => { throw new Error('audit store unreachable'); };
  await assert.rejects(
    () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B }, { resourceType: 'person', audit: broken }),
    (e) => {
      assert.equal(e.code, 'audit_unavailable');
      assert.equal(e.status, 503);
      assert.notEqual(e.status, 404, 'the caller must not receive the ordinary denial');
      assert.equal(e.cause.message, 'audit store unreachable', 'the storage failure is preserved');
      return true;
    },
  );
});

test('AC04/QA-02: a successful path never needs a sink', async () => {
  // Fail-closed applies to denying, not to working. Ordinary traffic is unaffected.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  assert.equal(await bindingForRepository(context), 'tenant-a-db');
  assert.equal(
    await assertRecordInTenant(context, { id: 'per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d', tenant_id: TENANT_A },
      { resourceType: 'person' }),
    true,
  );
});

test('AC04/QA-02: the ADR states both rules so they cannot be softened later', async () => {
  assert.match(ADR, /provably the product of resolution/);
  assert.match(ADR, /is inert: it is never read/);
  assert.match(ADR, /hard dependency of denying, not an option/);
  assert.match(ADR, /an unaudited denial is the one outcome worse than the access it refused/);
});

// --- BIO-ARC-002-QA-03 regression (Seyi re-review round 2, 2026-09-21) --------------
// emitDenial called the sink and ignored the promise it returned, so with a real
// (asynchronous) audit store the 404 was returned while the write was still in flight,
// and a rejection arriving afterwards could no longer become audit_unavailable. Denying
// is now asynchronous, because durable audit is asynchronous.

test('AC04/QA-03: an async sink persists before the denial response is returned', async () => {
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const order = [];
  const durable = (event) => new Promise((r) => setTimeout(() => { order.push(`persisted:${event.reason_code}`); r(); }, 10));
  await assert.rejects(
    () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B }, { resourceType: 'person', audit: durable })
      .catch((e) => { order.push(`response:${e.code}`); throw e; }),
    (e) => e.code === 'not_found',
  );
  assert.deepEqual(order, ['persisted:cross_tenant_access_denied', 'response:not_found'],
    'the write settles first, every time');
});

test('AC04/QA-03: an async sink that rejects after returning fails the denial closed', async () => {
  // The finding as filed: the rejection used to arrive after the caller already had a 404.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const order = [];
  const rejecting = () => new Promise((_r, reject) => setTimeout(() => {
    order.push('audit rejected'); reject(new Error('audit store down'));
  }, 10));
  await assert.rejects(
    () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B }, { resourceType: 'person', audit: rejecting })
      .catch((e) => { order.push(`response:${e.code}`); throw e; }),
    (e) => {
      assert.equal(e.code, 'audit_unavailable');
      assert.equal(e.status, 503);
      assert.notEqual(e.status, 404, 'the caller must never receive the ordinary denial');
      assert.equal(e.cause.message, 'audit store down');
      return true;
    },
  );
  assert.deepEqual(order, ['audit rejected', 'response:audit_unavailable'],
    'the failure is known before anything is returned');
});

test('AC04/QA-03: a microtask-deferred rejection also fails closed', async () => {
  // A promise that rejects on the next microtask, not a timer — the cheapest way for an
  // ignored promise to look like it succeeded.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  await assert.rejects(
    () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B },
      { resourceType: 'person', audit: () => Promise.reject(new Error('rejected immediately')) }),
    (e) => e.code === 'audit_unavailable' && e.cause.message === 'rejected immediately',
  );
});

test('AC04/QA-03: a sink that never settles times out and fails closed', async () => {
  // A hung audit store is not a reason to hold a request open, and not a reason to answer
  // without the record either.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const never = () => new Promise(() => {});
  const startedAt = Date.now();
  await assert.rejects(
    () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B },
      { resourceType: 'person', audit: never, auditTimeoutMs: 25 }),
    (e) => {
      assert.equal(e.code, 'audit_unavailable');
      assert.equal(e.detail.audit, 'timed_out');
      assert.equal(e.detail.audit_timeout_ms, 25);
      return true;
    },
  );
  assert.ok(Date.now() - startedAt < 2000, 'it gives up rather than hanging');
  assert.equal(AUDIT_TIMEOUT_MS, 5000, 'and there is a documented default');
});

test('AC04/QA-03: resolution denials await an async sink too, not just record checks', async () => {
  const cases = [
    { desc: 'no channel', input: {} },
    { desc: 'unknown host', input: { host: 'unknown.example.edu.ng' } },
    { desc: 'channel conflict', input: { host: HOST_OF[TENANT_A], principal: { tenant_id: TENANT_B, name: 'p' } } },
    { desc: 'no binding', input: { principal: { tenant_id: TENANT_C, name: 'p' } } },
  ];
  for (const { desc, input } of cases) {
    await assert.rejects(
      () => resolveTenantContext({
        ...input, domainRegistry: DOMAINS, bindingRegistry: BINDINGS,
        audit: () => Promise.reject(new Error('store down')),
      }),
      (e) => e.code === 'audit_unavailable', `${desc} must fail closed on an async audit failure`,
    );
  }
  // And bindingForRepository, which denies for forged and control-plane contexts.
  for (const context of [{ kind: 'tenant', tenant_id: TENANT_A, binding_key: 'k' }, createControlPlaneContext()]) {
    await assert.rejects(
      () => bindingForRepository(context, { audit: () => Promise.reject(new Error('store down')) }),
      (e) => e.code === 'audit_unavailable',
    );
  }
});

test('AC04/QA-03: an async sink that succeeds still yields the ordinary denial', async () => {
  // Fail-closed must not swallow the real outcome when the audit works.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const persisted = [];
  await assert.rejects(
    () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B },
      { resourceType: 'person', audit: async (e) => { persisted.push(e); } }),
    (e) => e.code === 'not_found' && e.status === 404,
  );
  assert.equal(persisted.length, 1);
  assert.equal(persisted[0].tenant_id, TENANT_A);
  assert.equal(persisted[0].attempted_tenant_id, TENANT_B);

  const conflict = await attempt({
    host: HOST_OF[TENANT_A], principal: { tenant_id: TENANT_B, name: 'p' },
    audit: async (e) => { persisted.push(e); },
  });
  assert.equal(conflict.error.code, 'tenant_context_conflict', 'not masked by the async sink');
});

test('AC04/QA-03: a synchronous sink still works and is not made async', async () => {
  // The contract accommodates a durable synchronous sink; it does not require a promise.
  const { context } = await resolve({ host: HOST_OF[TENANT_A] });
  const audit = sink();
  await assert.rejects(
    () => assertRecordInTenant(context, { id: 'x', tenant_id: TENANT_B }, { resourceType: 'person', audit }),
    (e) => e.code === 'not_found',
  );
  assert.equal(audit.events.length, 1);
});

test('AC04/QA-03: the ADR states that written means settled', async () => {
  assert.match(ADR, /the sink's promise has settled/);
  assert.match(ADR, /Denying is asynchronous because durable audit is asynchronous/);
});

test('AC04: the ADR records the decision, its owner and the freeze policy', async () => {
  assert.match(ADR, /# ADR-0002/);
  assert.match(ADR, /\*\*Status:\*\* Accepted \(BIO-ARC-002/);
  assert.match(ADR, /Kemi/);
  assert.match(ADR, /Freezes at W01 exit/);
  for (const ac of ['BIO-ARC-002-AC01', 'BIO-ARC-002-AC02', 'BIO-ARC-002-AC03', 'BIO-ARC-002-AC04']) {
    assert.ok(ADR.includes(ac), `${ac} is traceable to a decision section`);
  }
  assert.match(ADR, /ADR-0001/, 'builds on the frozen identifier and clock contract');
});
