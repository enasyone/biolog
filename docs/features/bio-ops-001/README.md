# BIO-OPS-001 local enrolment acceptance journey

This acceptance-only story exercises the approved local service-port workflow in `SIMULATION` mode with synthetic data. It does not claim hardware capture, production biometrics, or a remote deployment. The executable test starts in a fresh Vitest process and creates a new synthetic PERSON before running each downstream stage.

| Acceptance criterion | Executable evidence |
|---|---|
| BIO-OPS-001-AC01 | `tests/frontend/unit/bio-ops-001-enrolment-journey.test.ts` starts from a fresh test process and resets the card store before creating a new synthetic person. |
| BIO-OPS-001-AC02 | The same test executes person enrolment, photo capture, one right-index fingerprint capture, face capture, document capture/authenticity verification, and card issue through the existing module ports. |
| BIO-OPS-001-AC03 | `EnrolmentJourneyService.executeStage` passes an immutable tenant/PERSON/stage context into every callback. The callback persists its artifact in a separate owning `LocalArtifactStore`; the journey resolves that returned reference and rejects missing or mismatched ownership before recording it. The test independently reads `EnrolmentJourneyStore.listForPerson` and includes a cross-PERSON relabelling attack. |
| BIO-OPS-001-AC04 | `tests/operations/BIO-OPS-001.test.sh` fails when the repository has any configured Git remote; the journey uses only local adapters and synthetic fixtures. |
| BIO-OPS-001-AC05 | The service records and throws on the first failure/offline result without retry, and checks stored blockers before invoking every later callback. Parameterized tests catch the first exception, deliberately attempt card issue anyway, and prove the service rejects it before the card callback runs. Builder evidence records the focused journey, full frontend regression, repository traceability, module direction, and secret scan. |

The person identifier and outcomes exist only in memory for the test process. No production personally identifiable information, biometric samples, credentials, or tenant data are written to evidence.
