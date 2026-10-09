// BIO-MOB-001-QA-04 (Seyi, round 5, High): the round-3 provenance marker
// only guarded the read side. Any code holding a `TenantContext` — trivially
// obtained via the public `TenantContext.fromTrustedJson`, which validates
// shape only, exactly as documented — could call
// `SqlCipherAppDatabase.saveTenantContext` directly and have it stamped
// "trusted" unconditionally, forging exactly the resolved claim QA-02
// removed the UI path for. Fixed by mirroring `TenantContextMiddleware`'s
// own "unconditionally null" pattern (ADR-0002 Decision 6): the real store's
// `saveTenantContext` now has no working implementation at all until a real
// resolution boundary exists.
//
// This test instantiates the *real* `SqlCipherAppDatabase` (not the fake) —
// deliberately, since the finding was specifically that the real
// implementation had a working forge path. It never calls `.open()` and
// never touches SQLCipher/secure-storage platform channels: the fixed
// `saveTenantContext` throws before touching either, which is exactly what
// makes it safe to assert on the host.
import 'package:biolog_mobile/core/db/sqlcipher_app_database.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/synthetic_tenant_context.dart';

void main() {
  test(
      'the real store has no working saveTenantContext until a resolution boundary exists',
      () async {
    final database = SqlCipherAppDatabase();
    // Exactly Seyi's repro shape: a context built from raw, caller-supplied
    // fields via the public, shape-only-validating fromTrustedJson.
    final forged = syntheticTenantContext(bindingKey: 'attacker-input');

    await expectLater(
      () => database.saveTenantContext(forged),
      throwsA(isA<UnimplementedError>()),
    );
  });
}
