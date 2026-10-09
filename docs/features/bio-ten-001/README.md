# BIO-TEN-001 — Control-plane tenant registry

**Owner:** Tunde — Identity & Tenant Engineer
**Wave:** W03 / W03.1
**Conflict group:** `tenant-registry`

## User story

As BTS platform administration, we need a control-plane tenant registry containing
configuration, licensing and routing metadata but no tenant operational records.

## Acceptance criteria and where each is proven

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Tenant CRUD is control-plane scoped | `TenantService`/`ITenantRegistry`/`TenantEndpoints` take no tenant context anywhere on their surface; `Tenant` carries no `tenant_id` field (ADR-0002 §1.4) | `ControlPlaneScopeTests`, `TenantServiceTests`, `TenantEndpointsIntegrationTests` |
| **AC02** | Operational person/credential data cannot be stored in control-plane tables | `Tenant`'s fixed, named property list (no free-form bag); `TenantJsonOptions.Strict` rejects any unrecognised JSON property (`UnmappedMemberHandling.Disallow`) | `ControlPlaneScopeTests.CreateTenantRequest_deserialization_rejects_operational_or_secret_shaped_fields` (date_of_birth, email, biometric_template, credential_secret, national_id, password) |
| **AC03** | Tenant status supports active/suspended/disabled | `TenantStatus` enum, exactly those three values; `PUT /tenants/{id}/status` | `TenantServiceTests.UpdateStatus_*` |
| **AC04** | Audit events record administrative changes | `TenantAuditEventBuilder` + `ITenantAuditSink`, awaited before a create/update/status-change/delete is treated as complete | `TenantAuditTests` |

## Design decisions and scope boundaries

Each of these is a deliberate, disclosed choice, not an oversight — matching this
codebase's existing style of naming a boundary explicitly (see ADR-0005 Decision 4.6)
rather than overclaiming or silently under-delivering.

- **In-memory store.** No database/ORM story has wired MySQL (or any persistence) into
  `api/Biolog.Api` yet — `Biolog.Api.csproj` carries no such package — and ADR-0005
  requires a real schema change to be *proposed* to Kemi and assigned a sequence number,
  not authored unilaterally by one feature story. `ITenantRegistry`/`ITenantAuditSink`
  are the seam a later, dedicated persistence story swaps a real implementation into.
- **Audit-then-commit under a per-tenant lock, verified rather than assumed on a throw
  (BIO-TEN-001-QA-01, four rounds).** Every
  mutating `TenantService` method holds an in-process lock scoped to the one tenant it is
  touching (`LockTenantAsync`) across its entire read-validate-audit-commit sequence, and
  audits *before* committing the mutation to `ITenantRegistry`, not after. Each round's fix
  is what exposed the next round's defect:
  - **Round 1** committed first, then audited, and rolled back on an audit failure (Seyi
    review, High). The rollback CAS was itself skippable by a concurrent writer: writer A
    commits a field change, blocks in a failing audit sink; writer B reads A's committed
    value and commits its own newer version on top; A's rollback then conflicts with B's
    version and is swallowed — A's unaudited field survives *permanently inside B's
    committed record*, with no event ever naming it
    (`evidence/features/bio-ten-001/codex/REREVIEW.md`).
  - **Round 2** audited before committing, so nothing uncommitted is ever visible to
    another writer — closing round 1's race. It introduced the mirror-image defect: the
    audit event durably asserts `Outcome = Allowed` before the commit runs, so if a
    *different* concurrent writer of the same tenant wins the commit first, the audited
    writer's own commit correctly returns `concurrency_conflict` — but its permanent audit
    event still falsely claims the change succeeded
    (`evidence/features/bio-ten-001/codex/REREVIEW-ROUND3.md`).
  - **Round 3 (current)** stops trying to order two independently-racing steps and instead
    removes the race between them: an in-process, per-tenant-id `SemaphoreSlim` (one per
    tenant, held for `TenantService`'s process lifetime) serializes every mutation of one
    tenant. By the time a candidate mutation's audit event is written, no other writer in
    this process could have raced ahead of it, so the subsequent registry commit is
    guaranteed to succeed exactly as the audit event describes — the audited outcome and
    the committed outcome can no longer disagree. This works because
    `BioTen001Module` registers `TenantService` as a DI **singleton**: there is exactly one
    instance, and therefore one shared lock table, serving every request in the process.

  - **Round 4** closed a defect round 3 shared with round 2: `ITenantAuditSink.WriteAsync`
    throwing does not *prove* the event was never durably persisted — a real store can
    commit a write and then fail only on the acknowledgement (a timeout, a dropped
    connection after commit). Treating every throw as "definitely did not happen" (rounds
    2/3's behaviour) could leave a durably-written `ALLOWED` event with no corresponding
    commit — the identical false-success shape, from a different cause
    (`evidence/features/bio-ten-001/codex/REREVIEW-ROUND4.md`). `AuditAsync` now resolves an
    ambiguous throw by checking ground truth: a new `ITenantAuditSink.ExistsAsync(eventId)`
    is called only when `WriteAsync` throws, and its answer decides the outcome —
    confirmed-present is treated as success (the caller's subsequent commit, protected by
    the same per-tenant lock, is guaranteed to then make the event's claim true);
    confirmed-absent throws `audit_unavailable` with nothing committed. If `ExistsAsync`
    *itself* throws, that is a genuine, total outage of the audit store, not one ambiguous
    write — the same fail-closed `audit_unavailable` ADR-0002 §5 already accepts for a
    missing or unreachable sink, and the narrowest boundary this can honestly close without
    a real cross-store transaction. This mirrors ADR-0002 §5's own framing exactly:
    "'Written' means the sink's promise has settled, not that the call was made."

    Round 4 also caught a real bug in round 3's own proof, worth naming so it isn't
    repeated: `DelayFirstCallAuditSink` was installed on the `TenantService` instance used
    for the *seed* `CreateAsync` call too, so its one forced delay was consumed by setup and
    the two "concurrent" writers never actually contended — the test passed for the wrong
    reason (pure sequential execution), not because the lock did anything. Verified the fix
    by temporarily removing the lock and confirming the test then fails (two audit events
    instead of one) before restoring it — the general lesson: an in-memory async test that
    "passes" proves nothing about a race unless something is forced to genuinely yield, and
    that claim is worth spot-checking by breaking the fix on purpose once.

  See `TenantAuditTests.*_is_never_committed_when_the_audit_sink_fails` (round 2's proof),
  `A_failing_writers_field_never_survives_in_a_later_writers_committed_version` (reproduces
  round 1's exact interleaving),
  `Two_genuinely_concurrent_writers_of_the_same_tenant_never_produce_a_false_allowed_audit_for_a_rejected_commit`
  (reproduces round 3's exact interleaving) and
  `An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly`
  / `A_total_audit_store_outage_where_even_verification_fails_still_fails_closed` (round 4).

  **What this does not, and cannot, cover:** the lock is per-process — it makes every
  mutation of one tenant, across every concurrent request this instance is serving, strictly
  sequential, but cannot protect against a second OS process or a second deployed instance
  racing the same store, because there is no in-memory store shared across processes to
  protect. And even with write-ambiguity resolved by verification, a **process failure**
  between a *confirmed*-durable audit write and the registry commit that immediately follows
  it remains a true crash-consistency window: no caller-side code can close that without a
  durable write-ahead log or transactional outbox spanning both stores, which is
  infrastructure that exists nowhere in this codebase yet. Kemi is independently drafting a
  cross-cutting ADR-0003 amendment for exactly this class of gap (the identical shape
  recurred in Emeka's BIO-DEV-001); building bespoke cross-store transaction infrastructure
  unilaterally inside this one feature story's owned subtree, ahead of that shared decision,
  would itself be the kind of "shared code placed in one story's subtree by convenience"
  `SHARED-CONTRACT-FREEZE.md` BIO-ARC-006-AC03 reserves for Kemi to allocate. A later
  persistence story backed by a real database inherits this exact obligation and must
  provide the equivalent guarantee (a row lock or serializable transaction, or an outbox) at
  that layer.

  **Post-round-4 hardening (Kemi heads-up, 2026-09-22, cross-referencing Seyi's finding in
  Emeka's BIO-DEV-001 / QA-03):** once `AuditAsync` returns having confirmed the audit event
  is durable, each mutation's final `registry.CreateAsync`/`ReplaceAsync` call now passes
  `CancellationToken.None`, not the caller's live token. Without this, a cancellation signal
  arriving in the gap between the confirmed audit write and the commit could abort the
  commit itself, leaving a durable `ALLOWED` event for a change that never reached the
  store — the same false-success shape as the rest of this section, just triggered by
  cancellation instead of a store fault. `InMemoryTenantRegistry` does not check its token
  today, so this bug is not currently live here — it is closed pre-emptively so the
  guarantee is designed-in rather than an accident of the current in-memory implementation,
  and survives unchanged if the registry ever gains real cancellation support (e.g. a
  DB-backed store with a cancellable driver call). See
  `TenantAuditTests.A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit`.
- **No hard DELETE.** `DELETE /tenants/{id}` is a soft delete: it sets `status` to
  `disabled` and keeps the record. ADR-0001's append-only, audit-everything ethos has no
  described procedure for physically erasing a tenant boundary, and doing so would orphan
  every tenant-plane binding ADR-0002 describes with no documented repair path.
- **No `Idempotency-Key`.** ADR-0004 §4.3 names card activation, production completion,
  offline sync intake and webhook intake as the endpoints that require it. Admin tenant
  CRUD is none of those; a later story can opt this surface in the same way ADR-0004
  allows any write endpoint to.
- **No RBAC enforcement.** `BIO-IDN-006` ("rbac-core", W04) is the story that introduces
  RBAC. Until then, every mutating endpoint requires an explicit `X-Actor-Id` header (and
  optional `X-Actor-Type`) so the audit trail records a real, asserted identity instead of
  a fabricated one — see `ActorResolver`'s header note. This is a disclosed seam, not a
  claim that header-based actor naming is how the platform authenticates administrators.
- **Local, schema-conformant helpers instead of a shared .NET binding.** No .NET binding
  generator exists yet for `contracts/schemas/*.json` (`contracts/README.md`: "Bindings
  for .NET and Flutter are generated from the same schemas by the stories that introduce
  those surfaces"). `CanonicalId`, `ProblemResponse`, `TenantAuditEventBuilder` etc. are
  local implementations cross-checked field-for-field against the JSON Schemas, scoped to
  exactly what this feature needs (e.g. only the `ten_`/`evt_` identifier types) — not a
  restatement of the general contract, and not a new shared authority.
- **Routing metadata is a coarse tag, never a binding key.** `Tenant.RoutingRegion` (e.g.
  `"eu-west"`) is a placement hint. ADR-0002 §4.1 requires the real tenant→database
  binding to be *looked up*, never supplied over any channel; this story does not
  implement `TenantDataBinding` and never exposes a database name, schema, host or
  connection string.

## Integration seam — Program.cs wiring is a follow-up, not done by this story

`BioTen001Module` implements the same `IBiologModule` shape every module in
`api/Biolog.Api` uses, per `api/README.md`'s instruction that feature endpoints "register
into this same module host." It is **not** wired into `api/Biolog.Api/Program.cs`'s
module array by this story: `Program.cs` and `Biolog.Api.csproj` are outside
`api/features/bio-ten-001/**`, this story's only owned path
(`governance/feature-path-ownership.json`), and
`docs/architecture/SHARED-CONTRACT-FREEZE.md` BIO-ARC-006-AC03 requires Kemi to allocate
any change to shared code rather than a feature story editing another story's subtree.

The one remaining step for a real end-to-end deployment: add
`new Biolog.Features.TenantRegistry.BioTen001Module()` to the module array in
`api/Biolog.Api/Program.cs`, and a `ProjectReference` from `Biolog.Api.csproj` to
`api/features/bio-ten-001/Biolog.Features.TenantRegistry.csproj`. Every endpoint is
proven end-to-end in the meantime via `tests/features/bio-ten-001/TenantEndpointsIntegrationTests.cs`,
which self-hosts `BioTen001Module` directly through `Microsoft.AspNetCore.TestHost`.

## API surface

All routes are versioned per ADR-0004 §1.1 and live under a `/control-plane` prefix,
mirroring ADR-0002 §1.4's distinct control-plane context:

| Method | Path | Notes |
|---|---|---|
| `POST` | `/api/v1/control-plane/tenants` | Create. Requires `X-Actor-Id`. |
| `GET` | `/api/v1/control-plane/tenants/{id}` | 404 `tenant_not_found` if absent. |
| `GET` | `/api/v1/control-plane/tenants?limit=&cursor=&status=` | Cursor pagination (ADR-0004 §5.1); `status` filter allowlisted to `active`/`suspended`/`disabled`. |
| `PATCH` | `/api/v1/control-plane/tenants/{id}` | Requires `version` (optimistic concurrency) and `X-Actor-Id`. |
| `PUT` | `/api/v1/control-plane/tenants/{id}/status` | Requires `version`, `status`, `X-Actor-Id`. |
| `DELETE` | `/api/v1/control-plane/tenants/{id}?version=` | Soft delete → `status: disabled`. Requires `X-Actor-Id`. |

Every response carries `X-Correlation-Id` (ADR-0004 §3.2); every error is
`application/problem+json` shaped per `contracts/schemas/problem-response.schema.json`.

## Tenant fields

`name`, `status` (`active`/`suspended`/`disabled`), `license_plan`, `license_seats`,
`routing_region` — plus `id`, `version`, `created_at`, `updated_at` per ADR-0001 §3.1's
mutable-aggregate envelope. No `tenant_id` field (see AC01 above).
