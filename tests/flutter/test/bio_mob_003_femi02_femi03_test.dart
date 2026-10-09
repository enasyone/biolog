// BIO-MOB-003-FEMI-02/FEMI-03 (Femi, round 2).
import 'dart:async';

import 'package:biolog_mobile/core/connectivity/connectivity_signal.dart';
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/security/secure_key_value_store.dart';
import 'package:biolog_mobile/core/session/session_controller.dart';
import 'package:biolog_mobile/core/sync/device_credential_store.dart';
import 'package:biolog_mobile/core/sync/sync_engine.dart';
import 'package:biolog_mobile/core/sync/sync_transport.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'support/fake_app_database.dart';
import 'support/fake_connectivity_signal.dart';
import 'support/fake_sync_transport.dart';

class _InMemorySecureStore implements SecureKeyValueStore {
  final Map<String, String> _values = {};

  @override
  Future<String?> read(String key) async => _values[key];

  @override
  Future<void> write(String key, String value) async {
    _values[key] = value;
  }
}

Future<DeviceCredentialStore> _provisionedCredentialStore() async {
  final credentials = DeviceCredentialStore(store: _InMemorySecureStore());
  await credentials.writeSecret('synthetic-device-secret');
  return credentials;
}

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
  test(
      'FEMI-02: a mutation already queued before boot syncs on the very first connectivity '
      'reading, not only on a later transition', () async {
    final database = FakeAppDatabase();
    await database.open();
    final mutation = await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport();
    final syncEngine = SyncEngine(database: database, transport: transport);
    // Already online BEFORE bootstrap() ever runs — the exact scenario
    // FEMI-02 describes: the device never transitions offline->online
    // during this session, it simply starts connected.
    final connectivity = FakeConnectivitySignal(initial: ConnectivityState.online);
    final session = SessionController(
      database: database,
      connectivity: connectivity,
      syncEngine: syncEngine,
    );

    await session.bootstrap();
    // No connectivity.set(...) call anywhere in this test — if the initial
    // reading were missed (the FEMI-02 bug), nothing would ever trigger a
    // sync pass and this would time out waiting for it, since there is no
    // later transition to fall back on.
    await Future<void>.delayed(const Duration(milliseconds: 20));

    expect(transport.uploadCallCounts[mutation.mutationId], 1,
        reason: 'bootstrap must not silently drop the very first connectivity reading as a sync '
            'trigger just because it arrived before a later transition would have');
  });

  test('FEMI-03: a transport that never responds does not permanently block the single-flight guard',
      () async {
    final database = FakeAppDatabase();
    await database.open();
    final stuck = await database.enqueueMutation(_synthetic());
    final transport = FakeSyncTransport()
      ..script(stuck.mutationId, () => throw TimeoutException('simulated hung request'));
    final engine = SyncEngine(database: database, transport: transport);

    // The "hung" attempt: FakeSyncTransport throws synchronously rather than
    // literally hanging (deterministic, no real wall-clock wait needed) —
    // this exercises exactly what SyncEngine sees once HttpSyncTransport's
    // own `.timeout(...)` fires: a thrown TimeoutException from `upload`.
    final first = await engine.runSyncPass();
    expect(first.failed, 1);

    // The real regression: before the FEMI-03 fix, a hung `upload` call
    // never returned at all, so `runSyncPass`'s `finally` never ran and
    // `_inFlight` stayed true forever — every subsequent reconnect would
    // see the single-flight guard permanently "busy" and silently no-op,
    // not just this one mutation's retry. A THROWN exception (what a real
    // timeout produces) already lets `finally` run, so this pass must
    // proceed normally, not report `SyncPassResult.inFlight`.
    final second = await engine.runSyncPass();
    expect(identical(second, SyncPassResult.inFlight), isFalse,
        reason: 'the single-flight guard must be released after a failed (including timed-out) '
            'attempt, not left permanently locked');
  });

  test('FEMI-03: HttpSyncTransport enforces a request deadline instead of waiting forever',
      () async {
    final neverResponds = Completer<void>();
    addTearDown(neverResponds.complete);
    final transport = HttpSyncTransport(
      baseUrl: Uri.parse('https://sync.example.test'),
      requestTimeout: const Duration(milliseconds: 50),
      credentialStore: await _provisionedCredentialStore(),
      client: MockClient((request) async {
        // Never completes on its own within the test's lifetime — the
        // transport's own timeout, not the mock, must be what ends this.
        await neverResponds.future.timeout(const Duration(seconds: 5), onTimeout: () {});
        return http.Response('', 200);
      }),
    );

    await expectLater(
      transport.upload(_synthetic()),
      throwsA(isA<TimeoutException>()),
    );
  });
}
