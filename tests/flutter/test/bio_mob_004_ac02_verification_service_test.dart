// BIO-MOB-004-AC02: proves VerificationService picks the path the caller's
// own connectivity state says to use — online goes to the live decision
// service, offline goes to BIO-ACC-003's already-built revocation-cache
// check — never a guess, never a silent fallback hidden behind a try/catch.
import 'dart:convert';

import 'package:biolog_mobile/core/access/access_cache_service.dart';
import 'package:biolog_mobile/core/connectivity/connectivity_signal.dart';
import 'package:biolog_mobile/core/security/secure_key_value_store.dart';
import 'package:biolog_mobile/core/sync/device_credential_store.dart';
import 'package:biolog_mobile/core/verification/verification_outcome.dart';
import 'package:biolog_mobile/core/verification/verification_service.dart';
import 'package:biolog_mobile/core/verification/verification_sync_client.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'support/fake_app_database.dart';

class _InMemorySecureStore implements SecureKeyValueStore {
  final Map<String, String> _values = {};

  @override
  Future<String?> read(String key) async => _values[key];

  @override
  Future<void> write(String key, String value) async {
    _values[key] = value;
  }
}

void main() {
  test('online connectivity requests a live decision, never touching the local revocation cache', () async {
    final db = FakeAppDatabase();
    await db.open();
    var httpCalled = false;

    final credentialStore = DeviceCredentialStore(store: _InMemorySecureStore(), storageKey: 'test');
    await credentialStore.writeSecret('synthetic-secret');

    final service = VerificationService(
      database: db,
      accessCacheService: AccessCacheService(db),
      syncClient: VerificationSyncClient(
        baseUrl: Uri.parse('https://verify.example.test'),
        credentialStore: credentialStore,
        client: MockClient((request) async {
          httpCalled = true;
          return http.Response(
            jsonEncode({
              'event_id': 'evt_0000000000000000000000000',
              'person_id': 'per_0000000000000000000000000',
              'decision': 'allow',
              'reason_code': 'policy_matched',
              'presented_assurance_level': 'BASIC',
              'occurred_at': '2026-09-30T00:00:00.000Z',
              'recorded_at': '2026-09-30T00:00:00.100Z',
              'correlation_id': 'evt_1111111111111111111111111',
              'idempotency_key': null,
            }),
            201,
          );
        }),
      ),
    );

    final outcome = await service.verify(
      personId: 'per_0000000000000000000000000',
      credentialClass: 'card',
      locationId: 'main-gate',
      policyKey: 'default',
      connectivity: ConnectivityState.online,
    );

    expect(httpCalled, isTrue);
    expect(outcome.source, VerificationSource.online);
  });

  test('offline connectivity falls back to the local revocation-cache check, never attempting a network call', () async {
    final db = FakeAppDatabase();
    await db.open();
    var httpCalled = false;

    final service = VerificationService(
      database: db,
      accessCacheService: AccessCacheService(db),
      syncClient: VerificationSyncClient(
        baseUrl: Uri.parse('https://verify.example.test'),
        credentialStore: DeviceCredentialStore(store: _InMemorySecureStore(), storageKey: 'unprovisioned'),
        client: MockClient((request) async {
          httpCalled = true;
          return http.Response('', 200);
        }),
      ),
    );

    final outcome = await service.verify(
      personId: 'per_0000000000000000000000000',
      credentialClass: 'card',
      locationId: 'main-gate',
      policyKey: 'default',
      connectivity: ConnectivityState.offline,
    );

    expect(httpCalled, isFalse);
    expect(outcome.source, VerificationSource.offlineCache);
    // AC02's own reduced-capability default: no cached revocation entry
    // means allowed, never a fabricated deny.
    expect(outcome.decision, VerificationDecision.allow);
  });
}
