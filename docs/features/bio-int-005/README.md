# BIO-INT-005 — Implement external import conflict review

**Round-1 remediation (2026-10-07): ZB-INT005-01/ZB-INT005-02 (Zainab, both High).** Scan and
list had no provenance proof or permission check at all (only a forgeable `X-Actor-Id` header),
despite scan genuinely mutating `ImportConflictReview` state and writing audit events, and list
exposing exact existing-vs-incoming identity field values, person/external identifiers and
decision notes. Decide's own provenance+RBAC, tenant isolation/CAS and audit value-exclusion
were all confirmed sound - this was specifically the other two routes on this feature. Root
cause: an earlier design borrowed BIO-INT-002's `PreviewAsync`/`ListBatchesAsync` "audit-
attribution-only, no permission check (ADR-0006 Decision 2)" shape by endpoint-shape analogy
without re-justifying it against this story's own actual side effects (scan mutates and audits;
BIO-INT-002's preview never does either) or actual data sensitivity (this story's own field-level
comparison and notes are more sensitive than a mapping's own configuration). Fixed by adding
`ImportConflictReviewService.ReadPermission` (`integrations.conflict_review_read`), checked
inside the service for both scan and list, plus a verified `ActorProvenanceGate` proof checked at
the endpoint for all three routes now - the identical treatment BIO-TEN-003's own
`TenantFeatureConfigurationEndpoints` already gives its own GET route. See
`ImportConflictReviewService.ReadPermission`'s and `ImportConflictReviewEndpoints`'s own headers
for the full record. `ListForMappingAsync` gained an `AuditActor` parameter it previously lacked
(needed to check the real permission grant).

User story: "As data administrators, I need conflicting external records routed to human review."

- **AC01** — Conflict compares source versus BIOLOG values.
- **AC02** — Operator can keep/use source/escalate according to permission.
- **AC03** — Decision is audited.
- **AC04** — No automatic destructive overwrite of verified identity evidence.
- **AC05** — `/administration/integrations` is wired to a real `IntegrationService` surface for
  conflict review.

Dependencies: BIO-INT-002 (SIS/HR/ERP import framework — this story's own conflict detection,
already built), BIO-IDN-005 (duplicate review foundation — this story's structural precedent for
a human-review decision record). Conflict group: `enterprise-import`.

## What's built

New, self-contained project: `api/features/bio-int-005/Biolog.Features.ImportConflictReview.csproj`
(mirrors BIO-INT-002/BIO-IDN-005's own `Domain`/`Api`/`Audit` layout; cannot reference any sibling
feature project — see the csproj's own comment).

### AC01 — conflict compares source versus BIOLOG values

BIO-INT-002's own `ImportBatchService.ApplyAsync` already computes a field-level comparison
between an incoming source row and the currently-linked PERSON's own value, surfacing it as
`RowOutcome.Conflict` with a `FieldConflict[]` list — this story does not re-derive or
re-compare anything itself. `Domain/IImportConflictGateway.cs` is this feature's own narrow,
READ-ONLY seam onto that already-computed comparison, implemented in `BioInt005Module.cs` by
wrapping BIO-INT-002's own already-registered `ImportBatchService.ListBatchesAsync` directly (no
`ProjectReference` — the same cross-story gateway pattern every sibling story in this codebase
uses). Only **applied** batches are ever visible here: BIO-INT-002's own `PreviewAsync` never
persists anything (confirmed in that class's own header), so a dry-run preview is exploratory
and intentionally never reaches this story's review queue.

`ImportConflictReviewService.ScanAsync` reduces a mapping's full applied-batch outcome history
down to the MOST RECENT snapshot per external key (a later import attempt's own view of a row
always supersedes an earlier one), then:
- raises a new `ImportConflictReview` (`Domain/ImportConflictReview.cs`, natural key
  `(TenantId, MappingId, ExternalKey)`, mirroring BIO-IDN-005's own `DuplicateCandidate` shape)
  for a currently-conflicting row with nothing on file yet,
- augments an existing `PENDING_REVIEW` record if the conflicting value itself has changed since
  it was last seen,
- withdraws a `PENDING_REVIEW` record (`Status = WITHDRAWN`) once a later import run no longer
  shows a conflict for that key — keeps the review queue accurate rather than stale,
- re-raises (reopens) a previously `WITHDRAWN` row back to `PENDING_REVIEW` if it conflicts
  again later,
- and — the same "never re-detects a decided pair" discipline BIO-IDN-005's own
  `DuplicateReviewService` already established — never touches a row that already has a
  terminal human decision (`KEEP_EXISTING`/`USE_SOURCE`/`ESCALATED`), even if the underlying
  conflict value changes again. A human already reviewed this row; a routine rescan must never
  silently reopen or relitigate that call. A reviewer can still change a decision by calling
  `DecideAsync` again directly (fully audited either way).

### AC02 — operator can keep/use source/escalate according to permission

`ImportConflictReviewService.DecideAsync` requires `integrations.conflict_review_decide`
(checked via `Domain/IPermissionGateway.cs`, wrapping BIO-IDN-006's own real
`PermissionService.ActorHasPermissionAsync` directly — the same ADR-0006 Decision 3 real-RBAC
seam BIO-INT-002's own `ApplyAsync` and BIO-INT-004's own subscription-write endpoints use) and a
verified `X-Actor-Provenance` proof (`Api/ActorProvenanceGate.cs`). A reviewer records exactly
one of three outcomes (`Domain/ImportConflictReviewStatus.cs`): `KEEP_EXISTING`, `USE_SOURCE`,
`ESCALATED`.

Scan and list require their own, separate `integrations.conflict_review_read` permission
(`ImportConflictReviewService.ReadPermission`) plus a verified provenance proof — round-1
remediation, see this file's own top section. Viewing the review queue (or re-running the
detector) and recording a decision are two independently-gated authorization concerns.

### AC03 — decision is audited

Every raise, augment, withdraw, reopen and decide writes an append-only
`Audit/ImportConflictReviewAuditEvent` before committing — the identical ADR-0003 Decision 7
audit-atomicity pattern every sibling story in this codebase applies (per-row lock, audit before
commit, `ExistsAsync`-on-throw verification, `CancellationToken.None` post-confirm). The audit
`Detail` never carries a compared field VALUE — only the external key, mapping id, and (for a
raise) the WIRE LABEL of which signal fired, mirroring BIO-IDN-005's own "audit trail privacy"
discipline exactly.

### AC04 — no automatic destructive overwrite of verified identity evidence

**This is the central design decision this story inherits, not invents.** BIO-INT-002's own
`ImportBatchService` header already disclosed, as a deliberate scope boundary, that BIO-IDN-001's
`PersonService` exposes exactly `RegisterAsync` (create) and `UpdateStatusAsync` (lifecycle) —
**there is no method anywhere in this codebase to correct a PERSON's own name/DOB/etc. fields
after creation.** Conflict RESOLUTION (actually applying a human-reviewed correction) therefore
needs a PERSON profile-mutator this story is not authorised to add (a shared-contract change
needing Kemi's recorded approval, per every story's own standing constraint).

Consequence: even `USE_SOURCE` — the decision that says "the incoming source value should win" —
**never writes anything.** `ImportConflictReviewStatus.UseSource` records the human's choice as
routing information for a future, separately-authorised PERSON-correction capability, exactly
the same "no merge exists in this codebase" shape BIO-IDN-005's own `DuplicateReviewStatus`
already established for its own `ConfirmedDuplicate` outcome (a real decision, with zero write
capability behind it, by design). This is now the SECOND story in this codebase to hit this
exact gap (BIO-INT-002's own header already named it as "future scope for whichever story next
owns BIO-IDN-001's own aggregate") — worth a note to Kemi that a PERSON profile-correction story
is now twice-disclosed, but not something this story builds unilaterally.

Structurally enforced, not merely documented: this project has no `ProjectReference` to
BIO-IDN-001's or BIO-INT-002's project at all (see the csproj's own comment) — nothing in this
class, this project, or any endpoint it maps *can* call into `PersonService` to mutate a PERSON
record.

### AC05 — web wiring

New `web/features/bio-int-005/` (`ports.ts`/`mock.ts`/`live.ts`/`index.ts`, the same
`resolvePort`/`createMockAdapter` pattern every module in this epic uses) plus a new
`ConflictReviewPanel.tsx`, mounted on `/administration/integrations` ALONGSIDE (never replacing)
BIO-INT-002's `ImportConsole`, BIO-INT-003's `ServiceAccountsPanel` and BIO-INT-004's
`WebhookSubscriptionsPanel`. Three new, independently mode-switchable `IntegrationService` port
keys: `scanConflicts`/`listConflicts`/`decideConflict` (`web/lib/feature-mode.ts`).

**`/settings/authentication` is deliberately out of scope** — the same disclosed boundary every
sibling story adding `IntegrationService` ports has already stated (BIO-INT-004-QA-01). A
conflict review has no relationship to authentication settings, so there is nothing for this
story to wire there.

**Executable proof, not just `tsc` and manual code reading.**
`tests/frontend/playwright/bio-int-005-conflict-review.spec.ts` (SIMULATION mode, runs in the
default `pnpm --dir tests/frontend test:e2e` suite, the same bar
`bio-int-004-webhooks.spec.ts`/`bio-rpt-004-operational-health.spec.ts` already set for a panel
with no real backend wired into that default suite) proves: the panel mounts alongside all three
existing panels; a mapping with nothing scanned yet shows an explicit empty state; scanning
surfaces conflicting rows with the exact existing-vs-incoming field comparison; recording a
decision without a reason is rejected with a visible, accessible (`role="alert"`) failure state;
a real decision succeeds visibly and the row leaves the decidable state; `USE_SOURCE` records the
choice but the field comparison text is byte-for-byte unchanged afterward (AC04's own UI-level
proof); and a decision persists across an explicit refresh (durable state, not just local React
state).

**Self-caught regression while writing the Playwright proof.** The panel's first draft called
`handleList()` (to refresh the table) immediately after a successful decision, and `handleList`
itself unconditionally cleared the `notice` state at its own start — wiping out the very success
message the decision had just set, a split second before the assertion could see it. Running the
new spec surfaced this immediately (two failing assertions, "notice: element not found" despite
the decision genuinely having succeeded — confirmed by the row's own status cell already showing
the new state). Fixed by having `handleList` leave `notice` alone; only `handleScan` (an explicit
fresh load) clears it for itself.

**Pre-existing gap closed while touching this module, not newly introduced here.** Extending
`web/lib/module-registry.ts`'s `IntegrationService` entry surfaced that BIO-INT-003/BIO-INT-004
had added seven port keys to `MODULE_PORT_KEYS.IntegrationService`
(`listServiceAccounts`/`createServiceAccount`/`rotateServiceAccountSecret`/`revokeServiceAccount`/
`listWebhookSubscriptions`/`createWebhookSubscription`/`setWebhookSubscriptionEnabled`) but the
registry itself had never been updated to register a matching `ModulePort` for any of them —
`bio-ux-005-module-registry.test.ts`'s own "registry and feature-mode agree" check, and a second
test asserting the exact multi-port-module list, were BOTH failing before this story (confirmed
by running them against a stash of BIO-INT-004's own accepted commit). Fixed for all ten
non-`primaryAction` keys at once (BIO-INT-003/004's seven plus this story's own three) while
already touching this file — the same "fix the whole class in one pass, don't leave a known gap
half-closed" discipline this codebase has applied elsewhere.

## Test counts

- `tests/features/bio-int-005/ImportConflictReviewServiceTests.cs`: 22 tests — AC01 (raise/
  withdraw/augment/reopen/never-reopen-a-decision, most-recent-batch-wins), AC02/AC03 (all three
  decisions, permission denial for decide AND for scan/list separately, concurrency conflict,
  validation, not-found, re-decide, audit events), AC04 (`USE_SOURCE` never mutates the recorded
  conflict values).
- `tests/dotnet/Biolog.IntegrationTests/BioInt005ConflictReviewEndpointsTests.cs`: 11 tests —
  real packaged host, actor/provenance/tenant validation for all three routes, and the disclosed
  permission-check-reached-but-denied ceiling for scan, list AND decide (same shape as
  `WebhookEndpointsTests`' own ZB-INT003-02 disclosure — this test project has no
  `InternalsVisibleTo` access to seed a real grant).
- `tests/frontend/playwright/bio-int-005-conflict-review.spec.ts`: 6 tests — full browser
  workflow proof (see AC05 above; unaffected by the round-1 RBAC fix, since SIMULATION mode's
  mock port never models permission checks).

## Verification

```sh
dotnet build api/Biolog.Api/Biolog.Api.csproj                                                 # clean
dotnet test tests/features/bio-int-005/Biolog.Features.ImportConflictReview.Tests.csproj       # 22/22
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj                # 255/255
bash scripts/check-module-project-references.sh                                               # PASS
python3 scripts/check-contract-freeze.py                                                       # PASS
npx tsc --noEmit (web)                                                                          # clean
npx next build (web)                                                                            # clean, /administration/integrations prerenders
npx vitest run unit (tests/frontend)                                                            # 548/548
npx playwright test bio-int-005-conflict-review.spec.ts (tests/frontend, prod server)           # 6/6
npx playwright test bio-ux-002-shell.spec.ts bio-ux-013-axe-baseline.spec.ts bio-int-004-webhooks.spec.ts (zero-regression check)
```
