// Test-only construction of an already-"resolved" TenantContext.
//
// BIO-MOB-001-QA-02 (Chioma, acceptance round 2): production code has no
// factory that mints a resolved context from raw fields (that was exactly
// the finding: `TenantContext.resolve()` let a caller-supplied tenant id and
// binding key pass as if a trusted resolution boundary had produced them).
// The only production path is `TenantContext.fromTrustedJson`, deserializing
// a payload that would come from a trusted source. Tests that need a bound
// session to exercise the shell/nav/offline UI (there being no live
// production path to reach one yet) go through this helper — an explicit,
// clearly-labelled test double standing in for that future trusted
// boundary, per ADR-0002 Decision 6's interim-evidence accommodation. It
// must never be imported from `mobile/lib`.
library;

import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/identifiers/clock.dart';
import 'package:biolog_mobile/core/tenant/tenant_context.dart';

TenantContext syntheticTenantContext({String? tenantId, String bindingKey = 'gate-3'}) {
  return TenantContext.fromTrustedJson({
    'kind': 'tenant',
    'tenant_id': tenantId ?? newCanonicalId(CanonicalIdType.tenant),
    'binding_key': bindingKey,
    'channels': const ['principal'],
    'correlation_id': newCanonicalId(CanonicalIdType.event),
    'resolved_at': nowUtc(),
  });
}
