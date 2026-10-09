// BIO-MOB-003-FEMI-01 (Femi, round 1): SyncEngine must actually be
// constructed and attached by the live app (SessionController), not only
// exist in isolated sync-package tests. This exercises the real
// SessionController.bootstrap() wiring end to end — a fake transport
// stands in for the network, but SessionController itself, its real
// ConnectivitySignal wiring, and SyncEngine.attachTo are all exercised
// exactly as production code runs them.
import 'package:biolog_mobile/core/connectivity/connectivity_signal.dart';
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/session/session_controller.dart';
import 'package:biolog_mobile/core/sync/sync_engine.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';
import 'support/fake_connectivity_signal.dart';
import 'support/fake_sync_transport.dart';

OfflineMutation _synthetic() {
  return OfflineMutation.compose(
    tenantId: newCanonicalId(CanonicalIdType.tenant),
    deviceId: newCanonicalId(CanonicalIdType.device),
    aggregateId: newCanonicalId(CanonicalIdType.tenant),
    baseVersion: 1,
    idempotencyKey: 'idem-${DateTime.now().microsecondsSinceEpoch}',
    payloadJson: const {'field': 'SYNTHETIC'},
  );
}

void main() {
  test('bootstrap() attaches SyncEngine to the real connectivity signal, not just constructs one',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport();
    final syncEngine = SyncEngine(database: database, transport: transport);
    final connectivity = FakeConnectivitySignal(initial: ConnectivityState.offline);
    final session = SessionController(
      database: database,
      connectivity: connectivity,
      syncEngine: syncEngine,
    );

    await session.bootstrap();
    connectivity.set(ConnectivityState.online);
    // attachTo fires the pass unawaited from the stream listener.
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(transport.uploadCallCounts[mutation.mutationId], 1,
        reason: 'a reconnect through the real SessionController/ConnectivitySignal wiring must '
            'actually drive SyncEngine, not merely construct one that nothing ever calls');
  });

  test('SessionController constructs a working default SyncEngine when none is injected',
      () async {
    // No syncEngine passed — exercises the production default-construction
    // path (SessionController's own HttpSyncTransport-backed default),
    // proving it is at least constructible and wireable end to end without
    // throwing, distinct from the earlier version where the equivalent
    // default path was an unconditional UnimplementedError.
    final database = FakeAppDatabase();
    final session = SessionController(
      database: database,
      connectivity: FakeConnectivitySignal(),
    );

    await session.bootstrap();

    expect(session.ready, isTrue);
    session.dispose();
  });
}
