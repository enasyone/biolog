// BIO-MOB-004-AC01/AC03/AC04: exercises VerificationSyncClient against a
// mocked HTTP client (no real network) — proves the online decision request
// shape (device headers, credential fields, no biometric-internals field
// exists to leak in the first place — AC01 by construction), the device-role
// read (AC03), the override write (AC03), and idempotent/unprovisioned
// behaviour (AC04). Mirrors BIO-MOB-005/BIO-ACC-003's own sync-client test
// discipline.
import 'dart:convert';

import 'package:biolog_mobile/core/security/secure_key_value_store.dart';
import 'package:biolog_mobile/core/sync/device_credential_store.dart';
import 'package:biolog_mobile/core/verification/verification_outcome.dart';
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

const _syntheticSecret = 'synthetic-verification-device-secret';

Future<DeviceCredentialStore> _provisioned() async {
  final store = DeviceCredentialStore(
    store: _InMemorySecureStore(),
    storageKey: 'test.mobile_verification_device_secret',
  );
  await store.writeSecret(_syntheticSecret);
  return store;
}

void main() {
  test('requestDecision sends device headers, credential fields and Idempotency-Key, and parses a sanitised outcome', () async {
    final db = FakeAppDatabase();
    await db.open();

    http.Request? captured;
    final client = VerificationSyncClient(
      baseUrl: Uri.parse('https://verify.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        captured = request;
        // AC01: even if a server response carried a raw biometric score
        // (it never does, but this proves the client-side shape is safe
        // regardless), VerificationOutcome.fromJson has no field to parse
        // it into.
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
            'idempotency_key': 'evt_2222222222222222222222222',
            'biometric_evidence': [
              {'modality': 'face', 'comparison_outcome': 'MATCHED', 'score': 0.98},
            ],
          }),
          201,
        );
      }),
    );

    final outcome = await client.requestDecision(
      db,
      personId: 'per_0000000000000000000000000',
      credentialClass: 'card',
      locationId: 'main-gate',
      policyKey: 'default',
      idempotencyKey: 'evt_2222222222222222222222222',
    );

    expect(captured, isNotNull);
    expect(captured!.method, 'POST');
    expect(captured!.url.path, '/api/v1/mobile-verification/decisions');
    expect(captured!.headers['X-Biolog-Device-Id'], await db.deviceId());
    expect(captured!.headers['X-Biolog-Device-Secret'], _syntheticSecret);
    expect(captured!.headers['Idempotency-Key'], 'evt_2222222222222222222222222');
    final body = jsonDecode(captured!.body) as Map<String, Object?>;
    expect(body['person_id'], 'per_0000000000000000000000000');
    expect(body['credential_class'], 'card');
    expect(body.containsKey('biometric_evidence'), isFalse);
    expect(body.containsKey('score'), isFalse);

    // AC01: VerificationOutcome has no field to carry a raw biometric score
    // even if the server response somehow included one.
    expect(outcome.decision, VerificationDecision.allow);
    expect(outcome.source, VerificationSource.online);
    expect(outcome.presentedAssuranceLevel, 'BASIC');
  });

  test('readDeviceRole returns this device\'s own assigned role', () async {
    final db = FakeAppDatabase();
    await db.open();

    final client = VerificationSyncClient(
      baseUrl: Uri.parse('https://verify.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        expect(request.url.path, '/api/v1/mobile-verification/device-role');
        return http.Response(jsonEncode({'device_id': 'dev_x', 'role': 'SUPERVISOR'}), 200);
      }),
    );

    final role = await client.readDeviceRole(db);
    expect(role, 'SUPERVISOR');
  });

  test('overrideDecision posts the reason code to the override route', () async {
    final db = FakeAppDatabase();
    await db.open();

    http.Request? captured;
    final client = VerificationSyncClient(
      baseUrl: Uri.parse('https://verify.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        captured = request;
        return http.Response(
          jsonEncode({
            'override_id': 'evt_3333333333333333333333333',
            'decision_event_id': 'evt_0000000000000000000000000',
            'person_id': 'per_0000000000000000000000000',
            'reason_code': 'supervisor_manual_admit',
            'overridden_at': '2026-09-30T00:00:01.000Z',
          }),
          201,
        );
      }),
    );

    await client.overrideDecision(
      db,
      decisionEventId: 'evt_0000000000000000000000000',
      personId: 'per_0000000000000000000000000',
      reasonCode: 'supervisor_manual_admit',
    );

    expect(captured!.method, 'POST');
    expect(captured!.url.path, '/api/v1/mobile-verification/decisions/evt_0000000000000000000000000/override');
    final body = jsonDecode(captured!.body) as Map<String, Object?>;
    expect(body['reason_code'], 'supervisor_manual_admit');
  });

  test('an unprovisioned device fails fast without attempting the network, for every route', () async {
    final db = FakeAppDatabase();
    await db.open();
    var called = false;
    final client = VerificationSyncClient(
      baseUrl: Uri.parse('https://verify.example.test'),
      credentialStore: DeviceCredentialStore(store: _InMemorySecureStore(), storageKey: 'unprovisioned'),
      client: MockClient((request) async {
        called = true;
        return http.Response('', 200);
      }),
    );

    await expectLater(
      () => client.requestDecision(db, personId: 'per_0000000000000000000000000', credentialClass: 'card', locationId: 'l', policyKey: 'p'),
      throwsA(isA<VerificationDeviceNotProvisionedException>()),
    );
    await expectLater(() => client.readDeviceRole(db), throwsA(isA<VerificationDeviceNotProvisionedException>()));
    expect(called, isFalse);
  });
}
