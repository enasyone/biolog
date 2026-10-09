// BIO-MOB-005-AC01/AC05: the network half of limited offline enrolment —
// pulls the tenant's own offline capture policy
// (`AppDatabase.cacheCapturePolicy`) and uploads locally queued enrolment
// packages exactly once (`AppDatabase.pendingEnrolmentPackages`/
// `markEnrolmentPackageSynced`, server-side idempotency-key dedupe on
// `package_id` per `api/features/bio-mob-005/EnrolmentPackageService.cs`).
// Mirrors `AccessCacheSyncClient` (BIO-ACC-003) in shape — same
// device-header authentication, same request-timeout discipline, same
// "throw on anything unexpected, let the caller decide what a failure
// means" contract — but talks to this story's own dedicated
// `/api/v1/enrolment/*` routes and owns its own device-credential
// namespace (`OfflineEnrolment:DeviceCredentials`, distinct from
// BIO-MOB-003's `OfflineSync:DeviceCredentials` and BIO-ACC-003's
// `AccessCache:DeviceCredentials`): a device authorised to submit offline
// sync mutations or access-cache presentation events is not automatically
// authorised to submit an enrolment package.
//
// `pushPendingPackages`'s `tenantId` parameter has no production caller yet
// — the same disclosed, owner-approved scope boundary
// `AccessCacheSyncClient.pushPendingPresentationEvents` already carries
// (mobile has no real tenant-context resolution boundary yet, BIO-INT-001).
// Provisioning this client's own device secret is the same already-accepted
// gap `DeviceCredentialStore`'s own header establishes for BIO-MOB-003.
library;

import 'dart:convert';

import 'package:http/http.dart' as http;

import '../db/app_database.dart';
import '../security/secure_storage_key_value_store.dart';
import '../sync/device_credential_store.dart';
import 'enrolment_capture_policy.dart';
import 'enrolment_package.dart';

/// No deployment configuration mechanism exists anywhere in this app yet
/// (the same disclosed gap `HttpSyncTransport.defaultSyncBaseUrl` already
/// carries) — overridable via
/// `--dart-define=BIOLOG_ENROLMENT_BASE_URL=...` at build time.
final Uri defaultEnrolmentBaseUrl = Uri.parse(
  const String.fromEnvironment(
    'BIOLOG_ENROLMENT_BASE_URL',
    defaultValue: 'https://sync.biolog.invalid',
  ),
);

class EnrolmentSyncClient {
  EnrolmentSyncClient({
    required Uri baseUrl,
    http.Client? client,
    Duration requestTimeout = const Duration(seconds: 30),
    DeviceCredentialStore? credentialStore,
  })  : _baseUrl = baseUrl,
        _client = client ?? http.Client(),
        _requestTimeout = requestTimeout,
        _credentialStore = credentialStore ??
            DeviceCredentialStore(
              store: SecureStorageKeyValueStore(),
              storageKey: 'biolog.mobile.enrolment_device_secret_v1',
            );

  final Uri _baseUrl;
  final http.Client _client;
  final Duration _requestTimeout;
  final DeviceCredentialStore _credentialStore;

  static const String _idempotencyHeader = 'Idempotency-Key';
  static const String _deviceIdHeader = 'X-Biolog-Device-Id';
  static const String _deviceSecretHeader = 'X-Biolog-Device-Secret';

  /// AC01: pulls this device's tenant's current offline capture policy and
  /// caches it locally, so [EnrolmentService] can keep gating fields/
  /// biometrics correctly even through the next stretch of offline time.
  Future<EnrolmentCapturePolicy> pullCapturePolicy(AppDatabase db, {DateTime Function()? now}) async {
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw EnrolmentDeviceNotProvisionedException();
    }
    final deviceId = await db.deviceId();

    final response = await _client
        .get(
          _baseUrl.resolve('/api/v1/enrolment/capture-policy'),
          headers: {_deviceIdHeader: deviceId, _deviceSecretHeader: secret},
        )
        .timeout(_requestTimeout);

    if (response.statusCode != 200) {
      throw EnrolmentHttpException(statusCode: response.statusCode, problem: _decodeProblem(response.body));
    }

    final policy = EnrolmentCapturePolicy.fromJson(jsonDecode(response.body) as Map<String, Object?>);
    await db.cacheCapturePolicy(policy);
    return policy;
  }

  /// AC05: uploads every not-yet-synced enrolment package, using
  /// [EnrolmentPackage.packageId] as the `Idempotency-Key` — a reconnect
  /// storm re-invoking this method concurrently, or retrying after a
  /// partial failure, can never double-count the same submission
  /// server-side (`EnrolmentPackageService.SubmitPackageAsync`'s own
  /// idempotency claim/complete). One package's failure does not abort the
  /// batch — every other pending package still gets its own upload attempt
  /// this call. On success, this device learns and locally records the
  /// server's own AC03/AC04 outcome (`SUBMITTED`/`PENDING_REVIEW`/
  /// `ACCEPTED`/`REJECTED_DUPLICATE`).
  Future<void> pushPendingPackages(AppDatabase db, {required String tenantId}) async {
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw EnrolmentDeviceNotProvisionedException();
    }
    final deviceId = await db.deviceId();
    final pending = await db.pendingEnrolmentPackages();

    for (final package in pending) {
      final response = await _client
          .post(
            _baseUrl.resolve('/api/v1/enrolment/packages'),
            headers: {
              'Content-Type': 'application/json',
              _idempotencyHeader: package.packageId,
              _deviceIdHeader: deviceId,
              _deviceSecretHeader: secret,
            },
            body: jsonEncode(package.toSubmitJson(tenantId: tenantId, deviceId: deviceId)),
          )
          .timeout(_requestTimeout);

      if (response.statusCode == 200 || response.statusCode == 201) {
        final decoded = jsonDecode(response.body) as Map<String, Object?>;
        final status = EnrolmentPackageStatus.fromWire(decoded['status'] as String);
        await db.markEnrolmentPackageSynced(package.packageId, status);
        continue;
      }
      // Not thrown: a single package's transport failure must not abandon
      // the rest of this batch's upload attempts (mirrors SyncEngine's own
      // per-mutation isolation, BIO-MOB-002-AC03). It stays pending and is
      // retried on the next call.
    }
  }

  Map<String, Object?>? _decodeProblem(String body) {
    if (body.isEmpty) return null;
    try {
      final decoded = jsonDecode(body);
      return decoded is Map<String, Object?> ? decoded : null;
    } on FormatException {
      return null;
    }
  }
}

/// Thrown when this device has no provisioned secret for the
/// `OfflineEnrolment:DeviceCredentials` namespace yet — mirrors
/// `DeviceNotProvisionedException` (BIO-MOB-003): expected on every real
/// install today, a disclosed scope boundary, not a defect.
class EnrolmentDeviceNotProvisionedException implements Exception {
  @override
  String toString() =>
      'EnrolmentDeviceNotProvisionedException: no enrolment device secret is provisioned yet';
}

class EnrolmentHttpException implements Exception {
  EnrolmentHttpException({required this.statusCode, this.problem});

  final int statusCode;
  final Map<String, Object?>? problem;

  @override
  String toString() => 'EnrolmentHttpException(statusCode: $statusCode, problem: $problem)';
}
