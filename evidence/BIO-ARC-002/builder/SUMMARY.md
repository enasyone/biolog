# BIO-ARC-002 — builder evidence

- **Story:** BIO-ARC-002 — Define tenant context and database routing contract
- **Owner:** Kemi — Chief Architect & Integrator (claude-opus-5)
- **Wave / batch:** W01 / W01.2 · conflict group `shared-contract`
- **Reviewers:** Seyi, then Zainab (security review required) · acceptor Chioma
- **Branch:** `feature/BIO-ARC-002`, worktree `biolog-worktrees/BIO-ARC-002`
- **Date:** 2026-09-20
- **Remote:** none. Nothing pushed. `governance/release-state.yaml` untouched.

## Branch base — please read before reviewing

**This branch is cut from `feature/BIO-ARC-001` (`7ad8ccd`), not from `develop`.**

The packet said to branch from `develop` and also to build on the ARC-001 contracts.
Those two instructions conflict right now: `develop` is still at `2224d4d` and carries
only BIO-FND-001. BIO-ARC-001 and BIO-FND-002…006 are all accepted but unmerged, so the
identifier, clock and versioning contracts this story depends on do not exist on
`develop`.

I did not merge anything into `develop` — `06-LOCAL-DEVELOPMENT/LOCAL-GIT-WORKFLOW.md`
step 8 makes that Chinedu's. Branching from the accepted ARC-001 tip is the smaller
deviation: it builds on the real frozen contracts instead of a copy, and once ARC-001 is
merged this branch replays onto `develop` cleanly in story order. Reported to Chinedu at
builder-complete. This is the third time I have raised the unmerged-`develop` flag.

## What was built

`docs/adr/ADR-0002-tenant-context-and-database-routing.md` is the normative decision.
The schemas and code are its machine-readable and executable forms.

| Artifact | Purpose |
|---|---|
| `docs/adr/ADR-0002-…md` | Resolution rules, no-default rule, immutability, routing, audit, rejected alternatives |
| `contracts/schemas/tenant-domain.schema.json` | Verified hostname → tenant; only `VERIFIED` resolves |
| `contracts/schemas/tenant-data-binding.schema.json` | Tenant → database; only `ACTIVE` routes; forbids credentials as contract data |
| `contracts/schemas/tenant-context.schema.json` | The resolved, frozen context |
| `contracts/schemas/tenant-access-denial-event.schema.json` | Append-only denial audit; forbids `payload`/`record`/`version` |
| `contracts/js/tenant-context.mjs` | Resolution, hostname normalisation, routing, immutability, cross-tenant assertion, audit emission |
| `contracts/js/tenant-context.test.mjs` | 41 tests, each named with the AC it proves |
| `tests/foundation/BIO-ARC-002.test.sh` | 22 artifact/consistency checks, including a BIO-ARC-001 regression run |

## Decisions of record

1. **Three channels only** — `host` (a `VERIFIED` TenantDomain), `principal` (an
   authenticated token claim), `service` (an explicit job context). A tenant id in a
   header, query or body field is **not** a channel.
2. **Channels are not ranked; they must agree.** Every channel that supplies a tenant must
   name the same one. A disagreement between a verified host and an authenticated token is
   not a tie to break — it is a misconfiguration or an attempt to reach another tenant with
   a valid token. Both fail closed (403) and are audited. This is what the spec's "must not
   contradict each other silently" resolves to in code.
3. **No default tenant, anywhere, including development.** Zero channels → 400
   `tenant_context_missing`. An unrecognised or unverified host → **404, not 403**, because
   403 confirms a hostname exists and is an enumeration oracle over the tenant registry.
4. **Control-plane context is a distinct kind**, not a tenant context with a null tenant,
   so "unresolved" can never be mistaken for "resolved".
5. **Immutable for the request or job.** Set, delete and redefine all throw. Async work
   inherits the same frozen object. A job resolves its own context and never inherits one
   from the request that enqueued it.
6. **The database binding is looked up, never supplied.** No caller may name a database,
   schema, host, connection string or binding key. No `ACTIVE` binding → fail closed, no
   shared fallback.
7. **Cross-tenant access is denied as 404 `not_found`**, with the real reason
   (`cross_tenant_access_denied`) going only to the audit. The audit is written **before**
   the denial returns, records **both** tenants, and carries no payload.

## AC → evidence map

| AC | Proven by |
|---|---|
| **AC01** host / authenticated / service resolution rules | ADR §1; `tenant-domain` + `tenant-context` schemas; tests 1–12 (`AC01: …`), incl. per-channel resolution, hostname normalisation, non-ASCII homoglyph rejection, exhaustive domain-status sweep, platform-host-plus-token, control-plane kind; shell checks AC01 ×6 |
| **AC02** no implicit/default fallback | ADR §2, §4.1; tests 13–19 (`AC02: …`), incl. the 27-case channel matrix, 404-not-403 on unknown host, non-canonical tenant claims, exhaustive binding-status sweep, caller-supplied binding ignored; shell checks AC02 ×4, one of which greps the whole contract tree for any default-tenant constant |
| **AC03** immutable for the request lifetime | ADR §3; tests 20–25 (`AC03: …`), incl. set/delete/defineProperty, frozen channel array, async identity, control-plane immutability; shell checks AC03 ×2 |
| **AC04** cross-tenant fails closed and is audited | ADR §4.3, §5; `tenant-access-denial-event` schema; tests 26–41 (`AC04: …`), incl. audit-before-throw ordering, both tenants recorded, no payload, forged-context rejection, every failure path audited, reason-code/schema parity; shell checks AC04 ×4 |

## Security testing (Threat Model baseline, and the story's mandatory cross-tenant tests)

| Threat-model requirement | Covered by |
|---|---|
| Positive authorised path | Per-channel resolution; same-tenant record allowed with no denial event |
| Unauthorised role | Control-plane context refused at `bindingForRepository` and `assertRecordInTenant` |
| **Cross-tenant identifier substitution** | Foreign-record denial; 27-case channel matrix; tenant id in a request field ignored; caller-supplied binding ignored |
| Malformed / untrusted payloads | Malformed hostnames incl. path traversal, null bytes, SQL-shaped input, 254-byte labels, non-string types; non-canonical and wrong-type tenant claims |
| Logging / redaction behaviour | Denial event asserted to carry no `payload`/`record`/`version`; caller message asserted not to disclose the reason |
| Concurrency / state transitions | Context immutability, including async inheritance and re-resolution isolation |

**A real defect found and fixed during this story.** While probing my own code I found that
`assertRecordInTenant` compared `context.tenant_id` to `record.tenant_id` directly, so a
malformed context carrying `tenant_id: null` admitted a record whose tenant was also
`null` — two absent tenants matching each other. Fixed by `assertResolvedTenantContext`,
which validates that a context carries a canonical tenant identifier and a non-empty
binding before anything trusts it. A test I had written skipped that case with a
`continue`; the skip is removed and the case is now asserted across five forged-context
shapes. Recording it because the fix is the interesting part of this story's security
posture, not a tidy detail.

## Test results

```
tests/foundation/BIO-ARC-002.test.sh   22/22 checks passed   (exit 0)
node --test contracts/js/              101 pass, 0 fail      (exit 0)   41 new, 60 from BIO-ARC-001
tests/foundation/BIO-ARC-001.test.sh   24/24 checks passed   (exit 0)   no regression
scripts/secret-scan.sh --all           clean                 (exit 0)
```

Both suites are dependency-free and run offline.

## Constraints observed

- Synthetic data only: every tenant, hostname, principal and record is fabricated.
- No GitHub remote added, nothing pushed, `github_push_enabled` untouched.
- BIO-ARC-001's frozen files were **not modified** — this story only adds to
  `contracts/`, `docs/adr/`, `tests/foundation/`, plus index lines in
  `contracts/README.md` and `docs/adr/README.md`. One sentence in ADR-0002 was reflowed
  for greppability; ADR-0001 is untouched.

## Notes for the reviewers

1. **Branch base** — see the section above. Not a defect, but check it before reading the
   diff against `develop`.
2. **For Zainab (security review):** the three decisions I would attack first are the
   unanimity rule (§1.2), the 404-not-403 responses (§1.3 and §4.3), and the audit-before-
   response ordering (§5). The homoglyph rejection in `normaliseHostname` is the one place
   I chose refusal over normalisation, and it is deliberate — folding a Cyrillic а into an
   ASCII a here would put homoglyph confusion inside the tenant boundary.
3. **Follow-up owed, unchanged from BIO-ARC-001:** once BIO-FND-002…006 merge,
   `scripts/quality.sh` needs lines for both `tests/foundation/BIO-ARC-001.test.sh` and
   `tests/foundation/BIO-ARC-002.test.sh`.
4. **Applied lesson from BIO-ARC-001:** where an input domain is enumerable I assert it
   exhaustively rather than sampling — all 27 channel combinations, every domain status,
   every binding status, and reason-code parity between code and schema. Both findings
   against ARC-001 were boundaries a sampled test had skipped.

## Round 2 — QA-01 (Critical) and QA-02 (High) fixed (Seyi review, 2026-09-21)

Both findings are correct. I reproduced each one against `7b135f2` before changing
anything, and both are exactly the threats this ADR documents, working in my own code.
The ADR described the rules; the implementation did not enforce them.

### QA-01 (Critical) — a forged context could select an arbitrary database binding

**Reproduced:** `bindingForRepository({ kind: 'tenant', tenant_id: A, binding_key: 'ANY-DB-I-LIKE' })`
returned `ANY-DB-I-LIKE`. My `assertResolvedTenantContext` validated *field shape* — right
`kind`, canonical tenant, non-empty binding — which is precisely the part a forged object
controls completely. So "a caller can never name the database" was enforced only against
callers who did not try.

**Fix — provenance instead of shape.** A module-private `WeakMap`, keyed by object
identity, records the tenant and binding that control-plane resolution produced. Only
`freezeContext` writes to it, and only `resolveTenantContext` and
`createControlPlaneContext` call that. Validation now requires membership, and
`bindingForRepository` reads the binding **from the map, not from the object**.

That makes the rule structural rather than advisory:

- A hand-built context is refused as `tenant_context_not_resolved` (403), audited.
- A `binding_key` set on any object a caller supplies is **inert** — never read, so it
  cannot route anything.
- Provenance cannot be serialised, copied or reconstructed: `{...context}`,
  `JSON.parse(JSON.stringify(...))`, `Object.create(context)`, a wrapping `Proxy` and a
  function carrying the same fields are all refused, and each is asserted.

### QA-02 (High) — denials could return unaudited, and control-plane misuse could not be audited at all

**Reproduced:** with no `audit` argument, a cross-tenant read still threw its ordinary
`404 not_found` and wrote nothing. `emitDenial` returned quietly when the sink was absent.
Seyi's second half is also right and I fixed it too: `bindingForRepository` and
`assertResolvedTenantContext` took no audit parameter, so `control_plane_context_misuse`
was structurally unauditable through the public API.

**Fix — the audit sink is a hard dependency of denying.** A missing or non-callable sink
now fails closed with `audit_unavailable` (503) instead of returning the denial, and a
sink that throws propagates rather than being swallowed. `bindingForRepository` and
`assertResolvedTenantContext` take `{ audit, principal }` and route their refusals through
the same path, so every named denial reaches the audit.

The reasoning, recorded in ADR-0002 §5: an unaudited denial is the one outcome worse than
the access it refused — the caller is stopped and nobody learns it happened. Fail-closed
costs availability on a path that was already refusing; silence costs the security signal
permanently. A successful path still needs no sink: this constrains denying, not working.

`audit_unavailable` is deliberately **not** in the event `reason_code` enum — it names the
failure to write an event, so no event carries it. `tenant_context_not_resolved` **is**
added to the enum (now 8 codes), because it is a real audited denial.

### Files changed

| File | Change |
|---|---|
| `contracts/js/tenant-context.mjs` | `RESOLVED` WeakMap provenance; `isResolvedContext`; provenance-based validation and binding lookup; `emitDenial` fails closed; audit parameters on `bindingForRepository` / `assertResolvedTenantContext` |
| `contracts/schemas/tenant-access-denial-event.schema.json` | `tenant_context_not_resolved` added to `reason_code`; comment on why `audit_unavailable` is absent |
| `contracts/js/tenant-context.test.mjs` | 12 regression tests (`AC01/QA-01`, `AC04/QA-02`); 4 existing tests updated for the stricter contract |
| `tests/foundation/BIO-ARC-002.test.sh` | 4 checks: both ADR rules, forged-context refusal, denial-without-sink failing closed |
| `docs/adr/ADR-0002-…md` | §4.1 provenance rule; §5 hard-dependency rule; both costs in Consequences; two new rejected alternatives |

### Round 2 results

```
tests/foundation/BIO-ARC-002.test.sh                26/26 checks passed  (exit 0)   was 22/22
node --test contracts/js/                           113 pass, 0 fail     (exit 0)   was 101
tests/foundation/BIO-ARC-001.test.sh                24/24 checks passed  (exit 0)   no regression
scripts/secret-scan.sh --all                        clean                (exit 0)
evidence/BIO-ARC-002/codex/independent-review.sh    all 5 checks PASS    (exit 0)   was exit 1
```

Seyi's script is re-run unmodified and filed as `reviewer-script-rerun.txt`; its two new
probes — forged context cannot choose a binding, and a denial cannot return without a
mandatory sink — both pass.

### What I take from this

Both defects were places where the ADR stated a rule and the code checked something
adjacent to it. "A caller can never name the database" was enforced by validating fields a
caller writes; "audit before response" was enforced by an optional argument. **A rule
written in an ADR is not enforced until something unforgeable stands behind it** — object
identity for provenance, a required dependency for the audit. I have added that to my own
notes as the check to run over every remaining shared contract before W01 exit.

### Scope

QA-01 and QA-02 only, plus the control-plane audit gap Seyi raised inside QA-02. No change
to resolution rules, the no-default-tenant rule, immutability, or any BIO-ARC-001 file.

## Round 3 — QA-03 fixed (Seyi re-review round 2, 2026-09-21)

**Finding:** `emitDenial` called the audit sink and ignored the promise it returned. With
a real — asynchronous — audit store, the `404` was returned while the write was still in
flight, and a rejection arriving afterwards could no longer become `audit_unavailable`.
Correct, and it is the asynchronous form of QA-02.

**Reproduced first.** My probe crashed the process on an unhandled rejection, which is the
defect at its most visible: the denial had already been returned and the rejected audit
promise had nowhere to go.

### Decision: denying is asynchronous, because durable audit is asynchronous

The re-review offered two ways out — await an async sink through an async denial path, or
require and enforce a synchronous one. I took the first.

Requiring a synchronous sink would keep the API simple and push durability onto every
caller, where it is invisible and unenforceable; a caller that writes to a database would
have to either block or lie. Making the denial path async puts the audit dependency in the
*shape* of the call, so a caller cannot forget to wait for something it has to await
anyway. A synchronous sink still works — it simply has nothing to await.

**API change (the one thing to look at first in this diff):** `resolveTenantContext`,
`bindingForRepository`, `assertResolvedTenantContext` and `assertRecordInTenant` now
return promises. `normaliseHostname`, `lookupTenantDomain`, `collectTenantClaims`,
`createControlPlaneContext`, `isResolvedContext` and `buildDenialEvent` stay synchronous —
none of them can deny. No consumer code exists outside this contract yet, so nothing
downstream breaks; `contracts/README.md` states the rule for the stories that follow.

### Fix

| File | Change |
|---|---|
| `contracts/js/tenant-context.mjs` | `emitDenial` is async and awaits a thenable sink; the four denial-capable functions are async; `AUDIT_TIMEOUT_MS` (5 s, per-call `auditTimeoutMs`) bounds the write; every failure to persist — missing, non-callable, sync throw, rejection, timeout — becomes `audit_unavailable` with the original failure on `cause` |
| `contracts/js/tenant-context.test.mjs` | 8 QA-03 regression tests; 53 existing tests converted to the async contract |
| `tests/foundation/BIO-ARC-002.test.sh` | 4 QA-03 checks; existing node probes awaited |
| `docs/adr/ADR-0002-…md` | §5 records "written means settled", the async consequence, the timeout, the accepted cost and two more rejected alternatives |
| `contracts/README.md` | Consumer rule 4: denying is asynchronous, await it, pass a sink |

**Deliberate detail:** the timeout timer is **not** `unref`'d. It is what turns a hung sink
into a refusal, so it must hold the process open until it fires; `finally` clears it either
way, so it never outlives the call. I found this the hard way — with `unref()` the test
runner cancelled the timeout test instead of failing it.

**One behaviour change to flag.** A synchronously throwing sink previously propagated its
raw error; it now fails closed as `audit_unavailable` with the original error on `cause`,
matching every other way of failing to persist. Both satisfy QA-02's requirement; the
uniform code means a caller has one case to handle. The test that asserted the old shape
is updated and says why.

### Regression tests added

| Case | Assertion |
|---|---|
| Async sink resolving after a timer | Event persists **before** the `404` is returned — asserted as an ordered list, not a flag |
| Async sink rejecting after returning | `audit_unavailable` 503, `cause` preserved, and the rejection is observed before anything is returned |
| Promise rejecting on the next microtask | Also fails closed — the cheapest way for an ignored promise to look successful |
| Sink that never settles | Times out to `audit_unavailable`, `detail.audit === 'timed_out'`, and returns promptly |
| All four resolution denials + `bindingForRepository` | Each fails closed on an async audit failure, not just record checks |
| Async sink that succeeds | Still yields the ordinary `404`; fail-closed does not swallow the real outcome |
| Synchronous sink | Still works and is not forced to be async |

### Round 3 results

```
tests/foundation/BIO-ARC-002.test.sh                30/30 checks passed  (exit 0)   was 26/26
node --test contracts/js/                           121 pass, 0 fail     (exit 0)   was 113
tests/foundation/BIO-ARC-001.test.sh                24/24 checks passed  (exit 0)   no regression
scripts/secret-scan.sh --all                        clean                (exit 0)
evidence/BIO-ARC-002/builder/reviewer-probes-awaited.mjs   5/5 PASS      (exit 0)
```

**About the reviewer's script.** `evidence/BIO-ARC-002/codex/independent-review.sh` now
exits 1, and four probes that previously passed report FAIL. That is the calling
convention, not the behaviour: they call the now-async functions synchronously, so nothing
throws. I did not edit the reviewer's script — it is their artifact. Instead
`reviewer-probes-awaited.mjs` transcribes all five probes with `await` added and nothing
else changed, and all five pass, including the new asynchronous-persistence probe. Both
outputs are filed: `reviewer-script-rerun.txt` and `reviewer-probes-awaited.txt`.

### Pattern across all three findings on this story

QA-01, QA-02 and QA-03 are the same mistake at three depths: the ADR stated a rule and the
code enforced something weaker but adjacent. "A caller can never name the database" was
enforced by validating fields a caller writes. "Audit before response" was enforced by an
optional argument, then by a call that was made but not waited for. Each time the gap was
only visible from the hostile side, and each time my own tests passed because they
exercised the deny path with cooperative inputs.

The check I am now running on each rule before calling a contract done: *what would a
caller have to control for this rule to be false, and do they control it?* For QA-03 the
answer was "the sink's timing", which no synchronous test can reach.

### Scope

QA-03 only, plus the async conversion it requires. Resolution rules, the no-default-tenant
rule, immutability, provenance and every BIO-ARC-001 file are unchanged.
