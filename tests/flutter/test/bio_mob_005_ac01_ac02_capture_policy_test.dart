// BIO-MOB-005-AC01/AC02: tenant capture policy gates which fields/
// biometrics an offline enrolment may capture, and the resulting package is
// queued in the same encrypted local store every other offline queue in
// this app already uses (AC02 is satisfied by storage location, not a
// second, bespoke encryption primitive — proven here by asserting the
// package genuinely lands in `AppDatabase`, the same interface
// `SqlCipherAppDatabase`'s real SQLCipher-backed implementation satisfies).
import 'package:biolog_mobile/core/enrolment/enrolment_capture_policy.dart';
import 'package:biolog_mobile/core/enrolment/enrolment_service.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';

void main() {
  test('with no cached policy, capture falls back to the conservative text-only default', () async {
    final db = FakeAppDatabase();
    await db.open();
    final service = EnrolmentService(db);

    final policy = await service.currentCapturePolicy();

    expect(policy.allowedFields, {fieldFirstName, fieldLastName, fieldBornOn});
    expect(policy.allowPhotoCapture, isFalse);
    expect(policy.allowedBiometricModalities, isEmpty);
  });

  test('capture accepts every field the cached policy allows', () async {
    final db = FakeAppDatabase();
    await db.open();
    await db.cacheCapturePolicy(const EnrolmentCapturePolicy(
      allowedFields: {fieldFirstName, fieldLastName, fieldBornOn, fieldInstitutionalIdentifier},
      allowPhotoCapture: true,
      allowedBiometricModalities: {modalityFingerprint},
      version: 3,
      updatedAt: '2026-09-27T00:00:00.000Z',
    ));
    final service = EnrolmentService(db);

    final package = await service.capture(
      fieldValues: {
        fieldFirstName: 'Ada',
        fieldLastName: 'Obi',
        fieldBornOn: '2005-04-12',
        fieldInstitutionalIdentifier: 'STU-0001',
      },
      photoContentBase64: 'synthetic-photo',
      biometricPayloadsBase64: {modalityFingerprint: 'synthetic-fingerprint'},
    );

    expect(package.fieldValues[fieldFirstName], 'Ada');
    expect(package.photoCaptured, isTrue);
    expect(package.biometricModalitiesCaptured, [modalityFingerprint]);

    // AC02: genuinely persisted through AppDatabase, not merely returned.
    final all = await db.allEnrolmentPackages();
    expect(all, hasLength(1));
    expect(all.first.packageId, package.packageId);
  });

  test('a field the policy does not allow is rejected, not silently captured', () async {
    final db = FakeAppDatabase();
    await db.open();
    await db.cacheCapturePolicy(const EnrolmentCapturePolicy(
      allowedFields: {fieldFirstName},
      allowPhotoCapture: false,
      allowedBiometricModalities: {},
      version: 1,
      updatedAt: '2026-09-27T00:00:00.000Z',
    ));
    final service = EnrolmentService(db);

    await expectLater(
      () => service.capture(fieldValues: {fieldLastName: 'Obi'}),
      throwsA(isA<EnrolmentPolicyViolationException>()),
    );
    expect(await db.allEnrolmentPackages(), isEmpty);
  });

  test('a photo is rejected when the policy does not permit photo capture', () async {
    final db = FakeAppDatabase();
    await db.open();
    await db.cacheCapturePolicy(const EnrolmentCapturePolicy(
      allowedFields: {fieldFirstName},
      allowPhotoCapture: false,
      allowedBiometricModalities: {},
      version: 1,
      updatedAt: '2026-09-27T00:00:00.000Z',
    ));
    final service = EnrolmentService(db);

    await expectLater(
      () => service.capture(fieldValues: {fieldFirstName: 'Ada'}, photoContentBase64: 'synthetic'),
      throwsA(isA<EnrolmentPolicyViolationException>()),
    );
  });

  test('a biometric modality is rejected when the policy does not permit it', () async {
    final db = FakeAppDatabase();
    await db.open();
    await db.cacheCapturePolicy(const EnrolmentCapturePolicy(
      allowedFields: {fieldFirstName},
      allowPhotoCapture: false,
      allowedBiometricModalities: {modalityFace},
      version: 1,
      updatedAt: '2026-09-27T00:00:00.000Z',
    ));
    final service = EnrolmentService(db);

    await expectLater(
      () => service.capture(
        fieldValues: {fieldFirstName: 'Ada'},
        biometricPayloadsBase64: {modalityFingerprint: 'synthetic'},
      ),
      throwsA(isA<EnrolmentPolicyViolationException>()),
    );
  });
}
