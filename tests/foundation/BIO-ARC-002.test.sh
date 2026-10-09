#!/usr/bin/env bash
# Developer tests for BIO-ARC-002, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-ARC-002.test.sh
#
# Artifact and consistency checks here; behaviour is asserted by the node:test suite in
# contracts/js/tenant-context.test.mjs. Both are offline and dependency-free.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0

check() {
  local name="$1"; shift
  total=$((total + 1))
  if "$@"; then
    echo "PASS  $name"
  else
    echo "FAIL  $name"
    failed=$((failed + 1))
  fi
}

ADR=docs/adr/ADR-0002-tenant-context-and-database-routing.md

# --- AC01: resolution rules defined --------------------------------------------------
check "AC01 ADR-0002 exists and is accepted" \
  bash -c "test -f $ADR && grep -q '\*\*Status:\*\* Accepted (BIO-ARC-002' $ADR"
check "AC01 ADR defines all three channels" \
  bash -c "grep -q '\`host\`' $ADR && grep -q '\`principal\`' $ADR && grep -q '\`service\`' $ADR"
check "AC01 ADR states channels are not ranked" \
  bash -c "grep -q 'no precedence order' $ADR"
check "AC01 ADR requires resolution before data access" \
  bash -c "grep -q 'before any tenant repository is constructed' $ADR"
check "AC01 tenant-context and tenant-domain schemas published" \
  bash -c "test -f contracts/schemas/tenant-context.schema.json && test -f contracts/schemas/tenant-domain.schema.json"
check "AC01 tenant context implementation published" test -f contracts/js/tenant-context.mjs

# --- AC02: no implicit or default tenant ---------------------------------------------
check "AC02 ADR forbids a default tenant" bash -c "grep -q 'no default tenant' $ADR"
check "AC02 ADR forbids a development-only fallback" \
  bash -c "grep -q 'a fallback that exists in development is a fallback that ships' $ADR"
check "AC02 ADR forbids a caller-supplied database binding" \
  bash -c "grep -q 'never\*\* carry a database name' $ADR"
check "AC02 no default tenant constant anywhere in the contract code" \
  bash -c '! grep -RniE "default_?tenant|fallback_?tenant|DEFAULT_TENANT_ID" contracts/js contracts/schemas'

# --- AC03: immutable for the request lifetime ----------------------------------------
check "AC03 ADR states the context is frozen for the request" \
  bash -c "grep -q 'frozen for the lifetime of the request or job' $ADR"
check "AC03 mutation is refused at runtime" \
  bash -c 'node --input-type=module -e "
    import { resolveTenantContext } from \"./contracts/js/index.mjs\";
    const t = \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\";
    const ctx = await resolveTenantContext({
      principal: { tenant_id: t, name: \"p\" },
      bindingRegistry: [{ tenant_id: t, binding_key: \"k\", status: \"ACTIVE\" }],
    });
    try { ctx.tenant_id = \"ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03\"; process.exit(1); }
    catch (e) { if (e.code !== \"tenant_context_immutable\") process.exit(1); }
  "'

# --- AC04: cross-tenant fails closed and is audited ----------------------------------
check "AC04 ADR requires 404 rather than 403 on a cross-tenant read" \
  bash -c "grep -q 'never 403' $ADR && grep -q 'existence oracle' $ADR"
check "AC04 ADR requires the audit before the response" \
  bash -c "grep -q 'before\*\* the response is returned' $ADR"
check "AC04 denial event schema published" test -f contracts/schemas/tenant-access-denial-event.schema.json
check "AC04 cross-tenant read is denied and audited at runtime" \
  bash -c 'node --input-type=module -e "
    import { resolveTenantContext, assertRecordInTenant } from \"./contracts/js/index.mjs\";
    const a = \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\";
    const b = \"ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03\";
    const ctx = await resolveTenantContext({
      principal: { tenant_id: a, name: \"p\" },
      bindingRegistry: [{ tenant_id: a, binding_key: \"k\", status: \"ACTIVE\" }],
    });
    const seen = [];
    try {
      await assertRecordInTenant(ctx, { id: \"x\", tenant_id: b }, { resourceType: \"person\", audit: (e) => seen.push(e) });
      process.exit(1);
    } catch (e) {
      if (e.status !== 404) process.exit(1);
      if (seen.length !== 1) process.exit(1);
      if (seen[0].reason_code !== \"cross_tenant_access_denied\") process.exit(1);
      if (seen[0].tenant_id !== a || seen[0].attempted_tenant_id !== b) process.exit(1);
      if (\"payload\" in seen[0] || \"version\" in seen[0]) process.exit(1);
    }
  "'

check "AC01 QA-01 ADR requires provable resolution provenance" \
  bash -c "grep -q 'provably the product of resolution' $ADR"
check "AC01 QA-01 a forged context cannot select a binding" \
  bash -c 'node --input-type=module -e "
    import { bindingForRepository, isResolvedContext } from \"./contracts/js/index.mjs\";
    const t = \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\";
    const forged = { kind: \"tenant\", tenant_id: t, binding_key: \"caller-chosen-db\" };
    if (isResolvedContext(forged)) process.exit(1);
    const seen = [];
    try { await bindingForRepository(forged, { audit: (e) => seen.push(e) }); process.exit(1); }
    catch (e) {
      if (e.code !== \"tenant_context_not_resolved\") process.exit(1);
      if (seen.length !== 1 || seen[0].reason_code !== \"tenant_context_not_resolved\") process.exit(1);
    }
  "'
check "AC04 QA-02 ADR makes the audit sink a hard dependency" \
  bash -c "grep -q 'hard dependency of denying, not an option' $ADR"
check "AC04 QA-02 a denial without an audit sink fails closed" \
  bash -c 'node --input-type=module -e "
    import { resolveTenantContext, assertRecordInTenant } from \"./contracts/js/index.mjs\";
    const a = \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\";
    const b = \"ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03\";
    const ctx = await resolveTenantContext({
      principal: { tenant_id: a, name: \"p\" },
      bindingRegistry: [{ tenant_id: a, binding_key: \"k\", status: \"ACTIVE\" }],
    });
    try { await assertRecordInTenant(ctx, { id: \"x\", tenant_id: b }, { resourceType: \"person\" }); process.exit(1); }
    catch (e) { if (e.code !== \"audit_unavailable\" || e.status !== 503) process.exit(1); }
  "'

check "AC04 QA-03 ADR requires the audit write to have settled" \
  bash -c "grep -q \"the sink's promise has settled\" $ADR"
check "AC04 QA-03 an async sink persists before the denial is returned" \
  bash -c 'node --input-type=module -e "
    import { resolveTenantContext, assertRecordInTenant } from \"./contracts/js/index.mjs\";
    const a = \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\";
    const b = \"ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03\";
    const ctx = await resolveTenantContext({
      principal: { tenant_id: a, name: \"p\" },
      bindingRegistry: [{ tenant_id: a, binding_key: \"k\", status: \"ACTIVE\" }],
    });
    const order = [];
    const durable = () => new Promise((r) => setTimeout(() => { order.push(\"persisted\"); r(); }, 10));
    try { await assertRecordInTenant(ctx, { id: \"x\", tenant_id: b }, { resourceType: \"person\", audit: durable }); process.exit(1); }
    catch (e) { order.push(\"response:\" + e.code); }
    if (order.join(\",\") !== \"persisted,response:not_found\") process.exit(1);
  "'
check "AC04 QA-03 an async sink that rejects fails the denial closed" \
  bash -c 'node --input-type=module -e "
    import { resolveTenantContext, assertRecordInTenant } from \"./contracts/js/index.mjs\";
    const a = \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\";
    const b = \"ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03\";
    const ctx = await resolveTenantContext({
      principal: { tenant_id: a, name: \"p\" },
      bindingRegistry: [{ tenant_id: a, binding_key: \"k\", status: \"ACTIVE\" }],
    });
    const rejecting = () => new Promise((_r, rej) => setTimeout(() => rej(new Error(\"store down\")), 10));
    try { await assertRecordInTenant(ctx, { id: \"x\", tenant_id: b }, { resourceType: \"person\", audit: rejecting }); process.exit(1); }
    catch (e) { if (e.code !== \"audit_unavailable\" || e.status !== 503) process.exit(1); }
  "'
check "AC04 QA-03 a sink that never settles times out and fails closed" \
  bash -c 'node --input-type=module -e "
    import { resolveTenantContext, assertRecordInTenant, AUDIT_TIMEOUT_MS } from \"./contracts/js/index.mjs\";
    if (AUDIT_TIMEOUT_MS !== 5000) process.exit(1);
    const a = \"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77\";
    const b = \"ten_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03\";
    const ctx = await resolveTenantContext({
      principal: { tenant_id: a, name: \"p\" },
      bindingRegistry: [{ tenant_id: a, binding_key: \"k\", status: \"ACTIVE\" }],
    });
    try {
      await assertRecordInTenant(ctx, { id: \"x\", tenant_id: b },
        { resourceType: \"person\", audit: () => new Promise(() => {}), auditTimeoutMs: 25 });
      process.exit(1);
    } catch (e) { if (e.code !== \"audit_unavailable\" || e.detail.audit !== \"timed_out\") process.exit(1); }
  "'

# --- shared-contract hygiene ---------------------------------------------------------
check "every published schema is valid JSON" \
  bash -c "python3 -c 'import json,glob;[json.load(open(f)) for f in glob.glob(\"contracts/schemas/*.json\")]'"
check "ADR index lists ADR-0002" bash -c "grep -q 'ADR-0002' docs/adr/README.md"
check "contracts README documents the tenant contract" \
  bash -c "grep -q 'tenant-context' contracts/README.md"
check "fixtures are marked synthetic" bash -c "grep -q SYNTHETIC contracts/js/tenant-context.test.mjs"
check "BIO-ARC-001 contracts still green (no regression)" bash tests/foundation/BIO-ARC-001.test.sh
check "contract behaviour suite passes" node --test contracts/js/

echo "=============================="
echo "$((total - failed))/$total checks passed"
[ "$failed" -eq 0 ]
