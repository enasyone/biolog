# BIO-DEV-002 — Builder evidence

**Story:** Implement device certification matrix
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-DEV-002`, branch `feature/BIO-DEV-002` off `develop`
**Dependencies:** BIO-DEV-001 (DONE — device registry, the reference implementation this story mirrors), BIO-TEN-002 (DONE — real host-channel tenant resolution, already shipped before this story started)
**Commit:** `b90dab36f24f5a0c583c6813592aacd355f3c3a1` (content commit; metadata.json/governance spec synced to this hash in the immediately following commit, per the established two-commit evidence pattern).

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — certification records version SDK/firmware/test evidence | `Features/DeviceCertification/DeviceCertification.cs`: `Vendor`/`Model`/`Firmware`/`Sdk` (matching `Device.cs`'s own field names for direct lookup correspondence), `Status`, `TestEvidenceReference`, `Version` (ADR-0001 Decision 3). No canonical id minted (ADR-0001 §1.2 registers six types, not a seventh for this record) — a natural composite key instead, validated by `DeviceCertificationKeyValidator` (rejects `/`/`:`, the composite-key separators, closing a real collision the naive join would otherwise allow) at both the service and repository boundaries. | `dotnet-unit-test-output.txt` (`DeviceCertificationKeyValidatorTests`, `InMemoryDeviceCertificationRepositoryTests`), `structural-test-output.txt` |
| AC02 — unsupported combinations are visible to admins | `GET /api/v1/device-certifications` returns the full platform-wide matrix with an optional `?status=` filter; `GET /api/v1/tenant/device-certifications` gives the same view scoped to a tenant's own overrides. No real authn/authz behind "admin" yet — same documented gap as `DeviceStatusChangeRequest`'s self-reported actor fields, no auth story has shipped a verified session. | `dotnet-unit-test-output.txt` (`ListGlobalAsync_surfaces_an_unsupported_entry...`), `dotnet-integration-test-output.txt` (`AC02_the_global_listing_surfaces_an_unsupported_combination...`), `structural-test-output.txt` |
| AC03 — certification status is independent per tenant deployment when needed | Two scopes share one repository/lock/audit shape: `tenantId: null` = platform-wide default (control-plane, no tenant needed — "As platform operations" per the user story), a real tenant id = that tenant's own override (fails closed via `TenantContextMissingException`). `GetEffectiveAsync` prefers a resolved tenant's override, falls back to the default. Proven through the REAL BIO-TEN-002 host channel (verified `TenantDomain` + `Host` header), not a test double — that channel already shipped when this story started, so there is no interim accommodation or tracked upgrade obligation to record, unlike BIO-DEV-001's own AC01. | `dotnet-unit-test-output.txt` (`GetEffectiveAsync_prefers_the_calling_tenants_override...`, `RegisterTenantOverrideAsync_*`), `dotnet-integration-test-output.txt` (`Registering_a_tenant_override_through_a_verified_hostname...`, `A_tenants_override_takes_precedence_over_the_global_default...`), `structural-test-output.txt` |
| AC04 — changes are audited | Dedicated `Features/DeviceCertification/Audit/` envelope (`ICertificationAuditSink`/`CertificationAuditEvent`, nullable `TenantId` for the control-plane case) — NOT a reuse of `DeviceRegistryModule`'s already-registered `Biolog.Api.Audit.IAuditSink` (a second `AddSingleton` for that interface would silently shadow the first, not add a second sink). Both registration and status/evidence updates are audited, mirroring `DeviceService.ChangeStatusAsync`/`AuditAsync` line-for-line: audit-before-commit, per-aggregate lock (`DeviceCertificationLockRegistry`), `ExistsAsync` ambiguity resolution (ADR-0003 Decision 7 element 4), `CancellationToken.None` past the point of no return. | `dotnet-unit-test-output.txt` (`DeviceCertificationServiceTests` audit/concurrency/cancellation cases), `structural-test-output.txt` |

## Developer tests (296 pass: 200 unit + 96 integration, project-wide)

- `tests/dotnet/Biolog.UnitTests/DeviceCertification/DeviceCertificationKeyValidatorTests.cs` — the reserved-separator collision the validator prevents, control-char/length/empty rejection.
- `tests/dotnet/Biolog.UnitTests/DeviceCertification/InMemoryDeviceCertificationRepositoryTests.cs` — create/get/update/list, scope isolation (global vs. tenant override), a genuine concurrent compare-and-set race.
- `tests/dotnet/Biolog.UnitTests/DeviceCertification/DeviceCertificationServiceTests.cs` — registration (global, requires-no-tenant; tenant override, fails closed), duplicate-registration rejection, effective-status precedence, listing/filtering (AC02), audit-before-commit, `ExistsAsync` ambiguity resolution and total-outage fail-closed (ADR-0003 Decision 7 element 4), a genuinely-serialized concurrent update proven via a gated audit sink, and the update landing despite caller cancellation right after a confirmed audit write.
- `tests/dotnet/Biolog.IntegrationTests/DeviceCertificationTests.cs` — through the real composed host: control-plane registration/listing/update, duplicate rejection, concurrency conflict, and (AC03, upgraded evidence per ADR-0002 Decision 6) tenant-override registration/isolation/precedence through a REAL verified `TenantDomain` and `Host` header — BIO-TEN-002 was already live when this story started, so no test-double accommodation was needed.
- `tests/foundation/BIO-DEV-002.test.sh` (32 checks) — offline-safe structural wiring, wired into `scripts/quality.sh` in the same commit it was added.

Reproduce with:
```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
bash scripts/check-dotnet-vulnerabilities.sh api/Biolog.Api/Biolog.Api.csproj tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
bash tests/foundation/BIO-DEV-002.test.sh
bash scripts/secret-scan.sh --all
bash tests/foundation/BIO-FND-008.test.sh
```
(the exact `test_command` in `metadata.json`, chained with `&&`, exits 0.)

**Sandbox note**: every `dotnet` command above must run with the sandbox disabled in this
agent environment — `dotnet restore`/`dotnet test` hang indefinitely under the default
sandbox network restriction (unrelated to this story; see `docs/TOOLCHAIN-POLICY.md`'s
corrected entry). Not required for the bash-only `tests/foundation/*.test.sh`,
`scripts/secret-scan.sh`, or `scripts/check-module-project-references.sh`.

## Two real bugs found and fixed in my own test code while verifying (not shipped defects)

1. **A genuine hang, not a sandbox/SDK issue, in my own concurrency regression test.**
   `DeviceCertificationService.RegisterAsync` is itself audited (unlike
   `Features.DeviceRegistry.DeviceService.RegisterAsync`, which is not — only its
   `ChangeStatusAsync` is). An earlier draft of
   `UpdateGlobalAsync_genuinely_blocks_a_second_writer_until_the_first_releases_the_lock`
   constructed the service under test with a `GatedCertificationAuditSink` from the start
   and used that SAME service to also perform the setup registration — which then blocked
   forever waiting on `Release()`, a call that only happens later in the test, after the
   registration it was blocking would have needed to already return. This produced a
   genuine indefinite hang that looked exactly like the sandbox/SDK saga documented below,
   and very likely explains at least one of the "stuck build" observations along the way.
   Fixed by registering through a separate "setup" service (a normal recording sink)
   sharing the same repository and lock registry, then constructing the service under test
   with the special sink only for the update call actually being exercised. Four other
   tests in the same file had the identical latent defect (a failing sink also breaking the
   setup registration, just failing fast instead of hanging) — fixed the same way.
2. **A routing-proof assertion that couldn't actually prove what it claimed.**
   `device_certification_endpoints_are_routed_under_the_versioned_api_path` asserted
   "not a 404" against an unknown-combination lookup — but `GetEffectiveAsync` legitimately
   returns 404 for that case, indistinguishable over HTTP from an unmatched route also
   404ing. Fixed by registering an entry first and asserting a genuine 200, which can only
   happen if the exact path was both routed and executed.

## A pre-existing, unrelated flaky test — found, not fixed (out of this story's scope)

`Biolog.IntegrationTests.ObservabilityTests` (BIO-FND-009) redirects the process-wide
`Console.Out` to a `StringWriter` to capture console-formatted log output, then calls
`.ToString()` on it. This intermittently throws
`ArgumentOutOfRangeException ('chunkLength')` from inside `StringBuilder.ToString()` when
run as part of the full suite (not in isolation) — the signature of a second thread writing
to the same non-thread-safe `StringBuilder` concurrently, i.e. another test class's own
`WebApplicationFactory`-backed host logging in the same redirected window. Confirmed
independent of this story's changes: the same test file passes reliably filtered to itself
alone, and passes on repeated full-suite reruns — it is genuinely non-deterministic, not
something this story's code broke. `DeviceCertificationTests` adds one more
`WebApplicationFactory`-backed test class to the suite, which plausibly makes an
already-existing race marginally easier to hit, but does not introduce it. Reported
separately to the floor (BIO-FND-009's own test-isolation gap); this story's own
`test_command` above uses only the projects/harnesses BIO-DEV-002 actually touches, so it
does not depend on `ObservabilityTests` at all.

## Scope boundaries, documented rather than silently assumed

1. **No canonical identifier minted for a certification record (ADR-0001 §1.2).** Exactly
   six identifier types are registered by that ADR; adding a seventh needs its own ADR
   amendment and Kemi's approval, out of this story's scope. The natural
   vendor+model+firmware+sdk composite key is instead treated as a "business identifier"
   (ADR-0001 §1.6), with `DeviceCertificationKeyValidator` closing the one real correctness
   risk that shape introduces (composite-key collision via an embedded separator).
2. **No real authentication/authorisation behind "visible to admins" (AC02).** No auth
   story has shipped a verified session yet — the same gap `Features/DeviceRegistry`'s
   self-reported `ActorType`/`ActorId` already carries for actor identity generally. Both
   listing endpoints are open reads today; a future auth story gates them without changing
   this story's shape.
3. **The crash/ack residual (ADR-0003 Decision 7, element 5) is inert, for the same
   reason it is inert for BIO-DEV-001**: `ICertificationAuditSink`/`IDeviceCertificationRepository`
   are both `AddSingleton`, same-process, same-lifetime, volatile in-memory. Reopens the
   moment either side becomes durable, remote, transactional, or multi-process — named
   explicitly in `docs/DEVICE-CERTIFICATION.md` so a future story that changes either side
   knows the inertness argument doesn't travel with it.
4. **Unlike BIO-DEV-001, no tracked tenant-resolution upgrade obligation (ADR-0002
   Decision 6).** BIO-TEN-002's host channel had already shipped when this story started,
   so AC03's tenant-override evidence is real, upgraded evidence from day one — there is no
   interim accommodation to record a follow-up against.

Full suite green (sandbox disabled for every `dotnet` invocation): `dotnet build` 0
warnings/0 errors, unit 200/200, integration 96/96 (`DeviceCertificationTests` all pass;
the one pre-existing `ObservabilityTests` flake described above is unrelated and
independently confirmed non-deterministic), structural 32/32, vulnerability audit clean,
`secret-scan.sh --all` clean, `tests/foundation/BIO-DEV-001.test.sh` 47/47 and
`tests/foundation/BIO-FND-008.test.sh` 39/39 (both re-run to confirm no regression from
this story's changes).
