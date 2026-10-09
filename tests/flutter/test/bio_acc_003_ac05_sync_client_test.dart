// BIO-ACC-003-AC05: reconnect sync uploads events exactly once. Exercises
// AccessCacheSyncClient against a mocked HTTP client (no real network),
// mirroring BIO-MOB-003-FEMI-01's own HttpSyncTransport test discipline —
// proving the request shape (device headers, Idempotency-Key = event id)
// and the response handling (mark-synced only on success, one event's
// failure does not abort the batch) actually work.
import 'dart:convert';

import 'package:biolog_mobile/core/access/access_cache_sync_client.dart';
import 'package:biolog_mobile/core/access/presentation_event.dart';
import 'package:biolog_mobile/core/access/revocation_entry.dart';
import 'package:biolog_mobile/core/security/secure_key_value_store.dart';
import 'package:biolog_mobile/core/sync/device_credential_store.dart';
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

const _syntheticSecret = 'synthetic-access-cache-device-secret';

Future<DeviceCredentialStore> _provisioned() async {
  final store = DeviceCredentialStore(
    store: _InMemorySecureStore(),
    storageKey: 'test.access_cache_device_secret',
  );
  await store.writeSecret(_syntheticSecret);
  return store;
}

void main() {
  test('pullRevocationDelta sends device headers and the stored cursor, then caches the response', () async {
    final db = FakeAppDatabase();
    await db.open();
    await db.cacheRevocations(const [], cursor: '2026-09-23T00:00:00.000Z', syncedAt: DateTime.utc(2026, 9, 23));

    http.Request? captured;
    final client = AccessCacheSyncClient(
      baseUrl: Uri.parse('https://access-cache.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        captured = request;
        return http.Response(
          jsonEncode({
            'items': [
              {'person_id': 'per_revoked', 'revoked_at': '2026-09-24T00:00:00.000Z'},
            ],
            'cursor': '2026-09-24T00:00:00.000Z',
          }),
          200,
        );
      }),
    );

    await client.pullRevocationDelta(db, now: () => DateTime.utc(2026, 9, 24));

    expect(captured, isNotNull);
    expect(captured!.method, 'GET');
    expect(captured!.url.path, '/api/v1/access-cache/revocation-deltas');
    expect(captured!.url.queryParameters['since'], '2026-09-23T00:00:00.000Z');
    expect(captured!.headers['X-Biolog-Device-Id'], await db.deviceId());
    expect(captured!.headers['X-Biolog-Device-Secret'], _syntheticSecret);

    expect(await db.isRevoked('per_revoked'), isTrue);
    expect(await db.revocationCacheCursor(), '2026-09-24T00:00:00.000Z');
  });

  test('pushPendingPresentationEvents uses the event id as the Idempotency-Key and marks it synced on success', () async {
    final db = FakeAppDatabase();
    await db.open();
    const event = PresentationEvent(
      eventId: 'evt_test_0000000000000000000',
      personId: 'per_revoked',
      outcome: PresentationOutcome.denied,
      reasonCode: reasonDeniedRevokedCached,
      occurredAt: '2026-09-24T00:00:00.000Z',
      cacheSnapshotAt: '2026-09-23T00:00:00.000Z',
      stale: false,
    );
    await db.recordPresentationEvent(event);

    http.Request? captured;
    final client = AccessCacheSyncClient(
      baseUrl: Uri.parse('https://access-cache.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        captured = request;
        return http.Response(jsonEncode({'event_id': event.eventId, 'received_at': '2026-09-24T00:00:01.000Z'}), 201);
      }),
    );

    await client.pushPendingPresentationEvents(db, tenantId: 'ten_test_0000000000000000000');

    expect(captured, isNotNull);
    expect(captured!.method, 'POST');
    expect(captured!.headers['Idempotency-Key'], event.eventId);
    final body = jsonDecode(captured!.body) as Map<String, Object?>;
    expect(body['person_id'], 'per_revoked');
    expect(body['outcome'], 'DENIED');

    expect(await db.pendingPresentationEvents(), isEmpty);
  });

  test('one events transport failure does not abandon the rest of the batch', () async {
    final db = FakeAppDatabase();
    await db.open();
    const failing = PresentationEvent(
      eventId: 'evt_fail_00000000000000000000',
      personId: 'per_a',
      outcome: PresentationOutcome.allowed,
      reasonCode: reasonAllowedNotRevoked,
      occurredAt: '2026-09-24T00:00:00.000Z',
      cacheSnapshotAt: null,
      stale: true,
    );
    const succeeding = PresentationEvent(
      eventId: 'evt_ok_000000000000000000000',
      personId: 'per_b',
      outcome: PresentationOutcome.allowed,
      reasonCode: reasonAllowedNotRevoked,
      occurredAt: '2026-09-24T00:00:01.000Z',
      cacheSnapshotAt: null,
      stale: true,
    );
    await db.recordPresentationEvent(failing);
    await db.recordPresentationEvent(succeeding);

    final client = AccessCacheSyncClient(
      baseUrl: Uri.parse('https://access-cache.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        final body = jsonDecode(request.body) as Map<String, Object?>;
        if (body['event_id'] == failing.eventId) {
          return http.Response('', 500);
        }
        return http.Response(jsonEncode({'event_id': body['event_id'], 'received_at': '2026-09-24T00:00:02.000Z'}), 201);
      }),
    );

    await client.pushPendingPresentationEvents(db, tenantId: 'ten_test_0000000000000000000');

    final pending = await db.pendingPresentationEvents();
    expect(pending, hasLength(1));
    expect(pending.first.eventId, failing.eventId);
  });

  test('an unprovisioned device fails fast without attempting the network, for both pull and push', () async {
    final db = FakeAppDatabase();
    await db.open();
    var called = false;
    final client = AccessCacheSyncClient(
      baseUrl: Uri.parse('https://access-cache.example.test'),
      credentialStore: DeviceCredentialStore(store: _InMemorySecureStore(), storageKey: 'unprovisioned'),
      client: MockClient((request) async {
        called = true;
        return http.Response('', 200);
      }),
    );

    await expectLater(() => client.pullRevocationDelta(db), throwsA(isA<AccessCacheDeviceNotProvisionedException>()));
    await expectLater(
      () => client.pushPendingPresentationEvents(db, tenantId: 'ten_test_0000000000000000000'),
      throwsA(isA<AccessCacheDeviceNotProvisionedException>()),
    );
    expect(called, isFalse);
  });
}
