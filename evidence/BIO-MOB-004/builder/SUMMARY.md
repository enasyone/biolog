# BIO-MOB-004 — Builder evidence

**Story**: Implement mobile identity/card/face verification flows
**Owner**: Busola — Mobile & Edge Engineer
**Commit**: `6bc27084ad9fc9eba6c2d298f1cf1c97eb252bb3`
**Data classification**: SYNTHETIC only

## Scope decisions made before/during coding (recorded here for the record)

1. **AC03 required a genuine architecture decision — escalated to god, ruled on by Kemi
   (Chief Architect) before any code was written.** Research (two research passes) confirmed
   mobile has no human officer/actor identity anywhere in this app — only device-level auth
   (`mobile/lib/core/sync/device_credential_store.dart`'s own disclosed-gap comment). ADR-0006
   never mentions a device principal at all, and `PermissionService.ActorHasPermissionAsync`
   (BIO-IDN-006) is hard-typed to a `per_`-prefixed person id — a device id silently fails it.
   The one prior mobile AC shaped like this (BIO-MOB-005-AC04) was satisfied by keeping the
   permission-gated action entirely off the mobile surface; that precedent does not transfer
   here since BIO-MOB-004's role-gated actions are inherently the officer's own on-device
   actions. Proposed (hive thread `conv-bio-mob-004-ac03`, 2026-09-30) a device-scoped
   `VerificationDeviceRole` (`Officer`/`Supervisor`) attached to the already-authenticated
   device rather than a human. Kemi verified the research independently and approved,
   formalizing it as an extension to ADR-0006's own actor-identity/authorization-boundary scope
   for the device-authenticated case: an authenticated device identity (hashed,
   constant-time-compared secret) is a genuine principal-channel realization, not a bare
   asserted claim like `X-Actor-Id` — gating an authorization decision on it sidesteps
   ADR-0006's own anti-pattern rather than repeating it. Two refinements applied: (a) the
   deviceId→role mapping lives entirely in this story's own owned path
   (`api/features/bio-mob-004/**`), device id as an opaque foreign key — `DeviceRegistry`'s own
   `Device` record is never touched; (b) the type is named `VerificationDeviceRole`, distinct
   from BIO-IDN-006's own human `RoleType`, so the two can never be confused at a call site or
   log line.
2. **AC01/AC04's real seam onto BIO-ACC-002 — confirmed by reading `BioAcc002Module.cs`/
   `AccessDecisionService.cs` directly before designing, not inferred.** `AccessDecisionService`
   is `internal sealed`, Scoped-registered, `InternalsVisibleTo Biolog.Api`. Its own
   `ICurrentTenantProvider` is wired at the container level only to the host/principal channel
   (`ITenantContextAccessor`), which a device-authenticated mobile request never has. Calling
   BIO-ACC-002's own public HTTP endpoint was ruled out — it requires an `X-Actor-Provenance`
   HMAC proof mobile has no legitimate way to hold (baking a shared secret into the mobile
   binary is the exact anti-pattern `device_credential_store.dart`'s own header already rejects
   for a different reason). Instead, `BioMob004Module.AccessDecisionGatewayAdapter` manually
   constructs `AccessDecisionService` per call — the same "manual factory lambda, never the
   reflection-based DI activator" discipline already established for consuming
   `ICrossTenantDeviceLookup` — substituting a fixed tenant provider bound to the resolved
   device's own tenant, while every other dependency (the shared Singleton event
   repository/audit sink/lock registry, and the Scoped person/policy/fingerprint/face gateways)
   is pulled from the SAME request's `IServiceProvider` BIO-ACC-002's own endpoint would have
   used. This never touches `ActorProvenanceGate`/`ActorResolver` or
   `AccessDecisions:ActorProvenanceSecret` at all — the identical pattern BIO-ACC-002 itself
   already uses to call BIO-BIO-004/BIO-BIO-005/BIO-ACC-001 directly.
3. **AC01 is enforced structurally, not only by UI discipline.** `VerificationDecisionResult`
   (this feature's own project-local DTO) has no field at all for BIO-ACC-002's own
   `biometric_evidence[].score`/`comparison_outcome` — the module adapter's translation
   (`ToOurResult`) never reads `AccessDecisionEvent.BiometricEvidence`. There is nothing for the
   mobile UI to accidentally render even if it wanted to.
4. **AC02's "permitted offline behavior" reuses BIO-ACC-003's already-built offline decision
   engine rather than inventing a new one.** `AccessCacheService.checkPresentation(personId)`
   (BIO-ACC-003) is a genuine, already-reviewed offline access decision (local revocation-cache
   check, staleness never flips the outcome, always logs an event). `VerificationService.verify`
   routes to it when `SessionController.connectivityState != online`, and to this story's own
   live decision request when online — the caller's own connectivity state decides, never a
   try/catch fallback. No new mobile-side offline queue or SQLite schema change was needed for
   this story (`AppDatabase` is untouched) — the online path is a live, one-shot request, and
   the offline path already persists through `AccessCacheService`'s own established mechanism.
5. **No new canonical-id prefix minted.** `api/features/bio-mob-004/Identifiers/CanonicalId.cs`
   reuses the already-registered `evt_` prefix for a verification-request/correlation id and
   `per_` for the (never-minted-locally, only format-checked) target person id — the identical
   choice BIO-ACC-003's `PresentationEvent.EventId` and BIO-MOB-005's enrolment-package ids
   already made. On the mobile side, `person('per')` was added to
   `mobile/lib/core/identifiers/canonical_id.dart`'s own registered-type enum — this is
   extending which of the already-contract-registered prefixes this mirror file lists, not
   minting a new one (the file's own header forbids adding/renaming a prefix, not extending the
   subset it mirrors).
6. **No NFC/QR/card-reader/camera hardware integration exists anywhere in this app** (verified:
   no `nfc_manager`/`mobile_scanner`/`camera` dependency in `mobile/pubspec.yaml`, the same gap
   BIO-MOB-005's photo/biometric capture toggles already establish). The verify screen's person
   id and credential-class fields are entered directly, standing in for a real scan/read;
   `presentedCardStatus` sent to the backend is a fixed, clearly-labelled synthetic value
   (`ACTIVE`) representing "a credential was presented and read successfully" — the
   `credentialClass` itself (card/nfc/qr/face) still varies and is not synthetic.
7. **`scan_screen.dart` (the separate `/scan` route) was deliberately left untouched.** None of
   BIO-MOB-004's five ACs require a standalone camera-based barcode/QR scanning utility; "QR" is
   satisfied as one of the verify screen's own credential-class options (a QR presentation
   yields a person id, the same shape as a card/NFC read), not a separate scanner screen. Wiring
   an unrelated route would be exactly the "unapproved UI redesign" AC05 forbids.

## AC-to-evidence table

| AC | Requirement | Evidence |
|----|-------------|----------|
| AC01 | Flows show identity/status/decision without exposing unnecessary biometric internals | `VerificationDecisionResult`/`VerificationOutcome` (no biometric-evidence/score field exists at all); `BioMob004Module.AccessDecisionGatewayAdapter.ToOurResult` never reads `AccessDecisionEvent.BiometricEvidence`. `tests/features/bio-mob-004/VerificationEndpointsIntegrationTests.cs` (`A_decision_request_returns_no_biometric_evidence_field_at_all`); `tests/flutter/test/bio_mob_004_ac01_ac03_ac04_sync_client_test.dart` (`requestDecision sends device headers, credential fields and Idempotency-Key, and parses a sanitised outcome`) |
| AC02 | Offline state is visible | `mobile/lib/features/offline/offline_banner.dart` (pre-existing, app-wide, mounted above every screen); `VerificationService.verify` branches on `ConnectivityState`; `verify_screen.dart` labels the result source and flags a stale cache. `tests/flutter/test/bio_mob_004_ac02_verification_service_test.dart` |
| AC03 | Actions obey role permissions | `VerificationDeviceRole` (Officer/Supervisor, device-scoped, default Officer); `IVerificationDeviceRoleRepository` (own owned path, no `DeviceRegistry` change); `POST /api/v1/mobile-verification/decisions/{id}/override` (Supervisor-only, mandatory `reason_code`); `PUT /api/v1/mobile-verification/devices/{deviceId}/role` (human-actor/provenance/`Rbac`-permission gated). `tests/features/bio-mob-004/VerificationEndpointsIntegrationTests.cs` (`An_unassigned_Officer_device_may_not_override_a_decision`, `An_admin_can_assign_the_Supervisor_role_and_the_device_can_then_override`, `Overriding_without_a_reason_code_is_rejected`, `Assigning_a_device_role_without_the_required_permission_is_forbidden`, `Assigning_a_device_role_without_an_actor_id_is_rejected`, `Assigning_an_unrecognised_role_value_is_rejected`) |
| AC04 | Verification events sync idempotently | `Idempotency-Key` header required on `POST /api/v1/mobile-verification/decisions`, passed straight through to BIO-ACC-002's own already-reviewed `(TenantId, IdempotencyKey)` idempotency guarantee — no duplicated store. `tests/features/bio-mob-004/VerificationEndpointsIntegrationTests.cs` (`A_decision_request_without_an_Idempotency_Key_header_is_rejected`, `The_Idempotency_Key_header_is_passed_through_to_the_downstream_gateway_unchanged`) |
| AC05 | Mobile surface connected through MobileService | `mobile/lib/features/verify/verify_screen.dart` (real, replacing the BIO-MOB-001 placeholder) wired to `VerificationService`/`VerificationSyncClient` against `/api/v1/mobile-verification/*`. `governance/ui-integration-map.json`'s `MobileService` port names only the Flutter app itself as the surface (identical shape BIO-MOB-005's own AC05 already established). `tests/flutter/test/bio_mob_004_ac01_ac03_ac04_sync_client_test.dart`; `tests/flutter/test/bio_mob_004_ac02_verification_service_test.dart` |

## Test results (real command output, this commit)

- `dotnet test tests/features/bio-mob-004/Biolog.Features.MobileVerification.Tests.csproj`: **13/13 passed** — `dotnet-test-mobile-verification.txt`.
- `flutter test` (full suite, `tests/flutter/`): **134/134 passed** (6 net-new, 128 pre-existing, no regressions) — `flutter-test-full-suite.txt`.
- `flutter analyze` (`mobile/`): clean, no issues — `flutter-analyze-output.txt`.
- `dotnet build api/Biolog.Api/Biolog.Api.csproj` (full ~45-project host, including the new `BioMob004Module`): succeeded, 0 warnings, 0 errors — `dotnet-build-full-host.txt`.
- `bash tests/foundation/BIO-MOB-004.test.sh`: **23/23 passed** — `foundation-harness-output.txt`.
- `bash scripts/check-module-project-references.sh`: PASS — `module-reference-check.txt`.
- `bash scripts/secret-scan.sh --all`: clean — `secret-scan.txt`.
- `python3 scripts/check-traceability.py governance/stories/BIO-MOB-004.json`: run after this evidence directory was fully populated (see below).

## Environment notes

- No `DeviceRegistry` file was modified — this story only *consumes* the existing
  `ICrossTenantDeviceLookup` escape hatch already reviewed for BIO-MOB-003/BIO-ACC-003/
  BIO-MOB-005, via a manual DI factory lambda (never the reflection-based public-constructor
  activator), so no `DeviceRegistry` regression suite was needed beyond the unchanged full-host
  build.
- No `AccessDecisions` (BIO-ACC-002) file was modified either — consumed entirely through this
  feature's own project-local gateway interface and a module-level adapter.
- `dotnet build`/`dotnet test` briefly showed the same "Build FAILED, 0 Warnings, 0 Errors,
  Determining projects to restore..." symptom documented in prior stories' evidence (sandboxed
  NuGet-restore network block, not a real circular reference — confirmed by running
  `scripts/check-module-project-references.sh` first, which passed clean). Retried with the
  sandbox disabled; restore then completed normally.
- Two self-authored compile errors were caught and fixed before this evidence was captured:
  BIO-ACC-002's own `ValidationErrorItem` record uses `Path` as its first property, not `Field`;
  and `AssuranceLevelExtensions.ToWireString` needed a fully-qualified static call since this
  file does not (and should not) `using Biolog.Features.AccessDecisions.Domain;` wholesale.

## Deferred/out-of-scope, disclosed explicitly

- **A standalone camera-based QR/barcode scanner UI (`/scan` route)** — not built; none of
  BIO-MOB-004's five ACs require it, and "QR" is satisfied as one of the verify screen's own
  credential-class options. See scope decision 7 above.
- **Real NFC/card-reader/camera hardware capture** — no such integration exists anywhere in this
  repository yet; represented by direct manual entry plus a disclosed synthetic
  `presentedCardStatus` value, mirroring BIO-MOB-005's own photo/biometric capture toggles. See
  scope decision 6 above.
- **Officer/human-actor mobile login** — genuinely does not exist yet (BIO-INT-001 unshipped);
  AC03 is satisfied via the device-scoped role Kemi approved instead, with the residual gap
  ("what eventually closes this accommodation for mobile specifically") explicitly named by
  Kemi as needing its own future story, the same class of gap BIO-MOB-003's own device-enrolment
  boundary already carries. Not something this story can or should resolve.
- **`ADR-0006 Decision 6`** (Kemi's formalization of the device-authenticated-principal
  extension) is being written through Kemi's own BIO-ARC-006-AC04 change-control process,
  separately from this story — referenced here, not authored here.
