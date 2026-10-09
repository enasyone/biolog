// Seyi's five probes from evidence/BIO-ARC-002/codex/independent-review.sh, transcribed
// with `await` added to the calls that BIO-ARC-002-QA-03 made asynchronous. Nothing else
// is changed: same fixtures, same assertions, same exit conditions.
//
// Why this file exists: QA-03 said the interface must "either await an asynchronous
// durable sink through an asynchronous denial path or explicitly require and enforce a
// synchronous durable sink". I took the first option, so the four probes that previously
// passed now call promise-returning functions synchronously and fail on the calling
// convention, not on behaviour. This proves the behaviour they check is intact. The
// reviewer's own script is left untouched — it is their artifact, not mine to edit.
//
// Run: node evidence/BIO-ARC-002/builder/reviewer-probes-awaited.mjs
import {
  resolveTenantContext, assertRecordInTenant, bindingForRepository,
  createControlPlaneContext, TenantContextError,
} from '../../../contracts/js/index.mjs';

const a = 'ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77';
const b = 'ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03';
let failed = 0;
const check = async (name, fn) => {
  try { await fn(); console.log(`PASS ${name}`); } catch (error) { console.log(`FAIL ${name} — ${error.message}`); failed += 1; }
};
const fail = (why) => { throw new Error(why); };

await check('AC01 unanimity rejects every two-tenant channel conflict', async () => {
  const domains = [{ hostname: 'a.example', tenant_id: a, status: 'VERIFIED' }];
  const bindings = [a, b].map((tenant_id) => ({ tenant_id, binding_key: tenant_id, status: 'ACTIVE' }));
  for (const input of [
    { host: 'a.example', principal: { tenant_id: b } },
    { host: 'a.example', serviceContext: { tenant_id: b } },
    { principal: { tenant_id: a }, serviceContext: { tenant_id: b } },
  ]) {
    const events = [];
    try {
      await resolveTenantContext({ ...input, domainRegistry: domains, bindingRegistry: bindings, audit: (e) => events.push(e) });
      fail('resolution should have been refused');
    } catch (e) {
      if (e.code !== 'tenant_context_conflict' || events.length !== 1) fail(`got ${e.code}, ${events.length} events`);
    }
  }
});

await check('AC01/AC04 forged context cannot choose a repository binding', async () => {
  const forged = { kind: 'tenant', tenant_id: a, binding_key: 'caller-chosen-tenant-b-db' };
  try { await bindingForRepository(forged); fail('forged context should not bind'); }
  catch (e) { if (!(e instanceof TenantContextError)) fail(`wrong error type: ${e}`); }
});

await check('AC04 cross-tenant denial cannot return without a mandatory audit sink', async () => {
  const context = await resolveTenantContext({
    principal: { tenant_id: a }, bindingRegistry: [{ tenant_id: a, binding_key: 'a', status: 'ACTIVE' }],
  });
  try { await assertRecordInTenant(context, { id: 'x', tenant_id: b }); fail('should have been refused'); }
  catch (e) { if (e.code === 'not_found' && e.status === 404) fail('returned the ordinary denial with no audit'); }
});

await check('AC04 control-plane misuse is audited before denial', async () => {
  const events = [];
  try { await bindingForRepository(createControlPlaneContext(), { audit: (e) => events.push(e) }); fail('should have been refused'); }
  catch {
    if (events.length !== 1 || events[0].reason_code !== 'control_plane_context_misuse') fail('misuse was not audited');
  }
});

await check('AC04 asynchronous audit completes before the denial is returned', async () => {
  const context = await resolveTenantContext({
    principal: { tenant_id: a }, bindingRegistry: [{ tenant_id: a, binding_key: 'a', status: 'ACTIVE' }],
  });
  let persisted = false;
  const audit = async () => { await Promise.resolve(); persisted = true; };
  try { await assertRecordInTenant(context, { id: 'x', tenant_id: b }, { audit }); fail('should have been refused'); }
  catch (e) {
    if (e.code !== 'not_found' || e.status !== 404 || !persisted) fail(`code=${e.code} persisted=${persisted}`);
  }
});

console.log(failed === 0 ? 'ALL REVIEWER PROBES PASS (awaited)' : `${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
