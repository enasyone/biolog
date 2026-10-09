# BIO-BIO-009 round-three review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed builder `366ee60` and evidence `302c735` against BIO-BIO-009 AC01–AC05.

Round three closes the raw-image write-boundary bypass: capture now requires both the session flag and the tenant retention authority, and authority errors fail closed. It also removes the public fallback provenance key and distinguishes incomplete gallery cleanup from complete deletion. Those are material improvements, but the claimed durable recovery and live web workflow are not implemented.

## BIO-BIO-009-F04 — pending gallery cleanup is erased by an application restart (High)

AC04 requires deletion to propagate to permitted caches/providers. Round two failed because a gallery failure left searchable biometric data with no recoverable obligation. Round three calls the replacement “durable,” but the production registration is `AddSingleton<IPendingGalleryCleanupStore, InMemoryPendingGalleryCleanupStore>` (`BioBio009Module.cs:97`). That store is only a process-local `ConcurrentDictionary` (`InMemoryPendingGalleryCleanupStore.cs:5-26`).

Reproduction:

1. Make record deletion succeed and gallery eviction fail three times.
2. Observe `DeletedPendingGalleryCleanup` and one entry in the singleton dictionary.
3. Restart `Biolog.Api`, which constructs a new empty dictionary.
4. Call `POST /api/v1/biometrics/retention/reconcile-gallery-cleanup`.
5. It reports no pending work while the biometric gallery entry remains searchable.

The builder tests reuse one in-memory store in one process, so they cannot detect this data-loss boundary. A persistent store, transactional outbox, or equivalent restart-safe mechanism is required before the previous AC04 finding is closed.

There is a second unsafe loss path at `BiometricDeletionService.cs:108-112`: any delete-call exception removes the pending obligation based on the assertion that the record “was never actually deleted.” A thrown remote/storage call can be ambiguous after commit. The obligation must not be discarded unless non-deletion is positively established.

## BIO-BIO-009-F05 — the LIVE web actions cannot pass backend authorization (High)

AC05 requires the mapped web surface to connect through `BiometricService` to the live implementation or a documented HYBRID state, with a passing human workflow. The new live adapter sends only `Content-Type` (`web/features/biometrics/live.ts:36-43`). Both backend mutations require `X-Actor-Id`, a valid `X-Actor-Provenance` proof, and a real role-based access-control grant before processing (`BioBio009Module.cs:154-170`, `221-229`). Therefore every browser LIVE policy or deletion action is guaranteed to fail before reaching the service.

The reported Playwright 4/4 result is not live evidence. The port defaults to SIMULATION, and the tests never switch either action to LIVE (`bio-bio-009-retention-privacy.spec.ts:1-37`). They only prove the mock UI. The unit suite likewise checks port selection and mock results, not an authenticated browser-to-API flow.

Reproduction:

1. Set either new BiometricService action port to LIVE.
2. Open `/fingerprints` and submit the corresponding action.
3. Inspect the request: it has JSON content type only.
4. The API rejects it at `ActorResolver.Resolve` because `X-Actor-Id` is absent (and would next reject absent provenance).
5. The component has no error handling, so the operator receives no usable outcome.

This also fails the required permission/error-state coverage; the privileged controls are rendered without a role-visibility gate.

## BIO-BIO-009-F06 — weak provenance secrets remain accepted (High)

The public fallback key is gone, but `HmacActorProvenanceStore.Configure` accepts any string, including a one-byte secret (`ActorProvenanceGate.cs:31`). Provisioning rejects only null/whitespace (`:64-71`). This violates the canonical minimum secret length of 32 bytes identified in the review dispatch and makes an authorization-bearing HMAC key trivially brute-forceable after misconfiguration.

Reproduction: configure `BiometricRetention:ActorProvenanceSecret=x`, compute HMAC-SHA256 for a privileged actor with key `x`, and present it. `Verify` accepts the proof. Configuration must fail closed below the canonical minimum, with a boundary test for 31/32 bytes.

## Verification

- Exact reviewed HEAD: `302c735`; source commit: `366ee60`.
- Frontend unit regression: PASS, 63 files / 473 tests, including 10 BIO-BIO-009 port tests.
- `git diff --check 6ab9435..302c735`: PASS.
- Focused .NET test execution: environment-blocked because VSTest could not bind its local communication socket (`SocketException (13): Permission denied`). The project compiled successfully before the runner aborted; builder-reported counts are not treated as independent proof.
- Functional: FAIL (AC04, AC05).
- Security/privacy: FAIL (restart loses cleanup; weak provenance secret accepted).
- Tenant isolation: PASS by inspection for the new tenant-keyed authority and reconciliation query.
- Offline/recovery: FAIL (cleanup recovery is process-local, not restart-safe).
- Concurrency: FAIL (ambiguous delete exceptions discard cleanup state).
- Regression: PASS for frontend unit coverage; .NET execution environment-blocked as noted.

