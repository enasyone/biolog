# BIO-BIO-002 — Guided fingerprint enrolment (tenprint / single finger)

Implements the BIO-BIO epic's second story: guided tenprint and single-finger enrolment with
per-finger quality and exception handling, built directly on BIO-BIO-001's provider-neutral
biometric contracts (user story, verbatim: "As a biometric operator, I need guided tenprint and
single-finger enrolment with per-finger quality and exception handling.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | 10-print and single-finger modes exist | `Domain/EnrolmentMode.cs` (`TenPrint`/`SingleFinger`); `FingerprintEnrolmentSession.Start` fixes `TargetPositions` at session start - all ten `Domain/FingerPosition.cs` values for `TenPrint`, exactly one caller-chosen position for `SingleFinger` | `FingerprintEnrolmentSessionTests.AC01_*` |
| **AC02** | Finger identity and quality are recorded | `Domain/FingerRecord.cs` (`Position` + `QualityScore`); `Quality/IFingerQualityScorer.cs`/`SyntheticFingerQualityScorer.cs` score every capture | `FingerprintEnrolmentServiceTests.AC02_CaptureFingerAsync_records_finger_identity_and_a_quality_score` |
| **AC03** | Recapture and missing/amputated finger exceptions are supported | Recapture = calling `CaptureFinger` again for an already-captured position (`CaptureCount` increments, the record is superseded); `MarkMissingOrAmputated` is the distinct, terminal exception path, mutually exclusive with capture | `FingerprintEnrolmentSessionTests.AC03_*` |
| **AC04** | Raw fingerprint images are discarded by default after approved template derivation unless policy authorises retention | `FingerRecord.RetainedRawImage` is populated ONLY when the session's `RetentionAuthorised` flag (fixed at `Start`) is true - `FingerprintEnrolmentService.CaptureFingerAsync` never sets it otherwise | `FingerprintEnrolmentServiceTests.AC04_*` |
| **AC05** | Enrolment is audited | Every mutating `FingerprintEnrolmentService` method (start/capture/mark-missing/complete/cancel) writes a `FingerprintEnrolmentAuditEvent` before committing (ADR-0003 Decision 7: lock, audit-before-commit, `ExistsAsync` verification on an ambiguous throw, `CancellationToken.None` on the confirmed commit) | `FingerprintEnrolmentServiceTests.AC05_*` |

## Design decisions and scope boundaries

**Built directly on BIO-BIO-001, via a feature-to-feature `ProjectReference`.** This mirrors
BIO-DOC-002/BIO-DOC-003's identical, already-reviewed shape for BIO-DOC-001 - not the bio-idn
family's *different* rule of avoiding a `ProjectReference` to another feature's tenant-scoped,
access-restricted aggregate. BIO-BIO-001 carries no such aggregate: it is a plain, host-agnostic
contracts library written specifically to be built on (`IFingerprintProvider`,
`ExtractedBiometricFeatures`, `BiometricTemplate`) - reusing it directly avoids re-deriving AC02's
provider-isolation guarantee a second time.

**Providers are shared, not re-registered.** `Modules/BioBio002Module.cs` resolves the
`IFingerprintProvider` singleton BIO-BIO-001's own `BioBio001Module` already registers, rather
than registering a second fingerprint provider - there is exactly one synthetic fingerprint
provider in the packaged host, reused by both stories.

**"Approved template derivation" (AC04).** In this synchronous, single-operator flow, a
successful `CaptureFingerAsync` call - extraction and quality scoring both completing without
throwing - IS the approval: no template is derived, and no raw image is ever attached, unless
extraction succeeds first. A future story adding a distinct multi-step approval workflow (e.g. a
supervisor sign-off before a template is trusted) can extend `FingerRecord` without this story's
shape changing.

**No HTTP endpoint is mapped by this story**, the same judgment call BIO-BIO-001 made for
`BioBio001Module`: AC01-AC05 are about the guided-enrolment SESSION MODEL, not an HTTP surface,
and `FingerprintEnrolmentService` takes a caller-resolved tenant/person id rather than resolving
one itself, so a later story maps real HTTP routes onto it without this feature's shape changing.

**Repository/service are internal**, the same `InternalsVisibleTo`-gated seam BIO-IDN-001-QA-01
established: `IFingerprintEnrolmentRepository`/`InMemoryFingerprintEnrolmentRepository` and
`FingerprintEnrolmentService` are reachable only from this assembly, `Biolog.Api` (DI wiring) and
this feature's own test project - never a public, freely-constructible surface a caller could
bypass session invariants (CAS, tenant scoping, target-position validation) through.

**Identifiers are plain strings**, not a validated implementation of ADR-0001's canonical-id
contract - the same explicitly documented choice BIO-BIO-001/BIO-DOC-001 made; no
`identifier-registry.json` prefix is registered for a fingerprint enrolment session (that
registry is a frozen, ADR-governed contract this story's `read_only_shared_contracts` list
forbids touching).

**Every sample byte array in this suite is synthetic filler**, never a captured fingerprint image
(top-level story constraint: "Use synthetic data only" - AC05's own local-dev precedent from
BIO-BIO-001 applies here too, even though it is not itself an AC of this story).
