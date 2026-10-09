# BIO-CRD-005 — Builder evidence

## Round 2 — fix for Chioma's acceptance-review finding BIO-CRD-005-QA-01 (commit `9bf4cff`)

**Finding (evidence/BIO-CRD-005/acceptance/ACCEPTANCE.md, verdict FAIL):**
`CardIssueService.IssueAsync` committed the card-issue row via a single
`repository.AppendAsync` call BEFORE calling `AuditAsync`. A confirmed audit-sink failure
correctly surfaced `audit_unavailable` to the caller, but the row and its sequence number had
already been committed - the exact ADR-0003 Decision 7 "commit before audit" violation the
lock → validate → audit → commit pattern exists to prevent. Reproduced by the review's own
`evidence/BIO-CRD-005/acceptance/audit-failure-probe/Program.cs`, which reported
`persisted-card-count-after-confirmed-audit-failure=1`.

**Fix:** split `ICardIssueRepository`'s combined "assign sequence + commit" step into
`NextSequenceNumber` (a pure read - what a new card issue would be numbered) and `Create` (the
commit). `IssueAsync` now computes the sequence, builds the `CardIssue` entirely in memory,
audits it, and only then calls `Create` - the identical lock → validate → audit → commit order
every other mutation in this service (encode/print/verify/activate/deactivate) already followed;
only issuance had the bug. Concurrency safety for issuance moved from a repository-internal
optimistic-retry loop to the service's own per-(tenant, person, card class) lock, which was
already held across the read-then-commit sequence for every other mutation - `NextSequenceNumber`
and `Create` are safe to call apart only because the caller holds that lock throughout, documented
explicitly on both interface members.

**Verification:**
- Re-ran the review's own probe directly: `dotnet run --project
  evidence/BIO-CRD-005/acceptance/audit-failure-probe/AuditFailureProbe.csproj` now prints
  `persisted-card-count-after-confirmed-audit-failure=0` (was `1`).
- Added `tests/features/bio-crd-005/CardIssueAuditAtomicityTests.cs`: a direct regression test
  reproducing the exact probe scenario at the service level
  (`BIOCRD005QA01_a_confirmed_audit_failure_during_issuance_leaves_no_card_issue_committed`),
  a companion test confirming activation already held the same discipline (it did - only
  issuance was broken), a positive-path confirmation, and a service-level concurrent-issuance
  test (50 concurrent `IssueAsync` calls) proving the lock still prevents any sequence collision
  now that the retry-loop moved out of the repository.
- `bash tests/features/bio-crd-005/BIO-CRD-005.test.sh` - 34/34 PASS (3 new checks added: the
  split repository interface, the regression test's presence, and a structural proof that
  `AuditAsync` appears strictly before `repository.Create` in `IssueAsync`'s own source).
- `dotnet test tests/features/bio-crd-005/Biolog.Features.CardIssuance.Tests.csproj` - 31/31 PASS.
- `dotnet test .../CardIssuanceHostChannelTests` (real packaged host) - 5/5 PASS, unaffected.
- `bash scripts/quality.sh` (base) - PASS. `bash scripts/quality.sh --full` - PASS except the
  same pre-existing `ObservabilityTests` flake documented in round 1 (a different method this
  time, identical exception signature, confirmed unrelated).

## Round 1 (commit `0844100`, superseded by round 2's fix above)

**Story:** Implement physical card issue transaction
**Owner:** Ngozi — Credential & Card Engineer
**Conflict group:** `card-lifecycle` (distinct from BIO-CRD-002/003/004's own `card-designer`
group — no frontend UI is affected or required by this story)
**Dependencies:** BIO-IDN-001 (a genuine runtime dependency — person existence must be checked
before issuing a card), BIO-CRD-002 (sequencing only; no BIO-CRD-002 type or file is referenced
anywhere in this feature)

## Source hierarchy check

No conflict found between the Master Technical Specification, accepted ADRs and the approved
story/AC text. ADR-0001's identifier registry table was checked explicitly before design: it
names exactly six registered identifier types and states "a feature story may not invent a
prefix locally" — and its own `crd` (Credential) row states verbatim "a physical card issue is a
child record, not this id," directly anticipating this story's own shape. No story that mints a
real `crd_` Credential id is a declared dependency here, so `CardIssue` is keyed by a natural
composite key rather than assuming access to an aggregate this story cannot reach (see
`docs/features/bio-crd-005/README.md`). ADR-0004 §4.3's idempotency-key requirement was also
checked directly against this story's own endpoints before implementation — "card activation" is
named as one of exactly four required operations, and this story's own `ActivateAsync` is
literally that operation.

## AC-to-evidence map

- **AC01 (sequence starts 001, increments permanently per person/card class):**
  `ICardIssueRepository.AppendAsync`'s optimistic-retry loop, the same pattern BIO-CRD-004's own
  `SignatureAsset` versioning uses. `sequence_display` renders the "001, 002, 003…" form
  ADR-0001 §1.6 itself names verbatim. Tests: `CardIssueRepositoryTests.cs` (including a
  50-concurrent-issuance race-safety test), `CardIssueEndpointsIntegrationTests.cs`,
  `CardIssuanceHostChannelTests.cs` (AC01).
- **AC02 (sequence numbers never reused or reset):** no delete operation exists anywhere on
  `ICardIssueRepository` — retiring a card is `CardIssueStatus.Deactivated`, never a row removal,
  the same "governed deletion, no delete operation" principle `Person`'s own AC04 established.
  Tests: `CardIssueRepositoryTests.cs` (AC02).
- **AC03 (encode/print/verify may precede activation):** `EncodedAt`/`PrintedAt`/`VerifiedAt` —
  three independent, optional, set-once timestamps, settable in any order at any status other
  than `Deactivated`; activation succeeds whether or not any of them were ever recorded (the AC's
  own wording: "may", not "must"). Re-recording an already-recorded step is refused, not silently
  overwritten. Tests: `CardIssueEndpointsIntegrationTests.cs` (AC03).
- **AC04 (activation is transactional, prevents two active physical cards):**
  `CardIssueService.ActivateAsync` refuses activation when a sibling for the same (tenant,
  person, card class) already holds `Active`; the surrounding per-(tenant, person, card class)
  lock (`CardIssueLockRegistry`) — the SAME lock every other mutation against that key also
  acquires — makes the check-then-set a real transactional guarantee, not merely a race-prone
  check. `Idempotency-Key` is required and honoured (claim/replay/reuse-rejection), per ADR-0004
  §4.3's explicit naming of card activation. Tests: `CardIssueEndpointsIntegrationTests.cs`
  (including a genuine concurrent-activation race test asserting exactly one of two simultaneous
  attempts succeeds), `CardIssuanceHostChannelTests.cs` (AC04).
- **AC05 (provenance records template/photo/logo/signature/operator/printer/time):**
  `CardIssueProvenance` — captured once at issuance, immutable thereafter (no update operation
  exists for it), every named field present including a structured `AuditActor` operator (never
  a bare string, per ADR-0003). Tests: `CardIssueEndpointsIntegrationTests.cs`,
  `CardIssuanceHostChannelTests.cs` (AC05).

## Decisions and assumptions (read `docs/features/bio-crd-005/README.md` for the full reasoning)

- **No frontend UI** — this is an operator/production transaction workflow, not a design-time
  canvas; no AC describes one. Matches BIO-CRD-001's own backend-only precedent within this epic.
- **The first genuinely mutable, CAS-versioned aggregate in this epic** — every prior card-
  designer sibling (`PhotoMaster`, `BrandAsset`, `SignatureAsset`) is immutable/append-only.
  `CardIssue.Version` follows ADR-0001 §3.2's table exactly: absent `expected_version` is `428
  precondition_required`, a stale one is `409 concurrency_conflict` — a stricter reading than
  `PersonService.UpdateStatusAsync`'s own deviation (which maps an absent version to `422`
  instead), since this story follows the ADR directly as the higher authority.
- **No canonical identifier minted** — `CardIssue` is keyed by `(TenantId, PersonId, CardClass,
  SequenceNumber)`, citing ADR-0001's own anticipation of this exact shape in its `crd` registry
  row.
- **AC04's "at most one active" is scoped per (person, card class), not per person** — a person
  may plausibly hold simultaneously-active credentials of different classes; two different card
  classes for the same person each have their own independent active-card slot.
- **`DeactivateAsync` is a necessary, disclosed addition** — without it, AC04's own invariant
  could never be exercised (a second card could never be activated at all). Not named by any AC
  directly, but implied by "prevents two active physical cards" requiring an operator to have a
  real way to make room for the next one.
- **A full audit-atomicity apparatus** (lock registry + audit sink + `ExistsAsync`
  ambiguous-write verification), unlike BIO-CRD-004's leaner `SignatureAsset` — a deliberate,
  considered choice: `CardIssue` is genuinely mutable with a real security-relevant invariant the
  lock itself must enforce (not merely record), and this story records who performed each
  production step, exactly the kind of operationally-sensitive action ADR-0003 expects an audit
  trail for.
- **`Idempotency-Key` on activation only** — ADR-0004 §4.3 names exactly four operations; no
  other mutation in this feature carries the header requirement, matching the ADR's own scope
  precisely rather than over-applying it to every mutation.
- **A genuine cross-story runtime dependency, verified through the real packaged host** — unlike
  every prior card-designer sibling's "sequencing only" dependency line, this story really does
  call into BIO-IDN-001 (`IPersonExistenceGateway`, no `ProjectReference`, real adapter in
  `BioCrd005Module` wrapping `PersonService.GetAsync`).
  `tests/dotnet/Biolog.IntegrationTests/CardIssuanceHostChannelTests.cs` proves this through the
  REAL packaged host: a real tenant domain, a real registered person, a real card issued and
  activated, plus ADR-0001 §1.5's "no enumeration oracle" property (a real tenant-A person
  referenced through tenant B's host fails identically to a wholly unknown person id).

## Test results (commit `08441003cfd11e3ecfb13cc0804c3459c3d18b68`)

- `bash tests/features/bio-crd-005/BIO-CRD-005.test.sh` — 31/31 PASS
- `dotnet test tests/features/bio-crd-005/Biolog.Features.CardIssuance.Tests.csproj` — 26/26 PASS
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj --filter
  FullyQualifiedName~CardIssuanceHostChannelTests` — 5/5 PASS (real packaged host, real
  BIO-IDN-001/BIO-TEN-002 endpoints)
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` — 200/200 PASS (unaffected)
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` (full suite)
  — 109/109 PASS when run alone. Inside `scripts/quality.sh --full`'s combined parallel run,
  `ObservabilityTests` produced a single failure
  (`AC01_a_real_request_logs_correlation_and_tenant_scope_to_the_console_view`), the identical
  `System.ArgumentOutOfRangeException` inside `StringBuilder.ToString()` documented in every
  prior card-designer sibling's own builder evidence this session (BIO-PHO-002, BIO-CRD-002/003/
  004) — a pre-existing race in `ObservabilityTests`' own shared console-capture buffer under
  xUnit's parallel execution, in a file this story never touches, owned by BIO-FND-009. Not a
  BIO-CRD-005 regression.
- `pnpm --dir tests/frontend test:unit` — 133/133 PASS (unaffected — this story adds no frontend
  code)
- `pnpm --dir tests/frontend test:e2e` — 55/55 PASS (unaffected; verified against a scratch-port
  `next dev` instance on port 3102 in this worktree, since ports 3000/3100/3101 were each
  occupied by an unrelated stray process or this session's own other worktrees' dev servers)
- `bash scripts/check-module-project-references.sh` — PASS
- `python3 scripts/check-traceability.py --all` — PASS (24 implemented stories; this story is
  correctly outside the `governance/stories/*.json` gate, same as every project-per-feature
  sibling)
- `bash scripts/quality.sh` (base tier) — **PASS**, see `evidence/BIO-CRD-005/builder/quality-output.txt`
- `bash scripts/quality.sh --full` — every check this story owns passes: `BIO-CRD-005 card
  issuance harness` PASS, `.NET card issuance unit tests` PASS, `frontend unit tests` PASS
  (133/133, unaffected), `Playwright tests` PASS (55/55, unaffected), `.NET vulnerability audit`
  PASS, `.NET unit tests` PASS (200/200), `Flutter tests` PASS. The only failing line is `.NET
  integration tests`, entirely attributable to the pre-existing `ObservabilityTests` flake
  described above. See `evidence/BIO-CRD-005/builder/quality-full-output.txt`.

Full transcripts captured alongside this file.
