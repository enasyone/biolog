# BIO-BIO-004 — Implement fingerprint 1:1 verification

Implements the BIO-BIO epic's fourth story: 1:1 fingerprint verification against a claimed
identity, reusing BIO-BIO-001's own provider-neutral biometric contracts (user story, verbatim:
"As access/enrolment workflows, I need 1:1 fingerprint verification against a claimed identity.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Claimed identity resolves one authorised reference set | `Service/FingerprintVerificationService.VerifyAsync` resolves BIO-BIO-001's own `IBiometricTemplateStore.GetTemplatesAsync(tenantId, personId, Fingerprint)` and always compares against the single newest enrolled template - never a 1:N search. An empty result throws `Domain/FingerprintVerificationException.NoAuthorisedReference` (a precondition failure, not a routine outcome) | `FingerprintVerificationServiceTests.AC01_*` |
| **AC02** | Provider score/decision/model version are recorded | `Domain/FingerprintVerificationRecord.cs` (`Score`, `MatcherId`, `ProviderId`, `ProviderVersion`, `ComparisonOutcome`, `AccessDecision`) is persisted via `Domain/IFingerprintVerificationRecordStore.cs` on every call, regardless of outcome | `FingerprintVerificationServiceTests.AC02_*` |
| **AC03** | No-match and acquisition failure are distinct outcomes | `Domain/BiometricComparisonOutcome.cs` is a closed three-member enum (`Matched`/`NotMatched`/`AcquisitionFailed`); **any** exception `IFingerprintProvider.ExtractTemplateAsync` raises (not only `ArgumentException`) is caught and recorded as `AcquisitionFailed`, except a genuine caller cancellation, which propagates instead - never left to propagate as an unhandled, unrecorded exception | `FingerprintVerificationServiceTests.AC03_*`, `FingerprintVerificationAcquisitionFailureTests.*` |
| **AC04** | Suspended/revoked identities cannot be converted into access allow by match alone | `Domain/IIdentityStatusGateway.cs` is consulted whenever (and only when) the biometric comparison itself matched; `IdentityStatus.Suspended`/`.Revoked` downgrades what would otherwise be `AccessDecision.Allow` into `.Deny`, while `ComparisonOutcome.Matched` is still recorded | `FingerprintVerificationServiceTests.AC04_*` |

## Design decisions and scope boundaries

**Verifies against BIO-BIO-001's own `IBiometricTemplateStore`, not BIO-BIO-002's session
aggregate.** The approved story names BIO-BIO-002 (guided tenprint/single-finger enrolment) as a
dependency, but BIO-BIO-002's `IFingerprintEnrolmentRepository`/`FingerprintEnrolmentService` are
internal, Singleton-registered, and expose no "resolve by claimed identity" query at all (only
get-by-session-id); the templates a completed enrolment session captures live embedded in its own
`FingerprintEnrolmentSession.Fingers` dictionary and are never published to BIO-BIO-001's shared
`IBiometricTemplateStore`. Reaching into BIO-BIO-002's own owned paths to add such a query is out
of this story's write scope (`governance/feature-path-ownership.json` grants this story only
`api|web|mobile|tests|docs|evidence/features/bio-bio-004/**`). This story instead verifies
against `IBiometricTemplateStore` directly - the SAME seam
`Biolog.Features.Biometrics.Service.BiometricService.EnrollAsync`/`VerifyAsync` already define
for exactly this purpose (BIO-BIO-001-AC03) - and documents this gap explicitly rather than
silently working around it. A later story that has BIO-BIO-002 (or a real enrolment channel)
publish into `IBiometricTemplateStore` needs no change to this service's shape at all.

**A narrow `IIdentityStatusGateway`, not a `ProjectReference` to BIO-IDN-001 (AC04).**
BIO-IDN-001's `PersonService` is a tenant-scoped, Scoped-registered aggregate service - the same
shape BIO-BIO-003 already documents for why it has no direct reference to BIO-PHO-001. This
project defines its own narrow `Domain/IIdentityStatusGateway.cs` instead; the real
implementation is an adapter in `Modules/BioBio004Module.cs` wrapping BIO-IDN-001's already-
registered `PersonService.GetAsync`, the identical "narrow gateway + host-level adapter" shape
`BioBio003Module`/`BioIdn002Module`-through-`BioIdn006Module`/`BioPho001Module`/`BioPho002Module`
already use. `Domain/IdentityStatus.cs` mirrors BIO-IDN-001's own eight-state `PersonStatus` by
wire string, the same deliberate, documented mirror BIO-IDN-004's own `IPersonLifecycleGateway`
uses for the identical reason (no `ProjectReference` at all).

**The identity status gateway is consulted only after a match.** An identity that never matched
can never reach `AccessDecision.Allow` regardless of its lifecycle status, so querying status for
every non-matching attempt would be pure overhead with no observable effect - and would also risk
mislabelling an ordinary `no_match` denial as a status problem it was not. AC04 only requires that
a MATCH cannot become an allow for a suspended/revoked identity; it does not ask this story to
gate every other denial reason on status too.

**AC04 gates only on Suspended/Revoked, not on identity non-existence or any other status.** If
`IIdentityStatusGateway.GetStatusAsync` returns `null` (identity not found, or an unrecognised
wire string), the match is still allowed to proceed to `AccessDecision.Allow` - AC04's own wording
names exactly two statuses; inventing a broader "identity must positively exist and be Active"
policy is scope this story's four ACs do not ask for. A later story that wants that stricter rule
changes `FingerprintVerificationService.VerifyAsync`'s single `switch` without touching
`IIdentityStatusGateway`'s contract.

**Acquisition failure vs. no-match is a typed outcome, never an exception (AC03).** Both are
routine, expected results of an ordinary verification attempt an operator handles day to day -
the same typed-outcome-vs-exception split BIO-BIO-003 established for its own quality/liveness
gate. AC01's missing-reference precondition, by contrast, is a genuine caller/integration error
(the identity was never enrolled at all) and stays an exception, mirroring BIO-BIO-003's
`FaceEnrolmentException.SourcePhotoNotFound` treatment of its own upstream precondition.

**BIO-BIO-004-F01 (Seyi round 1, High).** `VerifyAsync` originally caught only
`ArgumentException` around `IFingerprintProvider.ExtractTemplateAsync`. `IFingerprintProvider` is
an external adapter seam - a vendor SDK implements it directly - so it can fail with an
`InvalidOperationException`, a `TimeoutException`, a vendor-specific exception type, or an
`OperationCanceledException` the provider itself raised on an internal timeout unrelated to the
caller's own `CancellationToken`; any of those escaped uncaught, and no
`FingerprintVerificationRecord` was ever written - a provider outage could turn a routine
verification attempt into an unhandled exception with no audit/decision trail at all, which is
exactly what AC02/AC03 exist to prevent. Fixed by catching every exception the provider call can
raise and recording `AcquisitionFailed`, with one carved-out exception: an `OperationCanceledException`
that correlates to the CALLER's own passed-in token (`cancellationToken.IsCancellationRequested`)
still propagates instead of being recorded - the caller abandoned the request, so nothing about
that attempt was ever decided, and recording a decision for it would be wrong. Regression coverage
in `FingerprintVerificationAcquisitionFailureTests.cs` reproduces the exact failure modes the
review named (`InvalidOperationException`, `TimeoutException`, a vendor-specific exception type,
a provider-internal cancellation) plus the caller-cancellation carve-out.

**Every verification attempt is recorded, not only successful ones (AC02).** AC02 says "are
recorded," not "successful attempts are recorded" - `FingerprintVerificationRecord` is written
exactly once per `VerifyAsync` call regardless of `ComparisonOutcome`/`AccessDecision`, so a
security review can see every attempt against a claimed identity, including ones that never
matched or never extracted at all.

**No HTTP endpoint is mapped by this story**, the same judgment call BIO-BIO-001/002/003 made:
AC01-AC04 are about the verification/decision model, not an HTTP surface, and the service takes a
caller-resolved tenant/person id rather than resolving one itself.

**Identifiers are plain strings**, the same explicitly documented choice BIO-BIO-001/002/003/
BIO-DOC-001 made; no `identifier-registry.json` prefix is registered for a verification record
(frozen, ADR-governed contract, out of this story's `read_only_shared_contracts` scope).

**Every sample byte array in this suite is synthetic filler**, never a captured fingerprint image
(this story's own top-level constraint: "Use synthetic data only").
