# BIO-IDN-006 — RBAC role catalogue and maker-checker hooks

As tenants, we need configurable operational roles and maker-checker controls for sensitive
actions.

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Baseline roles include Super Admin, Tenant Admin, Registrar/HR, Enrolment, Biometric, Card Production, Security, Gate, Auditor, Helpdesk and Read-only | `RoleType` - a closed enum naming all eleven verbatim; `PersonRoleAssignment` lets a person hold several at once | `RoleAssignmentServiceTests` (AC01 tests), `RbacEndpointsIntegrationTests` (catalogue endpoint) |
| **AC02** | Permissions are tenant-scoped | `RolePermissionGrant`, keyed `(TenantId, Role, PermissionKey)`; `PermissionService.HasPermissionAsync` is the real verification surface, resolved against the caller's OWN tenant only | `PermissionServiceTests` (AC02 tests), `RbacHostChannelTests.cs` (real-host tenant boundary) |
| **AC03** | Sensitive actions can require second approval | `SensitiveActionRule` (tenant-scoped opt-in, its own configuration gated by `rbac.sensitive_action_rule_manage` - QA-04) + `ApprovalRequest` (the maker-checker workflow itself, separation of duties structurally enforced, single-use consumption - QA-05, raising/deciding gated by `rbac.approval_request`/`rbac.approval_decide` - QA-07); `RoleAssignmentService`'s own `SUPER_ADMIN` mutation is the real, owned consumer of `TryConsumeApprovalAsync`, called only immediately before commit, never before validation (BIO-IDN-006-QA-02/QA-05/QA-06) | `MakerCheckerServiceTests` (AC03 + mandatory privilege-escalation + QA-04/QA-05/QA-07 tests), `RoleAssignmentServiceTests` (QA-02 clearance-gate + QA-06 consumption-timing tests) |
| **AC04** | Role changes are audited | Every `RoleAssignmentService` mutation follows ADR-0003 Decision 7's audit-atomicity pattern; revoking is a governed transition, never a delete | `RoleAssignmentServiceTests` (AC04/audit tests) |

**Security/privacy criteria**: "Privilege escalation negative tests are mandatory" - see
"Privilege escalation - the mandatory security surface" below.

## Design decisions and scope boundaries

### This story's scope: the catalogue and its own endpoints, not retrofitted enforcement

Research before designing this story confirmed a load-bearing fact: **BIO-INT-001** (real
authentication, W12) **depends on BIO-IDN-006**, not the other way around - real authentication
maps its own identity-provider claims onto the roles/permissions this story defines
(BIO-INT-001-AC02: "No provider-specific claims leak into authorization without mapping"). Six
other future stories (BIO-ACC-001, BIO-DOC-011, BIO-INT-003, BIO-OPS-002, BIO-RPT-003,
BIO-VIS-001) also declare this story as a dependency, all W05+, none built yet. This means:

- This story ships the role/permission/maker-checker **model** and **its own admin endpoints**
  (catalogue, assignment, grant configuration, sensitive-action rules, approval workflow) -
  genuinely and completely, including the real `HasPermissionAsync` verification surface
  BIO-IDN-004's own `PermissionResolver` already documented itself as waiting for ("real
  verification of the CLAIM is deferred to BIO-IDN-006").
- It does **not** retrofit real permission enforcement into any OTHER sibling story's already-
  accepted endpoints (BIO-TEN-001/002, BIO-IDN-001/002/003/004/005 all still only assert an
  actor via `X-Actor-Id`, with zero permission check). Wiring `HasPermissionAsync` into those
  endpoints is a disclosed, future integration step - mirroring the existence-gateway pattern
  already proven for BIO-IDN-002/003/004/005 (a sibling story reaches into this one through a
  narrow interface, never the reverse) - not something reopening five already-accepted stories
  right now would buy this story.
- **Revised by BIO-IDN-006-QA-01/QA-02, then generalized by QA-03/QA-04/QA-05, then QA-07** (see
  those sections below): the round 1 submission also claimed it would never gate its OWN
  endpoints behind its own permission model, reasoning from the same bootstrap problem. The
  review correctly rejected that reasoning first for the single highest-risk mutation
  (`SUPER_ADMIN`), then - round 2 - for every OTHER mutation this story's own endpoints expose:
  assigning or revoking ANY role, configuring a permission grant for ANY role, and configuring a
  sensitive-action rule itself all now require the calling actor to hold real authority, checked
  the same way `SUPER_ADMIN`'s own gate always was. Round 4 closed the last gap: raising and
  deciding a maker-checker approval request now ALSO require real authority
  (`rbac.approval_request`/`rbac.approval_decide`) - "ordinary staff participating in a process"
  still means those staff must be genuinely authorized to participate, not merely asserted. Only
  READ endpoints (the role catalogue, `HasPermissionAsync`, listing assignments/history) remain
  open to any asserted actor, matching the "asserted actor, no real enforcement yet" disclosed
  accommodation every sibling story in this codebase still uses for itself. "Deferring
  authentication does not justify deferring authorization" turned out to generalize to every
  MUTATING endpoint this story owns, not only its single most obviously named one - see QA-03's
  "root cause" below for why the round 1 fix's narrow
  scoping was itself the mistake round 2 caught.

### AC01: eleven baseline roles, a closed catalogue

`RoleType` is a closed enum, not a tenant-extensible catalogue like BIO-IDN-002's `ProfileType`
or BIO-IDN-003's `IdentifierType` - AC01 names a **fixed** baseline set verbatim; nothing in the
approved story asks for tenant-defined custom roles. AC02's "permissions are tenant-scoped" is
satisfied by making WHICH permissions a role carries tenant-configurable
(`RolePermissionGrant`), not by making the role NAMES themselves tenant-configurable. A person
may hold several roles at once (`PersonRoleAssignment`'s natural key is
`(TenantId, PersonId, Role)` - one record per role, not one record per person).

### AC02: fail-closed permission grants, and SUPER_ADMIN's special status

An absent `RolePermissionGrant` means NOT granted - a role has no permissions until a tenant
explicitly grants specific ones. This is a stricter default than BIO-IDN-003's own
`IdentifierUniquenessRule` ("missing rule defaults to Enforced=true" - that story owns and
understands its own invariant; this one is a cross-cutting facility with no built-in opinion
about what any given permission key should mean for any given role, so the safer failure
direction is to grant nothing until asked).

`RoleType.SuperAdmin` is deliberately the one exception: it implicitly holds every permission,
always, and a grant record can never be written for it at all
(`SuperAdminGrantNotConfigurable`) - a tenant must never be able to accidentally (or
adversarially) narrow its own top-level administrator's authority via a permission-grant
misconfiguration. `HasPermissionAsync` short-circuits to `true` for `SuperAdmin` before ever
consulting `IRolePermissionGrantRepository`.

`PermissionKey` (and `SensitiveActionRule.ActionKey`) are free-form, namespaced strings (the
exact shape BIO-IDN-004's own `PermissionResolver` already uses, e.g. `person.reactivate`) -
this story does not own or enumerate every permission or action key the rest of the system will
ever define; it validates FORMAT only (the same namespaced-verb pattern `audit.mjs`'s own action
validation uses), never a closed registry.

### AC03: maker-checker - a genuinely new key shape in this epic

Every prior aggregate in this identity wave found a natural composite key
(`TenantDomain`→hostname, `PersonProfile`→`(PersonId, ProfileType)`,
`InstitutionalIdentifier`→`(PersonId, Type, CustomLabel, SequenceNumber)`,
`DuplicateCandidate`→`(TenantId, PersonIdLow, PersonIdHigh)`). `ApprovalRequest` is the first
aggregate in this epic with no natural UNIQUENESS key of its own - a maker can legitimately raise
several requests for the same `(TenantId, ActionKey, TargetRef)` over time (a rejected request
followed by a corrected resubmission). Rather than mint a new ADR-0001 canonical identifier type
(a shared-contract/ADR amendment needing Kemi's recorded approval, for a facility with no proven
need yet), this mirrors BIO-IDN-003's `InstitutionalIdentifier` shape exactly: natural key
`(TenantId, ActionKey, TargetRef, SequenceNumber)`, with AT MOST ONE `Pending` record per target
at any time and a full, permanent history of every request ever raised - the same
"Retire-then-reassign" discipline, applied to "Decide-then-resubmit".

`SensitiveActionRule`'s own default is deliberately the OPPOSITE of `RolePermissionGrant`'s: an
absent rule means second approval is NOT required (fail-OPEN). This facility is an explicit
opt-in a tenant enables for a specific action key it has decided is sensitive; nothing is
silently gated behind an approval workflow nobody configured. The failure direction that matters
here is never silently BLOCKING an action nobody asked to protect - the opposite concern from
`RolePermissionGrant`'s fail-closed default, which protects against silently GRANTING authority.

`MakerCheckerService.IsActionClearedAsync(actionKey, targetRef)` is a read-only status check: true
when the tenant never required approval for this action, or when the MOST RECENT request for this
target was `Approved` - never derived from anything but the latest record, so a rejected or
still-pending request never silently clears. It does not consume anything, so a future consumer
that only needs to KNOW clearance (rather than perform the gated mutation itself) can still call
it directly. `TryConsumeApprovalAsync` (BIO-IDN-006-QA-05) is the separate, atomic, single-use
sibling a real mutation gate should call instead - see that section below.

### Privilege escalation - the mandatory security surface

The story's own security/privacy criteria require "privilege escalation negative tests" - the
core property this whole maker-checker workflow exists to enforce is separation of duties: the
CHECKER may never be the same actor as the MAKER. This is enforced, not merely documented, at
two independent layers:

- `MakerCheckerService.DecideAsync` reads the pending request under the per-target lock and
  rejects a self-approval attempt (in EITHER direction - approving OR rejecting one's own
  request) before any write is attempted.
- `InMemoryApprovalRequestRepository.ReplaceAsync` independently re-checks the same rule at the
  persistence boundary against the ACTUAL deciding actor it is given as its own parameter, not
  anything a caller could embed in the replacement record itself - a caller who bypassed the
  service entirely and forged a replacement record claiming a different decider still cannot
  smuggle a self-approval past this boundary (`PRIVILEGE_ESCALATION_a_forged_replacement_record_cannot_smuggle_a_different_decider_past_the_repository_boundary`
  drives this attack directly).

A second privilege-escalation vector this story closes structurally: `RolePermissionGrant` can
never be written for `SUPER_ADMIN` at all (see AC02 above) - a tenant admin misconfiguring, or an
attacker exploiting, a grant record for the top-level role could otherwise narrow (or, depending
on implementation, even widen unrelated) its authority; this is prevented by construction, not
by a code review convention.

Deciding an already-decided request is always a hard rejection
(`approval_request_already_decided`), never a silent no-op - unlike a revoke's idempotent "ensure
revoked" semantics, a decision carries content two racing callers could legitimately disagree on
(one might approve, another might reject the same request), so the loser must be told which
decision actually won, not silently absorbed into whichever happened to land second.

### BIO-IDN-006-QA-01 (Seyi round 1, Critical) - arbitrary actor could grant SUPER_ADMIN

**Finding.** The round 1 submission treated the story's own "no real authentication exists yet"
disclosed accommodation as justification for leaving EVERY endpoint, including role assignment,
open to any asserted `X-Actor-Id` with no authorization check at all. Seyi's reproduction sent
`POST /api/v1/persons/{personId}/roles/SUPER_ADMIN` with `X-Actor-Id: unprivileged_attacker` and
received `200 OK` with the role granted. The review rejected the framing directly: "Deferring
authentication does not justify deferring authorization: the story supplies a permission
verification service and the actor seam, but never connects them."

**Root cause, stated plainly.** I had correctly identified that this story cannot solve real
AUTHENTICATION (proving an asserted identity is genuine) before BIO-INT-001 exists, and
incorrectly generalized that into "therefore this story cannot enforce AUTHORIZATION
(does the asserted identity already hold the authority it claims) either" - conflating two
genuinely separable concerns. BIO-IDN-004's own `PermissionResolver` had already established the
distinction (an asserted permission CLAIM, pending real verification) without me applying the
same reasoning to my own story's most sensitive mutation.

**Fix.** Assigning or revoking `RoleType.SuperAdmin` now requires the calling actor to already
hold an ACTIVE `SUPER_ADMIN` assignment - looked up directly against
`IPersonRoleAssignmentRepository` by the actor's own id, which must itself be a well-formed
canonical `per_` id for this specific check to even be attempted (the free-form actor ids this
codebase's disclosed pre-auth convention otherwise allows everywhere else, like `"admin_alice"`,
can never satisfy it - proven by
`PRIVILEGE_ESCALATION_a_free_form_non_person_actor_id_can_never_satisfy_the_SUPER_ADMIN_check_even_by_coincidence`).
This is symmetric for both directions (revoking someone's `SUPER_ADMIN` without authority is a
denial-of-access/sabotage vector, not a lesser concern than granting it), and deliberately
ABSOLUTE - no bootstrap exception exists anywhere in the check. The direct, disclosed
consequence: **a tenant's very first `SUPER_ADMIN` can never be created through this endpoint at
all.** This mirrors how real production RBAC systems solve the identical bootstrap problem -
provisioning a tenant's first administrator is an out-of-band operation (seeding the repository
directly, an infrastructure/ops action), never something the ordinary self-service API should be
able to do for itself. `SeedBootstrapSuperAdminAsync` in the test suite models exactly this: a
direct repository write, bypassing the service entirely, standing in for that out-of-band path.

Round 1 left every OTHER role assignment unaffected - `Enrolment`, `Gate`, `Auditor`, and so on
remained open to any asserted actor. **Round 2's QA-03 finding shows why that narrow scoping was
itself a mistake**, not merely an incomplete fix - see below.

### BIO-IDN-006-QA-02 (Seyi round 1, High) - maker-checker clearance had no production consumer

**Finding.** `MakerCheckerService.IsActionClearedAsync` existed, was fully tested in isolation,
and was never called by anything real. "Enabling `requires_second_approval` therefore does not
block, defer, or otherwise govern any sensitive action... At minimum, an owned sensitive action
must consume the hook."

**Fix.** QA-01's fix gave this story a natural, owned answer: mutating `SUPER_ADMIN` - arguably
the single most sensitive operation this system has - is now itself gated by
`IsActionClearedAsync`, using action keys `"rbac.super_admin_assign"`/`"rbac.super_admin_revoke"`
and the target `personId` as `TargetRef`. The two QA-01/QA-02 gates run in a deliberate order:
the `SUPER_ADMIN`-authority check runs FIRST, so an actor with no authority at all learns nothing
about whether a tenant has even configured a maker-checker rule for this action
(`Maker_checker_authorization_gate_runs_before_the_SUPER_ADMIN_authority_gate_never_leaks_which_failed_first_to_an_unprivileged_caller`
proves this ordering directly). By default (no `SensitiveActionRule` configured) the clearance
check passes immediately - fail-OPEN, per that type's own default - so a tenant that never opts
in sees no behavioural change beyond QA-01's own gate; a tenant that DOES opt in genuinely cannot
mutate `SUPER_ADMIN` without an approved maker-checker request for that specific target, proven
by `Maker_checker_genuinely_blocks_a_legitimate_SUPER_ADMIN_from_granting_SUPER_ADMIN_when_the_tenant_requires_second_approval`
(even an actor who legitimately already holds `SUPER_ADMIN` is blocked until approval) and
`Maker_checker_clears_once_a_second_approval_is_obtained_and_the_mutation_then_succeeds` (the
gate genuinely opens once a real, separate checker approves the request).

### BIO-IDN-006-QA-03 (Seyi round 2, Critical) - every OTHER role was still wide open

**Finding.** Round 1's fix protected exactly one enum value, `SUPER_ADMIN`. Any OTHER role -
`TENANT_ADMIN` included - could still be assigned by an arbitrary asserted actor, who could then
call `PermissionService.SetGrantAsync` to grant that role any permission key at all, including
`tenant.delete_all_data`. Since permission keys are open-ended and tenant-defined, a
non-`SUPER_ADMIN` role configured this way is not a "safe, lesser" privilege - it is the
identical escalation, one hop removed through a role the round 1 fix never looked at.

**Root cause, stated plainly.** Round 1 fixed the ONE mutation the review's reproduction happened
to target, then documented that narrow scope as a deliberate choice ("matching the codebase's
disclosed accommodation") rather than asking whether the SAME reasoning that justified gating
`SUPER_ADMIN` also applied to every other role. It did: "deferring authentication does not
justify deferring authorization" is a property of the MUTATION (does this actor already hold the
authority it claims), not of which specific enum value the mutation happens to target. A
narrowly-scoped security fix that protects one instance of a vulnerability class invites exactly
this: the reviewer finding the SAME class of hole in every sibling instance the fix didn't touch.

**Fix.** A new, general authorization model, not a second special case bolted onto the first:
`PermissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey)` is a non-throwing
internal counterpart to the public `HasPermissionAsync` (a malformed or nonexistent actor id
simply reports "not granted" - the correct signal for a GATE, as opposed to a validation
endpoint). Three new permission keys gate three mutation surfaces: `rbac.role_assign` /
`rbac.role_revoke` (`RoleAssignmentService`, for any role OTHER than `SUPER_ADMIN`, which keeps
its own separate absolute gate), `rbac.permission_grant_manage` (`PermissionService.SetGrantAsync`,
for any role other than `SuperAdmin`), and `rbac.sensitive_action_rule_manage`
(`MakerCheckerService.SetActionRuleAsync` - QA-04, see below). Nobody holds any of these by
default (fail-closed, `RolePermissionGrant`'s own existing default) except `SUPER_ADMIN` itself
(implicit, all permissions) - so in practice only a `SUPER_ADMIN`, or a role it has explicitly
delegated one of these permissions to, may assign/revoke any other role or configure grants,
inheriting `SUPER_ADMIN`'s own already-solved bootstrap story rather than needing a second one.
The gate ordering discipline QA-01/QA-02 established holds here too: authority is always checked
before format validation, the existence gateway, or any lock.

**Direct, disclosed consequence.** Because every role mutation now requires the caller to already
hold real authority, and `SUPER_ADMIN` itself can never be bootstrapped through this story's own
endpoints (QA-01), **no role can ever be assigned to anyone through the public API without an
out-of-band-provisioned actor** - the same bootstrap story QA-01 already established for
`SUPER_ADMIN` specifically now applies uniformly to every role, not as a new gap but as the
direct, intended generalization of a choice the review already accepted once.
`tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs` makes this consequence visible: it
deliberately has no `InternalsVisibleTo` seeding backdoor into the real host's repository (the
same "no backdoor exists" proof QA-01's own real-host test already established), so its
role-assignment tests now prove the request reaches tenant/person resolution and is THEN rejected
on authority grounds, rather than demonstrating a live assignment succeeding.

### BIO-IDN-006-QA-04 (Seyi round 2, Critical) - the maker-checker rule itself was ungated

**Finding.** QA-02 wired maker-checker into `SUPER_ADMIN` mutation, but configuring the RULE
ITSELF - including disabling `requires_second_approval` for an action a tenant had already opted
into protecting - remained open to any asserted actor. An attacker could defeat the whole control
before a legitimate administrator's approval requirement ever took effect, by simply turning it
off first.

**Fix.** `MakerCheckerService.SetActionRuleAsync` now requires `rbac.sensitive_action_rule_manage`
(the same `PermissionService.ActorHasPermissionAsync` mechanism QA-03 introduced), checked before
any other validation in the method. `IsActionClearedAsync` (the read-only clearance check QA-02's
gate consumes) is unchanged - only the WRITE path (configuring the rule) is newly gated.

### BIO-IDN-006-QA-05 (Seyi round 2, High) - an approval was reusable indefinitely

**Finding.** `IsActionClearedAsync` derives clearance from the most recent request's `Status`
alone. Round 1 wired this straight into `SUPER_ADMIN`'s mutation gate as a read-only check, which
meant a single `Approved` request cleared EVERY subsequent `SUPER_ADMIN` mutation for that same
target forever, not only the ONE mutation the maker and checker actually reviewed and approved.

**Fix.** `ApprovalRequest` gained a new field, `ConsumedAt` - a separate fact from `Status`, so it
does not disturb the `Pending`/`Approved`/`Rejected` lifecycle or `DecideAsync`'s "decided
requests are terminal" invariant. `IApprovalRequestRepository.ConsumeAsync` is a genuinely
separate repository method from `ReplaceAsync` (no maker/checker identity check - the actor
performing the gated mutation may legitimately be either party), independently re-validating
`Status == Approved` and `ConsumedAt is null` at the persistence boundary.
`MakerCheckerService.TryConsumeApprovalAsync(actionKey, targetRef)` is the new atomic, single-use
gate primitive (under the same per-target `ApprovalRequestLockRegistry` lock `RequestApprovalAsync`
already uses), and `RoleAssignmentService`'s `SUPER_ADMIN` mutation gate now calls it instead of
the read-only `IsActionClearedAsync` - which remains unchanged and available as a pure status
check. The direct, disclosed consequence: a tenant that requires second approval for
`SUPER_ADMIN` mutation must obtain a FRESH approval for every single mutation, even when
mutating the same target twice in a row - proven by
`An_approved_requests_clearance_is_consumed_by_the_first_mutation_and_never_clears_a_second` and
`A_fresh_approval_is_required_and_does_work_for_a_second_mutation_of_the_same_target`.

### BIO-IDN-006-QA-06 (Seyi round 3, High) - an approval could be burned by its OWN failed mutation

**Finding.** QA-05's fix made consumption atomic and single-use, but the consuming call sat at
the very TOP of `AssignAsync`/`RevokeAsync` (inside `RequireSuperAdminMutationAuthorityAsync`) -
before person-id format validation, the existence gateway, the idempotent no-op check, and the
audit write. Any one of those failing afterward (a malformed or nonexistent target, a stale
concurrency-conflict retry, a transient audit-sink outage, or the mutation simply already being a
no-op) permanently burned a genuinely valid, already-approved request for a change that never
actually happened - "approval is consumed before target validation and before audit/mutation
commit; failed or retried sensitive mutations can permanently burn a valid approval." A legitimate
maker/checker pair would have to raise and re-approve an entirely new request just to retry the
SAME already-approved change.

**Root cause, stated plainly.** QA-05 correctly identified that consumption must be atomic and
single-use, and I placed the call at the same spot the OLD read-only clearance check had always
lived - the top of the method, alongside the authority check - without separately asking "is this
also the right place for the SIDE-EFFECTING half of this gate, given everything downstream that
can still cause the mutation to never happen?" A read-only check has no cost to running early; an
irreversible consumption does, and the two have different correct positions in the method even
though they'd always been colocated before.

**Fix.** Split the single gate back into its two genuinely different halves.
`RequireSuperAdminMutationAuthorityAsync` now performs ONLY the SUPER_ADMIN-authority check and a
READ-ONLY `IsActionClearedAsync` check (so the existing "authorized-but-unapproved caller learns
nothing" ordering property - proven by
`Maker_checker_authorization_gate_runs_before_the_SUPER_ADMIN_authority_gate_never_leaks_which_failed_first_to_an_unprivileged_caller`
- is completely unchanged). The actual, atomic `TryConsumeApprovalAsync` call moved into a new
`ConsumeSuperAdminApprovalAsync`, invoked at each of the three points a REAL mutation is about to
commit (assign-create, assign-reactivate, revoke) - always AFTER format validation, the existence
gateway, the lock, the no-op check, and the audit write's own confirmation, using
`CancellationToken.None` to match the commit's own established discipline (ADR-0003 Decision 7
element 4) so a late cancellation cannot leave the approval consumed with nothing committed. A
no-op mutation (already in the target state) never reaches this call at all - mirroring "nothing
to audit" for the identical case, now also "nothing to consume".

**Direct consequence, proven by tests.** The SAME approval now genuinely survives a retryable
failure and clears a subsequent retry once the transient condition clears (proven by
`BIOIDN006_QA06_a_failed_mutations_untouched_approval_genuinely_works_on_retry`); a no-op mutation
never touches the approval (`BIOIDN006_QA06_an_idempotent_no_op_mutation_never_consumes_the_approval`);
Seyi's own reproduction (`ADVERSARIAL_failed_sensitive_mutation_does_not_consume_its_approval`,
delivered as a live diff directly into the test suite, the same evidence-delivery mechanism used
for QA-01's own reproduction) now passes.

### BIO-IDN-006-QA-07 (Seyi round 4, Critical) - the maker-checker workflow itself had no real participants requirement

**Finding.** Rounds 1-2 gated who may CONFIGURE maker-checker (`SetActionRuleAsync`, QA-04) and
who may MUTATE `SUPER_ADMIN` once cleared (QA-01/QA-03), but `RequestApprovalAsync` (raising a
request) and `DecideAsync` (deciding one) themselves remained open to any asserted actor - the
only check was that the maker and checker be two DIFFERENT ids. Two equally arbitrary, equally
unauthorized asserted identities could still create and approve a sensitive request between
themselves, defeating the whole control: a maker-checker gate is only as strong as the authority
of its own participants, and "different from each other" was never a substitute for "genuinely
authorized to participate in this workflow at all".

**Root cause, stated plainly.** Round 2's own scope decision (documented in this README's
"deliberately out of scope" section) explicitly left the maker-checker WORKFLOW endpoints open,
reasoning that a maker/checker is "ordinary staff participating in a process, not administrators
configuring it." That distinction is real for CONFIGURATION vs. PARTICIPATION, but I treated
"participation needs no authority" as following from it, when the story's own purpose statement
("maker-checker controls for sensitive actions") already implied the opposite: the whole point of
requiring a SECOND approval is that the reviewer is meaningfully positioned to catch a problem the
maker missed, which requires the reviewer (and the maker) to be real, authorized participants -
not merely a second arbitrary string in the `X-Actor-Id` header.

**Fix.** `RequestApprovalAsync` now requires `rbac.approval_request`; `DecideAsync` now requires
`rbac.approval_decide` - both checked via the same `PermissionService.ActorHasPermissionAsync`
mechanism as every other gate in this story (fail-closed by default except `SUPER_ADMIN`,
implicit), checked before any other validation. The self-approval check remains completely
unchanged, now correctly understood as a SEPARATE property of an otherwise-authorized checker
(maker ≠ checker), never a substitute for either one being authorized in the first place. A
tenant must now explicitly delegate participation in a sensitive workflow to a role (e.g.
Registrar/HR as maker, Tenant Admin as checker) before that role's holders may raise or decide
requests for it - proven by
`A_role_explicitly_delegated_rbac_approval_request_and_rbac_approval_decide_can_participate_without_holding_SUPER_ADMIN`.

**Direct, disclosed consequence.** Extends the same "no in-band bootstrap" pattern QA-01/QA-03
already established (see those sections above) one step further: the real packaged host's own
integration-test project - which deliberately has no repository-seeding backdoor - can no longer
demonstrate the maker-checker workflow succeeding at all, only that an unprivileged actor is
rejected at the first step (raising a request). The self-approval property's own real-host proof
now lives in this feature's local `RbacEndpointsIntegrationTests.cs` (which DOES have a seeding
backdoor for exactly this reason), not in `RbacHostChannelTests.cs`.

### BIO-IDN-006-QA-08 (Zainab's security review, Critical) - the asserted-actor model itself has no authenticated principal binding

**Finding.** After Seyi PASSED round 5 (QA-01 through QA-07 all closed), Zainab's independent
security review found something all seven of those rounds shared as an unexamined premise:
`ActorResolver` copies the caller-supplied `X-Actor-Id` header directly into `AuditActor` with no
authenticated principal binding at all. Every authorization gate this story built (QA-01/QA-03's
role-mutation checks, QA-04's rule-configuration check, QA-07's maker-checker participation
check) authorizes purely by looking up roles for whatever id a caller ASSERTS - so a caller who
supplies a KNOWN or GUESSED `SUPER_ADMIN` (or any delegated maker/checker) person id is treated
as that person and can perform their privileged actions. Canonical `per_` shape validation proves
the id is well-formed, never that the caller who typed it is who they claim.

**This is not a bug in this story's authorization logic - it is a different KIND of finding
than QA-01 through QA-07.** Every prior round fixed "given the asserted-actor model as the
accepted baseline, is authorization actually checked" - genuine gaps in code I wrote. This
finding says the baseline itself is unsafe to build authorization on top of, for the specific
mutations this story protects. That baseline (`X-Actor-Id`, asserted not authenticated) is not
local to BIO-IDN-006 either - every already-accepted sibling story in this codebase (BIO-TEN-001/
002, BIO-IDN-001 through 005) uses the identical pattern, and the project's own approved
build-wave plan (`07-BUILD-WAVES/EXECUTABLE-BUILD-BATCHES.md`) places BIO-INT-001 ("Implement
authentication provider abstraction," the story that binds an asserted identity to a real,
verified principal) at W12.1 - deliberately 8 waves after this story (W04.6), which BIO-INT-001's
own `Dependencies: BIO-IDN-006` confirms is not an accident.

**Escalated rather than guessed at a fix**, since re-litigating an already-approved cross-story
sequencing decision is not a single story's call to make. God independently verified the finding
and the wave-plan evidence, then looped in Kemi (Architecture) for a ruling: is this the same
shape as an accepted, ADR-documented scope boundary (BIO-INT-001's own dependency graph
presupposes this exact interim state), or does being the RBAC/authorization story itself - not an
unrelated feature merely riding on the same seam - mean a narrower interim mitigation is owed
before this specific story can close.

**Kemi's ruling, and its correction.** No code-level fix inside RBAC itself can close this
finding - verifying WHO supplied an input (as opposed to WHAT it looks like) structurally
requires real authentication, which is BIO-INT-001's job, not something a permission-lookup
service can ever provide on its own. God's first relay of this direction (a simple shared
`X-Operator-Key` secret, checked directly) was her own imprecise paraphrase, built and shipped
before Kemi's actual ADR text landed - Seyi's next round correctly re-flagged the resulting gate
as still too coarse (a secret holder could assert ANY person id, with zero binding between the
secret and the identity claimed). Once **ADR-0006** (`docs/adr/ADR-0006-actor-identity-and-
authorization-boundary.md`) was committed, god corrected the direction: build to Decision 3's
actual text, not her earlier paraphrase.

**ADR-0006 Decision 3, the authoritative mechanism.** "`X-Actor-Id` may remain the transport, but
only if a trusted edge strips any caller-supplied value and injects a verified one, and the
application checks that injected value's provenance before trusting it for an authorization
decision - never accepting a caller-supplied value directly." This story implements the
APPLICATION-SIDE half only: verify the provenance of the actor id `ActorResolver` already
resolved, before any authorization decision uses it. The EDGE half - actually stripping a
caller-supplied value and injecting a verified one - is explicitly Emeka's infrastructure concern
(BIO-INT-001/deployment boundary), outside this story's own surface; until it exists, an
operator/deployment script computes the same proof out of band, modelling exactly what the edge
will eventually automate.

**Fix.** `ActorProvenanceGate` (`api/features/bio-idn-006/Api/ActorProvenanceGate.cs`) requires a
server-verifiable `X-Actor-Provenance` header - an HMAC-SHA256 of the SPECIFIC asserted actor id,
keyed by a shared provenance secret, checked strictly AFTER `ActorResolver` resolves the actor id
and BEFORE any authorization decision uses it - on the four highest-risk surfaces: role
assign/revoke (`RoleAssignmentEndpoints`), permission-grant configuration
(`PermissionEndpoints.SetGrantAsync`), sensitive-action-rule configuration
(`MakerCheckerEndpoints.SetRuleAsync`), and maker-checker request/decide
(`MakerCheckerEndpoints.RequestApprovalAsync`/`DecideAsync`). The real difference from the
retired round-6 `OperatorKeyGate`: the proof is cryptographically BOUND to the exact identity
being asserted in this request (via HMAC), not an independent flag that waves any request through
regardless of which actor id it names - a valid proof computed for one actor id does not satisfy
a request asserting a different one. Provisioning still mirrors BIO-CRD-001-QA-01's own
remediation (`ITenantCredentialStore`/`TenantCredentialProvisioning`): a constant-time-compared
secret (`IActorProvenanceStore`), loaded from `IConfiguration`'s `Rbac:ActorProvenanceSecret` out
of band (`ActorProvenanceProvisioning`), never reachable through any HTTP endpoint, never
committed to source, and failing CLOSED when unconfigured.

**This is explicitly, deliberately an INTERIM provenance mitigation - not authentication, and not
separation-of-duties**, per ADR-0006 Decision 3's own "what this interim closure explicitly does
not buy." Both this file's own header comment and this section say so in as many words, because
the residual gap is real and must stay visible, not quietly narrowed into looking closed: whoever
holds the shared provenance secret can compute a valid proof for ANY actor id, including
presenting two DIFFERENT ids across two separate calls to self-approve a maker-checker request
the same way QA-07's own self-approval check would otherwise prevent for a single identity -
provenance proves "this passed through something holding the shared secret," never "this is a
specific, distinct, verified person." This residual is accepted and disclosed per Decision 3, not
something a further round should try to close - and it is the identical residual the retired
round-6 gate already carried, cryptographic binding to the specific identity notwithstanding: the
real fix, binding `X-Actor-Id` to a genuinely authenticated principal, remains BIO-INT-001's job.

### AC04: role changes are audited, and never deleted

`RoleAssignmentService.AssignAsync`/`RevokeAsync` both follow the identical ADR-0003 Decision 7
audit-atomicity discipline every mutation in this epic applies: a per-`(PersonId, Role)` lock,
audit-before-commit, `ExistsAsync` verification on an ambiguous audit-write throw,
`CancellationToken.None` on the commit once the audit write is confirmed. Revoking a role is a
governed transition (`Active` → `Revoked`, terminal until re-assigned) - the record and its
history survive forever, mirroring BIO-IDN-003's `IdentifierStatus`. Re-assigning a previously
revoked role reactivates the SAME natural-key record rather than creating a duplicate.

### One unified audit trail for the whole feature

`RbacAuditEventBuilder`/`IRbacAuditSink` are shared by `RoleAssignmentService`, `PermissionService`
and `MakerCheckerService` alike - one audit trail for the whole feature, distinguished by each
event's `AuditTarget.Type` (`person_role_assignment`, `role_permission_grant`,
`sensitive_action_rule`, `approval_request`), rather than four parallel, near-identical audit
plumbing stacks.

### Applying BIO-IDN-001-QA-01 and BIO-IDN-002-QA-01 from the start

The sixth story in a row to apply both of these review-round lessons from the first commit:
every aggregate's constructor is `internal`, every repository is `internal` with an explicit
`InternalsVisibleTo` grant, every repository independently re-validates identity immutability
and initial state at the persistence boundary, and every mutation that names a `PersonId`
(`RoleAssignmentService.AssignAsync`, `PermissionService.HasPermissionAsync`) proves that person
exists in the resolved tenant through this project's own narrow `IPersonExistenceGateway` before
anything else - no `ProjectReference` to BIO-IDN-001's project.

### Lock scope reasoned explicitly, not assumed by analogy (BIO-IDN-003-QA-01's lesson)

Two genuinely different lock scopes are used deliberately, not by copying one shape everywhere:
`SensitiveActionRuleLockRegistry` is keyed `(TenantId, ActionKey)` - a small admin toggle with no
invariant beyond that single key, mirroring BIO-IDN-003's `IdentifierUniquenessRuleLockRegistry`.
`ApprovalRequestLockRegistry` is keyed `(TenantId, ActionKey, TargetRef)` - the SCOPE
`ApprovalRequest`'s own "at most one Pending per target" invariant spans, mirroring BIO-IDN-003's
`InstitutionalIdentifierLockRegistry` shape exactly (natural key + history). Neither scope ever
needs to lock the other - a rule change and an approval-request write touch disjoint keys.

### Organisation/campus/branch and pagination

Out of scope, matching every sibling identity-wave story's identical choices: no organisational
hierarchy registry exists yet; no pagination on the list/history endpoints.

### Tenant context seam

Identical shape to every sibling identity-wave story's own `Tenancy/ICurrentTenantProvider.cs`
(duplicated, not shared). Ships the real `Biolog.Api` wiring in-story
(`api/Biolog.Api/Modules/BioIdn006Module.cs`, `Program.cs` registration, a thin adapter reading
the already-shipped `ITenantContextAccessor`) rather than deferring it, per ADR-0002 Decision 6's
per-channel accommodation-ending rule.

## Deliberately out of scope for this story

- Retrofitting real permission enforcement into any sibling story's already-accepted endpoints
  (see "This story's scope" above) - a disclosed future integration step.
- A tenant-provisioning/seeding mechanism through the public API - a tenant's first `SUPER_ADMIN`,
  and by extension (BIO-IDN-006-QA-03/QA-04/QA-07) every subsequent role assignment, permission/
  rule configuration, or maker-checker workflow participation, is created out-of-band (see
  BIO-IDN-006-QA-01), the same way real production RBAC systems solve this exact bootstrap
  problem. This story's own mutating endpoints ARE now gated behind its own permission model
  (BIO-IDN-006-QA-01/QA-02/QA-03/QA-04/QA-07) - only the READ endpoints remain open to any
  asserted actor, not general enforcement across every endpoint this story exposes.
- Tenant-defined custom roles beyond the eleven baseline `RoleType` members.
- A "reopen" endpoint for a decided approval request distinct from raising a NEW request at the
  next sequence number.
- Organisational hierarchy targeting (no registry exists yet).
- Pagination on the list/history endpoints.
- Real authentication (binding `X-Actor-Id` to a verified principal) - explicitly BIO-INT-001's
  job (see BIO-IDN-006-QA-08 above and ADR-0006). `ActorProvenanceGate` is a disclosed, interim
  mitigation on this story's own four highest-risk mutation surfaces, not a substitute - it
  narrows blast radius via a proof bound to the specific asserted identity, it does not
  authenticate anyone.
- The trusted edge itself (stripping a caller-supplied `X-Actor-Id` and injecting a verified one)
  - explicitly Emeka's infrastructure concern per ADR-0006 Decision 3, outside this story's own
  surface.
