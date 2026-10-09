# BIO-BIO-009 round-two review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed builder `321624c` and evidence `69b3c2b` against BIO-BIO-009 AC01–AC05.

The round-two changes correctly replace caller-asserted permission headers with actor-provenance verification plus the real permission gateway. Deletion now uses the deleted record's resolved PERSON identity, and the Kemi-approved sibling-store delete/remove methods replace the prior stub. Those round-one defects are closed.

## BIO-BIO-009-F01 — AC03 policy does not govern actual raw-image retention (High)

AC03 requires raw-image retention to default off unless authorised. `IRawImageRetentionPolicy` is only read and written through BIO-BIO-009's policy API. Neither BIO-BIO-002's capture path nor an orchestration adapter consults it.

`FingerprintEnrolmentService.StartSessionAsync` still accepts an independent caller-controlled `retentionAuthorised` boolean. `CaptureFingerAsync` persists `sample.Payload` whenever that session flag is true. Therefore a tenant absent from, or explicitly disabled in, BIO-BIO-009's policy can still retain raw biometric images by passing `true` at session creation. The README explicitly confirms this disconnected authority.

Reproduction by inspection:

1. Leave `InMemoryRawImageRetentionPolicy` unset, so the tenant policy returns false.
2. Call `FingerprintEnrolmentService.StartSessionAsync(..., retentionAuthorised: true, ...)`.
3. Capture a finger.
4. `FingerRecord.RetainedRawImage` contains the sample payload despite no BIO-BIO-009 authorization.

This is the exact prohibited outcome, not a future optional integration.

## BIO-BIO-009-F02 — AC04 permits stale biometric caches after deletion (High)

AC04 says deletion propagates to permitted caches/providers. `BiometricDeletionService` deletes the source record first, then treats gallery removal as best-effort. When `RemoveFromFingerprintGalleryAsync` or `RemoveFromFaceGalleryAsync` returns false, the service still reports `DeletionOutcome.Deleted`; the existing test explicitly approves `GalleryPropagated == false`.

That leaves a deleted person's biometric template searchable in the 1:N gallery with no retry, pending state, reconciliation worker, or fail-closed search filter. Auditing `galleryPropagated=false` records the privacy failure but does not satisfy propagation.

Reproduction: configure a successful record deletion and a gallery gateway returning false. `DeleteAsync` returns `Deleted` while the gallery entry remains.

## BIO-BIO-009-F03 — AC05 mapped workflow is absent (High)

AC05 requires the approved `/fingerprints`, `/facial-recognition`, and `/enrolment` surface to connect through `BiometricService` to this implementation or an explicitly documented HYBRID state, with a passing mapped end-to-end workflow.

No `web/**` implementation, BiometricService port, retention/deletion action, module-mode registration, or end-to-end workflow was added. The README labels the criterion `n/a (disclosed boundary, not a connected surface)`. Disclosure is not either allowed completion path in AC05.

## Scope note

Retention expiry/legal hold is also absent despite the user-story wording. The three direct acceptance-criterion failures above independently block acceptance, so no separate verdict depends on resolving that scope interpretation.

## Verification

- Focused project build: PASS, 0 errors; one NU1900 advisory-source warning because network access is unavailable.
- Builder reports BIO-BIO-002 33/33, BIO-BIO-003 34/34, BIO-BIO-006 29/29, BIO-BIO-007 25/25, BIO-BIO-009 27/27, integration 201/201, unit 200/200.
- Functional: FAIL (AC03, AC04, AC05).
- Security/privacy: FAIL (raw-image policy bypass and stale gallery data).
- Tenant isolation: PASS for the reviewed authorization and storage-key changes.
- Concurrency/recovery: FAIL (no recovery path for failed gallery propagation).
- Regression: PASS for the focused build; builder regression results noted above.
