// BIO-ACC-003-AC02/AC05: the network half of the offline deny-list — pulls
// revocation deltas into the local cache (`AppDatabase.cacheRevocations`)
// and uploads locally logged presentation events exactly once
// (`AppDatabase.pendingPresentationEvents`/`markPresentationEventSynced`,
// server-side idempotency-key dedupe on `event_id` per
// `api/features/bio-acc-003/AccessCacheService.cs`). Mirrors
// `HttpSyncTransport` (BIO-MOB-003) in shape — same device-header
// authentication, same request-timeout discipline, same "throw on anything
// unexpected, let the caller decide what a failure means" contract — but
// talks to this story's own dedicated `/api/v1/access-cache/*` routes, not
// the generic offline-mutations queue: a presentation event is a distinct
// concern from an arbitrary domain-aggregate mutation, and reading it back
// out for the web `AccessService` "view access events" action (AC06) needs
// a store this feature's own backend can query, which the generic,
// internal `OfflineMutationIntakeService` (BIO-MOB-003) deliberately does
// not expose.
//
// This client uses its OWN device credential (a distinct secure-storage key
// from `DeviceCredentialStore`'s default) — this feature's backend
// deliberately owns its own device-credential namespace
// (`AccessCache:DeviceCredentials`, distinct from BIO-MOB-003's own
// `OfflineSync:DeviceCredentials`): a device authorised to submit offline
// mutations is not automatically authorised to pull a tenant's revocation
// deny-list or submit presentation events. Provisioning that secret is the
// same disclosed, owner-approved scope boundary `DeviceCredentialStore`'s
// own header already establishes for BIO-MOB-003 (device enrolment/pairing
// is a future story's concern) — this client's own credential is simply a
// second instance of that same, already-accepted gap.
library;

import 'dart:convert';

import 'package:http/http.dart' as http;

import '../db/app_database.dart';
import '../security/secure_storage_key_value_store.dart';
import '../sync/device_credential_store.dart';
import 'presentation_event.dart';
import 'revocation_entry.dart';

/// No deployment configuration mechanism exists anywhere in this app yet
/// (the same disclosed gap `HttpSyncTransport.defaultSyncBaseUrl` already
/// carries) — overridable via `--dart-define=BIOLOG_ACCESS_CACHE_BASE_URL=
/// ...` at build time.
final Uri defaultAccessCacheBaseUrl = Uri.parse(
  const String.fromEnvironment(
    'BIOLOG_ACCESS_CACHE_BASE_URL',
    defaultValue: 'https://sync.biolog.invalid',
  ),
);

class AccessCacheSyncClient {
  AccessCacheSyncClient({
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
              storageKey: 'biolog.mobile.access_cache_device_secret_v1',
            );

  final Uri _baseUrl;
  final http.Client _client;
  final Duration _requestTimeout;
  final DeviceCredentialStore _credentialStore;

  static const String _idempotencyHeader = 'Idempotency-Key';
  static const String _deviceIdHeader = 'X-Biolog-Device-Id';
  static const String _deviceSecretHeader = 'X-Biolog-Device-Secret';

  /// AC01/AC02: pulls every revocation since this device's own last-synced
  /// cursor and merges it into the local cache. Fails the same way
  /// `HttpSyncTransport.upload` does when unprovisioned or on any transport
  /// error — thrown, never silently swallowed, so a caller (a future
  /// connectivity-triggered sync loop, mirroring `SyncEngine.attachTo`) can
  /// decide how to react; the existing cache is left exactly as it was on
  /// failure, never partially cleared.
  Future<void> pullRevocationDelta(AppDatabase db, {DateTime Function()? now}) async {
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw AccessCacheDeviceNotProvisionedException();
    }
    final deviceId = await db.deviceId();
    final since = await db.revocationCacheCursor();

    final uri = _baseUrl
        .resolve('/api/v1/access-cache/revocation-deltas')
        .replace(queryParameters: since == null ? null : {'since': since});

    final response = await _client
        .get(
          uri,
          headers: {_deviceIdHeader: deviceId, _deviceSecretHeader: secret},
        )
        .timeout(_requestTimeout);

    if (response.statusCode != 200) {
      throw AccessCacheHttpException(statusCode: response.statusCode, problem: _decodeProblem(response.body));
    }

    final decoded = jsonDecode(response.body) as Map<String, Object?>;
    final items = (decoded['items'] as List<Object?>? ?? const [])
        .map((raw) => RevocationEntry.fromJson(raw as Map<String, Object?>))
        .toList(growable: false);
    final cursor = decoded['cursor'] as String?;

    await db.cacheRevocations(items, cursor: cursor ?? since, syncedAt: (now ?? DateTime.now)());
  }

  /// AC05: uploads every not-yet-synced presentation event, using
  /// [PresentationEvent.eventId] as the `Idempotency-Key` — a reconnect
  /// storm re-invoking this method concurrently, or retrying after a
  /// partial failure, can never double-count the same decision server-side
  /// (`AccessCacheService.IntakePresentationEventAsync`'s own idempotency
  /// claim/complete). One event's failure does not abort the batch — every
  /// other pending event still gets its own upload attempt this call.
  Future<void> pushPendingPresentationEvents(AppDatabase db, {required String tenantId}) async {
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw AccessCacheDeviceNotProvisionedException();
    }
    final deviceId = await db.deviceId();
    final pending = await db.pendingPresentationEvents();

    for (final event in pending) {
      final response = await _client
          .post(
            _baseUrl.resolve('/api/v1/access-cache/presentation-events'),
            headers: {
              'Content-Type': 'application/json',
              _idempotencyHeader: event.eventId,
              _deviceIdHeader: deviceId,
              _deviceSecretHeader: secret,
            },
            body: jsonEncode(event.toIntakeJson(tenantId: tenantId, deviceId: deviceId)),
          )
          .timeout(_requestTimeout);

      if (response.statusCode == 200 || response.statusCode == 201) {
        await db.markPresentationEventSynced(event.eventId);
        continue;
      }
      // Not thrown: a single event's transport failure must not abandon the
      // rest of this batch's upload attempts (mirrors SyncEngine's own
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
/// `AccessCache:DeviceCredentials` namespace yet — mirrors
/// `DeviceNotProvisionedException` (BIO-MOB-003): expected on every real
/// install today, a disclosed scope boundary, not a defect.
class AccessCacheDeviceNotProvisionedException implements Exception {
  @override
  String toString() =>
      'AccessCacheDeviceNotProvisionedException: no access-cache device secret is provisioned yet';
}

class AccessCacheHttpException implements Exception {
  AccessCacheHttpException({required this.statusCode, this.problem});

  final int statusCode;
  final Map<String, Object?>? problem;

  @override
  String toString() => 'AccessCacheHttpException(statusCode: $statusCode, problem: $problem)';
}
