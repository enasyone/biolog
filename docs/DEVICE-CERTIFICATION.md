# Device certification matrix (BIO-DEV-002)

Supported/certified/experimental/deprecated/unsupported statuses for device+SDK+firmware
combinations. Implemented as `Modules/DeviceCertificationModule.cs`, following the module
pattern established by BIO-FND-008/009 and mirroring `Features/DeviceRegistry/`'s own shape
(BIO-DEV-001) wherever the same problem recurs - concurrency, audit-before-commit, and
ambiguous-audit-write verification are all reused patterns, not re-derived from scratch.

## Two scopes, one storage/lock/audit shape (AC01/AC03)

`Features/DeviceCertification/DeviceCertification.cs`: `Vendor`/`Model`/`Firmware`/`Sdk`
(matching `Features/DeviceRegistry/Device.cs`'s own field names exactly, so a real device's
fields can be looked up against this matrix directly - BIO-DEV-003 will need exactly that
correspondence for "unsupported firmware/SDK combinations surface certification status"),
`Status` (`Certified`/`Supported`/`Experimental`/`Deprecated`/`Unsupported` - the five
values the user story names verbatim), `TestEvidenceReference` (a pointer to wherever the
real test report lives - a document id, a URL, a ticket key - never the evidence content
itself), `Version` (ADR-0001 Decision 3: starts at 1, +1 per mutation), `TenantId`.

`TenantId` is the mechanism behind AC03 ("independent per tenant deployment when needed"):
`null` for the **platform-wide default** every tenant sees unless it has its own ("As
platform operations" per the user story - control-plane, the same shape `BioTen001Module`'s
Tenant CRUD already uses for an operations action that is not itself a tenant-plane read or
write), a real tenant id for that **one tenant's own override**. The two scopes share the
same repository, per-aggregate lock registry and audit sink but never collide: internally, a
`null` tenant id and a real one produce different storage keys for the identical
vendor+model+firmware+sdk combination (`DATA-ARCHITECTURE.md` lists `DeviceCertification`
among tenant entities; this shape is what makes a *tenant-scoped* record also support a
platform-wide *default* without a second table or a special-cased "global tenant").

Not a `CanonicalId` (`dev_`, `ten_`, etc.): ADR-0001 §1.2 registers exactly six identifier
types and does not register one for a certification record, and minting a seventh prefix
locally would need its own ADR amendment and Kemi's approval - out of this story's scope.
`Features/DeviceCertification/DeviceCertificationKey.cs` is instead a "business identifier"
in ADR-0001 §1.6's sense: the natural `Vendor`+`Model`+`Firmware`+`Sdk` combination, joined
with `/` into a composite string used for in-memory storage, locking and audit targets.

**`DeviceCertificationKeyValidator` closes a real collision this composite-key design would
otherwise have**: if any field could itself contain `/`, two different combinations could
collide onto the same composite string (`Vendor: "Acme/Sub", Model: "C"` and
`Vendor: "Acme", Model: "Sub/C"` both produce `"Acme/Sub/C/..."`), silently merging two
distinct certification records into one. The validator rejects `/` and `:` (also used to
prefix the tenant scope), empty/whitespace values, control characters and overlong fields -
enforced at **both** the service boundary (`DeviceCertificationService`) and the repository
boundary (`InMemoryDeviceCertificationRepository.CreateAsync`), matching the belt-and-braces
posture BIO-DEV-001-QA-04 established for `CredentialReferenceValidator`.

## Effective status resolution (AC03)

`DeviceCertificationService.GetEffectiveAsync` is where AC03 becomes observable: given a
key, it prefers the calling tenant's own override (if one is resolved and one exists),
falling back to the platform-wide default otherwise. Deliberately **not** fail-closed on a
missing tenant the way every `Features/DeviceRegistry/DeviceService` method is (ADR-0002
Decision 2) - the platform-wide matrix is control-plane data every caller may read
(AC02: "visible to admins", not "visible only inside a resolved tenant"). A caller with no
resolved tenant simply sees the platform-wide default.

## Unsupported combinations are visible to admins (AC02)

`GET /api/v1/device-certifications` returns the full platform-wide matrix, with an optional
`?status=` filter so an admin can pull exactly the `Unsupported` (or any other status) rows
without hiding anything by default. `GET /api/v1/tenant/device-certifications` gives the
same view scoped to one tenant's own overrides (requires a resolved tenant). Neither
endpoint has real authentication/authorisation behind "admins" yet - no auth story has
shipped a verified session to gate this on, the same documented gap
`Features/DeviceRegistry/DeviceStatusChangeRequest`'s `ActorType`/`ActorId` fields already
carry for actor identity generally.

## Changes are audited (AC04)

`Features/DeviceCertification/Audit/` is a **dedicated** audit envelope
(`CertificationAuditEvent`/`ICertificationAuditSink`/`InMemoryCertificationAuditSink`), not
a reuse of `Biolog.Api.Audit.IAuditSink` (which `DeviceRegistryModule` already registers as
a singleton). ASP.NET Core's DI resolves a single-service registration to whichever module
registered it **last**, so a second `AddSingleton<IAuditSink, ...>` here would silently
shadow `DeviceRegistryModule`'s own registration instead of adding a second one - the same
choice BIO-TEN-001/BIO-TEN-002 already made for their own audit sinks, for the same reason.
`CertificationAuditEvent.TenantId` is also nullable (`null` for a change to the
platform-wide default, ADR-0003's schema: "Null only for control-plane actions"), which
`Biolog.Api.Audit.AuditEvent`'s `required string TenantId` does not allow without widening a
type `DeviceService` also depends on.

Both registration and status/evidence updates are audited (`device_certification.registered`
/ `device_certification.updated`), mirroring `DeviceService.ChangeStatusAsync`/`AuditAsync`
line-for-line in structure - the same proven, twice-reviewed shape from BIO-DEV-001
(QA-02/QA-03) and ADR-0003 Decision 7 element 4, applied to a second aggregate type in this
codebase rather than re-derived from scratch:

- **Audit-before-commit**: the audit event is durably recorded before the repository write,
  never after with a rollback on failure (BIO-DEV-001-QA-02's corrected design - a
  commit-then-rollback shape breaks under concurrent writers).
- **Per-aggregate locking** (`DeviceCertificationLockRegistry`, keyed by the same composite
  string that already encodes tenant scope) serializes the whole pre-check-audit-commit
  sequence per key+scope, so no second writer for the same combination can begin its own
  pre-check until the first's audit AND commit have both resolved - closing the same false-
  success gap a lock-less audit-before-commit alone would still leave (BIO-DEV-001-QA-02,
  second correction).
- **`ExistsAsync` ambiguity resolution** (ADR-0003 Decision 7, element 4): a thrown
  `RecordAsync` does not by itself prove the event never happened - a real store can commit
  a write and still throw on the acknowledgement. `DeviceCertificationService`'s private
  `AuditAsync` resolves this via `ICertificationAuditSink.ExistsAsync` before deciding:
  confirmed-present proceeds as a success, confirmed-absent or a verification call that
  itself throws raises `AuditUnavailableException` (reused directly from
  `Biolog.Api.Audit` - a plain message/exception wrapper, not tied to any specific audit
  envelope shape) with nothing committed.
- **`CancellationToken.None` past the point of no return** (BIO-DEV-001-QA-03): once the
  audit event durably exists, the repository call uses `CancellationToken.None` rather than
  the caller's token, so a cancellation request arriving after that point cannot abandon a
  write whose audit event already claims it happened.
- **Registration checks existence under the same lock before auditing**: an attempt against
  a combination that already has an entry in the same scope is rejected
  (`DeviceCertificationAlreadyExistsException`, `409 device_certification_already_exists`)
  before any audit event is written - an operation that never had a real effect to describe
  is not itself audit-worthy, the same rule `DeviceService` applies to an operation against
  an unknown or cross-tenant device.

The crash/ack residual (ADR-0003 Decision 7, element 5) is inert here for the identical
reason it is inert for BIO-DEV-001: `ICertificationAuditSink`/`IDeviceCertificationRepository`
are both `AddSingleton` (same-process, same-lifetime, volatile in-memory) - reopens the
moment either side becomes durable, remote, transactional, or multi-process.

## Endpoints

| Method | Path | Scope | Notes |
|---|---|---|---|
| `POST` | `/api/v1/device-certifications` | control-plane (no tenant needed) | Registers the platform-wide default entry. |
| `GET` | `/api/v1/device-certifications` | control-plane | Lists the full matrix; `?status=` filters (AC02). |
| `GET` | `/api/v1/device-certifications/lookup` | either | `?vendor=&model=&firmware=&sdk=`; effective status - a resolved caller's own override if one exists, else the default (AC03). |
| `PATCH` | `/api/v1/device-certifications/status` | control-plane | Updates the platform-wide entry's status/evidence. |
| `POST` | `/api/v1/tenant/device-certifications` | tenant (fails closed) | Registers this tenant's own override (AC03). |
| `GET` | `/api/v1/tenant/device-certifications` | tenant (fails closed) | Lists this tenant's own overrides only, not the merged view. |
| `PATCH` | `/api/v1/tenant/device-certifications/status` | tenant (fails closed) | Updates this tenant's own override. |

Every mutating endpoint requires `ExpectedVersion`/`ActorType`/`ActorId` in its body -
`ExpectedVersion` per ADR-0001 §3.2 (a mutating write with no precondition is a failure, not
a last-write-wins fallback), the actor fields self-reported for the same reason
`DeviceStatusChangeRequest`'s are: no auth story has shipped a verified session to derive
them from yet.

## Tenant resolution: real, upgraded evidence (ADR-0002 Decision 6)

Unlike BIO-DEV-001 (built before BIO-TEN-002 shipped, so its tenant-plane evidence pairs a
fail-closed integration test with a test-double positive-path proof plus a named upgrade
obligation), **BIO-TEN-002's host channel was already live when this story started** - the
tenant-override endpoints' positive-path evidence
(`tests/dotnet/Biolog.IntegrationTests/DeviceCertificationTests.cs`) is proven through a
*real* verified `TenantDomain` and the real `Host` header, matching
`PersonHostChannelTests.cs`'s already-upgraded shape, not an injected test accessor. There
is no interim accommodation or tracked follow-up obligation to record for this story's AC03.

## Developer tests

- `tests/dotnet/Biolog.UnitTests/DeviceCertification/DeviceCertificationKeyValidatorTests.cs` - AC01 key validation, including the reserved-separator collision it prevents.
- `tests/dotnet/Biolog.UnitTests/DeviceCertification/InMemoryDeviceCertificationRepositoryTests.cs` - AC01/AC03 storage, scope isolation, optimistic concurrency.
- `tests/dotnet/Biolog.UnitTests/DeviceCertification/DeviceCertificationServiceTests.cs` - AC01-AC04: registration, effective-status resolution, listing/filtering, audit-before-commit, `ExistsAsync` ambiguity resolution, per-aggregate locking under real concurrency, cancellation past the point of no return.
- `tests/dotnet/Biolog.IntegrationTests/DeviceCertificationTests.cs` - the same behaviours through the real composed host, including tenant-override precedence proven via a real verified `TenantDomain`.
- `tests/foundation/BIO-DEV-002.test.sh` - offline-safe structural checks mapped to each AC (see `scripts/quality.sh`).
