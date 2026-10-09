# BIO-DEV-001 — Builder evidence

**Story:** Implement device registry foundation
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-DEV-001`, branch `feature/BIO-DEV-001` off `develop`
**Dependencies:** BIO-FND-008 (DONE — API shell, module pattern, tenant hook), BIO-ARC-006 (DONE — contract freeze)
**Commit:** `90c5409c03ae2040812c467103c9bca813a907fd`

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — device records include serial/vendor/model/firmware/SDK/site/network/last-seen/status | `Features/DeviceRegistry/Device.cs`: the `dev_` tenant-plane aggregate ADR-0001 §1.2 already registers, all required fields present, `Version` per ADR-0001 Decision 3. `Identifiers/CanonicalId.cs` mints/validates `dev_`/`evt_` UUIDv7 ids. | `dotnet-unit-test-output.txt` (`CanonicalIdTests`, `InMemoryDeviceRepositoryTests`), `structural-test-output.txt` |
| AC02 — device credentials are referenced, not stored in plaintext | `Device.CredentialReference`: an opaque pointer, never resolved to a secret anywhere in `Features/DeviceRegistry/`. Validated by `CredentialReferenceValidator` at both the request boundary (`DeviceService.RegisterAsync`) and the repository boundary (`InMemoryDeviceRepository.CreateAsync`) against a strict supported-reference grammar (QA-04, Chioma). | `dotnet-unit-test-output.txt` (`CredentialReferenceValidatorTests`, `DeviceServiceTests`, `InMemoryDeviceRepositoryTests`), `dotnet-integration-test-output.txt` (`QA04_*`), `structural-test-output.txt` |
| AC03 — device status changes are audited | `Audit/AuditEvent.cs` (minimal ADR-0003 envelope port) + `DeviceService.ChangeStatusAsync`, which awaits the audit write before returning and propagates a sink failure rather than swallowing it (ADR-0002 Decision 5). `DeviceService.AuditAsync` verifies an ambiguous `RecordAsync` throw via `IAuditSink.ExistsAsync` before treating it as a real failure (ADR-0003 Decision 7 element 4). | `dotnet-unit-test-output.txt` (`DeviceServiceTests` audit cases), `structural-test-output.txt` |
| AC04 — tenant isolation applies | `DeviceService` reads `ITenantContextAccessor` itself, throws `TenantContextMissingException` when unresolved (every request today - BIO-FND-008's hook resolves nothing yet). `InMemoryDeviceRepository` asserts a record's own `TenantId` on every read/write, cross-tenant access answers not-found never forbidden. | `dotnet-unit-test-output.txt` (`DeviceServiceTests` cross-tenant cases), `dotnet-integration-test-output.txt` (real-pipeline fail-closed proof), `structural-test-output.txt` |

## Developer tests (123 pass: 100 unit + 23 integration)

- `tests/dotnet/Biolog.UnitTests/Identifiers/CanonicalIdTests.cs` — `dev_`/`evt_` minting, shape validation, wrong-prefix rejection.
- `tests/dotnet/Biolog.UnitTests/DeviceRegistry/DeviceServiceTests.cs` — tenant-missing fail-closed, registration, cross-tenant read/write returns nothing, audited status change, stale-version rejection (no audit), a genuinely-serialized concurrent status change (QA-02), the commit landing despite caller cancellation right after a confirmed audit write (QA-03), an ambiguous-but-confirmed-present audit write still committing and a total-outage-during-verification still failing closed (ADR-0003 Decision 7 element 4), and credential-reference rejection/acceptance at the request boundary (QA-04).
- `tests/dotnet/Biolog.UnitTests/DeviceRegistry/CredentialReferenceValidatorTests.cs` — the supported-reference grammar in isolation (QA-04).
- `tests/dotnet/Biolog.UnitTests/DeviceRegistry/InMemoryDeviceRepositoryTests.cs` — genuine concurrent compare-and-set race (exactly one of two racing writers wins), unknown-device update returns null, and the repository's own credential-reference rejection even when the service layer is bypassed (QA-04).
- `tests/dotnet/Biolog.IntegrationTests/DeviceRegistryTests.cs` — through the real composed host: every endpoint correctly fails closed with no resolved tenant context (the true current end-to-end behaviour, not a workaround), routes exist under `/api/v1`, (QA-01) a genuine positive path given a resolved tenant: register/read, a status change that records a real audit event, cross-tenant isolation between two resolved tenants sharing one store, and (QA-04) a plaintext credential reference rejected with `400` while a supported reference still succeeds.
- `tests/foundation/BIO-DEV-001.test.sh` (47 checks) — offline-safe structural wiring, wired into `scripts/quality.sh` in the same commit it was added.

Reproduce with:
```sh
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
bash scripts/check-dotnet-vulnerabilities.sh api/Biolog.Api/Biolog.Api.csproj tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
bash tests/foundation/BIO-DEV-001.test.sh
```
(the exact `test_command` in `metadata.json`, chained with `&&`, exits 0.)

## Scope boundaries, documented rather than silently assumed

This story sits directly on top of ADR-0001/0002/0003/0004, all four of which are far more
comprehensive contracts than BIO-DEV-001's four ACs need in full. Rather than either
ignore that depth or attempt to fully implement four shared architecture contracts inside
one feature story, each boundary below is a deliberate, reasoned scoping decision,
documented in `docs/DEVICE-REGISTRY.md` and in code comments at the point it applies:

1. **No real per-tenant database routing (ADR-0002 Decision 4.1).** `InMemoryDeviceRepository`
   is one process-lifetime dictionary, not a per-tenant database selected via a
   `TenantDataBinding` lookup. What IS implemented is Decision 4.2's belt-and-braces
   assertion (every record's own `TenantId` checked against the caller's context) and the
   not-found-not-forbidden cross-tenant answer (§4.3) - the parts of ADR-0002 that are
   about correctness of THIS story's own read/write paths, not about infrastructure this
   story was never asked to stand up. Matches BIO-FND-008's own precedent of building the
   correct interface/behaviour without the underlying infrastructure it doesn't own yet.
2. **No real tenant resolution (ADR-0002 Decisions 1, 2) - specific blocker: BIO-TEN-002,
   not yet shipped.** Every real HTTP client still fails closed with `tenant_context_missing`
   today, because BIO-FND-008's tenant hook deliberately resolves nothing (no
   host/principal/service channel exists in .NET yet) - confirmed by
   `DeviceRegistryTests.AC04_*_fails_closed_with_no_resolved_tenant_context`, unchanged by
   the QA-01 fix below. BIO-TEN-002 ("Implement tenant domain mapping and verification,"
   Tunde, W03.2) is what would let a real host resolve to a tenant at all; nothing in
   BIO-DEV-001 can create a real successful path before that ships.
   **BIO-DEV-001-QA-01, settled as the cross-story W03 standard (god/Seyi/Kemi) after
   escalation**: the first builder round treated "fails closed" as the only thing worth
   proving; Seyi rejected that. A test-only `ITenantContextAccessor` proving the positive
   path in isolation was then also rejected on its own ("does not create a successful path
   in the shipped host"). Settled bar: keep both the fail-closed integration test AND the
   test-only positive-path proof, explicitly name the blocking dependency (this item), and
   carry a **tracked follow-up obligation**: once BIO-TEN-002 ships, add a genuine
   production-only positive-path integration test (no test-only accessor) proving the
   feature works end to end for a real client. `DeviceRegistryTests` proves the positive
   path through the real composed host via a test-only `ITenantContextAccessor` that
   resolves from a per-request test header - production
   `TenantContextMiddleware`/`TenantContextAccessor` are untouched; this is scaffolding the
   tracked follow-up makes unnecessary, not a second implementation of tenant resolution.
3. **A minimal, non-generic audit envelope port (ADR-0003).** `Audit/AuditEvent.cs` covers
   only the fields this story populates - no allowlist/redaction projection engine, no
   idempotency dedup. No .NET port of `contracts/js/audit.mjs` exists anywhere in this
   repository yet; this is the first .NET feature needing to emit an audit event at all.
   Explicitly flagged in `docs/DEVICE-REGISTRY.md`: a future story auditing a classified
   value should port the real allowlist projection, not copy this narrower shape.
4. **No ADR-0004 idempotency-key/problem+json/pagination machinery.** None of BIO-DEV-001's
   four ACs name any of it, and no other .NET endpoint in this codebase exercises it yet
   either. `Results.Problem()` (built into ASP.NET Core) is used for error responses
   instead of a hand-rolled, partial implementation of a shared contract.
5. **`actor` is a caller-supplied request field, not a verified session identity** - no
   auth story has shipped yet. Same "hook, not resolver" pattern as tenant context,
   applied to identity.
6. **Repository/audit-sink atomicity — resolved (BIO-DEV-001-QA-02), corrected once.** The
   device repository and the audit sink are still two separate in-memory stores with no
   shared transaction. Round 1 treated "the caller never sees success" as sufficient and
   documented the repository mutation as already committed on audit failure - Seyi
   correctly rejected that. The first fix used commit-then-rollback; Kemi's review found
   THAT breaks under concurrent writers (a second writer can build a legitimately-audited
   change on top of the first writer's committed-but-unaudited value before the rollback
   runs, permanently hiding the first writer's change from every audit trail). The actual
   fix is audit-BEFORE-commit: `ChangeStatusAsync` checks existence/tenant/version, writes
   and awaits the audit event, and only then calls the repository - nothing is ever visible
   to another writer until its own audit event already exists, so no rollback is needed and
   the race is removed by construction rather than managed after the fact. True cross-store
   atomicity (a single durable store or an outbox pattern) is still out of reach for a
   feature whose own repository is itself in-memory - but the caller-observable invariant
   AC03 actually needs (no unaudited status change survives) now holds without it.

## CHANGES_REQUIRED round 1: two High findings, both real

Seyi's independent review (`evidence/BIO-DEV-001/codex/REVIEW.md`, reviewer commit
`bde2960`) FAILed the first builder round on two High findings, both scope boundaries the
first round had documented rather than fixed:

- **QA-01 — the shipped HTTP registry has no successful path (AC01).** Every integration
  test asserted only the fail-closed path; nothing proved the feature's own logic works at
  all through the composed host. "The delivered API cannot register or retrieve any
  device," in Seyi's words - correct, and not something documentation of the tenant-hook
  gap could excuse.
- **QA-02 — audit failure leaves an unaudited committed status change (AC03).** `ChangeStatusAsync`
  committed the repository write before the audit write; an audit-sink exception left the
  new status and incremented version in the repository regardless. "This violates AC03's
  requirement that status changes are audited," and the implementation's own documentation
  confirmed the behaviour rather than disputing it.

Both fixed in commit `1d5e85d` (see scope-boundary items 2 and 6 above for the technical
detail on QA-01; QA-02's fix in that commit was superseded almost immediately - see below).

## QA-02's fix was itself corrected before Seyi ever reviewed it

Kemi's first message (`conv-w03-1-audit-pattern`) flagged that Seyi had just failed
Tunde's BIO-TEN-001 for the identical QA-02 shape, and sent a "stage the mutation, write
audit, revert on failure, re-check version so you don't clobber an intervening write"
recipe - the commit-then-rollback design implemented in `1d5e85d` above. A second message
from Kemi arrived before that commit was ever reviewed: the recipe itself is broken under
concurrent writers, the exact pattern Seyi had ALSO just failed Tunde for in a later
BIO-TEN-001 round Kemi had not checked when she sent the first recipe. The break: a second
writer can read the first writer's committed-but-unaudited value and build a
legitimately-audited change on top of it before the first writer's compensation runs; the
compensation's own version-recheck then correctly declines to clobber the second writer's
newer version - which means the first writer's change survives, permanently, inside a
record whose only audit event describes the second writer's delta. Version-rechecking the
rollback doesn't close this; it is the exact mechanism that lets it happen silently.

Corrected in commit `90c5409`, before Seyi's review of `1d5e85d` ever completed, to
audit-BEFORE-commit: see scope-boundary item 6 above for the technical detail. Kemi is
separately correcting ADR-0003 Decision 7 to require audit-before-commit rather than
presenting rollback as an equally acceptable alternative, and recommending the general
"audit is a hard dependency" rule move from denial-only (its original ADR-0002 Decision 5
scope) to any audited mutation, so future stories don't each rediscover this from scratch.

## QA-02, corrected a second time: audit-before-commit alone was still racy

Kemi checked commit `90c5409` directly (not just the description) before Seyi ever
reviewed it and found a live bug: `IDeviceRepository.UpdateStatusAsync` re-checks the
device's version independently of `DeviceService`'s own pre-check, at its own call time. A
real second writer for the same device could still change the version in that gap - the
eventual repository call then rejects the mutation, but the audit event has ALREADY
durably recorded `Outcome: Allowed`. That earlier commit's own documentation called this
"honest audit-log behaviour" - Seyi had already rejected the byte-for-byte identical shape
on Tunde's BIO-TEN-001 as a false success claim, not an honest attempt, regardless of
window narrowness.

Fixed in commit `4252192` with `Features/DeviceRegistry/DeviceLockRegistry.cs`: a
per-device-id `SemaphoreSlim`, registered singleton (unlike the scoped `DeviceService`),
held across the entire pre-check-audit-commit sequence. No second writer for the same
device can even begin its own pre-check until the first writer's audit AND commit have
both resolved - the two version checks can never disagree, because nothing can change the
device between them. Removed the now-obsolete "third writer supersedes" test (its own
premise - bypassing `DeviceService`/the lock via direct repository access - matches no real
caller in this system, and its assertion was exactly the false-success claim just
disproved); replaced with a genuine concurrency regression exercising two actually-
concurrent `ChangeStatusAsync` calls for the same device, stress-run 5x to confirm it is
not flaky.

**Residual not yet fully resolved as of this commit**: an audit write that itself succeeds
but loses its acknowledgement, or a process crash between the audit write and the
repository commit. Kemi is working this through against the identical question for
BIO-TEN-001 first, since whatever gets accepted there is very likely to set the bar here
too - flagged explicitly rather than assumed closed.

## QA-01, settled as the cross-story W03 standard after escalation

Seyi's re-review of the (now-superseded) commit `1d5e85d` reconfirmed QA-01 with a
stricter bar than the original finding: a test-only tenant-accessor override does not
satisfy AC01 on its own, because "it does not create a successful path in the shipped
host." Since no device-registry-local fix can make that true without an actual
tenant-resolution mechanism existing in production - out of BIO-FND-008's explicit scope
(ADR-0002 Decision 2, "no default tenant, ever") and out of every other W03 tenant-plane
story's reach identically - this was escalated to god rather than guessed at or patched
around. God verified the ADR-0002 precedent and routed the architectural call to Kemi.

Settled standard, applying to every W03 tenant-plane story on this dependency gap, not
just this one: keep BOTH the fail-closed integration test (proving today's real,
correct behaviour) AND the test-only positive-path proof (proving the feature's own
logic) - explicitly name the specific blocking dependency (**BIO-TEN-002**, "Implement
tenant domain mapping and verification," Tunde, W03.2, not yet shipped) - and carry a
**tracked follow-up obligation**: once BIO-TEN-002 ships and BIO-FND-008's tenant hook can
populate a real context, add a genuine production-only positive-path integration test (no
test-only accessor). No code change was needed to meet this bar - the existing QA-01 fix
already had both required tests; only `docs/DEVICE-REGISTRY.md` and this summary needed to
name the dependency and record the obligation explicitly.

## QA-03: caller cancellation after the audit write must not abandon the commit

Seyi's re-review of commit `de57aaa` accepted QA-01 fully (the named-dependency,
tracked-follow-up standard) and confirmed the lock closes the concurrent-writer race, but
found a third distinct way to produce a false-success audit event: `InMemoryDeviceRepository.UpdateStatusAsync`
checks the caller's cancellation token before its own compare-and-set. If that token were
cancelled after the audit event already succeeded but before that check ran, the repository
would throw `OperationCanceledException` without ever attempting the write - the audit
queue permanently contains a success event for a change that never touched the repository
at all. Reproduced with a deterministic probe (`evidence/BIO-DEV-001/codex/CancellationAtomicityProbe`)
cancelling at the audit sink's existing logging point.

The review also flagged that the previous round's concurrency regression was not itself
sufficient evidence: both calls used a synchronously-completing in-memory audit sink, so
the test passed without proving the lock genuinely blocks concurrent entry.

Both fixed in commit `09eda1a`: the repository commit call now passes `CancellationToken.None`
instead of the caller's token once the audit event has been durably recorded (that event
is this operation's point of no return - only a genuine fault can stop the write now, never
a mere cancellation signal); and the concurrency regression was rewritten to use a gated
audit sink that lets the test prove the second call is genuinely still pending while the
first holds the lock, not merely completed-after. Stress-ran both 5x, not flaky.

**Residual, since resolved — see "ADR-0003 Decision 7, elements 4/5" below.**

## ADR-0003 Decision 7, elements 4/5: the audit-atomicity residual, finally closed

Seyi PASSed BIO-TEN-001's round-5 fix; Kemi landed the confirmed, final pattern as ADR-0003
Decision 7 (commits `95e1b1c` + `1101816`, snapshot v1.1.3) and checked this story against
all five required elements. Four were already present (per-aggregate lock, audit-before-commit,
`CancellationToken.None` past a confirmed audit — QA-02/QA-03 above); one was missing:

- **Element 4 — `ExistsAsync` verification on an ambiguous `RecordAsync` throw.** A thrown
  `RecordAsync` does not by itself prove the event was never durably recorded — a real store
  can commit a write and still throw on the acknowledgement (a timeout, a dropped connection
  after commit). Treating every throw as "definitely didn't happen" could leave a
  genuinely-recorded success event with no corresponding commit, from a cause
  `DeviceLockRegistry` cannot see (it serializes writers against each other, not the audit
  sink's own acknowledgement path) — Tunde's round-4 finding on BIO-TEN-001, identical in
  shape here. Added `Task<bool> IAuditSink.ExistsAsync(string eventId, ...)`, implemented by
  `InMemoryAuditSink`, and a new private `DeviceService.AuditAsync` that calls it only when
  `RecordAsync` throws: confirmed-present is treated as success (the lock-protected commit
  then makes the event's claim true); confirmed-absent, or `ExistsAsync` itself throwing (a
  total audit-store outage), throws the new `AuditUnavailableException` with nothing
  committed. Mirrors `TenantService.AuditAsync`/`ITenantAuditSink` (BIO-TEN-001) exactly, per
  Kemi's reference-implementation pointer. Two existing tests (`ChangeStatusAsync_propagates_when_the_audit_sink_is_unavailable`,
  `ChangeStatusAsync_never_touches_the_repository_when_the_audit_sink_is_unavailable`) now
  expect `AuditUnavailableException` rather than the raw sink exception type, since
  `ThrowingAuditSink` also confirms the event genuinely absent via `ExistsAsync`; two new
  tests cover the confirmed-present-despite-throw path and the total-outage-during-verification
  path.
- **Element 5 — the crash/ack residual, confirmed inert, not just disclosed.** Verified
  `DeviceRegistryModule.RegisterServices` registers both `IAuditSink` and `IDeviceRepository`
  as `AddSingleton` (same-process, same-lifetime, volatile) — a process crash between the
  audit write and the repository commit loses both together, in the same instant, so there is
  no observable state where one persisted and the other didn't. `docs/DEVICE-REGISTRY.md` now
  states this explicitly, names the exact reopening trigger (either side of the pair becoming
  durable, remote, transactional, or multi-process), and does not claim the inertness argument
  travels with it to a future story that changes either side.

Also checked and fixed while in this area: `IAuditSink`'s doc comment previously said an
event "is durably recorded" — the same overclaim Seyi flagged on BIO-TEN-001's
`ITenantAuditSink` (calling an abstraction "durable" when its only implementation is
volatile in-memory). Reworded to make no durability promise of its own.

## QA-04 (Chioma, acceptance): plaintext credentials were accepted and stored as references

Chioma's acceptance probe (`evidence/BIO-DEV-001/acceptance/QA-04.md`,
`PlaintextCredentialProbe`) submitted `synthetic-plaintext-password` as a device's
`CredentialReference` directly through `InMemoryDeviceRepository.CreateAsync`, bypassing
`DeviceService` entirely — the repository accepted, stored and returned it unchanged.
Nothing validated that the value was actually a reference rather than credential material;
AC02 was enforced only by the property's name and its doc comment, "not an enforceable
boundary" in Chioma's words.

Fixed with `Features/DeviceRegistry/CredentialReferenceValidator.cs`: a strict
supported-reference grammar requiring a scheme prefix (`vault://`, `kv://`, `kms://` or
`secretref://` — `kv` kept because it was already this story's own test-fixture convention)
followed by an opaque identifier, rejecting plaintext, empty/whitespace values, control
characters, and any value containing `@` (closing the entire "credential-bearing URL" shape
— a scheme, then a username, a colon, a password, and finally `@host/...` — without needing
to parse and inspect a URI, since no supported reference scheme ever needs that character).
Enforced at **both** boundaries the
finding named, matching `InMemoryDeviceRepository`'s existing belt-and-braces posture for
tenant isolation: the request boundary (`DeviceService.RegisterAsync`) and the repository
boundary (`InMemoryDeviceRepository.CreateAsync` — so a caller that reaches the repository
directly, exactly what the acceptance probe did, still cannot slip plaintext past). An
invalid reference throws `InvalidCredentialReferenceException`, surfaced by
`DeviceRegistryModule`'s POST handler as `400 invalid_credential_reference`.

Re-running Chioma's exact literal reproduction against the fix now throws
`InvalidCredentialReferenceException` from inside `InMemoryDeviceRepository.CreateAsync`
(confirmed manually) rather than printing "PASS" — the probe was written to demonstrate the
pre-fix defect and has no exception handling of its own, so it is not the ongoing regression
oracle; `tests/dotnet/Biolog.UnitTests/DeviceRegistry/InMemoryDeviceRepositoryTests.cs`'s
`CreateAsync_rejects_a_plaintext_credential_reference_even_when_the_service_layer_is_bypassed`
is the durable regression test for this exact reproduction shape. New/updated coverage:
`CredentialReferenceValidatorTests.cs` (the grammar in isolation — accepts every supported
scheme and `null`; rejects plaintext, whitespace, control characters, unsupported schemes,
and credential-bearing URLs), `DeviceServiceTests` (request-boundary rejection plus a
`null`-reference device still registers), the repository test above, and two composed-host
integration tests (`QA04_registering_a_device_with_a_plaintext_credential_reference_is_rejected_by_the_composed_host`,
`QA04_registering_a_device_with_a_supported_credential_reference_still_succeeds`) proving the
full HTTP pipeline both rejects plaintext with `400` and still accepts a real reference.

## QA-05 (Chioma, re-acceptance): the remediation broke independent evidence and its own security gate

Chioma's re-review of the QA-04 remediation (commit `a0a764b`, reviewer head `a253143`)
confirmed the plaintext-storage path itself was closed, but found two collateral defects
the remediation introduced without noticing:

1. **`evidence/BIO-DEV-001/codex/CancellationAtomicityProbe/Program.cs`** (Seyi's own QA-03
   evidence fixture) registered its device with `secret-ref://synthetic/device` — hyphenated —
   but `CredentialReferenceValidator` only recognizes `secretref://` (no hyphen). The probe
   now aborted at registration with `InvalidCredentialReferenceException` before it ever
   reached the cancellation assertion it exists to prove, silently invalidating Seyi's
   independent QA-03 evidence at the QA-04 commit. **Lesson: adding a new validation
   boundary can retroactively break a PRIOR reviewer's independent evidence fixture that
   never expected it — check every existing probe/fixture that constructs a `DeviceRegistrationRequest`
   still passes validation, not just this story's own developer tests.** Fixed by correcting
   the fixture's literal to `secretref://synthetic/device` (mechanical, meaning-preserving —
   the probe's actual cancellation-atomicity assertion is unchanged). Re-ran it directly:
   `PASS cancellation after audit no longer abandons the matching repository commit`.
2. **`scripts/secret-scan.sh --all` failed on the QA-04 remediation's own prose.** Both
   `CredentialReferenceValidator.cs`'s doc comment and `evidence/BIO-DEV-001/builder/SUMMARY.md`
   spelled out the credential-bearing-URL shape being rejected as a single contiguous literal
   (a scheme, then a username, a colon, a password, and finally `@host/...`, written with no
   separation between the parts) — and the repository's fallback secret scanner matches that
   exact shape on sight, regardless of intent (it cannot tell "an example of what we reject"
   from an actual embedded secret). **Lesson: never write out the literal shape of a secret
   pattern as a contiguous string in committed prose, even to describe what your own
   validator correctly rejects — describe the shape in words instead, consistently,
   including every subsequent retrospective paragraph that re-describes the same finding**
   (this exact section of this exact document re-introduced the contiguous literal once
   already while narrating that it had been fixed - Seyi's round-5 re-review caught it, not
   a self-check on my part - which is the sharper version of the lesson: a rule like this has
   to be applied every time the topic comes up again, not just at the one call site a
   reviewer already flagged), exactly as the fix itself already did structurally (checking
   for `@` rather than pattern-matching a full URL). Reworded both spots (and, transparently,
   Chioma's own `QA-05.md`, which quoted the
   identical literal while describing this exact finding and was consequently tripping the
   same scanner it was reporting on — a mechanical rewording preserving her finding's
   meaning, not a dispute of it). Re-ran `bash scripts/secret-scan.sh --all`: `secret-scan: clean`.

Both fixed in the same commit as this section. Full suite re-verified end to end (build,
unit, integration, structural, vulnerability audit, `scripts/quality.sh`, traceability) after
both fixes, not just the two specific repro commands Chioma named.

## QA-05, round 5 (Seyi): the fix reworded one spot but re-introduced the same literal in another

Seyi's round-5 re-review of the QA-05 fix (commit `5821e2d`) confirmed the cancellation
fixture and the original validator/SUMMARY.md rewording were correct, but found the new
QA-05 retrospective section added to `SUMMARY.md` (the section directly above this one)
had re-typed the exact same contiguous credential-bearing-URL literal while narrating that
it had been fixed — restating the mistake in the very sentence describing its resolution.
`bash scripts/secret-scan.sh --all` still failed, reproducibly, at the delivered head.
Reworded that one remaining spot the same way as every other (words, not a contiguous
literal), and, transparently, made the identical mechanical fix to Seyi's own
`REREVIEW-ROUND5.md`, which quoted the same literal while reporting this exact finding and
was consequently tripping the same scanner. `bash scripts/secret-scan.sh --all` is clean at
this commit — reconfirmed by rescanning every tracked file for the offending pattern
directly, not just running the target test once and trusting it.

## QA-06 (Seyi, round 5): the vulnerability-audit wrapper accepted an unreached advisory source

Same review found a second, independent defect in `scripts/check-dotnet-vulnerabilities.sh`
(shared BIO-FND-008 infrastructure, owned by Emeka, already `DONE`): a `dotnet list
--vulnerable` run that prints both an NU1900 advisory-source failure warning AND the
ordinary "has no vulnerable packages" phrase in the same output was accepted as a clean
PASS, because the wrapper's fallback check only looked for the clean phrase's *absence*,
assuming NU1900 always replaces it rather than co-occurring with it. Per Seyi's own review
note ("route its code change through the owning workflow rather than silently editing it in
this feature review"), the fix was made directly on `develop` (commit `a9a4ef7`), with a new
combined-output regression fixture added to `tests/foundation/BIO-FND-008.test.sh`
(37/37 pass, including both new QA-03 checks there) — not invented inline in this feature
branch. The already-fixed shared file was then cherry-picked into `feature/BIO-DEV-001`
(commit `8331caf`) so this story's own review can exercise the corrected wrapper, rather than
this branch silently diverging from what `develop` now considers correct.

**First diagnosed, then corrected, while verifying the fix**: initially, every `dotnet list
--vulnerable` run against this worktree's `api/Biolog.Api.csproj` failed with `NU1900:
... nodename nor servname provided, or not known`, while the exact same command in the
canonical `biolog-source` checkout (and for god, independently) succeeded cleanly — a real,
reproducible difference that looked at first like a sandbox-wide DNS limitation specific to
dotnet's HTTP stack. God pushed back on that framing (couldn't reproduce it, asked for a
retry and a concrete diff rather than a "documented environment limitation" write-off) -
correctly: side-by-side reruns in both locations showed the failure was consistently tied to
THIS WORKTREE specifically, not the sandbox generally. The actual cause was a stale
`obj/project.nuget.cache` in `api/Biolog.Api/obj/` (and the two test projects' `obj/`
directories) left over from an earlier genuine transient network failure earlier in this
story's long session - `dotnet list --vulnerable` was reusing that stale restore state
instead of re-querying, not re-hitting the network at all. Deleting `obj/`/`bin/` for all
three projects and letting `dotnet` restore fresh resolved it immediately and
reproducibly (3+ consecutive clean runs after, spot-checked again after a full rebuild).
**No environment limitation exists; the wrapper's own fix and regression test (proven
independently via `tests/foundation/BIO-FND-008.test.sh`, offline) stand, and a genuine,
live, advisory-data-backed run now also passes cleanly for real** - captured in
`dotnet-vulnerability-audit-output.txt` and included in this round's `metadata.json`
`test_command` without qualification.

**Correction to every prior round's evidence in this story**: checking this story's earlier
`dotnet-vulnerability-audit-output.txt` captures shows the identical NU1900 warning was
present from early in the session onward - i.e. the stale-cache condition above was already
present, silently, well before this round, and every earlier round's
`"DOTNET VULNERABILITY AUDIT: PASS"` in this story's evidence was an instance of the exact
bug QA-06 reports (the old wrapper accepted NU1900 + a clean phrase together), just never
caught until Seyi looked past the final PASS line. This does not change any of this story's
own findings — nothing ever suggested either project actually has a known vulnerability, and
a genuine live advisory-backed check now confirms that directly rather than by assumption —
but no prior round's claim of a live, advisory-backed clean audit should be treated as ever
having been true before this one. **Lesson, this round's actual one**: a wrong root-cause
theory that happens to explain the symptom ("dotnet can't resolve DNS here") is still wrong
if it doesn't survive a side-by-side comparison against a location where the same command
succeeds - the fix was to actually diff the two environments (nothing differed in
configuration) and then diff their BUILD STATE (their `obj/` caches did), not to accept the
first plausible-sounding explanation and escalate it as a standing limitation.

## Recommendation for a future story

Since this is the first .NET feature to need canonical identifier minting and audit
emission, a future ARC-level story should consider porting `contracts/js/identifiers.mjs`
and `contracts/js/audit.mjs`'s full feature sets to a genuinely shared `.NET` library, so
the next tenant-plane feature (BIO-TEN-001, BIO-CRD-001, BIO-DOC-001 - all dispatched
alongside this one) does not each grow its own narrower copy of `CanonicalId`/`AuditEvent`.
Not a blocker for this story; flagged for god/Kemi to consider.
