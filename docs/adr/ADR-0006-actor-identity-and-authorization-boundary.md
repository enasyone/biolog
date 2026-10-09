# ADR-0006 — Actor identity and the authorization boundary pending real authentication

- **Status:** Accepted (post-freeze addition, BIO-ARC-006-AC04 change control)
- **Date:** 2026-09-22
- **Owner:** Kemi — Chief Architect & Integrator
- **Triggering finding:** BIO-IDN-006 `ZA-IDN006-01` (Zainab, Critical, reviewed head `4c39477`) — `X-Actor-Id` treated as authenticated identity, with zero provenance binding, feeding a real authorization decision.
- **Builds on:** [ADR-0001](ADR-0001-canonical-identifiers-clocks-and-versioning.md) §1.5 — identifiers carry no authority; [ADR-0002](ADR-0002-tenant-context-and-database-routing.md)'s framing — "an identifier arriving in a request is a claim, not an authorisation"; [ADR-0003](ADR-0003-audit-evidence-and-event-envelopes.md) — audit evidence and actor attribution; [ADR-0005](ADR-0005-migration-ownership-and-sequencing.md) Decision 4.6 — the precedent this ADR's Decision 3 draws on for what a scope boundary is and is not.
- **Applies to:** every BIOLOG surface that resolves an actor identity for audit attribution or an authorization decision before BIO-INT-001 ships. Today: `BIO-TEN-001`, `BIO-TEN-002` (audit actor only) and `BIO-IDN-001` through `BIO-IDN-006` (audit actor; `BIO-IDN-006` additionally an authorization decision) for the asserted-actor accommodation (Decisions 1-5); `BIO-MOB-004`, and any future device-authenticated mobile/kiosk story, for the device-role mechanism (Decision 6).
- **Change policy:** shared contract. This ADR is itself a post-freeze addition (no prior version to amend); changing it going forward needs a superseding ADR and recorded Kemi approval, the same policy ADR-0001 through ADR-0005 carry.
- **Post-freeze addition:** added 2026-09-22 under BIO-ARC-006-AC04's change-control process, triggered by Zainab's BIO-IDN-006 security review (`ZA-IDN006-01`, Critical) and an architectural read requested by god (hive thread `conv-e12f10`). Kemi's approval recorded here. **Independent review (AC04 step 6): confirmed by god and Zainab.** Both read this ADR's full text directly against the reviewed BIO-IDN-006 code and independently re-ran verification (`bash tests/foundation/BIO-ARC-006.test.sh`, `python3 scripts/check-contract-freeze.py`, `node --test contracts/js/`); no text defect found.
- **Zainab's caveat, carried forward explicitly so this ADR is never misread as more than it is:** confirming this ADR's text is correct is not evidence that `BIO-IDN-006` itself is remediated. Decision 3's provenance-verification implementation and its edge/gateway evidence are `BIO-IDN-006` round 4's own, separate acceptance criteria — in flight, not closed by this ADR landing.
- **Post-freeze amendment (Decision 6, snapshot 1.1.10):** added 2026-09-30 under BIO-ARC-006-AC04's change-control process. `BIO-MOB-004`'s builder (Busola) found that mobile has no human-actor identity at all (not even the asserted `X-Actor-Id` shape Decisions 1-5 cover) and asked for a ruling before building a role-gated verification action. Decision 6 names a device-scoped role mechanism as the sanctioned pattern, distinct from and outside Decisions 1-5's asserted-actor accommodation because it rests on an authenticated (not merely asserted) principal, **confirmed by independent review (AC04 step 6) from god.** god read the full Decision 6 diff directly against the reviewed BIO-MOB-004 ruling - the authenticated-vs-asserted distinction holds and the residual (device role is not per-officer accountability) is named honestly, not overclaimed - and independently re-ran verification (`check-contract-freeze.py`, this test suite, `node --test contracts/js/`). Kemi's approval recorded here.

## Context

No authentication story has landed yet (`BIO-INT-001`, W12). Every mutating endpoint built
before it resolves "who is the caller" from `X-Actor-Id`, a caller-controlled HTTP header,
via the identical `ActorResolver` pattern — documented, story by story, as "the same
disclosed seam every sibling story in this epic already uses." `BIO-TEN-001`, `BIO-TEN-002`
and `BIO-IDN-001` through `BIO-IDN-006` all use it.

Two genuinely different things get built on top of that same asserted claim, and this ADR
exists because the difference was not previously stated anywhere as a rule:

- **Audit attribution** — recording who *claims* responsibility for an action every caller
  can already perform, regardless of who they claim to be. `BIO-TEN-001`, `BIO-TEN-002` and
  `BIO-IDN-001` through `BIO-IDN-005` all only do this: every endpoint they expose is open
  to any asserted actor, uniformly. A forged identity here changes who is blamed, not what
  the forger can do — there is no differential authority to escalate into.
- **Authorization decisions** — granting or withholding capability contingent on the
  identity claim. `BIO-IDN-006` is the RBAC and maker-checker story itself, and its round-1
  fix made mutating `SUPER_ADMIN` contingent on the caller already asserting an *active*
  `SUPER_ADMIN` identity — checked by reading the identical forgeable header.
  Zainab's review (`ZA-IDN006-01`) proved directly what that difference means: any caller
  who knows or guesses a privileged PERSON id can assert it, inherit that person's full
  authority, and have the resulting audit record falsely attribute the action to the
  victim. Round 1 built an elaborate check on an unauthenticated foundation; the check was
  real, the foundation was not.

`BIO-INT-001` itself depends on `BIO-IDN-006` (real authentication maps identity-provider
claims onto the roles `BIO-IDN-006` defines), not the other way around — so `BIO-IDN-006`'s
role model has to exist and be usable before real authentication exists to bind an identity
to it. That dependency inversion is why this gap cannot be closed by simply requiring
`BIO-INT-001` first, and why it needs its own decision rather than a deferral.

## Decision 1 — Naming the accommodation and its two uses

The **asserted-actor accommodation**: before `BIO-INT-001` ships, a request's actor identity
is whatever `X-Actor-Id` claims, with no cryptographic, session, or provenance proof binding
the request to that identity. This accommodation has exactly two legitimate shapes, and no
story may use it as anything other than one of these two:

1. **Audit-attribution use** — the claim is recorded as who a caller says performed an
   action that caller could perform regardless of who they claimed to be.
2. **Authorization-decision use** — the claim gates whether a caller may perform an action
   at all, or which action, based on an assumed identity.

Only shape 1 is safe under this accommodation. Shape 2 is not, and Decision 2 states why.

## Decision 2 — The boundary: audit-attribution-safe, authorization-unsafe

The asserted-actor accommodation may continue to back an audit-attribution field on an
endpoint that is otherwise open to any caller. This creates no differential authority a
forged identity could escalate into, and is what `BIO-TEN-001`, `BIO-TEN-002` and
`BIO-IDN-001` through `BIO-IDN-005` do today — confirmed by direct inspection of all five
`BIO-IDN` feature projects' production code (no `RoleType`, `HasPermission`, or any other
role-gating logic anywhere in them; every endpoint is uniformly open).

The accommodation must never be the sole input to an authorization decision — any check of
the shape "does this actor already hold role or permission X" — because the actor itself is
unauthenticated. `ZA-IDN006-01` is the proof: Round 1's SUPER_ADMIN-holds-SUPER_ADMIN gate
is exactly this shape, and it is bypassed by asserting a known privileged id.

This is a difference in **kind**, not degree, between `BIO-IDN-006` and every sibling story
that came before it — which is why `review-routing.json` already scoped
`security_review: true` to `BIO-IDN-006` alone among the six `BIO-IDN` stories, correctly,
before this ADR or Zainab's finding existed to explain why.

## Decision 3 — BIO-IDN-006's interim closure: verified provenance, not a stronger shape check

No further round of hardening `ActorResolver`, `RoleAssignmentService`, or
`PermissionService` in isolation can close `ZA-IDN006-01`. This is the same shape of gap
ADR-0005 Decision 4.6 names for a supplied migration runner: "there is no shape check,
however elaborate, that closes this," because the missing ingredient is proof of **who**
supplied a value, not a stronger inspection of **what the value looks like**. Round 1's fix
was already the elaborate version of that dead end — a second lookup keyed by the same
forgeable input does not verify the input.

Zainab's required closure states the only shape that can work: `X-Actor-Id` may remain the
transport, but only if a trusted edge strips any caller-supplied value and injects a
verified one, and the application checks that injected value's provenance before trusting
it for an authorization decision — never accepting a caller-supplied value directly.
`BIO-IDN-006`'s round 4 must implement the application-side half of this (verifying
provenance on every RBAC-mutating and maker-checker endpoint before `ActorResolver`'s value
reaches an authorization check); the edge/gateway half that actually strips and re-injects
the header is an infrastructure concern outside this story's own surface — Emeka's domain
to confirm exists or gets added at the deployment boundary.

**What this interim closure explicitly does not buy**, so it is never overclaimed:

- It is not authentication. It does not issue, verify, or expire a credential for a specific
  person.
- It does not support session revocation or replay protection beyond whatever the trusted
  edge itself provides.
- Unless the trusted edge performs genuine per-operator authentication (its own, separate
  concern, not something this ADR specifies), it does **not** restore separation-of-duties
  for maker-checker: a party who can reach the trusted edge can still self-approve by
  presenting two different asserted actor ids in two requests, since provenance proves "this
  passed through the edge," not "this is a specific, distinct, verified person."

That residual — no genuine separation-of-duties guarantee until identity itself is real — is
an accepted, disclosed scope boundary, not a defect carried forward silently, matching
ADR-0005 Decision 4.6's own precedent for what "this module cannot prove more without
becoming a different system" looks like when recorded honestly.

## Decision 4 — BIO-IDN-001 through BIO-IDN-005: a disclosed residual, not a retroactive review

Direct inspection (not a re-read of story documentation) of all five feature projects'
production code confirms zero authorization-gating logic exists in any of them — no
`RoleType`, `HasPermission`, or equivalent check. Every endpoint they expose is uniformly
open to any asserted actor; `X-Actor-Id` is used only for audit attribution, per Decision 1's
safe shape.

**Residual, catalogued here explicitly rather than left in five separate doc comments:** a
forged actor id can misattribute an audited action in any of these five stories to an
innocent person — a non-repudiation gap. This is real, and it does not close until
`BIO-INT-001` ships real identity, same as everything else in this ADR. It does **not** cross
the privilege-escalation boundary Decision 2 draws, because there is no differential
authority anywhere in these five stories for a forged identity to escalate into — so it does
not trigger a security review under the same convention that correctly scoped
`security_review: true` to `BIO-IDN-006` alone. No `BIO-IDN-001` through `BIO-IDN-005` story
requires reopening, a retroactive security review, or a code change as a consequence of this
Decision.

## Decision 5 — Closing story

`BIO-INT-001` (W12) ends the asserted-actor accommodation entirely, the same way ADR-0002
Decision 6 names `BIO-TEN-002` as the story that ends the tenant-resolution accommodation for
its own channel. Once `BIO-INT-001` ships real authentication mapping identity-provider
claims onto `BIO-IDN-006`'s roles:

- Every story under this ADR upgrades its actor resolution from the asserted `X-Actor-Id`
  read to the real authenticated principal — a named follow-up on each story, not silently
  left as permanent accommodation coverage, mirroring ADR-0002 Decision 6's own upgrade
  obligation.
- Any interim trusted-edge/provenance mechanism built under Decision 3 is retired, not left
  running in parallel with real authentication as an extra, redundant layer nobody owns.
- Decision 4's non-repudiation residual closes automatically, since audit attribution then
  reads from the same real authenticated principal.

## Decision 6 — Device-authenticated actors: a distinct case, and the sanctioned device-role pattern

`BIO-MOB-004` (mobile card/NFC/QR/face verification) needs "actions obey role permissions"
on a surface with no human-actor identity of any shape — not even the asserted `X-Actor-Id`
Decisions 1-5 cover. Mobile authenticates a *device* (`X-Biolog-Device-Id`/`X-Biolog-Device-Secret`,
hashed, constant-time-compared — the same mechanism `BIO-PHO-002` and `BIO-MOB-003` use, and
the same `principal`-channel realization ADR-0002 Decision 7 names), never a person. Neither
`DeviceRegistry` (`BIO-DEV-001`) nor this ADR's existing Decisions had a role concept attached
to that identity.

**Why this is a different case, not a fourth instance of Decisions 1-5's problem:** the
asserted-actor accommodation is unsafe as an authorization basis specifically because
`X-Actor-Id` is a bare, unauthenticated claim — Decision 2's boundary exists to keep it out
of authorization decisions for exactly that reason. Device identity is not that. It is
authenticated: a device proves possession of a provisioned secret before anything checks its
role. Gating an authorization decision on an authenticated device identity does not repeat
`ZA-IDN006-01`'s shape (an unverified claim standing in for authority) — it sidesteps it,
because the credential backing the decision is real, not asserted. This ADR's title says
"pending real authentication"; for the device half of a request, authentication already
exists today, just not for the human operating the device.

**The sanctioned pattern, for `BIO-MOB-004` and any future device-authenticated story with
the same shape:**

1. A closed, tenant-scoped role enum attached to the device's own already-authenticated
   identity (not to `DeviceRegistry`'s schema — a project-local mapping in the owning
   story's own path, keyed by device id as an opaque foreign key, so no story other than the
   one that owns `DeviceRegistry` ever needs to touch its record shape). Give it its own
   distinctly-named type; never reuse `BIO-IDN-006`'s `RoleType` enum, which names human
   RBAC roles and must never be mistaken for a device-scoped one at a call site or in a log
   line.
2. Default to the least-privileged role when a device has no assignment — the same
   fail-safe-default posture `ADR-0002` Decision 2 takes for tenant resolution and
   `BIO-ACC-001` takes for access-policy evaluation.
3. The role-**assignment** write (an administrator sets which role a device holds) is gated
   by real human RBAC — `X-Actor-Id` plus Decision 3's `ActorProvenanceGate` plus
   `PermissionService.ActorHasPermissionAsync` — the identical shape `BIO-MOB-005`'s
   `ManagePolicyPermissionKey` already uses. This is an ordinary authorization-decision use of
   the asserted-actor accommodation, safe under Decision 3 exactly as written; Decision 6 adds
   nothing new here.
4. The role-gated **action** endpoints (execute a verification, override a deny) check the
   *calling device's* assigned role server-side before allowing the action — never a
   client-asserted role, never trusted from a header the mobile app itself sets.

**What this explicitly does not buy, so it is never overclaimed:** a device's role is not a
person's role. Any officer who can operate a given device inherits whatever role is assigned
to that device — the platform cannot yet distinguish which individual is holding it. This is
not a new gap Decision 6 introduces; mobile already had no per-officer identity of any kind
before this decision, for the same reason Decisions 1-5 exist at all. It is named here so the
device-role mechanism is never mistaken for per-operator accountability. Closing it needs a
real per-officer identity on mobile — a distinct problem from `BIO-INT-001` (which ships
backend authentication providers, not a mobile login UX) and not yet scheduled as its own
story, the same "named but not yet built" treatment `BIO-MOB-003`'s device-enrollment gap
received.

## Consequences

- `BIO-IDN-006` round 4 implements provenance verification on its RBAC-mutating and
  maker-checker endpoints per Decision 3, and stops hardening `ActorResolver`'s identity
  check itself — a fifth round of that shape would not converge, for the same reason a sixth
  round of shape-checking a migration runner would not have in ADR-0005.
- `BIO-TEN-001`, `BIO-TEN-002` and `BIO-IDN-001` through `BIO-IDN-005` need no code change
  from this ADR; their existing audit-attribution-only use was already the safe shape.
- Every future W04+ story that reads an asserted actor identity — for audit attribution or,
  like `BIO-IDN-006`, for an authorization decision — must state which of Decision 1's two
  uses it is making, and if it is the authorization-decision use, must implement Decision 3's
  provenance check rather than inventing its own identity-hardening shape.
- The non-repudiation residual named in Decision 4, and the separation-of-duties residual
  named in Decision 3, are both tracked as closing only at `BIO-INT-001`, not before.
- `BIO-MOB-004` implements Decision 6's device-role pattern for its role-gated verification
  actions; any future device-authenticated story needing the same shape reuses this pattern
  rather than inventing its own, per Decision 6's own "for `BIO-MOB-004` and any future
  device-authenticated story" scope.

## Alternatives rejected

- **A further round of hardening the identity check inside `ActorResolver` or the RBAC
  services themselves** — the same shape as Round 1's fix, which `ZA-IDN006-01` already
  bypassed. Matches ADR-0005 Decision 4.6: no shape check converges when the missing proof
  is of who supplied a value, not what it looks like.
- **Requiring `BIO-INT-001` to ship before `BIO-IDN-006` can ship at all** — rejected by the
  dependency inversion in Context: `BIO-INT-001` needs `BIO-IDN-006`'s role model to map
  claims onto, so sequencing it first is not available.
- **Retroactive security review of all six `BIO-IDN` stories** — rejected for `BIO-IDN-001`
  through `BIO-IDN-005` specifically: Decision 4's direct code inspection found no
  differential authority in any of them for a forged identity to escalate into, so there is
  nothing a security review would be evaluating that Decision 2's boundary does not already
  cover.
- **Accepting `ZA-IDN006-01` as a pure disclosed boundary, with no interim code or
  infrastructure change, alongside ADR-0005 Decision 4.6** — rejected. Decision 4.6's
  untrusted input is already supplied by a trusted internal operator by construction (Kemi,
  at the `assignSequence` call site); `BIO-IDN-006`'s caller is untrusted by anything today,
  so a paper boundary alone would leave a live, unmitigated exploit rather than an accepted
  theoretical one.
- **For Decision 6: deferring all of `BIO-MOB-004`-AC03 off the mobile surface entirely,
  mirroring `BIO-MOB-005`-AC04's "keep the gated action backend-only, human-actor-only"
  treatment** — rejected as not transferable. `BIO-MOB-005`'s gated action (managing a
  capture policy) is an administrative action a human already performs through
  `X-Actor-Id`; `BIO-MOB-004`'s role-gated actions (submit a verification, override a deny)
  are the mobile officer's own on-device actions and are the story's own required
  acceptance criterion — there is no human-actor-only surface to defer them to yet.
- **For Decision 6: reusing `BIO-IDN-006`'s `PermissionService`/`RoleType` directly for the
  device-role check** — rejected as unavailable, not merely undesirable.
  `PermissionService.ActorHasPermissionAsync` gates on `CanonicalId.IsPersonId(actorId)`
  before anything else; a device id fails that check by construction and the gate silently
  reports "not granted," so there is no accommodation to make here, only a different,
  narrower mechanism to add for the device-identity case Decisions 1-5 never covered.
