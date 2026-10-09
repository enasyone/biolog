# BIO-IDN-004 — Identity lifecycle and emergency suspension

As authorized staff, I need lifecycle transitions and emergency suspension that can disable
credentials without destroying the identity.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Allowed identity transitions are explicit | `LifecycleTransitionPolicy.IsAllowed` - a closed, explicit whitelist checked BEFORE `IPersonLifecycleGateway` is ever called | `PersonLifecycleServiceTests` (AC01 tests, exhaustive) |
| **AC02** | Emergency suspension emits credential/access revocation events | A dedicated `CredentialAccessRevocationEvent`/`ICredentialAccessRevocationEventSink`, published in addition to the ordinary lifecycle audit event whenever `ACTIVE -> SUSPENDED` occurs | `PersonLifecycleServiceTests` (AC02 tests) |
| **AC03** | Reactivation requires permission and audit reason | `SUSPENDED -> ACTIVE` requires the caller to assert `X-Actor-Permission: person.reactivate` and supply a non-blank `reason` | `PersonLifecycleServiceTests` (AC03 tests) |
| **AC04** | Archived records remain traceable | `ListEventsAsync` returns every recorded lifecycle event unconditionally; `GetSnapshotAsync` keeps returning an archived person's record; nothing is ever deleted | `PersonLifecycleServiceTests` (AC04 tests) |

## Design decisions and scope boundaries

### A write-capable extension of the existing read-only gateway pattern

BIO-IDN-002 and BIO-IDN-003 each defined a narrow, READ-only `IPersonExistenceGateway` onto
BIO-IDN-001's real store, implemented in `Biolog.Api` by wrapping BIO-IDN-001's own already-
registered `PersonService.GetAsync`. This story's whole subject is PERSON's own `Status` field,
so the same seam had to grow a WRITE - `Domain/IPersonLifecycleGateway.cs` adds
`ChangeStatusAsync` alongside a `GetSnapshotAsync` read, implemented in
`api/Biolog.Api/Modules/BioIdn004Module.cs` by wrapping BIO-IDN-001's own already-registered,
tenant-scoped `PersonService.UpdateStatusAsync` directly - still **no** `ProjectReference` to
BIO-IDN-001's project.

**Why the gateway returns a Result record, not an exception.** BIO-IDN-001's own `PersonService`
throws its own `PersonError` - a type this project cannot reference, catch, or pattern-match on
without a `ProjectReference` it deliberately does not have. `LifecycleChangeResult` carries a
small, stable set of well-known `ErrorCode` strings that the two projects intentionally share
(`person_not_found`, `concurrency_conflict`, `invalid_status_transition`,
`tenant_context_missing`) - the adapter in `Biolog.Api` (which DOES reference both projects)
catches `PersonError` and passes its `.Code` straight through, and this feature's own
`PersonLifecycleService` translates those same strings back into its own `PersonLifecycleError`.

### BIO-IDN-001 already anticipated this story

BIO-IDN-001's own `Person.cs` documents its baseline transition table as "deliberately
conservative" and names BIO-IDN-004 by story key as the one that owns narrowing/finalizing it,
emitting credential/access revocation events, and gating reactivation behind permission and
reason. `LifecycleTransitionPolicy` is that ownership expressed as code: a closed, explicit
whitelist this project can enforce fail-fast, before the gateway is ever called. It
intentionally MIRRORS BIO-IDN-001's own `PersonLifecycleTransitions` table exactly (this project
has no way to read that table programmatically) - BIO-IDN-001's own `UpdateStatusAsync`
re-validates the identical rule independently at the real source of truth when the gateway is
called, so even if the two tables ever drifted, the result is a safe rejection
(`invalid_status_transition`, translated the same way), never a silent bypass.

One deliberate narrowing beyond BIO-IDN-001's own permissiveness: BIO-IDN-001's
`UpdateStatusAsync` treats a same-status request as a silent no-op. `LifecycleTransitionPolicy`
has no self-loops at all - a same-status request is rejected as `transition_not_allowed`. AC01
asks for EXPLICIT allowed transitions; "transition to the status you're already at" is not a
transition, and narrowing it out is squarely this story's to make.

### AC02 - a genuine event emission, not a call into a system that does not exist yet

No credential or device-access story has a real, registered `crd_` aggregate or a live
revocation consumer today. `BIO-DEV-001`'s `Device`/`CredentialReferenceValidator` only
validates a credential REFERENCE STRING's format on device registration - it has no "revoke by
person" operation - and `BIO-CRD-001` is tenant brand assets, unrelated to person credentials.
This story's only declared dependency is BIO-IDN-001. AC02 is therefore satisfied as a genuine,
distinct, purpose-built EVENT EMISSION: `ICredentialAccessRevocationEventSink` is a SEPARATE
stream from the ordinary lifecycle audit trail (which records every transition, not just
suspensions) - a future credential/device-access story can subscribe to it the same way it would
any other durable event stream, without scanning the whole audit log filtering for suspensions.
Nothing here presumes what that future subscriber looks like.

`IsEmergencySuspension` names exactly one pair, `ACTIVE -> SUSPENDED` - the only pair
`LifecycleTransitionPolicy` allows into `SUSPENDED` at all. A reason is recorded on the
revocation event when the caller supplies one, but AC02's own text does not require one (unlike
AC03's explicit "audit reason" requirement for reactivation), so it stays optional here.

### AC03 - permission is asserted, not verified, matching every prior story's identical accommodation

No RBAC story has landed yet (BIO-IDN-006) - there is nothing today a permission CLAIM could be
checked against. `PermissionResolver` reads a required `X-Actor-Permission` header and
`PersonLifecycleService` requires it to equal the fixed string `person.reactivate` for
`SUSPENDED -> ACTIVE` specifically - the same honest, disclosed, asserted-not-verified
accommodation `ActorResolver` already uses for actor IDENTITY (pending BIO-INT-001), applied
here to permission. This makes AC03 genuinely enforced and testable NOW; real verification of
the claim is deferred to BIO-IDN-006.

`IsReactivation` names exactly one pair, `SUSPENDED -> ACTIVE` - the ordinary, first-time
activation (`VERIFIED -> ACTIVE`) is NOT a reactivation and requires neither permission nor a
reason. Distinguishing the two by their FROM status, not just the target, is why
`PersonLifecycleService` reads a fresh snapshot before deciding what this specific request
requires.

### AC04 - nothing is ever deleted, so nothing needs special-casing

BIO-IDN-001's own repository never deletes a `Person` record; `ARCHIVED` is a terminal status,
not a tombstone. `ListEventsAsync` and `GetSnapshotAsync` have no branch that hides, filters, or
prunes anything once a person reaches `ARCHIVED` - there is nothing "AC04" had to specially
implement beyond simply never adding such a branch. The only new thing this story adds is
`ListEventsAsync` itself, exposing this feature's own supplementary lifecycle record as a
person-scoped, chronologically ordered history, independent of BIO-IDN-001's own audit trail.

### Audit ordering - two different orders, chosen per what each write actually guards

BIO-IDN-002/003 write their own audit event BEFORE their own repository commit, because in
THOSE stories the "commit" is a same-process, uncontested in-memory dictionary write with no
ambiguous-throw window once the audit is confirmed (ADR-0003 Decision 7). Here, the "commit" is
`IPersonLifecycleGateway.ChangeStatusAsync` - a call into ANOTHER feature's already-audited,
already-locked service. That call is itself synchronous and unambiguous: it either returns a
success snapshot (BIO-IDN-001's own `person.status_change` audit event is already durably
written) or a failure result (nothing changed anywhere) - there is no "did it durably land
despite throwing" ambiguity to resolve the way an audit SINK write has.

For an ORDINARY transition, `PersonLifecycleService.TransitionAsync` therefore calls the gateway
FIRST, and only writes this feature's OWN supplementary lifecycle event AFTER confirming
success. There is nothing to roll back if this feature's OWN audit write then fails, because the
underlying status change already durably happened regardless - `audit_unavailable` from this
service therefore means something DIFFERENT from every other story's identical code: the change
committed, but this feature's own supplementary record of it could not be confirmed.
`PersonLifecycleAuditTests` proves this explicitly
(`When_this_feature_s_own_audit_sink_fails_the_underlying_status_change_has_already_committed`).

### BIO-IDN-004-QA-01 - suspension/revocation atomicity, five rounds

AC02 requires a suspension to ALWAYS have a revocation event. The status commit
(`IPersonLifecycleGateway.ChangeStatusAsync`) and the revocation publish
(`ICredentialAccessRevocationEventSink.PublishAsync`) are two INDEPENDENT writes to two
DIFFERENT stores with no shared transaction - true atomicity between them is not achievable
outright, and getting this right took five rounds.

**Round 1 finding.** The original implementation committed the status change first, then
published. A publish failure left the person genuinely SUSPENDED with no revocation event and
no safe way to retry (a retry would itself be rejected as `transition_not_allowed`, since the
person was no longer `ACTIVE`) - a silent, un-closeable gap.

**Round 1 fix (rejected in round 2).** Reversed the order: publish first, commit second. This
closed the silent-gap finding, but introduced a new one: a REJECTED commit (e.g. a stale
version - an ordinary, expected outcome under concurrency, not a rare fault) now left a FALSE
revocation event for a suspension that never happened. The reviewer correctly rejected calling
this "an accepted trade-off" - a revocation event must correspond to an authorized, successful
suspension, not a rejected request, and "reverse the order" can only ever protect one of the two
writes at the other's expense.

**Round 2 fix (rejected in round 3).** The gateway commits FIRST for every transition,
suspension included, closing round 2's finding - a rejected request can never produce a
revocation event. A publish failure was made an EXPLICITLY MODELLED, OBSERVABLE
pending-reconciliation state instead of a silent gap - but "is reconciliation pending" was
DERIVED from the person's CURRENT status being `SUSPENDED`, and the only way to close it was
resubmitting a `SUSPENDED -> SUSPENDED` request. A legitimate, fully-permissioned reactivation
(`SUSPENDED -> ACTIVE`) succeeding before reconciliation silently erased BOTH the observability
(the status-derived check now reported "not pending") AND the only reconciliation path (which
required the person to still be `SUSPENDED`) - the missing revocation became permanently
unreported and unrecoverable. The same class of gap as round 1, reached by a different path.

**Round 3 fix (rejected in round 4).** `IPendingSuspensionReconciliationStore` tracks each
pending suspension as its OWN durable record, independent of the person's current status, so it
survives ANY subsequent transition (reactivation, re-suspension, archival, anything) -
`CredentialAccessRevocationEvent.SuspendedAtVersion` uniquely identifies one specific suspension
occurrence, `ReconcilePendingRevocationsAsync` is a SEPARATE operation reachable regardless of
current status, and `HasPendingRevocationsAsync` queries the durable record directly. But the
pending record was marked AFTER the ordinary supplementary lifecycle audit write, not before -
an audit-sink outage at that exact point aborted the method (as `audit_unavailable`) BEFORE the
pending record was ever created. The person was left `SUSPENDED` with no revocation event AND
no pending record for reconciliation to find - the gap undetectable and unrepairable. The same
class of gap as rounds 1 and 3, reached by a third path (an audit outage, not a revocation
failure or a reactivation).

**This fix (round 5) - the pending record is marked before anything else that can throw, and
the two subsequent writes never gate one another.** For a suspension, `TransitionAsync` now:

1. Marks the pending record the VERY FIRST thing after the gateway commit succeeds - before
   EITHER of the two writes below is even attempted.
2. Attempts the ordinary supplementary lifecycle audit event and the revocation publish
   INDEPENDENTLY of one another's outcome - a failure in one never prevents the other from being
   attempted. (A healthy revocation sink still succeeds, and the pending record is still cleared,
   even if the audit sink is down at the same moment.)
3. Surfaces the revocation publish's own failure with priority over the audit write's, since it
   is AC02's own security-critical write; the audit failure is only surfaced when the revocation
   publish (when applicable) otherwise succeeded.

`ReconcilePendingRevocationsAsync`, `HasPendingRevocationsAsync` and the `revocation_pending`
response field are unchanged from round 3 - the fix here is purely about WHEN the pending record
gets marked relative to the two writes that can fail, not about the durable-tracking design
itself, which round 4's own review confirmed was otherwise correct.

Verified with five break-it-on-purpose tests, one per round's exact finding, each confirmed to
fail when the corresponding fix is temporarily reverted and then restored and re-confirmed
green: `BIOIDN004_QA01_round2_a_rejected_suspension_never_emits_a_false_revocation_event`
(round 2's finding, ordering reverted to publish-before-commit),
`BIOIDN004_QA01_round3_when_the_revocation_sink_fails_the_suspension_still_commits_and_enters_pending_reconciliation`
(the commit is never rolled back),
`BIOIDN004_QA01_a_reconciliation_retry_after_the_sink_recovers_completes_the_pending_revocation_exactly_once`
(round 1's "stuck forever" finding, reconciliation disabled),
`BIOIDN004_QA01_round4_the_pending_fact_survives_a_reactivation_and_stays_observable_and_reconcilable`
(round 3's finding, `HasPendingRevocationsAsync` reverted to a status-derived check), and
`BIOIDN004_QA01_round5_an_audit_sink_outage_during_a_suspension_still_leaves_a_pending_record_behind`
(round 4's exact finding, the pending mark reverted to after the audit write - fails with
`audit_unavailable` and no pending record, precisely reproducing the report).

### What the per-person lock does and does not protect

`PersonLifecycleLockRegistry` only serializes concurrent callers going THROUGH this feature's
own `PersonLifecycleService`. It cannot serialize against a hypothetical concurrent write to the
same person from BIO-IDN-001's own endpoints directly (or any other future feature holding its
own write-capable gateway onto PERSON) - the real source-of-truth safety for the underlying
status field is BIO-IDN-001's own per-person lock inside `PersonService.UpdateStatusAsync`,
which this project has no visibility into or control over. Because this service always reads a
FRESH snapshot under its own lock before deciding what is allowed, two concurrent BIO-IDN-004
callers for the same person can never both succeed - the loser sees either
`transition_not_allowed` (the winner's new status no longer permits the loser's request) or
`concurrency_conflict` (a stale caller-supplied version), and either is a safe, fail-closed
outcome.

## Deliberately out of scope for this story

- Real RBAC enforcement of `person.reactivate` or any other permission (BIO-IDN-006).
- A real credential/device-access consumer of `ICredentialAccessRevocationEventSink` - no such
  system exists yet anywhere in this codebase.
- Bulk/batch suspension across multiple people.
- A configurable transition table (BIO-IDN-001's own table, and this story's narrowing of it,
  are both fixed in code - no tenant ever configures its own lifecycle rules).
