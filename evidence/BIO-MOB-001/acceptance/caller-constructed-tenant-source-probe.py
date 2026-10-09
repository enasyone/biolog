from pathlib import Path

screen = Path("mobile/lib/features/tenant_select/tenant_select_screen.dart").read_text()
session = Path("mobile/lib/core/session/session_controller.dart").read_text()
context = Path("mobile/lib/core/tenant/tenant_context.dart").read_text()

failures = []
if "binding-key-field" in screen or "Binding key" in screen:
    failures.append("operator UI accepts a binding key")
if "tenantId: _tenantIdController.text.trim()" in screen:
    failures.append("operator-entered tenant id is passed into binding")
if "bindingKey: _bindingKeyController.text.trim()" in screen:
    failures.append("operator-entered binding key is passed into binding")
if "TenantContext.resolve(tenantId: tenantId, bindingKey: bindingKey)" in session:
    failures.append("session constructs a resolved context from caller fields")
if "channelsValue: const ['principal']" in context:
    failures.append("locally constructed context claims the authenticated principal channel")

if failures:
    print("FAIL: ADR-0002 tenant resolution is caller-constructed")
    for failure in failures:
        print(f"- {failure}")
    raise SystemExit(1)

print("PASS: mobile tenant context comes from a trusted resolution boundary")
