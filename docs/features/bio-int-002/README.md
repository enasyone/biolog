# BIO-INT-002 — Implement SIS/HR/ERP import framework

**Owner:** Tunde — Identity & Tenant Engineer
**Dependencies:** BIO-IDN-001 (universal PERSON, DONE), BIO-ARC-004 (ADR-0004 API/error/idempotency conventions, DONE — a documentation ADR, not a code feature).

## User story

As tenants, we need controlled imports/syncs from SIS, HR, payroll, ERP and CSV/Excel.

## AC → evidence map

| AC | Statement | Where it lives |
| --- | --- | --- |
| AC01 | Source mappings are versioned. | `Domain/ImportSourceMapping.cs` (CAS-versioned), `ImportMappingService` |
| AC02 | BIOLOG can be master or downstream per integration. | `Domain/ImportSourceMapping.cs`'s `SyncDirection`, enforced in `ImportBatchService.PreviewAsync`/`ApplyAsync` |
| AC03 | Dry-run/conflict preview exists. | `ImportBatchService.PreviewAsync` |
| AC04 | Imports do not silently overwrite conflicting identity evidence. | `ImportBatchService.ClassifyRowAsync` — see "The central design decision" below |
| AC05 | Idempotent reprocessing is supported. | `Domain/IdempotencyStore.cs` (copied from BIO-MOB-003's own precedent), `ImportBatchService.ApplyAsync` |
| AC06 | `/administration/integrations` + `/settings/authentication` connected through `IntegrationService`. | `/administration/integrations` is fully live; `/settings/authentication` is an explicitly documented HYBRID boundary — see below. |

## The central design decision: AC04 and why there is no profile-mutator

BIO-IDN-001's own `PersonService` exposes exactly two mutations: `RegisterAsync` (create) and
`UpdateStatusAsync` (lifecycle only — PRE_ENROLLED → VERIFIED → ... → ARCHIVED). There is **no
method anywhere** to correct a PERSON's own name/DOB/etc. fields after creation — verified
directly against `api/features/bio-idn-001/PersonService.cs`, not assumed.

This story therefore never overwrites an existing PERSON's profile fields, by construction, not
merely "tries not to":

- **No existing link for an external key** → a brand-new PERSON is created via
  `IPersonGateway.CreateAsync` (a real, uncontested gap — this path has always existed on
  BIO-IDN-001). Outcome `created`.
- **Already linked, every incoming field matches the current PERSON value (or the current value
  is simply unset)** → nothing is written at all, since even a pure "fill in a blank field"
  requires a PERSON-mutation method this story does not have. Outcome `unchanged` — detected,
  never applied.
- **Already linked, at least one incoming field disagrees with the current PERSON value** →
  recorded as a `FieldConflict` (field, existing value, incoming value), never written, surfaced
  to a human reviewer via the batch's own row outcomes. Outcome `conflict`.

This fully satisfies AC04 (nothing is EVER silently overwritten) without touching BIO-IDN-001's
own shared `Person.cs`/`PersonService.cs`, which would need Kemi's recorded approval.

**Disclosed, deliberate scope boundary:** conflict *resolution* (actually applying a human-
reviewed correction, or even a harmless fill-in of a blank field) needs a PERSON profile-mutator
this story is not authorized to add. That is future scope for whichever story next owns
BIO-IDN-001's own aggregate — not something built here, and not silently worked around by writing
into PERSON through a side channel.

## AC02: master or downstream per integration

`ImportSourceMapping.SyncDirection` is `source_is_master` (the normal inbound-import flow AC01/
AC03/AC04/AC05 describe) or `biolog_is_master`. When a mapping is `biolog_is_master`, BIOLOG is
the system of record for that integration: both `PreviewAsync` and `ApplyAsync` reject the call
outright (`ImportError.BiologIsMasterForThisIntegration`, HTTP 409) rather than silently accepting
and discarding inbound data. This is the only meaningful, testable effect a master/downstream flag
can have on a framework whose only operation is "accept inbound data" — there is no existing
outbound-sync direction in this story's scope to make symmetric.

## AC05: idempotent reprocessing

`Domain/IdempotencyStore.cs` is a fresh, project-local copy of the exact same `IIdempotencyStore`
shape BIO-MOB-003's own `OfflineMutationIntakeService` already established — "each feature project
owns its own small primitives rather than a cross-feature reference" is the explicit, stated
convention in this codebase, not an oversight. `ApplyAsync` claims the key, compares the stored
request fingerprint (a SHA-256 hash of the mapping id plus every row's own canonical content) and:

- a matching fingerprint on a `Completed` claim replays the original batch result without
  reprocessing any row;
- a mismatched fingerprint on the SAME key is rejected (`idempotency_key_reused`, HTTP 409) — a
  client bug, never silently treated as the original request;
- a failure before completion releases the claim so a genuine retry can proceed.

`PreviewAsync` never touches the idempotency store at all — it is a pure read (AC03's own
"preview" guarantee), so there is nothing to make idempotent.

### Post-review remediation round 1 (BIO-INT-002-F01, Seyi round-1 FAIL): concurrent identical claims both executed

The original `ApplyAsync` checked only `claimed.State == Completed` before deciding whether to
execute. A shared `InFlight` record — the one `Claim` correctly returns to EVERY caller racing on
the same key — looked identical whether this caller had just created it or another concurrent
caller already owned it, so a second caller fell through, re-executed, and silently overwrote the
first caller's durable result. Reproducible exactly as Seyi described: two concurrent callers for
the same tenant/mapping/key/rows, one completes first, the other's own completion then clobbers it.

Fixed two levels down:

- `IdempotencyClaim` (a new `readonly record struct` wrapping the existing `IdempotencyRecord`)
  adds `IsNewClaim` — true ONLY for the single caller whose attempt actually won the race to create
  the key's record, detected in `InMemoryIdempotencyStore.Claim` via reference-identity on the
  object the factory delegate itself constructed (`ReferenceEquals(stored, created)`), never by
  inspecting the record's own content, which is identical either way for a genuine replay.
- `ApplyAsync` now loops: claim, and if `IsNewClaim` is false (someone else's claim is in flight),
  wait — bounded by an injectable `_idempotencyWaitTimeout` (production default 5s, short in
  tests) — re-attempting its own `Claim` each poll interval, which naturally becomes a fresh
  ownership attempt if the original owner's attempt fails and releases the key. If the bound is
  exceeded, the caller fails closed with a new `ImportError.IdempotencyWaitTimeout`
  (`idempotency_wait_timeout`, 503) — ADR-0004/BIO-ARC-004's own documented "claim-loser waits
  bounded then `idempotency_wait_timeout`" contract, not an invented behavior.

Two new deterministic tests: `ApplyAsync_two_genuinely_concurrent_callers_with_the_same_idempotency_key_execute_exactly_once`
(a `TaskCompletionSource` start-gate releases two real concurrent `Task.Run` callers together,
proving exactly one PERSON-create / one audit record / one stable batch id, never two) and
`ApplyAsync_a_second_caller_times_out_if_the_owners_claim_never_completes` (a short, injectable
wait bound proves the 503 timeout path fires deterministically and fast, without ever executing a
second time, rather than waiting forever).

**Cross-cutting note, not fixed here:** `api/features/bio-mob-003/OfflineMutationIntakeService.cs`
has the identical claim-ownership gap in its own copy of this exact pattern (only checks `State`,
never distinguishes an already-in-flight claim from a newly-owned one). That is a separate,
already-accepted story's own file — not touched by this story — flagged to god/Kemi as a
cross-cutting advisory instead.

### Post-review remediation round 1, Zainab's own refinement (ZAINAB-INT-002-F01): claim scope and a per-row duplicate-PERSON risk

Zainab converged on the same claim race Seyi found, but identified two gaps the round-1 fix above
did not close:

1. **The claim was only released on a failure inside the rows loop.** Batch construction, the
   audit write, and `batchRepository.CreateAsync` all happened AFTER that `try`/`catch` —
   completely unguarded. A failure in any of them propagated with neither `Release` nor `Complete`
   ever called, leaving the idempotency key stuck `InFlight` forever; every future caller
   (including a genuine retry) would wait out the bound and fail with 503, permanently. Fixed: the
   same `try`/`catch` now covers the full create→link→batch→audit→persist sequence — only
   `idempotencyStore.Complete` (reached once everything has durably succeeded) exits without
   releasing.
2. **A per-row partial failure could duplicate a PERSON.** `personGateway.CreateAsync` (no delete
   capability exists anywhere on BIO-IDN-001) could succeed, then that SAME row's
   `linkStore.CreateAsync` could throw — the (now-widened) catch releases the claim, and a genuine
   retry's `ClassifyRowAsync` finds no link for that external key, so it classifies the row as
   `Created` AGAIN and mints a SECOND, orphaned PERSON. Every retry would compound another orphan.

Fixed by `IOrphanedPersonLedger` (`api/features/bio-int-002/Domain/IOrphanedPersonLedger.cs`) — the
exact "adoption" backstop BIO-VIS-002 established for its own analogous problem (its F03/F04
remediation): the new PERSON's id is durably recorded in this story's own in-memory ledger
immediately once created, BEFORE the link-write that could fail. A retry's row processing checks
the ledger FIRST — if an entry already exists for this `(tenant, mapping, external key)`, it
*adopts* that PersonId (creates the link against it) instead of minting a second one.

Three new deterministic tests prove: a partial failure releases the claim cleanly (not stuck); a
retry after that partial failure adopts the SAME PersonId rather than creating a second one
(`PersonGateway.CreateCallCount` stays at 1 across both attempts); and a failure AFTER the rows
loop (the audit write) also releases the claim rather than leaving it permanently stuck.

### Post-review remediation round 1 (BIO-INT-002-F02, Zainab round-1 FAIL): the disable switch did nothing

`ImportSourceMapping.Active` existed but neither `PreviewAsync` nor `ApplyAsync` ever checked it —
an actor retaining the apply permission could keep importing through a mapping an administrator had
disabled. Fixed: both methods now reject with `ImportError.MappingDisabled`
(`import_mapping_disabled`, 409) at the same point as the existing `BiologIsMaster` check, right
after the mapping is loaded. Tests prove both paths reject while disabled and that re-enabling
(`Active = true`) restores normal behavior.

**Disclosed, out-of-scope note (Zainab's own non-blocking observation):** conflict detection only
applies to an ALREADY-linked external key. A brand-new external key always creates a new PERSON,
even if its identity fields happen to match an existing PERSON exactly — this story makes no
cross-source identity-matching/reconciliation decision at all. That is an accepted, explicit scope
boundary for this story, not an oversight: deciding whether two different source systems referring
to the same real person should be matched or should create separate PERSONs is a governed policy
question for a later, dedicated reconciliation workflow, not something this import framework
decides implicitly.

### Post-review remediation round 2 (Seyi round-2 FAIL / Zainab round-2 FAIL): three findings on the round-1 fixes

Both reviewers confirmed round 1's three fixes work for their own sequential/basic cases, then found
three further gaps once they pushed on cancellation, changed content, and lock ordering.

**Finding A — orphan adoption was cancellation-unsafe and content-blind.** `orphanedPersonLedger
.RecordAsync` used the caller's own `CancellationToken` right after a durable PERSON create; a
cancellation landing in that exact window lost the only reference to the new PERSON, and a retry
would mint a second one — the very bug the ledger exists to prevent. Separately, adopting an orphan
never checked whether the orphan's own current fields still agreed with the incoming row — a later
request with CHANGED content could silently link to a mismatched identity and report a clean
`Created`. Fixed: the record write now always uses `CancellationToken.None`, with the same
ambiguous-failure verification `AuditAsync` already establishes (on throw, check via `FindAsync`
whether it landed anyway before treating it as a genuine failure). Adoption now runs the exact same
field-comparison `ClassifyRowAsync` already uses for an already-linked row (extracted into a shared
`CompareFields` helper) before linking — a disagreement becomes `RowOutcome.Conflict`, the orphan
stays unresolved, and the mismatched PersonId is never linked.

**Finding B — a late batch/audit failure lost "Created" provenance on retry.** If a row's PERSON and
link were already durably created, but the LATER batch-construction/audit/persist step then failed,
the claim released for a genuine retry — whose own `ClassifyRowAsync` saw the link already present
and reported `Unchanged`, losing the fact that this exact idempotency attempt originally created a
brand-new identity. Fixed by `IImportRowProvenanceLedger` (new, `Domain/IImportRowProvenanceLedger
.cs`): keyed by `(TenantId, IdempotencyKey, ExternalKey)` — deliberately scoped to ONE idempotency
attempt, never globally — it records each row's final decided outcome the moment it is decided. A
retry under the SAME key checks this ledger first and replays the outcome verbatim, with no
re-classification and no re-write; a genuinely different request (a different key) is unaffected.
Best-effort by design (see its own header) — a recording failure here never aborts an
already-successful row, it only narrows how much provenance a future retry can recover, which is
strictly less severe than duplicating a PERSON.

**Finding C — the disable check was a TOCTOU race.** `ApplyAsync` read and checked `mapping.Active`/
`SyncDirection` BEFORE acquiring the per-mapping lock `ImportMappingService.UpdateAsync` uses to
commit a disable — a concurrent disable could commit between that read and the lock acquisition,
letting the caller proceed on a stale "still active" snapshot. Fixed: the early mapping fetch now
exists ONLY to fail fast on an unknown mapping id, never to gate execution. The authoritative
`Active`/`SyncDirection` check happens exactly once, immediately after the per-mapping lock is held,
against a FRESH read — so whatever the most recently committed config actually is is always what
gates the write. A deterministic (not timing-based) test proves this: a test-only
`GateableImportSourceMappingRepository` holds ApplyAsync's own early (already-fetched, Active=true)
read suspended while a concurrent disable commits through the same lock, then releases it — proving
the decision is never made on the stale snapshot.

### Post-review remediation round 4 (Seyi round-3 FAIL = Zainab round-3 FAIL finding 1; Zainab round-3 FAIL finding 2, ZAINAB-INT-002-F04; Zainab's own residual durability note)

**Finding 1 — the claim was stranded if lock-acquisition or the fresh mapping guard threw.** Round
2's finding-C fix moved the authoritative `Active`/`SyncDirection` re-read and its guard checks to
AFTER the per-mapping lock was acquired — but still BEFORE the release-on-failure `try`/`catch` that
already wrapped the rows loop and batch/audit/persist. So a disabled mapping, a direction flipped to
`biolog_is_master`, the mapping disappearing, or cancellation while acquiring the lock/doing that read
all threw AFTER this caller won the idempotency claim but BEFORE anything could release it — stranding
the key `InFlight` forever, even after an administrator fixed the underlying problem (re-enabled the
mapping, reverted the direction). Fixed: lock acquisition, the fresh mapping read, and both guard
checks are now ALL inside the same `try`/`catch` as the rows loop and batch/audit/persist — nothing
that can throw after claim ownership sits outside it. No concurrency is even needed to reproduce this
one: a plain, already-disabled mapping at the time of the call already took the stranding path.

**Finding 2 (ZAINAB-INT-002-F04) — the provenance ledger's key was too narrow and could replay the
WRONG row.** `IImportRowProvenanceLedger` was keyed only by `(TenantId, IdempotencyKey, ExternalKey)`
— missing `MappingId`. Two distinct bugs: (a) an idempotency key is scoped per-operation, not
per-mapping, so after one attempt failed and released its key, that SAME key could legitimately be
reused later against a DIFFERENT mapping; a matching external key under the new mapping would then
incorrectly replay the FIRST mapping's recorded outcome/PersonId, skipping classification and link
creation entirely. Fixed by adding `MappingId` to the ledger's key. (b) Two rows in ONE request
sharing an external key: after row one was recorded, row two would immediately replay row one's
just-recorded outcome without ever evaluating its OWN content, silently defeating conflict detection
for row two. Fixed not in the ledger but in `ImportBatchService` itself: both `PreviewAsync` and
`ApplyAsync` now reject a request containing duplicate external keys up front
(`ImportError.DuplicateExternalKeysInRequest`), before any row is processed — the simpler alternative
Zainab's own review explicitly offered over a per-row content fingerprint. Why this combination is
sufficient without a fingerprint: `ComputeFingerprint(mappingId, rows)`'s own whole-batch check (run
before the claim loop) already rejects a reused idempotency key whose `(mappingId, rows)` content
differs AT ALL from the original claim's — so by the time the provenance ledger is ever consulted,
this exact `(idempotencyKey, mappingId, rows)` combination is already confirmed identical to whatever
the original attempt was for. Once cross-mapping reuse and within-request duplicate keys are both
closed, there is no remaining scenario where `(tenantId, idempotencyKey, mappingId, externalKey)`
could legitimately correspond to two different row contents.

**Residual durability note — provenance recording is no longer silently best-effort.** Zainab's point:
if preserving `Created` provenance across a retry is presented as a guaranteed remediation (round 2's
fix claimed exactly that), silently swallowing a genuine recording failure undermines the guarantee.
`RecordRowProvenanceBestEffortAsync` is replaced by `RecordRowProvenanceSafelyAsync`: the same
ambiguous-failure verification `RecordOrphanedPersonSafelyAsync` already establishes (try, catch,
`FindAsync` to check whether it actually landed despite the throw) — but if verification confirms it
genuinely did NOT land, this now RE-THROWS, failing the whole attempt and releasing the idempotency
claim, rather than silently completing with a provenance gap.

### Post-review remediation round 5 (ZAINAB-INT-002-F05): a genuine provenance-write failure still lost Created provenance

Round 4's durability fix made a genuine provenance-recording failure fail the whole attempt — but
`RecordRowProvenanceSafelyAsync` still ran AFTER `linkStore.CreateAsync` had already durably created
the link for that row. So releasing the claim on a provenance-write failure left a row with a REAL
link but NO provenance record. A retry's `ClassifyRowAsync` then found that link and returned
`Unchanged` — it never re-entered the `Created` branch, so it never consulted the orphan ledger that
could have shown this exact PERSON was created by the failed attempt. The final batch/audit still
lost the original `Created` provenance, exactly the invariant round 2 through 4 were meant to close.

Fixed by REORDERING, not by adding new machinery: provenance is now recorded BEFORE the link is
created, for every row, right when its outcome reaches final form. If the provenance write fails now,
no link exists yet — a retry finds no link, correctly re-enters the `Created` branch, and adopts the
already-created PERSON from `IOrphanedPersonLedger` (round 2's own adoption path) instead of losing
its provenance. One consequence of this reordering needed its own guard: the replay branch (a
provenance record already exists for this exact idempotency attempt) now also idempotently ensures
the link exists before continuing — covering the case where THIS attempt's own link write, not its
provenance write, is what failed and triggered the retry. The updated regression test asserts the
retry returns exactly one `Created` outcome (not `Unchanged`) for the ORIGINAL PersonId, and the
resulting batch's own audit record reports `created_count = 1`, not `0`.

### Post-review remediation round 6 (BIO-INT-002-F06, Seyi round-6 FAIL): an architecture-checkpoint finding

Round 5 closed the "provenance before link" ordering for an IDENTICAL retry. Seyi's round-6 review
found the gap had simply moved one step further downstream, and explicitly invoked the architecture
checkpoint this whole remediation arc had been building toward: `IIdempotencyStore.Release` deletes
the claim record AND its own stored `RequestFingerprint` entirely, but `IImportRowProvenanceLedger`'s
recorded row outcomes carried no fingerprint of their own and were never cleaned up. So a released
attempt's AC05 content-reuse protection vanished along with the claim, while the attempt's own
recovery state (the orphan ledger, the provenance ledger) survived untouched. A LATER request
reusing the same (tenant, idempotency key, mapping, external key) with genuinely DIFFERENT row
content could win a fresh claim (there was no live record left to compare a fingerprint against)
and land straight in the replay branch, which trusted the stale provenance without ever
re-evaluating content - silently linking the new request to the ORIGINAL attempt's PERSON and
reporting a clean `Created`, reintroducing AC04's exact silent-overwrite problem through a path none
of rounds 2 through 5 had touched.

This was the third consecutive round where closing one failure-recovery point in the create-orphan-
provenance-link-audit chain opened another just downstream of it (orphan ledger -> provenance key ->
provenance-vs-link ordering -> now the fingerprint surviving past the claim's own deletion). Per the
checkpoint, this was escalated directly rather than patched a fourth time. God's recorded decision:
extend the existing durable-ledger pattern with one more field, not a cross-store transactional
rewrite - this codebase has no shared transactional substrate across the claim store, orphan ledger,
provenance ledger, link store and audit sink, and building one would be a Kemi-scale investment the
same proportionality standard already applied to BIO-VIS-002's own outbox-pattern question. A
follow-up, more specific course-correction then replaced an initially-drafted separate new ledger
with the simpler mechanism actually shipped: carry the fingerprint as one more field on the EXISTING
`IImportRowProvenanceLedger` entry, rather than introduce a second ledger.

Fixed: `IImportRowProvenanceLedger.RecordAsync` now also takes the recording attempt's own
whole-batch `ComputeFingerprint` value (already computed once per `ApplyAsync` call - no new per-row
hash), and `FindAsync` returns both the outcome and the fingerprint it was recorded under
(`ImportRowProvenanceEntry`). The replay branch in `ApplyAsync` now compares the stored fingerprint
against the CURRENT request's own fingerprint before trusting a replay: a match proceeds exactly as
every prior round's own resume-after-failure tests already prove; a mismatch falls through to fresh
classification instead of a silent adopt - which, since the orphan ledger entry is still genuinely
unresolved (no link exists for that external key yet), lands in round 2's own content-aware adoption
check and correctly surfaces a `Conflict` rather than a clean `Created`. No new ledger, no change to
the shared `IIdempotencyStore` contract, no Kemi.

**Round cap.** This is round 6's fix. If a future review finds yet ANOTHER distinct gap in this same
create/orphan/provenance/link/audit recovery chain, the next action is to stop and escalate directly
rather than attempt a further local patch - at that point it would mean the recovery model itself
needs a from-scratch redesign with Kemi, not another incremental fix.

### Post-review remediation round 7 (BIO-INT-002-F07, Zainab round-6 FAIL, confirmed by Seyi): a fingerprint-collision bug, not another recovery-chain gap

Round 6's own fix closed F06 by carrying the attempt's fingerprint alongside each recorded
provenance entry. Zainab's round-6 review found that `ComputeFingerprint` itself - the thing round
6's whole fix depends on - was not injective: it joined external keys, field names and field values
with raw, unescaped `; : , =` delimiters. Distinct content could serialize to the identical string.
For example, a field map `{"a": "b,c=d"}` and `{"a": "b", "c": "d"}` both produced the literal
segment `a=b,c=d`, hashing identically. After a claim release, a request carrying one of these
colliding-but-different payloads could pass round 6's own fingerprint-match check and still land in
the replay branch with stale, disagreeing provenance - defeating the exact protection round 6 had
just built.

Seyi initially read this as triggering the round cap above (a third-in-a-row downstream recovery
gap) and briefly reversed an earlier provisional pass to say so. God's recorded decision after both
reviewers weighed in: this does NOT trigger the cap. The cap is about the create/orphan/provenance/
link/audit SEQUENCING and atomicity design recurring as the source of each new gap (rounds 2
through 6); F07 is a narrow, well-understood, mechanical bug one layer below that - a non-injective
serialization in one helper function, with an unambiguous correct answer, unrelated to what gets
retained, released, or ordered across the recovery chain's stores. Proceeded as a normal fix.

Fixed: `ComputeFingerprint` now hashes a canonical JSON serialization (`mappingId` plus rows sorted
by external key, each row's fields sorted by key) instead of hand-rolled delimiter concatenation.
JSON's own string quoting and escaping rules make the serialized form unambiguous regardless of
what characters appear in an external key, field name, or field value - two distinct `(mappingId,
rows)` inputs can no longer produce the same canonical text. `ComputeFingerprint` was widened from
`private` to `internal` (the same test-visibility convention `ReconcileStrandedCardsAsync` uses in
BIO-VIS-002) so tests can assert on its output directly, not only through `ApplyAsync`'s observable
behavior. Added direct collision tests reproducing both of the old scheme's distinct ambiguities (a
field value embedding the field-separator syntax, and a field value embedding the row-separator
syntax) plus an end-to-end regression proving a release-then-reuse with one of these
formerly-colliding payloads is now correctly routed through fresh classification rather than a
silent replay.

## Why `ExternalIdentityLink` is this story's own table, not a BIO-IDN-001 field

BIO-IDN-001's `Person` carries no external-system correlation of any kind (by its own design —
see that story's README). `ExternalIdentityLink` (tenant, mapping, external key → person id) is
this story's own, purely additive correlation table, the same "a side table this story owns to
track its own relationship to another story's aggregate, without writing into that aggregate's
own repository" pattern BIO-VIS-002's `IStrandedCardLedger` already established.

## AC06: why `/settings/authentication` stays HYBRID

Both `/administration/integrations` and `/settings/authentication` share the same frontend
`servicePort: 'IntegrationService'` in `web/lib/route-manifest.ts`. `/administration/integrations`
is unambiguously this story's own domain and is now fully live (`web/features/integrations/live.ts`
calls this story's own real `/api/v1/integrations/*` backend). `/settings/authentication`'s own
primary actions ("Save authentication policy", "Test sign-in") are BIO-INT-001's auth-provider/SSO
domain — a different story's scope, and BIO-INT-001 is notably **not** listed in this story's own
dependencies. BIO-INT-002 does not reach into another story's domain to wire that page live; AC06
itself explicitly permits "an explicitly documented HYBRID state" for exactly this situation. The
page's own file carries a one-line disclosure of this boundary; nothing about its existing
behavior otherwise changed.

### A real conflict found and fixed, and a stale check left disclosed, not patched

Extending `web/features/integrations/live.ts` initially repointed its base `createLiveAdapter`
call from `/api/v1/integrations/primary-action` to this story's own `/mappings` endpoint — that
would have silently broken BIO-INT-001's own already-accepted `primaryAction()` wiring (its own
"Test connection" secondary action, per `route-manifest.ts`). Fixed: the base adapter's endpoint
is untouched; this story's new methods (`listMappings`/`createMapping`/`previewImport`/
`applyImport`) are purely additive via `{ ...base, ... }`, same as every other extended port in
this codebase.

Running `scripts/quality.sh` also surfaced a second, pre-existing structural check in
`tests/features/bio-int-001/BIO-INT-001.test.sh` ("this story does not modify BIO-UX-005's owned
Integrations frontend surface") that asserts nothing under `web/features/integrations/` ever
changes again after BIO-INT-001. That check is now unavoidably stale: `ports.ts`'s own doc
comment explicitly invites "the domain story that builds real Integrations screens" to extend it
here, and BIO-INT-002 is precisely that story. This is the same kind of disclosed cross-story
handoff BIO-ATT-002 (taking over BIO-ATT-001's demo bridge routes) and BIO-VIS-002 (taking over
BIO-VIS-001's checkout route) already established as acceptable when the later story's own ACs
require it. BIO-INT-002 does not edit BIO-INT-001's own test file to silence this — that call
belongs to whoever owns BIO-INT-001's own acceptance record, not to this story.

## Cross-story scope discipline

This story never touches `api/features/bio-idn-001/` (BIO-IDN-001's own `Person.cs`/
`PersonService.cs`) or `api/features/bio-int-001/`. BIO-IDN-001's PERSON create/read is reached
exclusively through this story's own narrow `Domain/IPersonGateway.cs`, implemented as a private
nested adapter in `api/Biolog.Api/Modules/BioInt002Module.cs` — the same cross-story gateway
pattern every story in this codebase already uses.
