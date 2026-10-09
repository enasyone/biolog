from pathlib import Path

session = Path("mobile/lib/core/session/session_controller.dart").read_text()
database = Path("mobile/lib/core/db/sqlcipher_app_database.dart").read_text()
context = Path("mobile/lib/core/tenant/tenant_context.dart").read_text()

failures = []
if "_tenantContext = await _database.loadTenantContext()" in session:
    failures.append("bootstrap binds any context returned from local storage")
if "return TenantContext.fromJson(json)" in database:
    failures.append("local storage proves only schema shape, not resolution provenance")
if "factory TenantContext.fromJson" in context and "binding_key" in context:
    failures.append("a schema-valid context minted by the removed vulnerable form still deserializes")

# A remediation must either invalidate/migrate contexts written by the vulnerable
# producer or authenticate their provenance. Merely removing the producer does not
# revoke already-persisted values across an app upgrade.
provenance_markers = (
    "trusted_resolution_provenance",
    "resolution_provenance",
    "invalidateLegacyTenantContext",
    "invalidate_legacy_tenant_context",
)
combined = session + database + context
if not any(marker in combined for marker in provenance_markers):
    failures.append("no provenance authentication or legacy-context invalidation exists")

if failures:
    print("FAIL: a tenant context created by the removed caller-input path survives upgrade")
    for failure in failures:
        print(f"- {failure}")
    raise SystemExit(1)

print("PASS: legacy caller-created tenant contexts cannot become bound after upgrade")
