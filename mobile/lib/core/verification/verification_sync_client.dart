// BIO-MOB-004-AC01/AC03/AC04: the online half of the verification flow -
// requests a live, policy-evaluated decision from this story's own backend
// (`/api/v1/mobile-verification/*`, BioMob004Module), reads/assigns this
// device's own verification role (AC03), and records a Supervisor-role
// override. Mirrors `AccessCacheSyncClient`/`EnrolmentSyncClient` in shape:
// its own device-credential namespace (a device authorised for offline sync
// or enrolment is not automatically authorised to request a verification
// decision), same header-authentication/timeout discipline, "throw on
// anything unexpected" contract.
//
// No NFC/QR/camera hardware package exists anywhere in this app (verified:
// `mobile/pubspec.yaml` has no `nfc_manager`/`mobile_scanner`/`camera`
// dependency) - the same disclosed gap BIO-MOB-005's photo/biometric
// capture toggles already establish. `presentedCardStatus` is therefore
// always a fixed, clearly-labelled synthetic value representing "a
// credential was presented and read successfully" (`credentialClass`
// itself - card/nfc/qr/face - still varies and is not synthetic).
library;

import 'dart:convert';

import 'package:http/http.dart' as http;

import '../db/app_database.dart';
import '../security/secure_storage_key_value_store.dart';
import '../sync/device_credential_store.dart';
import 'verification_outcome.dart';

/// No deployment configuration mechanism exists anywhere in this app yet
/// (the same disclosed gap `HttpSyncTransport.defaultSyncBaseUrl` already
/// carries) - overridable via `--dart-define=BIOLOG_MOBILE_VERIFICATION_BASE_URL=...`
/// at build time.
final Uri defaultVerificationBaseUrl = Uri.parse(
  const String.fromEnvironment(
    'BIOLOG_MOBILE_VERIFICATION_BASE_URL',
    defaultValue: 'https://sync.biolog.invalid',
  ),
);

/// A fixed, clearly-labelled synthetic value - no real card reader exists
/// in this environment (see this file's own header).
const String syntheticPresentedCardStatus = 'ACTIVE';

class VerificationSyncClient {
  VerificationSyncClient({
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
              storageKey: 'biolog.mobile.mobile_verification_device_secret_v1',
            );

  final Uri _baseUrl;
  final http.Client _client;
  final Duration _requestTimeout;
  final DeviceCredentialStore _credentialStore;

  static const String _idempotencyHeader = 'Idempotency-Key';
  static const String _deviceIdHeader = 'X-Biolog-Device-Id';
  static const String _deviceSecretHeader = 'X-Biolog-Device-Secret';

  /// AC01/AC04: a live, real-time decision request. [idempotencyKey], when
  /// supplied by the caller, lets a UI-level retry of the exact same tap
  /// (e.g. after a timeout) be answered idempotently rather than computing
  /// a second decision - defaults to a freshly minted id (a fresh officer
  /// action is a fresh idempotency scope).
  Future<VerificationOutcome> requestDecision(
    AppDatabase db, {
    required String personId,
    required String credentialClass,
    required String locationId,
    required String policyKey,
    String? profileKey,
    String? idempotencyKey,
  }) async {
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw VerificationDeviceNotProvisionedException();
    }
    final deviceId = await db.deviceId();

    final response = await _client
        .post(
          _baseUrl.resolve('/api/v1/mobile-verification/decisions'),
          headers: {
            'Content-Type': 'application/json',
            _idempotencyHeader: idempotencyKey ?? _fallbackIdempotencyKey(),
            _deviceIdHeader: deviceId,
            _deviceSecretHeader: secret,
          },
          body: jsonEncode({
            'person_id': personId,
            'profile_key': profileKey,
            'credential_class': credentialClass,
            'location_id': locationId,
            'presented_card_status': syntheticPresentedCardStatus,
            'policy_key': policyKey,
          }),
        )
        .timeout(_requestTimeout);

    if (response.statusCode != 200 && response.statusCode != 201) {
      throw VerificationHttpException(statusCode: response.statusCode, problem: _decodeProblem(response.body));
    }

    return VerificationOutcome.fromJson(jsonDecode(response.body) as Map<String, Object?>);
  }

  /// AC03: lets the UI hide (not merely disable) an action this device's
  /// role does not permit - "no visible primary action may be dead."
  Future<String> readDeviceRole(AppDatabase db) async {
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw VerificationDeviceNotProvisionedException();
    }
    final deviceId = await db.deviceId();

    final response = await _client
        .get(
          _baseUrl.resolve('/api/v1/mobile-verification/device-role'),
          headers: {_deviceIdHeader: deviceId, _deviceSecretHeader: secret},
        )
        .timeout(_requestTimeout);

    if (response.statusCode != 200) {
      throw VerificationHttpException(statusCode: response.statusCode, problem: _decodeProblem(response.body));
    }

    final decoded = jsonDecode(response.body) as Map<String, Object?>;
    return decoded['role'] as String;
  }

  /// AC03: Supervisor-role devices only - the server re-checks this
  /// device's own role independently (defense-in-depth); this client never
  /// assumes the UI's own role gate is sufficient.
  Future<void> overrideDecision(
    AppDatabase db, {
    required String decisionEventId,
    required String personId,
    required String reasonCode,
  }) async {
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw VerificationDeviceNotProvisionedException();
    }
    final deviceId = await db.deviceId();

    final response = await _client
        .post(
          _baseUrl.resolve('/api/v1/mobile-verification/decisions/$decisionEventId/override'),
          headers: {
            'Content-Type': 'application/json',
            _deviceIdHeader: deviceId,
            _deviceSecretHeader: secret,
          },
          body: jsonEncode({'person_id': personId, 'reason_code': reasonCode}),
        )
        .timeout(_requestTimeout);

    if (response.statusCode != 200 && response.statusCode != 201) {
      throw VerificationHttpException(statusCode: response.statusCode, problem: _decodeProblem(response.body));
    }
  }

  /// AC03 (read_history, permitted to both roles): the person's decision
  /// history as this server's own record of it - never the local
  /// device's own log, which the offline cache path does not keep beyond
  /// its own revocation-check event.
  Future<List<VerificationOutcome>> fetchHistory(AppDatabase db, {required String personId}) async {
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw VerificationDeviceNotProvisionedException();
    }
    final deviceId = await db.deviceId();

    final uri = _baseUrl.resolve('/api/v1/mobile-verification/decisions').replace(queryParameters: {'person_id': personId});
    final response = await _client
        .get(uri, headers: {_deviceIdHeader: deviceId, _deviceSecretHeader: secret})
        .timeout(_requestTimeout);

    if (response.statusCode != 200) {
      throw VerificationHttpException(statusCode: response.statusCode, problem: _decodeProblem(response.body));
    }

    final decoded = jsonDecode(response.body) as Map<String, Object?>;
    final items = (decoded['data'] as List<Object?>? ?? const [])
        .map((raw) => VerificationOutcome.fromJson(raw as Map<String, Object?>))
        .toList(growable: false);
    return items;
  }

  String _fallbackIdempotencyKey() => DateTime.now().toUtc().microsecondsSinceEpoch.toString();

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
/// `MobileVerification:DeviceCredentials` namespace yet - mirrors
/// `AccessCacheDeviceNotProvisionedException`/`EnrolmentDeviceNotProvisionedException`:
/// expected on every real install today, a disclosed scope boundary, not a
/// defect.
class VerificationDeviceNotProvisionedException implements Exception {
  @override
  String toString() =>
      'VerificationDeviceNotProvisionedException: no mobile-verification device secret is provisioned yet';
}

class VerificationHttpException implements Exception {
  VerificationHttpException({required this.statusCode, this.problem});

  final int statusCode;
  final Map<String, Object?>? problem;

  @override
  String toString() => 'VerificationHttpException(statusCode: $statusCode, problem: $problem)';
}
