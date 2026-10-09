// BIO-MOB-001-AC03 / AC05: SessionController never fabricates a tenant, a
// fresh install never has pre-seeded tenant data bundled with it, and
// (BIO-MOB-001-QA-02) there is no method here that turns caller-supplied
// fields into a bound session — a resolved context can only ever arrive
// pre-built (`syntheticTenantContext` stands in for a future trusted
// resolution boundary; see its doc comment). (BIO-MOB-001-QA-03) a
// schema-valid record with no verified resolution provenance — such as one
// left over from before the QA-02 fix shipped — must never bind either, only
// a record this device itself wrote through the provenance-wrapping
// `saveTenantContext` does.
import 'package:biolog_mobile/core/session/session_controller.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';
import 'support/fake_connectivity_signal.dart';
import 'support/synthetic_tenant_context.dart';

void main() {
  test('bootstrap never fabricates a tenant on a fresh install (AC03/AC05)', () async {
    final session = SessionController(
      database: FakeAppDatabase(),
      connectivity: FakeConnectivitySignal(),
    );
    await session.bootstrap();
    expect(session.ready, isTrue);
    expect(session.hasBoundTenant, isFalse);
    expect(session.tenantContext, isNull);
  });

  test('a context already resolved by a trusted boundary persists and survives a reload',
      () async {
    final database = FakeAppDatabase();
    final context = syntheticTenantContext();
    // Stands in for a future resolution boundary handing the app an
    // already-resolved context to persist. SessionController has no method
    // that builds one from raw fields (BIO-MOB-001-QA-02).
    await database.open();
    await database.saveTenantContext(context);

    final session = SessionController(database: database, connectivity: FakeConnectivitySignal());
    await session.bootstrap();
    expect(session.hasBoundTenant, isTrue);
    expect(session.tenantContext!.tenantId, equals(context.tenantId));

    // Simulate a cold restart against the same underlying store.
    final reloaded = SessionController(database: database, connectivity: FakeConnectivitySignal());
    await reloaded.bootstrap();
    expect(reloaded.hasBoundTenant, isTrue);
    expect(reloaded.tenantContext!.tenantId, equals(context.tenantId));
  });

  test('signOut clears the bound tenant explicitly', () async {
    final database = FakeAppDatabase();
    await database.open();
    await database.saveTenantContext(syntheticTenantContext());

    final session = SessionController(database: database, connectivity: FakeConnectivitySignal());
    await session.bootstrap();
    expect(session.hasBoundTenant, isTrue);

    await session.signOut();
    expect(session.hasBoundTenant, isFalse);
    expect(session.tenantContext, isNull);
  });

  test(
      'a corrupted-but-provenanced persisted tenant context fails closed on bootstrap (BIO-MOB-001-QA-01)',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    // Wrapped with valid provenance (as a real write always is) but the
    // inner context is malformed — mirrors
    // evidence/BIO-MOB-001/acceptance/persisted-tenant-validation-probe.dart,
    // exercising TenantContext.fromTrustedJson's own schema validation
    // specifically, distinct from the provenance check below.
    database.debugInjectRawTenantContextJson(const {
      'provenance': 'trusted_resolution_provenance',
      'context': {
        'kind': 'not-tenant',
        'tenant_id': 'attacker-controlled-tenant',
        'binding_key': ' ',
        'channels': <String>[],
        'correlation_id': 'not-an-event-id',
        'resolved_at': 'not-an-instant',
      },
    });

    final session = SessionController(database: database, connectivity: FakeConnectivitySignal());
    await session.bootstrap();

    expect(session.ready, isTrue);
    expect(session.hasBoundTenant, isFalse);
    expect(session.tenantContext, isNull);
    // Self-heals: the corrupt record is cleared, not retried on every launch.
    expect(await database.loadTrustedTenantContext(), isNull);
  });

  test(
      'a schema-valid but unprovenanced legacy tenant context never binds after upgrade (BIO-MOB-001-QA-03)',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    // A record that is perfectly schema-valid but was written by the
    // now-removed vulnerable local-input path (BIO-MOB-001-QA-02) before
    // this app upgraded past it — i.e. exactly what the old
    // `context.toJson()` wrote directly, with no provenance wrapper at all.
    // Mirrors evidence/BIO-MOB-001/acceptance/legacy-forged-context-source-probe.py.
    database.debugInjectRawTenantContextJson(
      syntheticTenantContext(bindingKey: 'legacy-gate').toJson(),
    );

    final session = SessionController(database: database, connectivity: FakeConnectivitySignal());
    await session.bootstrap();

    expect(session.ready, isTrue);
    expect(session.hasBoundTenant, isFalse, reason: 'schema validity is not resolution provenance');
    expect(session.tenantContext, isNull);
    // Self-heals: the legacy record is invalidated, not silently trusted
    // again on the next launch.
    expect(await database.loadTrustedTenantContext(), isNull);
  });

  test('device id is minted once and stable across bootstraps', () async {
    final database = FakeAppDatabase();
    final first = SessionController(database: database, connectivity: FakeConnectivitySignal());
    await first.bootstrap();
    final second = SessionController(database: database, connectivity: FakeConnectivitySignal());
    await second.bootstrap();
    expect(second.deviceId, equals(first.deviceId));
  });
}
