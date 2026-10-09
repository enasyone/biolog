// BIOLOG tenant context and database routing — ADR-0002.
// Owner: Kemi (Chief Architect & Integrator). Shared contract: feature modules consume a
// resolved context; they never resolve tenancy themselves and never accept a tenant
// identifier from a request field.

import { newId, parseId } from './identifiers.mjs';
import { nowUtc } from './clock.mjs';
import { buildAuditEvent, deepFreeze } from './audit.mjs';

/** The only channels that may carry a tenant claim (ADR-0002 Decision 1.1). */
export const TENANT_CHANNELS = Object.freeze(['host', 'principal', 'service']);

/** Only a VERIFIED domain resolves; every other status fails closed (Decision 1.3). */
export const RESOLVING_DOMAIN_STATUS = 'VERIFIED';
/** Only an ACTIVE binding routes; there is no shared or default database (Decision 4.1). */
export const ROUTING_BINDING_STATUS = 'ACTIVE';

/**
 * How long a durable audit write may take before the denial fails closed. A hung audit
 * store is not a reason to hold a request open indefinitely, and it is not a reason to
 * answer without the record either — so it becomes `audit_unavailable` like any other
 * failure to persist. Override per call with `auditTimeoutMs`.
 */
export const AUDIT_TIMEOUT_MS = 5000;

/**
 * Provenance for resolved contexts (ADR-0002 Decision 4.1).
 *
 * A context is trusted because it is in this map, not because its fields look right. The
 * map is module-private and keyed by object identity, so a context cannot be forged,
 * copied, serialised or rebuilt from outside — only `freezeContext` puts entries in, and
 * only `resolveTenantContext` and `createControlPlaneContext` call it.
 *
 * The authoritative tenant and binding live HERE, not on the public object. That is what
 * makes "a caller can never name the database" structural: `bindingForRepository` reads
 * the binding that control-plane resolution recorded, so a `binding_key` set on any
 * object a caller hands in is inert.
 */
const RESOLVED = new WeakMap();

export class TenantContextError extends Error {
  constructor(message, code, status, detail = {}) {
    super(message);
    this.name = 'TenantContextError';
    this.code = code;
    this.status = status;
    this.detail = detail;
  }
}

/**
 * Normalise a hostname for lookup. This is a security boundary, so it is strict about
 * what it is willing to normalise: case, a trailing dot and a port are mechanical, but a
 * non-ASCII hostname is rejected rather than converted. Two hostnames that render
 * identically and differ in bytes must not be folded together here — punycode conversion
 * belongs at the edge, before this contract sees the value.
 */
export function normaliseHostname(host) {
  if (typeof host !== 'string') return null;
  const trimmed = host.trim();
  if (trimmed === '') return null;
  // eslint-disable-next-line no-control-regex
  if (/[^\x21-\x7e]/.test(trimmed)) return null; // non-ASCII or control characters
  const withoutPort = trimmed.replace(/:\d+$/, '');
  const withoutTrailingDot = withoutPort.replace(/\.$/, '');
  const lowered = withoutTrailingDot.toLowerCase();
  if (lowered.length === 0 || lowered.length > 253) return null;
  if (!/^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$/.test(lowered)) return null;
  return lowered;
}

/** Look up a hostname in the control-plane domain registry. Unverified does not resolve. */
export function lookupTenantDomain(registry, host) {
  const hostname = normaliseHostname(host);
  if (hostname === null) return null;
  const record = (registry ?? []).find((d) => d.hostname === hostname);
  if (!record || record.status !== RESOLVING_DOMAIN_STATUS) return null;
  return record;
}

/**
 * Collect the tenant claims a request presents. Only the three channels are read; a
 * tenant identifier appearing anywhere else in a request is caller-controlled data and is
 * not a claim (Decision 1.1).
 */
export function collectTenantClaims({ host, domainRegistry, principal, serviceContext } = {}) {
  const claims = [];
  const domain = lookupTenantDomain(domainRegistry, host);
  if (domain) claims.push({ channel: 'host', tenant_id: domain.tenant_id });
  if (principal && typeof principal.tenant_id === 'string') {
    claims.push({ channel: 'principal', tenant_id: principal.tenant_id });
  }
  if (serviceContext && typeof serviceContext.tenant_id === 'string') {
    claims.push({ channel: 'service', tenant_id: serviceContext.tenant_id });
  }
  return claims;
}

/**
 * Resolve the tenant context for one request or job.
 *
 * Channels are not ranked. Every channel that supplied a tenant must name the same one:
 * a disagreement between an infrastructure-verified host and a cryptographically
 * authenticated token is not a tie to be broken, it is a misconfiguration or an attempt
 * to reach another tenant's data with a valid token. Both are refused and audited.
 *
 * `audit` is called before this function throws, so a denial cannot be returned without
 * its audit record (Decision 5).
 */
export async function resolveTenantContext({
  host, domainRegistry, principal, serviceContext, bindingRegistry, audit, correlationId,
  auditTimeoutMs,
} = {}) {
  const correlation_id = correlationId ?? newId('event');
  const principalName = principal?.name ?? serviceContext?.name ?? 'anonymous';
  const deny = async (code, status, detail, attempted = null) => {
    await emitDenial(audit, {
      context_tenant_id: null,
      attempted_tenant_id: attempted,
      principal: principalName,
      resource_type: 'tenant_context',
      resource_id: null,
      reason_code: code,
      correlation_id,
    }, { auditTimeoutMs });
    throw new TenantContextError(detail.message, code, status, { ...detail, correlation_id });
  };

  // An unrecognised host is only a failure when it was the request's only hope of a
  // tenant; a platform hostname that maps to no tenant is normal for mobile and service
  // traffic, which carry the tenant in the token instead.
  const claims = collectTenantClaims({ host, domainRegistry, principal, serviceContext });

  if (claims.length === 0) {
    if (host !== undefined && host !== null && !principal && !serviceContext) {
      // 404 rather than 403: a 403 would confirm that a hostname exists but is not yet
      // verified, which is a free enumeration oracle over the tenant registry.
      await deny('host_not_recognised', 404, { message: 'host does not map to a verified tenant domain' });
    }
    await deny('tenant_context_missing', 400, { message: 'no channel supplied a tenant; there is no default tenant' });
  }

  const distinct = [...new Set(claims.map((c) => c.tenant_id))];
  if (distinct.length > 1) {
    await deny('tenant_context_conflict', 403, {
      message: `tenant channels disagree: ${claims.map((c) => `${c.channel}=${c.tenant_id}`).join(', ')}`,
      channels: claims.map((c) => c.channel),
    }, distinct[1]);
  }

  const tenant_id = distinct[0];
  try {
    parseId(tenant_id, 'tenant');
  } catch {
    await deny('tenant_context_missing', 400, { message: `not a canonical tenant identifier: ${JSON.stringify(tenant_id)}` });
  }

  const binding = (bindingRegistry ?? []).find((b) => b.tenant_id === tenant_id);
  if (!binding || binding.status !== ROUTING_BINDING_STATUS) {
    await deny('tenant_binding_unavailable', 503, {
      message: `tenant ${tenant_id} has no active data binding; there is no shared fallback database`,
    }, tenant_id);
  }

  return freezeContext({
    kind: 'tenant',
    tenant_id,
    binding_key: binding.binding_key,
    channels: claims.map((c) => c.channel),
    correlation_id,
    resolved_at: nowUtc(),
  });
}

/**
 * Control-plane work resolves no tenant. It is a distinct kind rather than a tenant
 * context with a null tenant, so "no tenant resolved" can never be mistaken for
 * "tenant resolved successfully" by a forgotten null check.
 */
export function createControlPlaneContext({ principal, correlationId } = {}) {
  return freezeContext({
    kind: 'control_plane',
    principal: principal?.name ?? 'system',
    correlation_id: correlationId ?? newId('event'),
    resolved_at: nowUtc(),
  });
}

/**
 * Freeze a context for the lifetime of the request. Mutation throws rather than failing
 * silently: if a context can change after the boundary check, the check describes a
 * moment rather than the request.
 */
function freezeContext(fields) {
  const target = { ...fields };
  if (Array.isArray(target.channels)) target.channels = Object.freeze([...target.channels]);
  const context = new Proxy(Object.freeze(target), {
    set(_t, property) {
      throw new TenantContextError(
        `tenant context is immutable for the lifetime of the request; cannot set ${String(property)}`,
        'tenant_context_immutable', 500, { property: String(property) },
      );
    },
    deleteProperty(_t, property) {
      throw new TenantContextError(
        `tenant context is immutable for the lifetime of the request; cannot delete ${String(property)}`,
        'tenant_context_immutable', 500, { property: String(property) },
      );
    },
    defineProperty(_t, property) {
      throw new TenantContextError(
        `tenant context is immutable for the lifetime of the request; cannot redefine ${String(property)}`,
        'tenant_context_immutable', 500, { property: String(property) },
      );
    },
  });
  // Record provenance against the object we hand out, capturing the control-plane values
  // out of reach of anything the caller can touch.
  RESOLVED.set(context, Object.freeze({
    kind: fields.kind,
    tenant_id: fields.tenant_id ?? null,
    binding_key: fields.binding_key ?? null,
  }));
  return context;
}

/** True only for a context this module resolved. Not forgeable, not serialisable. */
export function isResolvedContext(context) {
  return (typeof context === 'object' || typeof context === 'function') && context !== null
    && RESOLVED.has(context);
}

/**
 * Assert that a value really is a resolved tenant context before anything trusts it.
 *
 * Checking `kind === 'tenant'` alone is not enough: a hand-made object carrying that
 * field would otherwise be accepted, and one whose `tenant_id` is null or undefined would
 * then compare equal to a record whose tenant is also null — a malformed context
 * admitting a tenantless record. Both fields are validated, so an unresolved or forged
 * context cannot reach data at all.
 */
export async function assertResolvedTenantContext(context, { audit, principal, auditTimeoutMs } = {}) {
  const refuse = async (message, code, status, detail, attempted = null) => {
    await emitDenial(audit, {
      context_tenant_id: isResolvedContext(context) ? RESOLVED.get(context).tenant_id : null,
      attempted_tenant_id: attempted,
      principal: principal ?? 'unknown',
      resource_type: 'tenant_context',
      resource_id: null,
      reason_code: code,
      correlation_id: correlationOf(context),
    }, { auditTimeoutMs });
    throw new TenantContextError(message, code, status, detail);
  };

  // Provenance first. Field shape is what a forged object imitates most easily, so it is
  // never what grants access; only an object this module resolved can pass here.
  if (!isResolvedContext(context)) {
    await refuse(
      'context did not come from tenant resolution; a hand-built context cannot select a tenant or a binding',
      context?.kind === 'control_plane' ? 'control_plane_context_misuse' : 'tenant_context_not_resolved',
      403, { kind: context?.kind ?? null },
      typeof context?.tenant_id === 'string' ? context.tenant_id : null,
    );
  }
  const resolved = RESOLVED.get(context);
  if (resolved.kind !== 'tenant') {
    await refuse('a tenant repository requires a resolved tenant context, not a control-plane context',
      'control_plane_context_misuse', 403, { kind: resolved.kind });
  }
  try {
    parseId(resolved.tenant_id, 'tenant');
  } catch {
    await refuse('resolved context does not carry a canonical tenant identifier',
      'tenant_context_missing', 403, { tenant_id: resolved.tenant_id });
  }
  if (typeof resolved.binding_key !== 'string' || resolved.binding_key.length === 0) {
    await refuse('resolved context does not carry a data binding',
      'tenant_binding_unavailable', 503, { tenant_id: resolved.tenant_id }, resolved.tenant_id);
  }
  return resolved;
}

/**
 * A repository may only be opened from a resolved tenant context (Decision 1.5, 4.1).
 *
 * The binding returned is the one control-plane resolution recorded, read from module
 * -private provenance rather than from the context object. A `binding_key` a caller sets
 * on an object is therefore inert: it is never read, so it can never route anything.
 */
export async function bindingForRepository(context, { audit, principal, auditTimeoutMs } = {}) {
  const resolved = await assertResolvedTenantContext(context, { audit, principal, auditTimeoutMs });
  return resolved.binding_key;
}

function correlationOf(context) {
  const id = context?.correlation_id;
  return typeof id === 'string' && id.startsWith('evt_') ? id : newId('event');
}

/**
 * Assert a tenant-plane record belongs to the resolved tenant.
 *
 * Belt and braces over the routing: routing should make a foreign record unreachable, and
 * this catches the case where it was reachable anyway — a mis-seeded binding, a cached
 * connection, a query that escaped the repository layer. The denial is audited and then
 * surfaced as 404, never 403, so an identifier cannot be used to probe another tenant's
 * data (ADR-0001 section 1.5).
 */
export async function assertRecordInTenant(context, record, { resourceType = 'record', audit, principal, auditTimeoutMs } = {}) {
  const resolved = await assertResolvedTenantContext(context, { audit, principal, auditTimeoutMs });
  // Compare against the tenant that control-plane resolution recorded, not a field on the
  // object. The record's tenant must be a canonical identifier in its own right, because
  // comparing raw values would let two absent tenants match each other.
  const raw = record?.tenant_id;
  const recordTenant = typeof raw === 'string' && raw.length > 0 ? raw : null;
  if (recordTenant !== null && recordTenant === resolved.tenant_id) return true;

  await emitDenial(audit, {
    context_tenant_id: resolved.tenant_id,
    attempted_tenant_id: recordTenant,
    principal: principal ?? 'unknown',
    resource_type: resourceType,
    resource_id: record?.id ?? null,
    reason_code: 'cross_tenant_access_denied',
    correlation_id: context.correlation_id,
  }, { auditTimeoutMs });
  throw new TenantContextError(
    `${resourceType} not found`, 'not_found', 404,
    { correlation_id: context.correlation_id, denial_reason: 'cross_tenant_access_denied' },
  );
}

/**
 * Build and emit the append-only denial event. Written before the denial is returned, so
 * the audit is part of the deny path rather than a side effect a refactor can drop.
 * Carries no payload: an audit that copies the record it protected is a second copy of
 * that record outside the boundary.
 */
export function buildDenialEvent(fields) {
  // ADR-0003 Decision 6.2: a denial is an audit event with outcome DENIED, not a shape of
  // its own. One field is denial-specific — attempted_tenant_id — because the pair of
  // tenants is what distinguishes a cross-tenant attempt from an ordinary miss.
  const principal = fields.principal ?? 'unknown';
  const actor = typeof principal === 'object' && principal !== null
    ? principal
    // This surface never learns what kind of principal it refused, so it says so rather
    // than guessing `user` and putting a wrong fact in the audit.
    : { type: 'unknown', id: String(principal) };

  const base = buildAuditEvent({
    tenant_id: fields.context_tenant_id ?? null,
    actor,
    action: fields.resource_type === 'tenant_context' ? 'tenant.resolve' : 'tenant.access',
    target: { type: fields.resource_type, id: fields.resource_id ?? null },
    outcome: 'DENIED',
    reason_code: fields.reason_code,
    correlation_id: fields.correlation_id,
    // A denial names a tenant and often a person or credential identifier, so it is not
    // public even though it carries no fields.
    min_classification: 'internal',
  });
  return deepFreeze({ ...base, attempted_tenant_id: fields.attempted_tenant_id ?? null });
}

/**
 * Write the denial event, and do not come back until it is durable.
 *
 * ADR-0002 Decision 5 says the audit is written before the response is returned. A real
 * audit store is asynchronous, so "written" means the sink's promise has settled — not
 * that the call was made. Ignoring that promise would return the denial while the write
 * is still in flight, and a rejection arriving afterwards could no longer turn into
 * `audit_unavailable`: the caller already has its answer. That is the asynchronous form
 * of an unaudited denial.
 *
 * Every way of failing to persist — a missing or non-callable sink, a synchronous throw,
 * a rejected promise, or a sink that never settles — becomes `audit_unavailable`, so a
 * caller has one machine code to handle and the original failure survives on `cause`.
 */
async function emitDenial(audit, fields, { auditTimeoutMs = AUDIT_TIMEOUT_MS } = {}) {
  const unavailable = (detail, cause) => {
    const error = new TenantContextError(
      `audit sink unavailable; a ${fields.reason_code} denial may not be returned without its audit record`,
      'audit_unavailable', 503, { reason_code: fields.reason_code, ...detail },
    );
    if (cause !== undefined) error.cause = cause;
    return error;
  };

  if (typeof audit !== 'function') throw unavailable({ audit: 'missing' });

  const event = buildDenialEvent(fields);
  let pending;
  try {
    pending = audit(event);
  } catch (error) {
    throw unavailable({ audit: 'threw' }, error);
  }
  if (pending === null || typeof pending?.then !== 'function') return event; // synchronous sink

  let timer;
  try {
    await Promise.race([
      pending,
      new Promise((_resolve, reject) => {
        timer = setTimeout(
          () => reject(unavailable({ audit: 'timed_out', audit_timeout_ms: auditTimeoutMs })),
          auditTimeoutMs,
        );
        // Deliberately not unref'd: the timer is what turns a hung sink into a refusal, so
        // it must keep the process alive until it fires. `finally` clears it either way,
        // so it never outlives the call.
      }),
    ]);
  } catch (error) {
    throw error instanceof TenantContextError && error.code === 'audit_unavailable'
      ? error
      : unavailable({ audit: 'rejected' }, error);
  } finally {
    clearTimeout(timer);
  }
  return event;
}
