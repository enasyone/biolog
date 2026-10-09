// BIO-MOB-003-QA-01 (Chioma, acceptance round 2): the central intake
// endpoint requires device-header authentication
// (`X-Biolog-Device-Id`/`X-Biolog-Device-Secret`, api/features/bio-mob-003/
// Api/DeviceAuthenticator.cs), but nothing on the mobile side had anywhere
// to keep a device secret at all — `HttpSyncTransport` sent neither header,
// so every production reconnect received a generic 401 and no queued
// mutation could ever reach the server.
//
// This store is the real, functional storage/retrieval half of that gap,
// mirroring `PassphraseVault`'s own shape over the same
// `SecureKeyValueStore` (AC02-grade, Keystore-backed at rest).
//
// **[writeSecret] having no production caller is a disclosed, owner-approved
// scope boundary, not an unremediated defect (BIO-MOB-003-QA-01, Chioma's
// acceptance round 3; architecture call: Kemi, 2026-09-23, hive thread
// `conv-9e1ae1`).** Self-service device enrolment/pairing was considered and
// deliberately ruled OUT of this story's scope: it is a genuine device-
// lifecycle capability any future device-authenticated story needs
// identically (not something specific to offline sync), and it carries its
// own authorization question — who may issue an enrolment credential — that
// lands on ADR-0006's actor-identity boundary, not on sync semantics. No
// reusable mechanism exists today either way: `DeviceService.RegisterAsync`
// (BIO-DEV-001) is admin/operator-authenticated and unreachable until
// BIO-FND-008 ships real tenant resolution, and BIO-PHO-002's
// `DeviceCredentialProvisioning` is operator/config-driven per device — the
// right shape for a small, admin-administered kiosk fleet, the wrong shape
// for a mobile app installed by end users. This story's scope is AC01-AC05
// GIVEN an already-provisioned device secret; sync correctness is
// demonstrated by provisioning [writeSecret] directly, out of band, for
// tests — never by a shortcut that regresses security (e.g. one static
// secret baked into the build and shared fleet-wide, which has no
// per-device revocation and would itself be a defect, not a fix — this
// implementation deliberately does not do that; see `HttpSyncTransport`'s
// own header for why `defaultSyncBaseUrl`'s `--dart-define` pattern does
// not transfer to a per-device secret). A dedicated device-enrolment/pairing
// story is recommended to cover the real provisioning flow.
library;

import '../security/secure_key_value_store.dart';

class DeviceCredentialStore {
  DeviceCredentialStore({
    required SecureKeyValueStore store,
    String storageKey = 'biolog.mobile.device_secret_v1',
  })  : _store = store,
        _storageKey = storageKey;

  final SecureKeyValueStore _store;
  final String _storageKey;

  /// `null` means this device has never been provisioned with a secret.
  Future<String?> readSecret() => _store.read(_storageKey);

  Future<void> writeSecret(String secret) => _store.write(_storageKey, secret);
}
