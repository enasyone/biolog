// Acceptance probe for BIO-MOB-001-AC03. Synthetic data only.
import 'package:biolog_mobile/core/tenant/tenant_context.dart';

void main() {
  final malformed = <String, Object?>{
    'kind': 'not-tenant',
    'tenant_id': 'attacker-controlled-tenant',
    'binding_key': ' ',
    'channels': <String>[],
    'correlation_id': 'not-an-event-id',
    'resolved_at': 'not-an-instant',
  };

  try {
    final restored = TenantContext.fromTrustedJson(malformed);
    print('FAIL: malformed persisted tenant context was accepted');
    print('tenant_id=${restored.tenantId}');
    print('channels=${restored.channels.length}');
    print('correlation_id=${restored.correlationId}');
    print('resolved_at=${restored.resolvedAt}');
    throw StateError('TenantContext.fromJson must fail closed');
  } on TenantBindingError {
    print('PASS: malformed persisted tenant context was rejected');
  }
}
