# BIO-OPS-002 — tenant/role switching and isolation acceptance

Status: **PASS — all BIO-OPS-002 acceptance criteria verified.**

Round 2 reviewed remediation `56927c7` at merged head `db17392`. The signed tenant claim is now used when both synthetic tenant mappings exist, and a context-change endpoint exists. Acceptance remains blocked because beta silently falls back to alpha when its optional mapping is absent, while the UI still exposes beta; and the audit call is best-effort into a process-memory sink, allowing a completed switch with no durable record. Full evidence is in `evidence/features/bio-ops-002/codex/REVIEW-Seyi-round2.md`.

Round 3 reviewed remediation `8c01d45` at merged head `3be38f5`. Beta mapping and audit-before-cookie ordering are fixed. AC04 remains blocked because the browser session can outlive the API's process-memory audit queue, and the bespoke context-change event violates ADR-0003's frozen envelope. Full evidence is in `evidence/features/bio-ops-002/codex/REVIEW-Seyi-round3.md`.

Round 4 reviewed `d3c6d95` and `203b41b` at merged head `9aebfbc`. The canonical audit shape and caller-controlled actor gap are fixed, and AC01/AC02/AC03/AC05 pass by code/test trace. Final acceptance is blocked only because the claimed Kemi addendum accepting the independent-lifetime audit residual is not present in the authoritative ADR or contract-freeze history. See `evidence/features/bio-ops-002/codex/REVIEW-Seyi-round4.md`.

Final governance closure landed in `7435abf` and `6d6ab93`, merged at `aa1c744`: ADR-0003 Decision 8 and contract-freeze snapshot 1.1.11 narrowly accept the synthetic demo-session restart residual without creating precedent for any real credential, grant, or authorization effect. The independent acceptance harness now passes AC01–AC05. Final evidence is in `evidence/features/bio-ops-002/builder/FINAL.md`.

This is an acceptance-only story. It must not repair cross-cutting tenant-resolution, session, shell, or audit architecture from its isolated `bio-ops-002` paths. Inspection of the accepted dependency chain found two product gaps before an honest passing acceptance harness could be written.

| Criterion | Result | Evidence |
|---|---|---|
| AC01 — switching tenant changes database/data context | **FAIL** | The shell reissues a signed token with the selected `tenantId`, but `DemoSessionTenantResolver.TryResolve` deliberately ignores that claim and always constructs a `TenantContext` for the single configured `WebSessionBridge:SyntheticTenantId`. Alpha and beta therefore reach the same backend tenant and repositories. Existing browser tests only prove React/mock-data changes. |
| AC02 — cross-tenant identifiers cannot be fetched | Not promoted to story PASS while AC01 is false | Host-channel repositories have existing isolation coverage, but the approved selector cannot establish two real backend tenant contexts for the required switching acceptance journey. |
| AC03 — role switching changes navigation/actions | **PASS at existing component/dependency level** | `SessionProvider` changes navigation permissions; the signed token changes the derived actor, and BIO-TEN-003's real bridge tests prove admin and viewer reach different permission outcomes in the fixed synthetic tenant. |
| AC04 — audit records context changes | **FAIL** | `startDemoSession` revokes the prior session, issues a token and sets a cookie. It writes no audit event. No tenant/role context-change audit endpoint or sink is invoked by the selector. The revocation store is session security state, not an audit trail. |
| AC05 — unknown host/tenant fails closed | **PASS at dependency level** | BIO-TEN-002's host resolver returns no tenant context for unknown/unverified hosts when no independently valid tenant channel is present. |

## Required product work

1. A Kemi-approved tenant-resolution design must make the selected tenant claim load-bearing, or the UI must stop presenting alpha/beta as real tenant switching. It must preserve cross-channel agreement and synthetic-data confinement.
2. Tenant and role changes must emit durable, actor-attributed audit records, including old/new context and outcome, through an owned backend boundary.
3. After those changes, BIO-OPS-002 can add the genuine two-tenant/two-role end-to-end acceptance harness, cross-tenant URL manipulation negatives, and unknown-host negative.

No shared product file was changed and `builder-complete` was not run, because doing so would falsely claim the acceptance story is implemented.
