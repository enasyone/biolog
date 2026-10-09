// BIO-MOB-005-AC03/AC05: exercises EnrolmentSyncClient against a mocked HTTP
// client (no real network) — proves the request shape (device headers,
// Idempotency-Key = package id) and the response handling (a package's
// server-decided status, from AC03's central duplicate scan, is recorded
// locally; one package's failure does not abort the batch) actually work.
// Mirrors BIO-ACC-003-AC05's own AccessCacheSyncClient test discipline.
import 'dart:convert';

import 'package:biolog_mobile/core/enrolment/enrolment_capture_policy.dart';
import 'package:biolog_mobile/core/enrolment/enrolment_package.dart';
import 'package:biolog_mobile/core/enrolment/enrolment_sync_client.dart';
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

const _syntheticSecret = 'synthetic-enrolment-device-secret';

Future<DeviceCredentialStore> _provisioned() async {
  final store = DeviceCredentialStore(
    store: _InMemorySecureStore(),
    storageKey: 'test.enrolment_device_secret',
  );
  await store.writeSecret(_syntheticSecret);
  return store;
}

void main() {
  test('pullCapturePolicy sends device headers and caches the response', () async {
    final db = FakeAppDatabase();
    await db.open();

    http.Request? captured;
    final client = EnrolmentSyncClient(
      baseUrl: Uri.parse('https://enrolment.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        captured = request;
        return http.Response(
          jsonEncode({
            'tenant_id': 'ten_test_0000000000000000000',
            'allowed_fields': [fieldFirstName, fieldLastName],
            'allow_photo_capture': true,
            'allowed_biometric_modalities': [modalityFingerprint],
            'version': 2,
            'updated_at': '2026-09-27T00:00:00.000Z',
          }),
          200,
        );
      }),
    );

    final policy = await client.pullCapturePolicy(db);

    expect(captured, isNotNull);
    expect(captured!.method, 'GET');
    expect(captured!.url.path, '/api/v1/enrolment/capture-policy');
    expect(captured!.headers['X-Biolog-Device-Id'], await db.deviceId());
    expect(captured!.headers['X-Biolog-Device-Secret'], _syntheticSecret);

    expect(policy.allowedFields, {fieldFirstName, fieldLastName});
    expect(policy.allowPhotoCapture, isTrue);
    expect(await db.loadCapturePolicy(), isNotNull);
    expect((await db.loadCapturePolicy())!.version, 2);
  });

  test('pushPendingPackages uses the package id as the Idempotency-Key and records the server outcome', () async {
    final db = FakeAppDatabase();
    await db.open();
    const package = EnrolmentPackage(
      packageId: 'evt_0000000000000000000000000',
      fieldValues: {fieldFirstName: 'Ada'},
      photoCaptured: false,
      biometricModalitiesCaptured: [],
      biometricPayloadsBase64: {},
      submittedAt: '2026-09-27T00:00:00.000Z',
    );
    await db.enqueueEnrolmentPackage(package);

    http.Request? captured;
    final client = EnrolmentSyncClient(
      baseUrl: Uri.parse('https://enrolment.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        captured = request;
        return http.Response(
          jsonEncode({'package_id': package.packageId, 'status': 'PENDING_REVIEW', 'received_at': '2026-09-27T00:00:01.000Z'}),
          201,
        );
      }),
    );

    await client.pushPendingPackages(db, tenantId: 'ten_test_0000000000000000000');

    expect(captured, isNotNull);
    expect(captured!.method, 'POST');
    expect(captured!.headers['Idempotency-Key'], package.packageId);
    final body = jsonDecode(captured!.body) as Map<String, Object?>;
    expect(body['field_values'], {fieldFirstName: 'Ada'});

    // AC03: this device learns and records the server's own central-scan
    // outcome, not merely "uploaded".
    final all = await db.allEnrolmentPackages();
    expect(all.single.status, EnrolmentPackageStatus.pendingReview);
    expect(await db.pendingEnrolmentPackages(), isEmpty);
  });

  test('one packages transport failure does not abandon the rest of the batch', () async {
    final db = FakeAppDatabase();
    await db.open();
    const failing = EnrolmentPackage(
      packageId: 'evt_1111111111111111111111111',
      fieldValues: {fieldFirstName: 'A'},
      photoCaptured: false,
      biometricModalitiesCaptured: [],
      biometricPayloadsBase64: {},
      submittedAt: '2026-09-27T00:00:00.000Z',
    );
    const succeeding = EnrolmentPackage(
      packageId: 'evt_2222222222222222222222222',
      fieldValues: {fieldFirstName: 'B'},
      photoCaptured: false,
      biometricModalitiesCaptured: [],
      biometricPayloadsBase64: {},
      submittedAt: '2026-09-27T00:00:01.000Z',
    );
    await db.enqueueEnrolmentPackage(failing);
    await db.enqueueEnrolmentPackage(succeeding);

    final client = EnrolmentSyncClient(
      baseUrl: Uri.parse('https://enrolment.example.test'),
      credentialStore: await _provisioned(),
      client: MockClient((request) async {
        final body = jsonDecode(request.body) as Map<String, Object?>;
        if (body['package_id'] == failing.packageId) {
          return http.Response('', 500);
        }
        return http.Response(jsonEncode({'package_id': body['package_id'], 'status': 'ACCEPTED', 'received_at': '2026-09-27T00:00:02.000Z'}), 201);
      }),
    );

    await client.pushPendingPackages(db, tenantId: 'ten_test_0000000000000000000');

    final pending = await db.pendingEnrolmentPackages();
    expect(pending, hasLength(1));
    expect(pending.first.packageId, failing.packageId);
  });

  test('an unprovisioned device fails fast without attempting the network, for both pull and push', () async {
    final db = FakeAppDatabase();
    await db.open();
    var called = false;
    final client = EnrolmentSyncClient(
      baseUrl: Uri.parse('https://enrolment.example.test'),
      credentialStore: DeviceCredentialStore(store: _InMemorySecureStore(), storageKey: 'unprovisioned'),
      client: MockClient((request) async {
        called = true;
        return http.Response('', 200);
      }),
    );

    await expectLater(() => client.pullCapturePolicy(db), throwsA(isA<EnrolmentDeviceNotProvisionedException>()));
    await expectLater(
      () => client.pushPendingPackages(db, tenantId: 'ten_test_0000000000000000000'),
      throwsA(isA<EnrolmentDeviceNotProvisionedException>()),
    );
    expect(called, isFalse);
  });
}
