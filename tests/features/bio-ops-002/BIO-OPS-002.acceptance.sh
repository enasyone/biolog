#!/usr/bin/env bash
set -u

failures=0
pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1"; failures=$((failures + 1)); }

resolver='api/Biolog.Api/Tenancy/DemoSessionTenantResolver.cs'
actions='web/context/session-actions.ts'
endpoint='api/Biolog.Api/Tenancy/DemoSessionContextAuditEndpoints.cs'
audit_event='api/Biolog.Api/Audit/AuditEvent.cs'
adr='docs/adr/ADR-0003-audit-evidence-and-event-envelopes.md'

# AC01: the selected signed claim must map to its own backend context, never alpha fallback.
if rg -q '"beta" => _syntheticTenantIdBeta' "$resolver" \
  && ! rg -q '_syntheticTenantIdBeta \?\? _syntheticTenantId' "$resolver"; then
  pass 'AC01 beta maps only to its own configured backend tenant'
else
  fail 'AC01 beta can collapse to the alpha backend tenant'
fi

# AC02/AC05: retain real host-bound cross-tenant and unknown-host HTTP regressions.
if rg -q 'BIOIDN002_QA01_a_tenant_B_caller_cannot_attach_a_profile_to_a_real_tenant_A_person' tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs; then
  pass 'AC02 real-host integration suite covers cross-tenant URL identifier manipulation'
else
  fail 'AC02 has no real-host cross-tenant identifier manipulation regression'
fi
if rg -q 'Assigning_a_profile_through_an_unrecognised_host_fails_closed' tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs \
  && rg -q 'beta_is_rejected_outright' tests/dotnet/Biolog.IntegrationTests/DemoSessionContextAuditEndpointsTests.cs; then
  pass 'AC05 unknown host and unconfigured tenant fail closed'
else
  fail 'AC05 fail-closed regressions are missing'
fi

# AC03: role change must alter visible navigation/actions, not only a stored role label.
if rg -q 'switching role updates visible nav items' tests/frontend/playwright/bio-ux-002-shell.spec.ts \
  && rg -q "selectOption\('admin'\)" tests/frontend/playwright/bio-ux-002-shell.spec.ts; then
  pass 'AC03 browser regression proves role-dependent navigation changes'
else
  fail 'AC03 has no browser-visible role/action regression'
fi

# AC04: shared envelope, derived old actor, and audit completion before cookie commit.
if rg -q 'new AuditEvent' "$endpoint" \
  && rg -q 'Actor = new AuditActor' "$endpoint" \
  && rg -q 'Device = null' "$endpoint" \
  && rg -q 'ReasonCode = "context_switch"' "$endpoint" \
  && rg -q 'Classification = "internal"' "$endpoint" \
  && rg -q 'CausationId = null' "$endpoint"; then
  pass 'AC04 uses the shared ADR-0003 audit envelope'
else
  fail 'AC04 does not populate the shared ADR-0003 audit envelope'
fi
if rg -q '\["previous_actor_id"\] = derivedPreviousActorId' "$endpoint" \
  && rg -q 'previous_actor_id_role_mismatch' "$endpoint"; then
  pass 'AC04 previous actor is derived from the closed role mapping'
else
  fail 'AC04 trusts caller-controlled previous actor attribution'
fi
audit_line=$(rg -n 'const audited = await recordContextChangeAudit' "$actions" | cut -d: -f1)
cookie_line=$(rg -n 'jar.set\(SESSION_TOKEN_COOKIE, token' "$actions" | cut -d: -f1)
if [[ -n "$audit_line" && -n "$cookie_line" && "$audit_line" -lt "$cookie_line" ]]; then
  pass 'AC04 audit settles before the session cookie is committed'
else
  fail 'AC04 session effect can commit before audit settles'
fi

# The merged ADR remains authoritative. A verbal intent or builder note cannot supersede it.
if rg -q 'SY-OPS002-05|demo-session.*stateless|stateless.*demo-session' "$adr"; then
  pass 'AC04 independent-lifetime residual is recorded in the accepted ADR'
else
  fail 'AC04 claimed Kemi addendum is absent; accepted ADR still reopens independent process lifetimes'
fi

if (( failures > 0 )); then
  printf '%d required acceptance invariant(s) failed\n' "$failures"
  exit 1
fi

printf 'PASS: BIO-OPS-002 acceptance invariants satisfied\n'
