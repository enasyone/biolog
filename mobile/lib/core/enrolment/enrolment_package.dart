// BIO-MOB-005-AC02/AC03/AC05: an offline-captured enrolment package, queued
// locally (encrypted at rest by the same SQLCipher-encrypted database every
// other offline queue in this app already uses - AC02 is satisfied by
// storage location, not a second, bespoke encryption primitive) and
// uploaded exactly once (AC05) once connectivity returns.
library;

enum EnrolmentPackageStatus {
  submitted,
  pendingReview,
  accepted,
  rejectedDuplicate;

  static EnrolmentPackageStatus fromWire(String value) => switch (value) {
        'SUBMITTED' => EnrolmentPackageStatus.submitted,
        'PENDING_REVIEW' => EnrolmentPackageStatus.pendingReview,
        'ACCEPTED' => EnrolmentPackageStatus.accepted,
        'REJECTED_DUPLICATE' => EnrolmentPackageStatus.rejectedDuplicate,
        _ => throw ArgumentError('unrecognised enrolment package status: $value'),
      };

  String toWire() => switch (this) {
        EnrolmentPackageStatus.submitted => 'SUBMITTED',
        EnrolmentPackageStatus.pendingReview => 'PENDING_REVIEW',
        EnrolmentPackageStatus.accepted => 'ACCEPTED',
        EnrolmentPackageStatus.rejectedDuplicate => 'REJECTED_DUPLICATE',
      };
}

/// A single offline enrolment capture, as this device's own local queue
/// records it. [tenantId]/[deviceId] are deliberately NOT stored per-row
/// (AC01/minimum-necessary, the same convention `PresentationEvent` already
/// establishes) - the sync client attaches them at upload time from this
/// device's own ambient sources.
class EnrolmentPackage {
  const EnrolmentPackage({
    required this.packageId,
    required this.fieldValues,
    required this.photoCaptured,
    this.photoContentBase64,
    required this.biometricModalitiesCaptured,
    required this.biometricPayloadsBase64,
    required this.submittedAt,
    this.status = EnrolmentPackageStatus.submitted,
  });

  final String packageId;
  final Map<String, String> fieldValues;
  final bool photoCaptured;
  final String? photoContentBase64;
  final List<String> biometricModalitiesCaptured;
  final Map<String, String> biometricPayloadsBase64;
  final String submittedAt;

  /// Locally recorded outcome - `submitted` until the next successful sync
  /// updates it from the server's own response (AC03/AC04's outcome is
  /// decided centrally; this device only ever reflects it, never decides it).
  final EnrolmentPackageStatus status;

  EnrolmentPackage copyWith({EnrolmentPackageStatus? status}) => EnrolmentPackage(
        packageId: packageId,
        fieldValues: fieldValues,
        photoCaptured: photoCaptured,
        photoContentBase64: photoContentBase64,
        biometricModalitiesCaptured: biometricModalitiesCaptured,
        biometricPayloadsBase64: biometricPayloadsBase64,
        submittedAt: submittedAt,
        status: status ?? this.status,
      );

  /// The upload envelope `POST /api/v1/enrolment/packages` expects
  /// (`EnrolmentPackageSubmitRequest` in
  /// `api/features/bio-mob-005/Api/EnrolmentDtos.cs`).
  Map<String, Object?> toSubmitJson({required String tenantId, required String deviceId}) => {
        'package_id': packageId,
        'tenant_id': tenantId,
        'device_id': deviceId,
        'field_values': fieldValues,
        'photo_captured': photoCaptured,
        'photo_content_base64': photoContentBase64,
        'biometric_modalities_captured': biometricModalitiesCaptured,
        'biometric_payloads_base64': biometricPayloadsBase64,
        'submitted_at': submittedAt,
      };
}
