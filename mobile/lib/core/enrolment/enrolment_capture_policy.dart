// BIO-MOB-005-AC01: "Tenant policy controls which enrolment fields/biometrics
// may be captured offline." Wire shape matching
// `GET /api/v1/enrolment/capture-policy`
// (`api/features/bio-mob-005/Api/EnrolmentDtos.cs`'s `CapturePolicyResponse`).
library;

/// This feature's own closed field vocabulary - mirrors
// `Domain/EnrolmentCapturePolicy.cs`'s `KnownFields` exactly.
const String fieldFirstName = 'first_name';
const String fieldMiddleName = 'middle_name';
const String fieldLastName = 'last_name';
const String fieldBornOn = 'born_on';
const String fieldInstitutionalIdentifier = 'institutional_identifier';

const List<String> allKnownFields = [
  fieldFirstName,
  fieldMiddleName,
  fieldLastName,
  fieldBornOn,
  fieldInstitutionalIdentifier,
];

const String modalityFingerprint = 'fingerprint';
const String modalityFace = 'face';

/// A tenant's offline capture policy, as pulled from the server (or the
/// story's own conservative default - see [EnrolmentCapturePolicy.textOnlyDefault]
/// - if never fetched, e.g. a device's very first launch before any
/// connectivity). The mobile enrolment screen shows/enables only the fields
/// and biometric toggles this policy permits; the server independently
/// re-enforces the same policy on submission (defense-in-depth, mirroring
/// `Person.cs`'s own "validated at the request boundary AND the repository
/// boundary" discipline).
class EnrolmentCapturePolicy {
  const EnrolmentCapturePolicy({
    required this.allowedFields,
    required this.allowPhotoCapture,
    required this.allowedBiometricModalities,
    required this.version,
    required this.updatedAt,
  });

  final Set<String> allowedFields;
  final bool allowPhotoCapture;
  final Set<String> allowedBiometricModalities;
  final int version;
  final String updatedAt;

  /// This story's own conservative, disclosed default (no ADR/spec value
  /// exists) - text-only demographic fields, no photo, no biometrics, until
  /// a tenant admin explicitly opts in server-side. Used only when this
  /// device has never successfully fetched a real policy - mirrors the
  /// server's own `EnrolmentCapturePolicy.Default` exactly, so an offline
  /// device's very first capture session behaves identically to what the
  /// server would have told it.
  static const EnrolmentCapturePolicy textOnlyDefault = EnrolmentCapturePolicy(
    allowedFields: {fieldFirstName, fieldLastName, fieldBornOn},
    allowPhotoCapture: false,
    allowedBiometricModalities: {},
    version: 0,
    updatedAt: '',
  );

  factory EnrolmentCapturePolicy.fromJson(Map<String, Object?> json) => EnrolmentCapturePolicy(
        allowedFields: {...(json['allowed_fields'] as List<Object?>? ?? const []).cast<String>()},
        allowPhotoCapture: json['allow_photo_capture'] as bool? ?? false,
        allowedBiometricModalities: {
          ...(json['allowed_biometric_modalities'] as List<Object?>? ?? const []).cast<String>(),
        },
        version: json['version'] as int? ?? 0,
        updatedAt: json['updated_at'] as String? ?? '',
      );

  Map<String, Object?> toJson() => {
        'allowed_fields': allowedFields.toList(),
        'allow_photo_capture': allowPhotoCapture,
        'allowed_biometric_modalities': allowedBiometricModalities.toList(),
        'version': version,
        'updated_at': updatedAt,
      };
}
