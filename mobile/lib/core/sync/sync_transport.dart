// BIO-MOB-003: the boundary between the local sync engine and "the server".
// Deliberately an interface, mirroring `AppDatabase` — `SyncEngine`'s own
// state-machine/idempotency/priority-ordering logic (AC01-05) is the
// substance of this story and is fully testable against a fake transport
// without a real network dependency.
library;

import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import '../offline/offline_mutation.dart';
import '../security/secure_storage_key_value_store.dart';
import 'device_credential_store.dart';
import 'sync_ack.dart';

/// The offline-intake server's base URL. No deployment configuration
/// mechanism exists anywhere in this app yet (BIO-MOB-003 is the first
/// story to need one) — overridable via `--dart-define=BIOLOG_SYNC_BASE_URL=
/// ...` at build time; the compiled-in default is a placeholder that
/// resolves to nothing real until an actual deployment target is decided.
/// A future story that adds real environment/build-flavor configuration
/// replaces this with whatever mechanism it establishes; `SyncEngine`/
/// `HttpSyncTransport` need no change when it does — only this default.
final Uri defaultSyncBaseUrl = Uri.parse(
  const String.fromEnvironment(
    'BIOLOG_SYNC_BASE_URL',
    defaultValue: 'https://sync.biolog.invalid',
  ),
);

abstract class SyncTransport {
  /// Uploads [mutation]'s delta ([OfflineMutation.toSyncDeltaJson]) and
  /// returns the server's acknowledgement. Throwing signals a transient
  /// failure (network error, 5xx, timeout) — `SyncEngine` treats any thrown
  /// exception as a failed upload attempt (`AppDatabase.recordUploadFailure`,
  /// BIO-MOB-002-AC03's bounded retry), not a conflict and not success.
  Future<SyncAck> upload(OfflineMutation mutation);
}

/// BIO-MOB-003-FEMI-01 (Femi, round 1): a transport that unconditionally
/// throws is not a working sync path, regardless of how well the engine
/// around it is designed — a real HTTP write is not a security-trust
/// boundary the way `SqlCipherAppDatabase.saveTenantContext` is (there is no
/// forgeable-claim risk in *attempting* a networked write; ADR-0004 already
/// fully specifies the idempotency/error conventions any such write must
/// follow), so "no dependency exists yet to build against" does not apply
/// here the way it did for BIO-MOB-001-QA-04's "hook, not resolver". This is
/// a real implementation.
///
/// **The exact route below was originally this story's own reasonable
/// choice**, not a value read from an approved contract — flagged
/// explicitly (BIO-MOB-003 evidence, "Decisions and assumptions"), the same
/// way BIO-MOB-002 flagged its own retry-policy constants. It follows every
/// convention that *is* documented: path versioning (`/api/v1/...`,
/// ADR-0004 Decision 1.1), the `Idempotency-Key`/`Idempotency-Replayed`
/// headers (ADR-0004, `contracts/js/api.mjs`), and the problem-response
/// envelope for errors (`contracts/schemas/problem-response.schema.json`,
/// ADR-0004 Decision 2) including `409 concurrency_conflict` (ADR-0001
/// §3.2/§3.5). A real central endpoint now exists at this exact route
/// (`api/features/bio-mob-003/`, Kemi's ruling on Chioma's acceptance
/// round-1 finding) and requires device-header authentication — see
/// [DeviceCredentialStore] and `DeviceNotProvisionedException` below
/// (BIO-MOB-003-QA-01). Until a device is provisioned with a secret, every
/// call fails fast without attempting the network, which `SyncEngine`
/// already treats as an ordinary transient failure
/// (`AppDatabase.recordUploadFailure`) — correct, expected offline-resilience
/// behaviour, not a defect.
class HttpSyncTransport implements SyncTransport {
  // BIO-MOB-003-FEMI-03 (Femi, round 2): an `http.Client` call has no
  // implicit deadline — a server that accepts the connection but never
  // responds (a genuinely hung request, not a fast connection-refused)
  // previously left `await _client.post(...)` pending forever. Because
  // `SyncEngine.runSyncPass` only clears its single-flight `_inFlight` guard
  // in a `finally` block that runs after `upload` returns or throws, a
  // single hung request permanently blocked every future reconnect-
  // triggered sync attempt, not just that one mutation's retry — a far
  // worse failure mode than an ordinary transient error, which the bounded
  // retry policy already handles correctly. [requestTimeout] gives every
  // call a hard deadline; exceeding it throws `TimeoutException`, which
  // `SyncEngine` already treats like any other transport failure
  // (`AppDatabase.recordUploadFailure`), naturally recovering through the
  // existing bounded-retry mechanism with no change needed there. No ADR or
  // contract specifies a value — 30 seconds is this story's own reasonable
  // choice (a common mobile-client default), the same kind of flagged
  // judgment call as the endpoint route and BIO-MOB-002's retry-policy
  // constants.
  // BIO-MOB-003-QA-01 (Chioma, acceptance round 2): the central endpoint
  // requires device-header authentication; this transport previously sent
  // neither required header, so every production call received a generic
  // 401 regardless of how correct the rest of the request was.
  // [credentialStore] defaults to the real Keystore-backed store, the same
  // "default-construct the real thing, injectable for tests" pattern
  // `SqlCipherAppDatabase`'s own `passphraseVault` param already uses.
  HttpSyncTransport({
    required Uri baseUrl,
    http.Client? client,
    Duration requestTimeout = const Duration(seconds: 30),
    DeviceCredentialStore? credentialStore,
  })  : _baseUrl = baseUrl,
        _client = client ?? http.Client(),
        _requestTimeout = requestTimeout,
        _credentialStore = credentialStore ??
            DeviceCredentialStore(store: SecureStorageKeyValueStore());

  final Uri _baseUrl;
  final http.Client _client;
  final Duration _requestTimeout;
  final DeviceCredentialStore _credentialStore;

  static const String _idempotencyHeader = 'Idempotency-Key';
  static const String _idempotencyReplayedHeader = 'Idempotency-Replayed';
  static const String _deviceIdHeader = 'X-Biolog-Device-Id';
  static const String _deviceSecretHeader = 'X-Biolog-Device-Secret';

  @override
  Future<SyncAck> upload(OfflineMutation mutation) async {
    // Fail fast, before ever attempting the network call: an unprovisioned
    // device already knows the server will reject it (device credentials
    // are mandatory, DeviceAuthenticator.cs), so there is no point spending
    // battery/connectivity on a request that cannot succeed. `SyncEngine`
    // already treats any thrown exception from `upload` as an ordinary
    // transient failure (bounded retry/backoff) — correct here too: "not
    // provisioned yet" is exactly as retryable-later as "no real server
    // exists yet" was in round 1.
    final secret = await _credentialStore.readSecret();
    if (secret == null) {
      throw DeviceNotProvisionedException();
    }

    final response = await _client
        .post(
          _baseUrl.resolve('/api/v1/offline-mutations'),
          headers: {
            'Content-Type': 'application/json',
            _idempotencyHeader: mutation.idempotencyKey,
            _deviceIdHeader: mutation.deviceId,
            _deviceSecretHeader: secret,
          },
          body: jsonEncode(mutation.toSyncDeltaJson()),
        )
        .timeout(_requestTimeout);

    if (response.statusCode == 200 || response.statusCode == 201) {
      final replayed = response.headers[_idempotencyReplayedHeader.toLowerCase()] == 'true';
      return SyncAck.accepted(replayed: replayed);
    }

    if (response.statusCode == 409) {
      final body = _decodeProblem(response.body);
      if (body?['code'] == 'concurrency_conflict') {
        final detail = body?['detail'] as Map<String, Object?>?;
        final serverVersion = detail?['current_version'];
        return SyncAck.conflict(serverVersion is int ? serverVersion : mutation.baseVersion);
      }
      // A 409 that is not a version conflict (e.g. idempotency_key_reuse,
      // ADR-0001 §3.4 — a genuine client defect, not a retryable transient
      // failure or a conflict for the human queue) is surfaced as a thrown
      // error like any other unexpected status, not silently absorbed into
      // either outcome.
    }

    throw HttpSyncTransportException(
      statusCode: response.statusCode,
      problem: _decodeProblem(response.body),
    );
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

/// Thrown by [HttpSyncTransport.upload] when [DeviceCredentialStore] has no
/// stored secret yet (BIO-MOB-003-QA-01) — this device has not been
/// provisioned, so no request was attempted at all. `SyncEngine` catches
/// this exactly like any other transport failure. This is expected on every
/// real install today: real device-secret provisioning is a disclosed,
/// owner-approved scope boundary of THIS story, not a defect — see
/// [DeviceCredentialStore]'s own header for the full ruling.
class DeviceNotProvisionedException implements Exception {
  @override
  String toString() => 'DeviceNotProvisionedException: no device secret is provisioned yet';
}

/// Thrown by [HttpSyncTransport] for any response that is neither a success
/// nor a recognised version conflict — `SyncEngine` catches this exactly
/// like any other transport failure (network error, timeout).
class HttpSyncTransportException implements Exception {
  HttpSyncTransportException({required this.statusCode, this.problem});

  final int statusCode;
  final Map<String, Object?>? problem;

  @override
  String toString() =>
      'HttpSyncTransportException(statusCode: $statusCode, problem: $problem)';
}
