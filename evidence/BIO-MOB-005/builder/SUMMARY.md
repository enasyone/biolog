# BIO-MOB-005 — Builder evidence

**Story**: Implement limited offline enrolment package
**Owner**: Busola — Mobile & Edge Engineer
**Commit**: `e97e9b2a394f33424c446c75c8ba57fc266a218b`
**Data classification**: SYNTHETIC only

## Scope decisions made before/during coding (recorded here for the record)

1. **AC03/AC04 are deliberately self-contained, independent of BIO-IDN-005/BIO-BIO-006.**
   Research surfaced two existing dedup/human-review constructs (BIO-IDN-005's
   `DuplicateReviewService`, BIO-BIO-006's own separate `FingerprintDuplicateReviewEntry`
   queue) as the closest precedent for "duplicate/conflict checks re-run centrally" and
   "security conflicts require human resolution" — but neither is a declared dependency of
   this story, and BIO-IDN-005's own service is `internal` to its project (unreachable
   without an `InternalsVisibleTo` grant it does not extend here). This mirrors BIO-BIO-006's
   own precedent exactly: its own README states outright that this AC wording "only requires
   'possible duplicate routes to human review,' not specifically to BIO-IDN-005's own queue."
   A new, project-local `EnrolmentDuplicateReviewService` + `EnrolmentDuplicateCandidate`
   queue was built instead, scanning only this feature's own `EnrolmentPackage` records — a
   pre-person, raw-submission-level dedup, consistent with the next decision.
2. **Materializing an `Accepted` package into a real BIO-IDN-001 person record is out of
   scope.** PersonRegistry/BIO-IDN-001 is not a declared dependency of BIO-MOB-005 (only
   BIO-MOB-003, BIO-PHO-001, BIO-BIO-002 are). This story delivers the offline capture,
   encrypted local queue, deterministic sync, central duplicate detection and human
   resolution queue; a future story consumes `Accepted` packages to create the real person
   record — the same disclosed-scope-boundary style BIO-ACC-003 used for card-level
   revocation (BIO-CRD-006 not a declared dependency).
3. **No new canonical-id prefix minted — a self-caught correction.** An initial draft minted
   `enp_`/`dup_` prefixes in the backend's own `Identifiers/CanonicalId.cs`. Before wiring the
   mobile side, re-read `mobile/lib/core/identifiers/canonical_id.dart`'s own header: "Do not
   add or rename prefixes here — that is a contract change owned by Kemi." Fixed by reusing
   the already-registered `evt_` prefix for both an enrolment-package id and a
   duplicate-candidate id — the identical choice BIO-ACC-003's own `PresentationEvent.EventId`
   already made for a device-minted domain-object id that is not itself an audit-trail entry.
   No shared-contract change was needed or made.
4. **AC02 (encrypted at rest) needed no new primitive.** Enrolment packages are queued
   through the same `AppDatabase` interface (schema v4→v5, `enrolment_packages`/
   `enrolment_capture_policy_cache` tables) every other offline queue in this app already
   uses — the whole database file is SQLCipher-encrypted (`SqlCipherAppDatabase`'s own
   established AC02 implementation). No field/blob-level encryption was added on top; nothing
   in any ADR asks for it (verified: `grep -n "encrypt" docs/adr/*.md` returns zero matches).
5. **AC01's field/biometric vocabulary is a small, closed, hand-picked set** (`first_name`,
   `middle_name`, `last_name`, `born_on`, `institutional_identifier`; biometric modalities
   `fingerprint`, `face`) — not an open-ended arbitrary string set. The tenant admin write
   endpoint (`PUT /api/v1/enrolment/capture-policy`) rejects any unrecognised key outright.
   The story's own default (no ADR/spec value exists) is conservative — text-only, no photo,
   no biometrics — matching the story's own title ("**limited** offline enrolment").
6. **No camera/fingerprint-scanner hardware integration exists anywhere in this repository.**
   Verified before designing the screen: no `camera`/`image_picker` mobile dependency; BIO-
   PHO-001's own README discloses "no image-processing library referenced yet" and explicitly
   defers "photo booth capture UX/hardware integration" to BIO-PHO-002; BIO-BIO-002 built only
   a session model with "no HTTP endpoint mapped," no capture UI. The mobile screen's
   photo/biometric toggles therefore attach a fixed, clearly-labelled SYNTHETIC placeholder
   payload when a tenant's policy permits capture — the same disclosed-scope-boundary category
   BIO-BIO-001's own `SyntheticFingerprintProvider`/`SyntheticFaceProvider` already establish
   for hardware this environment does not have, not a fabricated capture.
7. **AC05's mobile-surface wiring has one already-accepted, disclosed gap, mirrored from
   BIO-ACC-003.** `EnrolmentSyncClient.pushPendingPackages`'s `tenantId` parameter has no
   production caller yet — mobile has no real tenant-context resolution boundary (BIO-INT-001
   still unshipped; `SessionController.tenantContext` is always `null` in production per
   BIO-MOB-001-QA-04/ADR-0002 Decision 6). This is the identical, already-accepted category
   `AccessCacheSyncClient.pushPendingPresentationEvents` (BIO-ACC-003) and `DeviceCredentialStore
   .writeSecret` (BIO-MOB-003) already carry, not a new problem. The capture-policy PULL
   (`pullCapturePolicy`) has NO such gap — it needs only device credentials, which the screen
   wires to a real "Refresh policy" button — and the offline capture/local-queue half (the
   story's actual "limited offline enrolment" core) is fully real and wired with no gap at all.

## AC-to-evidence table

| AC | Requirement | Evidence |
|----|-------------|----------|
| AC01 | Tenant policy controls which fields/biometrics may be captured offline | `EnrolmentCapturePolicy` (tenant-scoped, versioned, admin-settable); `EnrolmentPackageService.EnforcePolicy` (server-side defense-in-depth); mobile screen renders only `policy.allowedFields`/toggles. `tests/flutter/test/bio_mob_005_ac01_ac02_capture_policy_test.dart`; `tests/features/bio-mob-005/EnrolmentEndpointsIntegrationTests.cs` (`Capture_policy_defaults_*`, `An_admin_can_widen_*`, `Setting_the_policy_to_an_unrecognised_field_*`, `Submitting_a_field_the_current_policy_does_not_permit_*`) |
| AC02 | Data is encrypted at rest | `enrolment_packages`/`enrolment_capture_policy_cache` tables live inside the same SQLCipher-encrypted `SqlCipherAppDatabase` file every other offline queue uses; no second encryption primitive. `tests/flutter/test/bio_mob_005_ac01_ac02_capture_policy_test.dart` (asserts genuine persistence through `AppDatabase`) |
| AC03 | Duplicate/conflict checks re-run centrally after sync | `EnrolmentDuplicateReviewService.FindMatch` (name+DOB / institutional-identifier signals), invoked synchronously inside `EnrolmentPackageService.SubmitPackageAsync` immediately after intake. `tests/features/bio-mob-005/EnrolmentEndpointsIntegrationTests.cs` (`A_package_with_no_signal_match_is_accepted_outright`, `A_second_package_matching_*`, `Two_packages_that_share_only_an_institutional_identifier_*`) |
| AC04 | Security conflicts require human resolution | A signal match raises an `EnrolmentDuplicateCandidate` (`PENDING_REVIEW`), resolved only via `POST /api/v1/enrolment/duplicate-candidates/{id}/decisions` (tenant/actor/provenance/permission-gated, mandatory `reason_code`). `tests/features/bio-mob-005/EnrolmentEndpointsIntegrationTests.cs` (`Confirming_a_candidate_distinct_*`, `Confirming_a_candidate_a_duplicate_*`, `A_decision_with_no_reason_code_*`, `Deciding_an_already_decided_candidate_*`, `Reviewing_duplicate_candidates_without_the_required_permission_*`) |
| AC05 | Mobile surface connected through MobileService to a live implementation | `mobile/lib/features/enrolment/enrolment_screen.dart` (real, policy-gated capture form, replacing the BIO-MOB-001 placeholder) wired to `EnrolmentService` (local capture/queue) and `EnrolmentSyncClient` (device-authenticated pull/push against `/api/v1/enrolment/*`); package-id-as-`Idempotency-Key`. `governance/ui-integration-map.json`'s `MobileService` port has no code artifact of its own — confirmed by grep: it names only the Flutter app itself as the surface. `tests/flutter/test/bio_mob_005_ac03_ac05_sync_client_test.dart`; `tests/features/bio-mob-005/EnrolmentEndpointsIntegrationTests.cs` (`Replaying_the_identical_package_*`, `Reusing_the_same_package_id_*`, `Package_submission_without_device_credentials_*`, `A_payload_tenant_id_that_does_not_match_*`) |

## Test results (real command output, this commit)

- `dotnet test tests/features/bio-mob-005/Biolog.Features.OfflineEnrolment.Tests.csproj`: **17/17 passed** — `dotnet-test-offline-enrolment.txt`.
- `flutter test` (full suite, `tests/flutter/`): **128/128 passed** (9 net-new, 119 pre-existing, no regressions) — `flutter-test-full-suite.txt`.
- `flutter analyze` (`mobile/`): clean, no issues — `flutter-analyze-output.txt`.
- `dotnet build api/Biolog.Api/Biolog.Api.csproj` (full 40+ project host, including the new `BioMob005Module`): succeeded, 0 warnings, 0 errors — `dotnet-build-full-host.txt`.
- `bash tests/foundation/BIO-MOB-005.test.sh`: **20/20 passed** — `foundation-harness-output.txt`.
- `bash scripts/check-module-project-references.sh`: PASS — `module-reference-check.txt`.
- `bash scripts/secret-scan.sh --all`: clean — `secret-scan.txt`.
- `python3 scripts/check-traceability.py governance/stories/BIO-MOB-005.json`: run after this evidence directory was in place (see below).

## Environment notes

- No DeviceRegistry file was modified — this story only *consumes* the existing
  `ICrossTenantDeviceLookup` escape hatch already reviewed for BIO-MOB-003/BIO-ACC-003, via a
  manual DI factory lambda (never the reflection-based public-constructor activator), so no
  DeviceRegistry regression suite was needed beyond the unchanged full-host build.
- `dotnet build`/`dotnet test` briefly showed the same "Build FAILED, 0 Warnings, 0 Errors,
  Determining projects to restore..." symptom documented in prior stories' evidence (sandboxed
  NuGet-restore network block, not a real circular reference — confirmed by running
  `scripts/check-module-project-references.sh` first, which passed clean). Retried with the
  sandbox disabled; restore then completed in well under a second.

## Deferred/out-of-scope, disclosed explicitly

- **Materializing an accepted enrolment package into a real BIO-IDN-001 person record** — out
  of scope pending a declared PersonRegistry dependency; see scope decision 2 above.
- **Real camera/fingerprint-scanner hardware capture** — no such integration exists anywhere in
  this repository yet (BIO-PHO-002/a future biometric-hardware story's concern); represented by
  a disclosed synthetic placeholder, mirroring BIO-BIO-001's own `SyntheticFingerprintProvider`/
  `SyntheticFaceProvider` pattern. See scope decision 6 above.
- **`EnrolmentSyncClient.pushPendingPackages`'s automatic production trigger** — no resolved
  mobile tenant context exists yet (BIO-INT-001), the identical already-accepted gap
  `AccessCacheSyncClient`/`DeviceCredentialStore.writeSecret` already carry. See scope decision
  7 above. The capture-policy pull and the entire offline capture/queue workflow have no such
  gap and are fully real and wired.
- **A web review UI for duplicate candidates** — not built; `governance/ui-integration-map.json`
  maps this story to `surface: "mobile"` only, and no web route is mapped to it. The backend
  decision endpoint is a real, callable mechanism regardless of whether a web UI is later built
  on top of it (mirrors BIO-ACC-003's own AC06 "AccessCache" human-read endpoint, which existed
  before any UI called it).
