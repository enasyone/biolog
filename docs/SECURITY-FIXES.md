# Security fixes

This is the human-readable ledger of security-relevant findings discovered during independent review. It records both product vulnerabilities and defects in controls that are meant to prove security. A finding is closed only when the cited re-review passes; disclosed boundaries remain explicit rather than being presented as fixes.

For the related architecture rules, see [API-SHELL.md](API-SHELL.md) and [SHARED-CONTRACT-FREEZE.md](architecture/SHARED-CONTRACT-FREEZE.md). BIO-FND-009's `OBSERVABILITY.md` remains on its active feature branch until that story passes review.

## Status key

- **Closed**: the root cause was corrected and independently re-reviewed.
- **Open**: the issue remains blocking in the latest review.
- **Disclosed boundary**: the behavior remains reproducible but was explicitly ruled outside the story's contract; it is not described as fixed.

## 2026-09-20

### BIO-FND-001 — committed-secret scan filename bypass

- **Severity/status:** High — Closed.
- **Gap:** the fallback secret scanner skipped tracked files whose names contained whitespace, so a committed credential could evade both the scan and bootstrap validation.
- **Root cause:** `git ls-files` output was stored and split as whitespace-delimited shell words.
- **Fix:** **Principled/root-cause.** Paths are consumed as a NUL-delimited stream. Independent cases cover spaces, newlines, leading dashes, and Unicode filenames.
- **Evidence:** [`evidence/BIO-FND-001/codex/REVIEW.md`](../evidence/BIO-FND-001/codex/REVIEW.md), [`REREVIEW-ROUND-2.md`](../evidence/BIO-FND-001/codex/REREVIEW-ROUND-2.md).

### BIO-ARC-001 — invalid timestamps silently normalized

- **Severity/status:** High — Closed.
- **Gap:** impossible dates such as 30 February passed validation and were normalized into different instants, allowing a claimed audit or device time to change silently.
- **Root cause:** the clock contract checked numeric ranges but not real Gregorian month lengths and leap rules; code and schema boundaries also disagreed at year zero during remediation.
- **Fix:** **Principled/root-cause.** Real calendar validation and shared schema/code parity were enforced over the supported year range `1000..9999`, with an exhaustive date sweep.
- **Evidence:** [`evidence/BIO-ARC-001/codex/REVIEW.md`](../evidence/BIO-ARC-001/codex/REVIEW.md), [`REREVIEW-ROUND-2.md`](../evidence/BIO-ARC-001/codex/REREVIEW-ROUND-2.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-ARC-001/codex/REREVIEW-ROUND-3.md).

## 2026-09-21

### BIO-ARC-002 — forgeable tenant routing context

- **Severity/status:** Critical — Closed.
- **Gap:** a caller-built object could claim to be resolved and select an arbitrary database binding, bypassing control-plane tenant resolution.
- **Root cause:** trust was based on caller-visible object shape and a caller-supplied `binding_key`, with no unforgeable provenance.
- **Fix:** **Principled/root-cause.** A module-private provenance store now authorizes contexts and supplies the binding; copied, reconstructed, proxied, or forged objects fail closed.
- **Evidence:** [`evidence/BIO-ARC-002/codex/REVIEW.md`](../evidence/BIO-ARC-002/codex/REVIEW.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-ARC-002/codex/REREVIEW-ROUND-3.md), [`SECURITY-REVIEW-ZAINAB.md`](../evidence/BIO-ARC-002/codex/SECURITY-REVIEW-ZAINAB.md).

### BIO-ARC-002 — unaudited and prematurely returned denials

- **Severity/status:** High — Closed.
- **Gap:** denial paths could return normally with no audit sink, and later could return before a promise-based audit sink had durably settled.
- **Root cause:** audit was optional on required paths and asynchronous results were invoked without being awaited.
- **Fix:** **Principled/root-cause.** Every denial uses the required audit boundary; missing, rejected, or timed-out sinks produce `audit_unavailable`, and ordinary denial is observable only after successful audit settlement.
- **Evidence:** [`evidence/BIO-ARC-002/codex/REVIEW.md`](../evidence/BIO-ARC-002/codex/REVIEW.md), [`REREVIEW-ROUND-2.md`](../evidence/BIO-ARC-002/codex/REREVIEW-ROUND-2.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-ARC-002/codex/REREVIEW-ROUND-3.md).

### BIO-ARC-003 — redaction bypasses and mutable audit events

- **Severity/status:** High — Closed after four review rounds.
- **Gap:** shallow allowlisting copied nested unclassified data; unknown classifications were emitted; retained nested references let post-construction mutation alter audit content; required device/causation fields could be omitted. A later probe found that a flat key such as `profile.name` collided with the nested path `profile` → `name`, allowing a nested sensitive value to inherit the flat field's allowlist entry.
- **Root cause:** redaction trusted containers rather than classifying every leaf, retained source references, and represented caller-controlled path segments by ambiguous dot joining.
- **Fix:** **Principled/root-cause.** Recursive projection now fails closed for unknown fields, copies and deeply freezes approved output, aligns required schemas, escapes path segments injectively, and withholds both locations when a raw dotted key is structurally ambiguous.
- **Evidence:** [`evidence/BIO-ARC-003/codex/REVIEW.md`](../evidence/BIO-ARC-003/codex/REVIEW.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-ARC-003/codex/REREVIEW-ROUND-3.md), [`REREVIEW-ROUND-4.md`](../evidence/BIO-ARC-003/codex/REREVIEW-ROUND-4.md).

### BIO-ARC-004 — idempotency double execution

- **Severity/status:** High — Closed.
- **Gap:** two identical concurrent retries could both execute the underlying write before either result was stored.
- **Root cause:** idempotency used a non-atomic check, execute, then store sequence.
- **Fix:** **Principled/root-cause.** Atomic claims serialize a scope; identical requests replay one result, changed content conflicts, failed execution releases the claim, and waits are bounded.
- **Evidence:** [`evidence/BIO-ARC-004/codex/REVIEW.md`](../evidence/BIO-ARC-004/codex/REVIEW.md), [`REREVIEW-ROUND-2.md`](../evidence/BIO-ARC-004/codex/REREVIEW-ROUND-2.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-ARC-004/codex/REREVIEW-ROUND-3.md).

### BIO-ARC-004 — idempotency scope collisions and tenant aliasing

- **Severity/status:** High — Closed.
- **Gap:** colon-concatenated operation/key tuples collided across operation boundaries. Separately, raw malformed tenant values were accepted and `NaN` encoded like `null`, aliasing the deliberate control-plane scope.
- **Root cause:** scope components were serialized with a non-injective delimiter scheme and tenant scope lacked canonical/provenance validation.
- **Fix:** **Principled/root-cause.** Structured JSON tuple encoding preserves component boundaries; tenant scope is now either explicit control-plane `null` or a canonical `ten_` identifier, with malformed values rejected before storage or execution.
- **Evidence:** [`evidence/BIO-ARC-004/codex/REVIEW.md`](../evidence/BIO-ARC-004/codex/REVIEW.md), [`REREVIEW-ROUND-2.md`](../evidence/BIO-ARC-004/codex/REREVIEW-ROUND-2.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-ARC-004/codex/REREVIEW-ROUND-3.md).

### BIO-ARC-005 — migration sequence authority and deprecation matching

- **Severity/status:** High — Closed.
- **Gap:** callers could allocate the same sequence from stale snapshots, clone authorities by object identity, or create multiple caller-named registries. Breaking migrations could also cite an unrelated earlier deprecation window.
- **Root cause:** sequence allocation was not attached to one authoritative atomic registry, and destructive targets were not matched exactly to the deprecation contract they relied on.
- **Fix:** **Principled/root-cause.** Production exposes one non-caller-named migration authority with atomic gapless allocation; the test factory cannot alias it. Breaking targets must exactly match the referenced deprecation target.
- **Evidence:** [`evidence/BIO-ARC-005/codex/REVIEW.md`](../evidence/BIO-ARC-005/codex/REVIEW.md), [`REREVIEW-ROUND-4.md`](../evidence/BIO-ARC-005/codex/REREVIEW-ROUND-4.md), [`REREVIEW-ROUND-5.md`](../evidence/BIO-ARC-005/codex/REREVIEW-ROUND-5.md).

### BIO-ARC-005 — migration runner fidelity

- **Severity/status:** High — **Disclosed boundary; not fixed.** Non-blocking only under the explicit scope ruling in ADR-0005 Decision 4.6.
- **Gap:** a caller-supplied no-op `applyUp` can ignore the opaque migration body and still make fresh-create and upgrade comparisons converge.
- **Root cause:** the policy contract runs and compares caller-supplied execution functions; it does not own or prove the fidelity of a trusted migration execution engine.
- **Treatment:** **Scope boundary, not a patch.** Assignment structurally requires and runs both comparisons, but runner fidelity remains the responsibility of the future execution engine. This limitation remains reproducible and must not be treated as closed by code.
- **Evidence:** [`evidence/BIO-ARC-005/codex/REREVIEW-ROUND-4.md`](../evidence/BIO-ARC-005/codex/REREVIEW-ROUND-4.md), [`REREVIEW-ROUND-5.md`](../evidence/BIO-ARC-005/codex/REREVIEW-ROUND-5.md), [`ADR-0005-migration-ownership-and-sequencing.md`](adr/ADR-0005-migration-ownership-and-sequencing.md).

### BIO-ARC-006 — contract-freeze integrity bypass

- **Severity/status:** High — Closed.
- **Gap:** a fake/nonexistent baseline commit was accepted; frozen version 1 content could be changed and its manifest regenerated in place; future stories could modify the ownership registry, checker, or freeze policy that enforced their own restrictions.
- **Root cause:** validation trusted mutable working-tree metadata instead of an accepted Git tree, and the enforcement surfaces were omitted from protected read-only ownership.
- **Fix:** **Principled/root-cause.** The checker verifies the exact accepted ancestor commit and every frozen byte/hash against its tree. Existing versions cannot be re-blessed in place, and all future stories declare the registry, validator, policy, contracts, architecture decision records, and snapshots read-only.
- **Evidence:** [`evidence/BIO-ARC-006/codex/REVIEW.md`](../evidence/BIO-ARC-006/codex/REVIEW.md), [`REREVIEW-ROUND-2.md`](../evidence/BIO-ARC-006/codex/REREVIEW-ROUND-2.md).

### BIO-ARC-006 — tamper-test false success and destructive setup edge

- **Severity/status:** High — Closed.
- **Gap:** the tamper-rejection fixture could continue after clone or mutation setup failed; the expected checker failure was then negated into a passing test without testing a tampered snapshot. An earlier temporary-directory failure path could also direct writes or cleanup at an unintended location, risking the real frozen baseline.
- **Root cause:** destructive fixture setup was not fail-closed and the assertion accepted any validator failure rather than the expected baseline-mismatch diagnostic.
- **Fix:** **Principled/root-cause.** The fixture validates a non-empty temporary target, aborts on every clone/mutation/rewrite failure, and requires the exact rejection diagnostic. Forced setup failure must fail the harness and leave the real manifest unchanged.
- **Evidence:** [`evidence/BIO-ARC-006/codex/REREVIEW-ROUND-3.md`](../evidence/BIO-ARC-006/codex/REREVIEW-ROUND-3.md), [`REREVIEW-ROUND-4.md`](../evidence/BIO-ARC-006/codex/REREVIEW-ROUND-4.md).

### BIO-FND-008 — vulnerable Microsoft.OpenApi dependency

- **Severity/status:** High — Closed.
- **Gap:** the API directly pinned `Microsoft.OpenApi` 2.4.1, affected by GHSA-v5pm-xwqc-g5wc / CVE-2026-49451, although patched 2.x releases existed. A crafted circular OpenAPI document could trigger process termination.
- **Root cause:** the package was pinned to a genuinely vulnerable patch and documentation incorrectly claimed the whole 2.x line was affected.
- **Fix:** **Principled/root-cause.** The dependency moved to advisory-free 2.12.2 and build, OpenAPI, and live vulnerability-audit evidence were rerun.
- **Evidence:** [`evidence/BIO-FND-008/codex/REVIEW.md`](../evidence/BIO-FND-008/codex/REVIEW.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-FND-008/codex/REREVIEW-ROUND-3.md).

### BIO-FND-008 — vulnerability scanner false success

- **Severity/status:** High — Closed.
- **Gap:** the wrapper printed PASS and exited zero when `dotnet list package --vulnerable` itself failed, so network, advisory-source, authentication, restore, or tool failures bypassed the quality gate.
- **Root cause:** the script searched captured text for a vulnerability phrase but discarded the audit command's exit status and did not require a positive clean result.
- **Fix:** **Principled/root-cause.** The gate now rejects nonzero audit exits, vulnerable output, and zero-exit output lacking the explicit clean phrase. Controlled clean, vulnerable, command-failure, and advisory-failure cases are regression tested.
- **Evidence:** [`evidence/BIO-FND-008/codex/REREVIEW-ROUND-2.md`](../evidence/BIO-FND-008/codex/REREVIEW-ROUND-2.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-FND-008/codex/REREVIEW-ROUND-3.md).

### BIO-FND-009 — incomplete log-redaction boundary

- **Severity/status:** High — **Closed** after acceptance audit QA-05 and independent review round 6.
- **Gap:** earlier rounds exposed name-based redaction, arbitrary strings treated as safe, forged logger-category/scope labels granting trust, and a custom-state formatter bypass. Those paths now default to withheld unless wrapped in `SafeLogValue`, suppress exception messages, and never invoke a caller formatter. The reviewed host still retains ASP.NET Core's default Debug and EventSource providers beside the redacting console provider; those sinks receive raw log events independently and can emit content the console formatter withholds.
- **Root cause:** the formatter corrections covered one provider, not the full logging-provider topology. Configuring a safe console formatter does not make separate Debug and EventSource providers pass through it.
- **Fix:** **Principled/root-cause.** Classification is authoritative for every enabled provider: the module clears the host defaults before adding the sole redacting console provider, and a composed-host regression requires that exact provider set. Earlier fixes default unknown values to withheld, require explicit `SafeLogValue` opt-in, suppress exception messages, reject forgeable category/scope trust, and prevent custom formatters from bypassing classification.
- **Evidence:** active feature evidence at `evidence/BIO-FND-009/codex/REVIEW.md`, `REREVIEW-ROUND-2.md` through `REREVIEW-ROUND-6.md`, `evidence/BIO-FND-009/acceptance/QA-05.md`, and `REACCEPTANCE.md`; acceptance evidence commit `1eba587` (not yet merged to `develop`).

### BIO-FND-010 — acceptance-evidence authorization bypasses

- **Severity/status:** High — Closed after four review rounds.
- **Gap:** stale, failed, cross-story, or owner-only evidence could authorize acceptance; implemented stories could be omitted from inventory; a mutable story could remove its independent-review requirement; and one evidence file could impersonate both builder and adversarial stages.
- **Root cause:** the acceptance checker treated existence as validity and let the artifact under review define and relabel its own controls.
- **Fix:** **Principled/root-cause.** Evidence is bound to story, reviewed commit, PASS result, command, stage, and reviewer. Repository-wide inventory is mandatory; builder plus adversarial review is a non-removable minimum; duplicate paths and shared reviewer identities across independent stages are rejected.
- **Evidence:** [`evidence/BIO-FND-010/codex/REVIEW.md`](../evidence/BIO-FND-010/codex/REVIEW.md), [`REREVIEW-ROUND-2.md`](../evidence/BIO-FND-010/codex/REREVIEW-ROUND-2.md), [`REREVIEW-ROUND-3.md`](../evidence/BIO-FND-010/codex/REREVIEW-ROUND-3.md), [`REREVIEW-ROUND-4.md`](../evidence/BIO-FND-010/codex/REREVIEW-ROUND-4.md).

## 2026-09-22

### BIO-DEV-001 — plaintext device credentials accepted as references

- **Severity/status:** High — **Closed** at BIO-DEV-001 acceptance.
- **Gap:** device registration accepts `CredentialReference` as any caller-supplied string. The repository copies it directly into the stored device and returns it unchanged, so a plaintext password, token, credential-bearing URL, or other secret is persisted while merely being named a reference.
- **Root cause:** AC02 is enforced only by a property name and documentation; no value object, reference grammar, request validation, or repository invariant distinguishes a secret-store pointer from credential material.
- **Fix/status:** **Principled/root-cause fix accepted.** Service and repository boundaries require a constrained `vault://`, `kv://`, `kms://`, or `secretref://` reference and reject plaintext, embedded userinfo, empty/control-character values, and unsupported schemes. QA-05 is also closed: the repaired cancellation probe reaches its atomicity assertion and the repository secret scan is clean. QA-06 hardened the shared vulnerability wrapper to fail closed when `NU1900` co-occurs with a clean-looking summary; the delivered head then completed a live clean advisory audit.
- **Required closure:** Complete. Reopen credential handling if a future adapter resolves or stores the referenced secret, and reopen the audit-atomicity obligation before either store becomes durable, remote, independently transactional, or multi-process.
- **Evidence:** feature acceptance commit `744ae2e`; manifest and re-acceptance report under `evidence/BIO-DEV-001/`, Seyi round-6 PASS at `a2b01ae`, structural 47/47, security-gate 37/37, cancellation probe PASS, live vulnerability audit PASS, and secret scan clean.

### BIO-CRD-001 — request-controlled tenant authority

- **Severity/status:** Critical original bypass closed; **High canonical-identifier incompatibility closed.**
- **Gap:** a caller-controlled tenant-id header selected the authorization namespace, so an attacker could claim another tenant and make correctly tenant-filtered repository calls as that victim.
- **Root cause:** repository filtering was mistaken for tenant authentication; there was no provenance binding between the caller and the supplied tenant identifier.
- **Fix:** **Principled/root-cause.** Tenant id now requires its matching per-tenant secret with constant-time comparison, generic authentication failure, no HTTP self-registration path, and trusted configuration-based startup provisioning. Independent forgery and production-wiring probes pass.
- **Retroactive specialist result:** Zainab found that `HeaderTenantAuthenticator` rejected `_`, while every canonical tenant identifier is `ten_<UUIDv7>`. Correctly provisioned real tenants therefore could not authenticate. The fix makes the pattern exactly match the frozen canonical contract, migrates fixtures to genuine canonical ids, and adds unit plus composed-host HTTP regressions while retaining malformed-id and cross-tenant negatives.
- **Evidence:** original acceptance `0a142bd`; fix `159ec1c`; evidence update `fe8d6d9`; develop merge `be8683b`; Zainab re-review structural 28/28, unit 112/112, secret scan clean; retroactive report [`evidence/TD-ZAINAB-SECURITY-REVIEW/RETROACTIVE-REVIEW.md`](../evidence/TD-ZAINAB-SECURITY-REVIEW/RETROACTIVE-REVIEW.md).

### BIO-TEN-001 — administrative mutation/audit divergence

- **Severity/status:** High — **Closed only for the current singleton, same-process, volatile tier; specialist review PASS.**
- **Gap:** earlier implementations could commit an unaudited mutation, preserve it after a rollback conflict, or write a false-success `ALLOWED` event for a rejected concurrent mutation.
- **Root cause:** mutation and audit used separate stores without a shared transaction, and successive ordering/rollback designs did not cover concurrency, ambiguous acknowledgement, or cancellation after audit confirmation.
- **Fix:** **Principled current-tier fix with explicit boundary.** A singleton per-tenant lock covers pre-check, audit, and commit; ambiguous audit throws are verified; commits use `CancellationToken.None` after audit confirmation; deterministic concurrency evidence proves one commit and one matching success event. Reopen before either store becomes durable, remote, independently transactional, or multi-process, when a shared transaction/outbox or modeled recovery protocol is required.
- **Retroactive specialist result:** Zainab completed the waived review on 2026-09-22 and confirmed the current-tier closure. The durable/remote/multi-process boundary remains disclosed and must be reopened when persistence changes.
- **Evidence:** Seyi round-5 PASS `03086c1`, delivered hardening `a91d116`, acceptance `d53fc43`, xUnit 51/51, structural 29/29, independent atomicity probe PASS.

### BIO-TEN-002 — forgeable and replaceable resolved tenant context

- **Severity/status:** High — **Code path closed; specialist review PASS with disclosed future-channel boundary.**
- **Gap:** the first shipped host resolver exposed public tenant-context construction and a writable accessor, allowing downstream code to forge or replace the middleware-resolved tenant. Mutable channel collections could also change after resolution. Earlier rounds additionally accepted a query parameter as a tenant channel and aliased malformed port suffixes to verified hosts.
- **Root cause:** the resolved tenant object was treated as a data shape rather than an authority token whose provenance, construction and single assignment are security invariants.
- **Fix:** **Principled/root-cause.** The packaged API resolves only the real HTTP Host channel; malformed authorities fail exact matching; context construction and `SetResolved` are internal; consumer access is read-only and set-once; tenant ids are canonical; channel collections are defensively copied and read-only. Unknown or unverified hosts fail closed.
- **Retroactive specialist result:** Zainab completed the waived review on 2026-09-22 and confirmed the host-channel closure. Principal/service channel agreement, database binding, and RBAC remain explicit later-story boundaries rather than claims made by BIO-TEN-002.
- **Evidence:** Seyi round-4 PASS `195d0f3`, implementation `5e771b3`, acceptance `3c3d60b`; feature 90/90, unit 154/154, tenant-registry regression 51/51, structural 37/37 and 39/39, module-direction and secret scan PASS.

### BIO-IDN-001 — public repository bypassed tenant, lifecycle and audit governance

- **Severity/status:** High — **Closed** at acceptance.
- **Gap:** public aggregate construction and repository mutation let ordinary consumers move a PERSON across tenants, replace its identity, jump lifecycle states, and archive it without the governed service or an audit event.
- **Root cause:** tenant/lifecycle/audit rules existed only in `PersonService`; the persistence boundary trusted caller-built aggregates and exposed its mutation surface publicly.
- **Fix:** **Principled/root-cause.** Aggregate construction and repository/service mutation surfaces are internal; identity and tenant are get-only; the repository independently enforces initial state, immutable identity/tenant and the same shared lifecycle transition table as the service. Explicit friend assemblies are limited to composition and tests.
- **Evidence:** implementation `f1b143d`, Seyi PASS `45f9d88`, acceptance `e749c94`; feature 70/70, unit 154/154, structural 47/47, module-direction, traceability inventory and secret scan PASS.

### BIO-IDN-002 — orphan and cross-tenant profile assignment

- **Severity/status:** High — **Closed** at acceptance.
- **Gap:** profile assignment validated only canonical PERSON identifier syntax, so it could attach and audit a profile for a missing PERSON or a PERSON owned by another tenant.
- **Root cause:** module ordering was mistaken for a runtime aggregate-existence invariant; no tenant-scoped PERSON lookup occurred before audit and commit.
- **Fix:** **Principled/root-cause.** A narrow read-only gateway delegates to BIO-IDN-001's tenant-scoped service and runs before lock, audit or persistence. Missing and foreign-tenant identifiers return the same not-found response and leave no profile or success audit.
- **Evidence:** implementation `bf72649`, Seyi PASS `2f15921`, acceptance `88e7f00`; feature 47/47, unit 154/154, structural 48/48, module-direction, traceability inventory and secret scan PASS.

### BIO-IDN-003 — tenant-wide identifier uniqueness race and unsafe rule enablement

- **Severity/status:** High — **Closed for the current singleton, same-process, volatile tier** at acceptance.
- **Gap:** simultaneous assignments to different people could both claim the same supposedly unique identifier. Enforcement could also be enabled over pre-existing duplicate active or retired values, recording a rule the stored population already violated.
- **Root cause:** assignment locking was scoped to a person even though uniqueness spans a tenant/type/custom-label policy scope, and rule enablement did not validate the complete population under the same lock.
- **Fix:** **Principled current-tier fix with explicit boundary.** Assignments and rule changes share a tenant/type/custom-label lock. Enabling enforcement scans active and retired identifiers and returns stable conflict `uniqueness_already_violated` before changing the rule or audit trail when duplicates exist. Reopen before multi-process or durable persistence, where a database uniqueness constraint or equivalent distributed transaction is required.
- **Evidence:** implementation `c5d299e`, Seyi round-3 PASS `2647e41`, acceptance `d07ff4a`; feature 55/55, unit 154/154, integration 64/64, structural 63/63, traceability, module-direction, vulnerability audit and secret scan PASS.

### BIO-IDN-004 — emergency suspension/revocation split state

- **Severity/status:** High — **Closed for the current singleton, same-process, in-memory tier** at acceptance.
- **Gap:** independent status, audit, and revocation writes allowed a committed suspension with no revocation work, a rejected suspension with a false revocation, pending work that vanished after reactivation, or an audit outage that prevented pending tracking.
- **Root cause:** security-critical revocation was treated as a supplementary write and pending reconciliation was initially derived from current status or recorded only after another fallible operation.
- **Fix:** **Principled current-tier fix with explicit boundary.** A successful suspension first creates durable version-keyed pending work, before audit or publication. Audit and revocation are attempted independently; rejected suspension emits nothing; pending work survives later status changes and is reconciled idempotently. Reopen for durable, remote, or multi-process stores, where status and outbox work require a shared transaction or equivalent recovery protocol.
- **Evidence:** implementation `d01b3a3`, Seyi round-5 PASS `7b463b4`, acceptance `15609a7`; independent atomicity probe 3/3, feature 70/70, unit 160/160, integration 70/70, structural 59/59, traceability, module-direction and secret scan PASS.

### BIO-IDN-006 — caller-controlled actor identity fed authorization

- **Severity/status:** Critical — **Interim application-side mitigation accepted under ADR-0006; real identity binding remains open to BIO-INT-001.** Current same-process authorization and approval tests pass.
- **Gap:** RBAC and maker-checker originally trusted caller-selected `X-Actor-Id`, allowing impersonation of a privileged person. Earlier narrow gates also left role/grant/rule escalation, unauthorized makers/checkers, reusable approvals, and approval-loss paths.
- **Root cause:** authorization was built on an unauthenticated identity claim; maker-checker enforcement and approval consumption were initially incomplete or ordered independently from the governed mutation.
- **Treatment:** **Principled interim mitigation plus disclosed boundary.** Every RBAC and maker-checker authorization path now verifies an HMAC proof bound to the exact resolved actor identifier before authority lookup; tenant permissions, distinct authorized maker/checker roles, single-use approvals, safe retry ordering, and audit controls are enforced. This is not person authentication, replay protection, or genuine separation of duties when one party controls the shared provenance secret. ADR-0006 assigns trusted-edge replacement, real authenticated principal binding, and retirement of this accommodation to BIO-INT-001. Reopen atomicity before independent durability or multi-process stores.
- **Evidence:** implementation `67bcb8e`, Seyi round-7 scoped PASS `93fce5d`, Zainab scoped PASS, acceptance `519384a`; feature 113/113, integration 86/86, structural 121/121, traceability, module-direction and secret scan PASS.

### BIO-PHO-001 — mutable photo-content aliasing

- **Severity/status:** High — **Closed** at acceptance.
- **Gap:** master and derivative `byte[]` content was exposed by reference, allowing caller-buffer or returned-buffer mutation to change supposedly immutable stored photo content without a versioned operation.
- **Root cause:** the domain objects retained and returned mutable array references despite append-only repository semantics.
- **Fix:** **Principled/root-cause.** Master and derivative content is defensively copied on construction and on every read, with regressions for both mutation directions.
- **Evidence:** implementation `f376d46`, Seyi round-2 PASS `926fe8d`, acceptance `57ff69b`; feature 41/41, structural 35/35, unit 160/160, integration 91/91, vulnerability audit, traceability, module-direction and secret scan PASS.

### BIO-BIO-001 — biometric payload aliasing and tenant-key collision

- **Severity/status:** High — **Closed** at acceptance; Zainab specialist review PASS.
- **Gap:** mutable biometric arrays allowed post-enrolment template tampering, and delimiter-joined tenant/person/modality keys allowed distinct tuples containing embedded delimiters to collide across tenant lookups.
- **Root cause:** provider, canonical-template, and storage boundaries shared mutable array references; storage encoded a compound security key as an ambiguous string.
- **Fix:** **Principled/root-cause.** Samples, extracted features, and templates clone payload bytes on input and output. Storage uses a structural `(TenantId, PersonId, Modality)` tuple, eliminating delimiter ambiguity.
- **Evidence:** implementations `3fe6a49` and `80669cb`, Seyi round-3 PASS `5e63925`, Zainab round-4 PASS, acceptance `27f9491`; feature 29/29, structural 40/40, traceability, module-direction and secret scan PASS.

### BIO-DOC-005 — tenant-authored regular-expression denial of service

- **Severity/status:** High — **Closed** at acceptance; Zainab specialist review PASS.
- **Gap:** parser validation ran tenant-authored regular expressions with no timeout, allowing catastrophic backtracking against untrusted barcode fields to monopolize process CPU.
- **Root cause:** the parser executor used unbounded regular-expression matching on both an untrusted pattern and untrusted input.
- **Fix:** **Principled/root-cause.** Every validation match has a bounded timeout; timeout exceptions are contained and reported as explicit `pattern_timeout` failures. A catastrophic-pattern regression proves bounded completion.
- **Evidence:** implementation `1b2fcb4`, Seyi round-2 PASS `9a42a64`, Zainab PASS, acceptance `84b306c`; feature 36/36, structural 29/29, traceability, module-direction and secret scan PASS.

### BIO-BIO-004 — uncontained fingerprint-provider failures

- **Date:** 2026-09-22.
- **Severity/status:** High — **Closed** at acceptance; Zainab specialist review PASS.
- **Gap:** fingerprint extraction caught only `ArgumentException`. Device timeouts, vendor exceptions, and provider-internal cancellation could escape without the required `AcquisitionFailed` decision record, turning an expected acquisition failure into an unhandled outage with no verification trail.
- **Root cause:** the service treated one synthetic provider's exception type as the complete external adapter failure contract.
- **Fix:** **Principled/root-cause.** The provider boundary now contains all provider/vendor exceptions and persists an `AcquisitionFailed` denial. A genuine cancellation requested by the caller remains the sole explicit propagation path. Regressions cover non-argument failures, timeout, vendor-specific failure, provider-internal cancellation, and caller cancellation.
- **Evidence:** remediation `2ff084d`, Seyi round-2 PASS `0bec3f7`, Zainab PASS, acceptance `3baa761`; feature 34/34, structural 38/38, test-project build, traceability, module-direction and secret scan PASS.

### BIO-CRD-005 — physical card issuance commits before audit

- **Date:** 2026-09-22; closed 2026-09-23.
- **Severity/status:** High — **Closed; acceptance passed.**
- **Gap:** `IssueAsync` persists the physical-card row and consumes its sequence before attempting the mandatory audit. When the audit write fails and the existence check confirms no event was stored, the request fails but the unaudited card remains committed.
- **Root cause:** sequence permanence was implemented by committing the domain row early, overriding ADR-0003 Decision 7's required lock → audit → commit ordering instead of separating irreversible sequence reservation from successful issue creation.
- **Fix:** **Principled/root-cause.** Sequence calculation and row creation are separate operations held under the existing per-person/card-class lock. `IssueAsync` now builds the candidate in memory, confirms the mandatory audit, and only then creates the row. Confirmed audit failure therefore exposes no issued card, while the lock preserves collision-free sequence allocation.
- **Evidence:** remediation `9bf4cff`, builder evidence `cc50f7f`, Seyi round-two PASS `734f998`, acceptance `9b807fe`; the behavioral probe returns `audit_unavailable` with zero persisted cards, the source-order probe proves compute → audit → commit, and structural 34/34, build, traceability, module-direction and secret scan PASS.

### BIO-MOB-001 — tenant context trust-boundary failures

- **Date:** 2026-09-23.
- **Severity/status:** High — **QA-01 through QA-04 closed; acceptance passed.**
- **QA-01 gap/root cause:** the persisted restore path constructed `TenantContext` directly and bypassed schema validation, allowing malformed stored tenant context to become bound.
- **QA-01 fix:** **Principled/root-cause.** `resolve` and `fromJson` share complete schema validation; bootstrap clears malformed persisted context and remains unbound. The original probe and negative restore/bootstrap regressions pass.
- **QA-02 gap/root cause:** the operator UI accepts `tenant_id` and `binding_key`, then local code stamps those caller-controlled fields with the `principal` channel and treats the result as resolved. ADR-0002 says caller fields are not resolution channels, callers may never name database binding, and object shape does not prove resolution provenance.
- **QA-02 fix:** **Principled/root-cause for new sessions.** The operator form, local `resolve` factory, binding method and asserted-actor shim are removed; without a trusted resolver, fresh installs remain unbound. The caller-construction probe passes.
- **QA-03 gap/root cause:** the remediation does not revoke schema-valid contexts already written by the vulnerable producer. The database version and restore path are unchanged, so an ordinary application upgrade loads an old caller-created row by shape and binds it despite having no resolution provenance.
- **QA-03 fix:** **Principled/root-cause.** Local storage requires a provenance envelope and invalidates old unwrapped rows before parsing, so contexts written by the vulnerable producer cannot survive upgrade as bound state. The seeded legacy-state regression passes.
- **QA-04 gap/fix:** Seyi found that the first provenance marker could be self-issued through the real store's public save method. The real `SqlCipherAppDatabase.saveTenantContext` now throws before storage access until a genuine resolution boundary replaces the hook; an exact forged-context regression proves no production save path exists.
- **Evidence:** final remediation `5036662`, builder evidence `6dedc6a`, Seyi round-six PASS `9ef5276`, acceptance `bb89039`; all three acceptance probes, foundation 36/36, builder Flutter 50/50/analyze, traceability, module-direction and secret scan PASS. Fresh emulator evidence shows the unbound informational screen with no crash.

### BIO-BIO-005 — stale source-photo face reference remains usable

- **Date:** 2026-09-23.
- **Severity/status:** High — **Closed; acceptance passed.**
- **Gap:** face verification refreshes the latest approved face reference, but it never checks whether that reference was derived from the person's current photo master. A reference derived from photo v1 can still produce `Matched` after the current photo changes to v2.
- **Root cause:** AC04's reference/photo lifecycle requirement was narrowed in builder documentation to reference recapture only. `FaceVerificationService` has no trusted current-photo lifecycle dependency and performs no comparison against `SourcePhotoMasterVersion`.
- **Fix:** **Principled/root-cause.** The trusted gateway now resolves both the latest approved face reference and the current photo-master version on every verification. A mismatch or missing current photo is recorded as `ReferenceStale` before extraction, records both lifecycle versions, produces no score, and deterministically denies with `reference_stale`.
- **Evidence:** remediation `a4afee5`, builder evidence `9e2c337`, Seyi round-two PASS `0d24b2b`, Zainab post-fix PASS, acceptance `ae7ef99`; the compiled acceptance probe rejects photo-v1 evidence when the current photo is v2, structural 46/46, builder feature 27/27, traceability inventory 26, module-direction and secret scan PASS.

### BIO-CRD-006 — replacement/revocation failure atomicity

- **Date:** 2026-09-23.
- **Severity/status:** High — **Closed; acceptance passed.**
- **QA-01 gap/fix:** activation originally committed before the permanent-revocation sweep, allowing a later audit/repository failure to leave an active replacement with earlier cards still recoverable. Remediation `31bb1de` reverses the order and failure-injection tests prove a sweep failure leaves the replacement unactivated. **QA-01 is closed.**
- **QA-02 gap/fix:** the reversed order originally revoked earlier cards before validating stale/missing/invalid activation requests. Remediation `1fef658` adds story-local idempotency reservation and read-only target/version validation before revocation. **QA-02 is closed.**
- **QA-03 gap/root cause:** the precheck could not reserve the independently callable BIO-CRD-005 gateway record. A concurrent gateway update between `GetAsync` and the post-sweep authoritative `ActivateAsync` could therefore reject activation after earlier-card side effects.
- **QA-03 fix:** **Principled compensating protocol.** Remediation `0279e1f` moves earlier cards into a durable, conservatively denied `RevocationPending` state before authoritative activation, recording each prior state. A failed activation compensates every pending record; a successful activation confirms permanent revocation. Interrupted pending work remains durable and retryable.
- **Evidence:** remediation `0279e1f`, builder evidence `cb51df9`, Seyi round-four PASS `9373228`, acceptance `7a669c9`; deterministic compare-and-set conflict tests restore both previously locked and never-reported cards, and structural 47/47, all three acceptance probes, traceability and module-direction checks PASS.

### BIO-DOC-007 — mutable NFC trust-bearing results

- **Date:** 2026-09-23.
- **Severity/status:** High — **Closed; acceptance passed.**
- **QA-02 gap/fix:** encryption and message-authentication keys were returned as shared mutable arrays. Remediation `09665c6` clones on construction and every getter; the original probe and mutation regressions pass. **QA-02 is closed.**
- **QA-03 gap/fix:** `ChipReadSession` created a mutable list and exposed it through `IReadOnlyList`. Remediation `2cecb4e` snapshots and wraps every outcome/authentication collection in `ReadOnlyCollection<T>`; runtime downcast/mutation regressions pass. **QA-03 is closed.**
- **Root cause:** accessibility-only interfaces and internal setters were treated as immutability, without snapshotting mutable collection implementations at trust-bearing result boundaries.
- **Fix:** **Principled/root-cause.** Mutable buffers and collections are copied at every trust-bearing result boundary and exposed only through defensive copies or read-only wrappers.
- **Evidence:** QA-02 remediation `09665c6`, QA-03 remediation `2cecb4e`, Seyi round-four PASS `2b7314f`, acceptance `5b25256`; both acceptance probes, foundation 36/36, traceability and module-direction PASS.

### BIO-BIO-007 — unapproved face templates enter the identification population

- **Date:** 2026-09-23.
- **Severity/status:** High — **Closed; acceptance passed.**
- **Gap:** the face-search gallery accepts an arbitrary tenant id, person id and raw biometric sample, extracts a template directly, and represents it as part of the tenant population without proving that BIO-BIO-003 approved the capture.
- **Root cause:** the implementation treats the story's declared BIO-BIO-003 dependency as an intentional non-dependency and substitutes a caller-populated gallery with no quality, liveness, approval-status or approved-reference provenance check.
- **Fix:** **Principled/root-cause.** Raw-sample indexing is removed. A narrow approval gateway resolves the claimed tenant/person's current BIO-BIO-003-approved reference, and indexing fails closed when none exists. The production adapter uses BIO-BIO-003's enrolment service, where references are created only after quality and liveness approval.
- **Evidence:** remediation `5e51d78`, Seyi round-two PASS `77e5388`, acceptance `8df54a0`; approved-population probe, structural 41/41, traceability and module-direction checks PASS.

### BIO-DOC-008 — comparison results claim audit without an audit event

- **Date:** 2026-09-23.
- **Severity/status:** High — **Closed; acceptance passed.**
- **Gap:** comparison results copy caller-supplied version, actor and timestamp fields but write no durable audit event. Invalid or forged attribution and arbitrary versions are accepted.
- **Root cause:** AC05's “versioned/audited” requirement was narrowed to attribution-shaped fields on a pure return object, confusing data that an audit record might contain with proof that an audit actually occurred.
- **Fix:** **Principled/root-cause.** Remediations add confirmed durable audit, strict RFC 3339 attribution, and session-scoped monotonic versioning. The final `e145357` protocol reserves the next version, commits only after audit confirmation, and releases on failure, preserving same-version retry without sequence holes.
- **Evidence:** final remediation `e145357`, Seyi round-five PASS `cf4228b`, acceptance `011e04e`; audit-failure retry regression, version/audit probe, foundation 52/52, traceability and module-direction checks PASS.

### BIO-ACC-001 — access-policy rules exposed by unauthenticated reads

- **Date:** 2026-09-23.
- **Severity/status:** High — **Closed; acceptance passed.**
- **Gap:** tenant policy GET/list endpoints return complete draft and published access rules without resolving an actor, verifying actor provenance, or checking a read permission. Exposed content includes persons/profiles, credentials, locations, schedules, statuses, deny rules and assurance requirements.
- **Root cause:** read routes were copied from an unrelated role-assignment listing convention and explicitly exempted from the authorization boundary, even though access-policy content is a sensitive physical-security decision surface.
- **Fix:** **Principled/root-cause.** Every GET/list endpoint now resolves an actor and verifies provenance; both service read methods independently require the tenant-scoped `access_policy.read` permission before repository access.
- **Evidence:** remediation `72daa5f`, Seyi round-two PASS `d881ea6`, acceptance `8cea1a2`; policy-read authorization probe, feature 51/51, traceability and module-direction checks PASS.

### BIO-MOB-003 — device-authenticated central sync boundary incomplete

- **Date:** 2026-09-23.
- **Severity/status:** High — **QA-02 closed; QA-01 accepted as an owner-approved scope boundary and remains open to a future device-enrolment story.**
- **QA-01 gap/root cause:** the new central offline-intake endpoint requires device ID and device secret headers. Remediation `01044b0` makes `HttpSyncTransport` read and send a Keystore-backed secret, but `DeviceCredentialStore.writeSecret` has no production caller; only tests provision it. The shipped `SessionController` therefore remains permanently unprovisioned and fails before every network request. Moving the missing capability behind a secure store does not deliver central synchronization.
- **QA-02 gap/fix:** the service previously cross-validated only tenant, not body `device_id`, and omitted identity claims from the fingerprint. Remediation `01044b0` rejects device mismatch before idempotency claim and fingerprints both tenant and device. **QA-02 is closed.**
- **Boundary/treatment:** **Disclosed dependency boundary, not fixed.** Kemi formally ruled self-service enrollment/pairing outside BIO-MOB-003 because it is a reusable device-lifecycle capability with its own ADR-0006 authorization decision. BIO-MOB-003 is accepted for already-provisioned devices. A dedicated story must add trusted enrollment, securely write the server-recognized secret, and prove the default mobile-to-host path; until then real unprovisioned installs fail closed before network access.
- **Evidence:** central/audit remediation `e8f332d`, identity remediation `01044b0`, scope ruling `8ebe1da`, Seyi round-seven PASS `ec0841c`, acceptance `21eaafa`; mobile 19/19, backend 28/28, traceability, module-direction and secret scan PASS. Boundary probe passes headers/device/fingerprint and documents absent provisioning.

### BIO-DOC-009 — authenticity read and override routes exposed without authorization

- **Date:** 2026-09-24.
- **Severity/status:** High — **Closed; acceptance passed.**
- **Gap:** the first HTTP surface exposed assessment lookup by caller-supplied session id and allowed a caller-supplied `X-Actor-Id` to perform and audit a durable manual override without trusted actor provenance, permission checks, or tenant binding.
- **Root cause:** an application-layer identity string was treated as authentication and authorization before the shared trusted actor/RBAC/tenant boundary exists.
- **Fix:** **Principled non-exposure boundary.** The unauthenticated GET-by-id and override routes and handlers are removed entirely. The tested read/override service capability remains inaccessible over HTTP until a later story supplies trusted actor provenance, explicit permission, and tenant context. The session-id-free compute route exposes only fixed synthetic demo assessments and cannot read or rewrite caller-selected records.
- **Evidence:** remediation `6cd77a6`, Seyi round-two PASS `9700502`, Zainab round-two PASS `0e2c6bf`, acceptance `dd0358a`; feature 54/54 and live browser 4/4 in builder evidence, structural 54/54, frontend unit 398/398, traceability, TypeScript, module-direction and secret scan PASS.

## Maintenance rule

The acceptor updates this ledger whenever acceptance closes a security-relevant finding. Each new entry must name the story and date, state severity and closure status, explain the gap and root cause in plain language, classify the treatment as a root-cause fix, narrow patch, or disclosed boundary, and link the evidence that proves the stated status. Open or scope-bounded behavior must never be described as fixed.
