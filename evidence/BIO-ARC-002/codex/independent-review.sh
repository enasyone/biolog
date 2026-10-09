#!/usr/bin/env bash
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

failed=0
check() {
  local name="$1"
  shift
  if "$@"; then
    printf 'PASS %s\n' "$name"
  else
    printf 'FAIL %s\n' "$name"
    failed=$((failed + 1))
  fi
}

check "builder AC suite" bash tests/foundation/BIO-ARC-002.test.sh

check "AC01 unanimity rejects every two-tenant channel conflict" node --input-type=module -e '
  import { resolveTenantContext } from "./contracts/js/index.mjs";
  const a = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
  const b = "ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03";
  const domains = [{ hostname: "a.example", tenant_id: a, status: "VERIFIED" }];
  const bindings = [a, b].map((tenant_id) => ({ tenant_id, binding_key: tenant_id, status: "ACTIVE" }));
  for (const input of [
    { host: "a.example", principal: { tenant_id: b } },
    { host: "a.example", serviceContext: { tenant_id: b } },
    { principal: { tenant_id: a }, serviceContext: { tenant_id: b } },
  ]) {
    const events = [];
    try { await resolveTenantContext({ ...input, domainRegistry: domains, bindingRegistry: bindings, audit: (e) => events.push(e) }); process.exit(1); }
    catch (e) { if (e.code !== "tenant_context_conflict" || events.length !== 1) process.exit(1); }
  }
'

check "AC01/AC04 forged context cannot choose a repository binding" node --input-type=module -e '
  import { bindingForRepository, TenantContextError } from "./contracts/js/index.mjs";
  const forged = {
    kind: "tenant",
    tenant_id: "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77",
    binding_key: "caller-chosen-tenant-b-db",
  };
  try { await bindingForRepository(forged); process.exit(1); }
  catch (e) { if (!(e instanceof TenantContextError)) process.exit(1); }
'

check "AC04 cross-tenant denial cannot return without a mandatory audit sink" node --input-type=module -e '
  import { resolveTenantContext, assertRecordInTenant } from "./contracts/js/index.mjs";
  const a = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
  const b = "ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03";
  const context = await resolveTenantContext({
    principal: { tenant_id: a },
    bindingRegistry: [{ tenant_id: a, binding_key: "a", status: "ACTIVE" }],
  });
  try { await assertRecordInTenant(context, { id: "x", tenant_id: b }); }
  catch (e) {
    if (e.code === "not_found" && e.status === 404) process.exit(1);
    process.exit(0);
  }
  process.exit(1);
'

check "AC04 control-plane misuse is audited before denial" node --input-type=module -e '
  import { createControlPlaneContext, bindingForRepository } from "./contracts/js/index.mjs";
  const events = [];
  try { await bindingForRepository(createControlPlaneContext(), { audit: (e) => events.push(e) }); }
  catch { if (events.length === 1 && events[0].reason_code === "control_plane_context_misuse") process.exit(0); }
  process.exit(1);
'

check "AC04 asynchronous audit completes before the denial is returned" node --input-type=module -e '
  import { resolveTenantContext, assertRecordInTenant } from "./contracts/js/index.mjs";
  const a = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
  const b = "ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03";
  const context = await resolveTenantContext({
    principal: { tenant_id: a },
    bindingRegistry: [{ tenant_id: a, binding_key: "a", status: "ACTIVE" }],
  });
  let persisted = false;
  const audit = async () => { await Promise.resolve(); persisted = true; };
  try { await assertRecordInTenant(context, { id: "x", tenant_id: b }, { audit }); process.exit(1); }
  catch (e) {
    if (e.code !== "not_found" || e.status !== 404 || !persisted) process.exit(1);
  }
'

check "AC04 rejected audit promise fails closed with its cause" node --input-type=module -e '
  import { resolveTenantContext } from "./contracts/js/index.mjs";
  const cause = new Error("store rejected");
  try {
    await resolveTenantContext({ audit: async () => { throw cause; } });
    process.exit(1);
  } catch (e) {
    if (e.code !== "audit_unavailable" || e.status !== 503 || e.cause !== cause || e.detail.audit !== "rejected") process.exit(1);
  }
'

check "AC04 hung audit promise times out and fails closed" node --input-type=module -e '
  import { resolveTenantContext } from "./contracts/js/index.mjs";
  const started = Date.now();
  try {
    await resolveTenantContext({ audit: () => new Promise(() => {}), auditTimeoutMs: 25 });
    process.exit(1);
  } catch (e) {
    const elapsed = Date.now() - started;
    if (e.code !== "audit_unavailable" || e.detail.audit !== "timed_out" || elapsed < 15 || elapsed > 1000) process.exit(1);
  }
'

check "AC04 malformed thenable cannot yield an ordinary unaudited denial" node --input-type=module -e '
  import { resolveTenantContext } from "./contracts/js/index.mjs";
  const cause = new Error("then getter failed");
  const malformed = {};
  Object.defineProperty(malformed, "then", { get() { throw cause; } });
  try {
    await resolveTenantContext({ audit: () => malformed });
    process.exit(1);
  } catch (e) {
    if (e.code === "tenant_context_missing") process.exit(1);
  }
'

exit "$([ "$failed" -eq 0 ] && echo 0 || echo 1)"
