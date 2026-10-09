// BIO-MOB-005-AC01/AC02/AC03: the actual offline enrolment capture. Fully
// local and synchronous-in-spirit — no network call is made or awaited
// anywhere in `capture` — the "limited offline enrolment" the user story
// asks for. AC02 (encrypted at rest) is satisfied by where the resulting
// [EnrolmentPackage] is stored (`AppDatabase.enqueueEnrolmentPackage`,
// backed by the same SQLCipher-encrypted database every other offline queue
// in this app already uses) — this class adds no second, bespoke
// encryption primitive of its own. AC03's actual duplicate/conflict check
// is entirely server-side (`EnrolmentDuplicateReviewService`,
// `api/features/bio-mob-005/`) — this device has no visibility into any
// other device's submissions, so it structurally cannot run that check
// itself; this class only ever builds and locally queues the package.
library;

import '../db/app_database.dart';
import '../identifiers/canonical_id.dart';
import '../identifiers/clock.dart';
import 'enrolment_capture_policy.dart';
import 'enrolment_package.dart';

/// Thrown when a caller attempts to capture a field, photo or biometric
/// modality the resolved [EnrolmentCapturePolicy] does not permit — the
/// same "validated at the request boundary" defense-in-depth the server's
/// own `EnrolmentPackageService.EnforcePolicy` independently re-checks
/// (mirrors `Person.cs`'s own two-boundary discipline).
class EnrolmentPolicyViolationException implements Exception {
  EnrolmentPolicyViolationException(this.message);

  final String message;

  @override
  String toString() => 'EnrolmentPolicyViolationException: $message';
}

class EnrolmentService {
  EnrolmentService(this._db, {DateTime Function()? now}) : _now = now ?? DateTime.now;

  final AppDatabase _db;
  final DateTime Function() _now;

  /// The most recently known capture policy (AC01) — this device's own
  /// last successful pull, or [EnrolmentCapturePolicy.textOnlyDefault] if it
  /// has never fetched one. Always resolvable fully offline.
  Future<EnrolmentCapturePolicy> currentCapturePolicy() async =>
      (await _db.loadCapturePolicy()) ?? EnrolmentCapturePolicy.textOnlyDefault;

  /// Builds and durably queues an [EnrolmentPackage] from [fieldValues]/
  /// [photoContentBase64]/[biometricPayloadsBase64] (AC02: encrypted at
  /// rest by storage location; AC03's actual scan happens centrally once
  /// this reaches the server). Rejects any field/photo/biometric the
  /// resolved policy does not permit — the enrolment screen's own field
  /// visibility already prevents this in the ordinary case, but this is the
  /// authoritative, policy-checked write path regardless of caller.
  Future<EnrolmentPackage> capture({
    required Map<String, String> fieldValues,
    String? photoContentBase64,
    Map<String, String> biometricPayloadsBase64 = const {},
  }) async {
    final policy = await currentCapturePolicy();

    for (final field in fieldValues.keys) {
      if (!policy.allowedFields.contains(field)) {
        throw EnrolmentPolicyViolationException("'$field' is not permitted by the tenant's offline capture policy");
      }
    }
    if (photoContentBase64 != null && !policy.allowPhotoCapture) {
      throw EnrolmentPolicyViolationException("photo capture is not permitted by the tenant's offline capture policy");
    }
    for (final modality in biometricPayloadsBase64.keys) {
      if (!policy.allowedBiometricModalities.contains(modality)) {
        throw EnrolmentPolicyViolationException("'$modality' biometric capture is not permitted by the tenant's offline capture policy");
      }
    }

    final package = EnrolmentPackage(
      packageId: newCanonicalId(CanonicalIdType.event),
      fieldValues: fieldValues,
      photoCaptured: photoContentBase64 != null,
      photoContentBase64: photoContentBase64,
      biometricModalitiesCaptured: biometricPayloadsBase64.keys.toList(),
      biometricPayloadsBase64: biometricPayloadsBase64,
      submittedAt: toInstant(_now().toUtc()),
    );

    // AC02/AC03: durably queued locally regardless of connectivity — this is
    // the record a later `EnrolmentSyncClient.pushPendingPackages` call
    // uploads (AC05), not something this method itself transmits.
    await _db.enqueueEnrolmentPackage(package);
    return package;
  }
}
