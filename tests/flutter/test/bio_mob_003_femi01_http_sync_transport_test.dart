// BIO-MOB-003-FEMI-01 (Femi, round 1): HttpSyncTransport must be a real,
// working implementation, not a permanent hook — these tests exercise it
// against a mocked HTTP client (no real network), proving the request shape
// and response handling actually work, which the earlier
// `throw UnimplementedError()` version could never do.
//
// BIO-MOB-003-QA-01 (Chioma, acceptance round 2): the central intake
// endpoint requires X-Biolog-Device-Id/X-Biolog-Device-Secret headers,
// which the original HttpSyncTransport never sent. These tests now also
// prove the real device-credential wiring: both headers are sent when a
// secret is provisioned (every pre-existing test below is now given one,
// since that is orthogonal to what each one is actually testing), and
// upload() fails fast — without making an HTTP request at all — when none
// is provisioned yet.
import 'package:biolog_mobile/core/identifiers/canonical_id.dart';
import 'package:biolog_mobile/core/offline/offline_mutation.dart';
import 'package:biolog_mobile/core/security/secure_key_value_store.dart';
import 'package:biolog_mobile/core/sync/device_credential_store.dart';
import 'package:biolog_mobile/core/sync/sync_ack.dart';
import 'package:biolog_mobile/core/sync/sync_transport.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

class _InMemorySecureStore implements SecureKeyValueStore {
  final Map<String, String> _values = {};

  @override
  Future<String?> read(String key) async => _values[key];

  @override
  Future<void> write(String key, String value) async {
    _values[key] = value;
  }
}

const _syntheticDeviceSecret = 'synthetic-device-secret';

Future<DeviceCredentialStore> _provisionedCredentialStore() async {
  final credentials = DeviceCredentialStore(store: _InMemorySecureStore());
  await credentials.writeSecret(_syntheticDeviceSecret);
  return credentials;
}

OfflineMutation _synthetic() {
  return OfflineMutation.compose(
    tenantId: newCanonicalId(CanonicalIdType.tenant),
    deviceId: newCanonicalId(CanonicalIdType.device),
    aggregateId: newCanonicalId(CanonicalIdType.tenant),
    baseVersion: 3,
    idempotencyKey: 'idem-${DateTime.now().microsecondsSinceEpoch}',
    payloadJson: const {'field': 'SYNTHETIC'},
    operation: OfflineMutationOperation.revocation,
  );
}

void main() {
  test(
      'POSTs to the versioned offline-mutations route with the Idempotency-Key, device-auth headers and delta body',
      () async {
    final mutation = _synthetic();
    http.Request? captured;
    final transport = HttpSyncTransport(
      baseUrl: Uri.parse('https://sync.example.test'),
      credentialStore: await _provisionedCredentialStore(),
      client: MockClient((request) async {
        captured = request;
        return http.Response('', 200);
      }),
    );

    await transport.upload(mutation);

    expect(captured, isNotNull);
    expect(captured!.method, 'POST');
    expect(captured!.url.path, '/api/v1/offline-mutations');
    expect(captured!.headers['Idempotency-Key'], mutation.idempotencyKey);
    expect(captured!.headers['X-Biolog-Device-Id'], mutation.deviceId);
    expect(captured!.headers['X-Biolog-Device-Secret'], _syntheticDeviceSecret);
    expect(captured!.headers['Content-Type'], contains('application/json'));
    expect(captured!.body, contains('"operation":"REVOCATION"'));
    expect(captured!.body, contains(mutation.mutationId));
  });

  test('a 200/201 response with no replay header is a fresh acceptance', () async {
    final transport = HttpSyncTransport(
      baseUrl: Uri.parse('https://sync.example.test'),
      credentialStore: await _provisionedCredentialStore(),
      client: MockClient((request) async => http.Response('', 201)),
    );

    final ack = await transport.upload(_synthetic());

    expect(ack.outcome, SyncAckOutcome.accepted);
    expect(ack.replayed, isFalse);
  });

  test('the Idempotency-Replayed response header is surfaced as a replayed acceptance', () async {
    final transport = HttpSyncTransport(
      baseUrl: Uri.parse('https://sync.example.test'),
      credentialStore: await _provisionedCredentialStore(),
      client: MockClient(
        // A real parsed HTTP response's headers map is already
        // lowercase-keyed (HTTP header names are case-insensitive on the
        // wire); constructing an http.Response literally, as here, does not
        // normalize case, so the fixture matches that lowercase reality.
        (request) async => http.Response('', 200, headers: {'idempotency-replayed': 'true'}),
      ),
    );

    final ack = await transport.upload(_synthetic());

    expect(ack.outcome, SyncAckOutcome.accepted);
    expect(ack.replayed, isTrue);
  });

  test('a 409 concurrency_conflict problem response is a conflict carrying the server version',
      () async {
    final transport = HttpSyncTransport(
      baseUrl: Uri.parse('https://sync.example.test'),
      credentialStore: await _provisionedCredentialStore(),
      client: MockClient((request) async {
        return http.Response(
          '{"code":"concurrency_conflict","message":"stale base_version","status":409,'
          '"correlation_id":"evt_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14",'
          '"detail":{"current_version":7}}',
          409,
        );
      }),
    );

    final ack = await transport.upload(_synthetic());

    expect(ack.outcome, SyncAckOutcome.conflict);
    expect(ack.serverVersion, 7);
  });

  test('a 5xx response throws, which SyncEngine treats as a transient failure, not success/conflict',
      () async {
    final transport = HttpSyncTransport(
      baseUrl: Uri.parse('https://sync.example.test'),
      credentialStore: await _provisionedCredentialStore(),
      client: MockClient((request) async => http.Response('', 503)),
    );

    expect(() => transport.upload(_synthetic()), throwsA(isA<HttpSyncTransportException>()));
  });

  test('a 409 that is not a version conflict (e.g. idempotency_key_reuse) throws, not conflict',
      () async {
    final transport = HttpSyncTransport(
      baseUrl: Uri.parse('https://sync.example.test'),
      credentialStore: await _provisionedCredentialStore(),
      client: MockClient((request) async {
        return http.Response(
          '{"code":"idempotency_key_reuse","message":"different payload for the same key",'
          '"status":409,"correlation_id":"evt_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14"}',
          409,
        );
      }),
    );

    expect(() => transport.upload(_synthetic()), throwsA(isA<HttpSyncTransportException>()));
  });

  test('no device secret is provisioned yet: upload fails fast without making an HTTP request',
      () async {
    var requested = false;
    final transport = HttpSyncTransport(
      baseUrl: Uri.parse('https://sync.example.test'),
      credentialStore: DeviceCredentialStore(store: _InMemorySecureStore()),
      client: MockClient((request) async {
        requested = true;
        return http.Response('', 200);
      }),
    );

    await expectLater(
      () => transport.upload(_synthetic()),
      throwsA(isA<DeviceNotProvisionedException>()),
    );
    expect(requested, isFalse);
  });
}
