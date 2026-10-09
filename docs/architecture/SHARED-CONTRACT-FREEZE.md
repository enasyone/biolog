# W01 shared-contract freeze

**Freeze version (original):** `1.0.0`
**Current snapshot:** `1.1.12` — ADR-0003 Decision 8 (scoped exception to Decision 7 for the demo-session context-change audit, BIO-OPS-002/BIO-TEN-002) confirmed by independent review — see Amendment history below

**Story:** BIO-ARC-006

**Baseline parent (original freeze):** `a28830e0cb8a0e21006b20942ab7e2d9c686b662`
**Owner after freeze:** Kemi — Chief Architect & Integrator

This record freezes the accepted W01 contracts before feature waves begin. The original
executable snapshot was `governance/contract-freeze/v1.0.0/manifest.json`; its hashes
covered every file under `contracts/` and ADR-0001 through ADR-0005.
`scripts/check-contract-freeze.py` refuses a changed, missing or unlisted frozen artifact
against the **current** snapshot (`v1.1.12`, since ADR-0003 Decision 8's confirmation below). It also
resolves the accepted baseline parent as a real Git commit and compares every manifest
entry and working file to that commit tree. Regenerating hashes inside an existing version
therefore cannot bless a rewrite — a change always requires a new version directory; prior
ones stay exactly as they were.

## Amendment history

| Snapshot | Baseline commit | Change |
|---|---|---|
| `1.0.0` | `a28830e0cb8a0e21006b20942ab7e2d9c686b662` | Original W01 freeze (BIO-ARC-006) |
| `1.1.0` | `290c6f531723bfccf7fd1e75ea4ce5326dc5712b` | **Superseded by 1.1.1 before independent review — see below.** ADR-0003 Decision 7's first draft: audit is a hard dependency of every audited mutation, presenting audit-before-commit and commit-then-rollback as equally valid. |
| `1.1.1` | `b2d6682013e9587cefa40d7ce156a7eea6b37823` | Corrects `1.1.0`: Seyi's BIO-TEN-001 round-2 re-review proved commit-then-rollback is not concurrency-safe. Decision 7 required audit-before-commit only. **FAILED Seyi's independent review (AC04 gate 6):** audit-before-commit durably records `outcome=ALLOWED` before the commit's compare-and-swap is known to succeed; a concurrent writer can still win the commit, leaving a permanent audit record that falsely claims success for a mutation that was actually rejected (BIO-TEN-001 round-3 re-review, `evidence/features/bio-ten-001/codex/REREVIEW-ROUND3.md`, reviewer commit `92e95ce`, in the BIO-TEN-001 worktree). Mechanics remain valid (freeze validator, regression suite); the semantic content of Decision 7 needs a further amendment before this is accepted. |
| `1.1.2` | `49711a88b79aaddc91ec2359e9629b4ff477b616` | Adds ADR-0002 Decision 6 (interim tenant-resolution review evidence, unrelated to Decision 7): confirmed by Seyi (`2026-09-21T22-53-07-838Z-0d67e1`) and god independently before landing. ADR-0003 is otherwise byte-identical to `1.1.1` — Decision 7's FAIL carried forward unchanged; this snapshot did not resolve it. |
| `1.1.3` | `95e1b1cc5a3c60630e0f14711a78879e0751c018` | **Superseded by 1.1.4 — see below.** Decision 7's first fully-passing pattern, but with three citation/scope overstatements (most notably attributing `CancellationToken.None` to BIO-TEN-001 round 5, which did not verify it) caught by Seyi's spot-check before this text was relied on further. |
| `1.1.4` | `40c40d70099ff65b6ac55455dddc7daf9274db99` | Corrects `1.1.3`: each Decision 7 element now cites its actual confirming review individually — lock/audit-before-commit/`ExistsAsync`-as-pattern-for-this-tier/crash-boundary: BIO-TEN-001 round 5 (`evidence/features/bio-ten-001/codex/REREVIEW-ROUND5.md`, reviewer commit `03086c1`, PASS); `CancellationToken.None`: BIO-DEV-001 round 3 (`evidence/BIO-DEV-001/codex/REREVIEW-ROUND3.md`, reviewer commit `1e8eccb`, PASS) — at the commit BIO-TEN-001 round 5 actually reviewed, `TenantService` still passed the caller's token through unchanged. Also: `ExistsAsync` reworded from "confirmed" to "recommended pattern for this tier, not exercised against a genuine append-then-throw case"; crash residual reworded from "provably unreachable" to "the failure's bad consequence is unreachable, not the failure itself." ADR-0002 is byte-identical to `1.1.2`. |
| `1.1.5` | `d906407f3118d5d0d72fc0e06c010f3e34d97a12` | **Superseded by 1.1.6 — see below.** Adds **ADR-0006** (new file, not an amendment to ADR-0001–0005): names the "asserted, unauthenticated actor" accommodation used by BIO-TEN-001/002 and all six BIO-IDN stories, draws the line Zainab's BIO-IDN-006 security review (`ZA-IDN006-01`, Critical) proved matters — safe as audit attribution on a uniformly-open endpoint, unsafe as the sole input to an authorization decision — records BIO-IDN-006's interim trusted-edge-provenance closure and its explicit non-goals, catalogues BIO-IDN-001–005's non-repudiation residual (confirmed by direct code inspection: no differential authority exists in any of them, so no retroactive security review is needed), and names BIO-INT-001 (W12) as the closing story. ADR-0001 through ADR-0005 are byte-identical to `1.1.4`. Kemi's approval recorded in the ADR header; independent review (AC04 step 6) was pending as of this snapshot, not yet confirmed. |
| `1.1.6` | `0f1298fb06a605a60d19d7ad3f019bad1a5697e4` | Corrects `1.1.5`'s "pending" status: **ADR-0006's independent review (AC04 step 6) is confirmed by both god and Zainab.** god re-read the full text against the reviewed BIO-IDN-006 code and independently re-ran verification (this test suite, `check-contract-freeze.py`, `contracts/js`); Zainab confirmed the text accurately captures `ZA-IDN006-01` and its required closure. Zainab's caveat is now recorded directly in the ADR header: confirming the ADR's text is not evidence BIO-IDN-006 itself is remediated — round 4's provenance-verification implementation and edge/gateway evidence are separate, still-open acceptance criteria. ADR-0006's content is otherwise unchanged from `1.1.5`; ADR-0001 through ADR-0005 remain byte-identical. |
| `1.1.7` | `671d69629e39086020ec69a06fbc74c1616b5c38` | **Superseded by 1.1.8 — see below.** Adds **ADR-0002 Decision 7** (amends an existing ADR, unlike `1.1.5`/`1.1.6`'s new-file ADR-0006): BIO-MOB-003's central offline-sync intake has none of Decision 1.1's three channels in their existing shape — no `host` (a single fixed address, not a verified per-tenant hostname), no interactive `principal` (`BIO-INT-001` hasn't shipped) and no `service` (an external device's own request is not internal job work). Names device-secret authentication (mirroring `BIO-PHO-002`'s `IDeviceAuthenticator`) as a realization of the existing `principal` channel, not a fourth one, and records the narrow, internal `ICrossTenantDeviceLookup` mechanism (`api/Biolog.Api/Features/DeviceRegistry/`) that resolves tenant from the authenticated device's own record without reopening Decision 4.3's cross-tenant protection for any other consumer. ADR-0006 is byte-identical to `1.1.6`; ADR-0001, ADR-0003–0005 unchanged throughout. Kemi's approval recorded in the ADR header; independent review (AC04 step 6) was pending as of this snapshot, not yet confirmed. |
| `1.1.8` | `627f84a8628d11a9bd3e8adb129887901e2a5e18` | Corrects `1.1.7`'s "pending" status: **ADR-0002 Decision 7's independent review (AC04 step 6) is confirmed by god.** god read the text directly against the code - the principal-realization argument (proof-of-possession property, not a PKI/mTLS-equivalence claim) holds, and `ICrossTenantDeviceLookup`'s three safety properties are accurately described - and independently re-ran verification (`check-contract-freeze.py`, this test suite, full `scripts/quality.sh`). Decision 7's content is otherwise unchanged from `1.1.7`; ADR-0001, ADR-0003–0006 remain byte-identical. |
| `1.1.9` | `167186c9c32ccda053b6f19f466a434b0c874c2d` | **Superseded by 1.1.10 — see below.** Adds **ADR-0006 Decision 6** (amends an existing ADR, same shape as ADR-0002 Decision 7 at `1.1.7`): `BIO-MOB-004` needs role-gated mobile verification actions on a surface with no human actor identity at all - only an authenticated device identity (`X-Biolog-Device-Id`/`Secret`). Names a project-local, tenant-scoped device-role mechanism as sanctioned, distinct from and safe outside Decisions 1-5's asserted-actor accommodation because it rests on an authenticated principal (the same class as ADR-0002 Decision 7's principal-channel realization) rather than a bare, unauthenticated claim. Role-*assignment* stays gated by real human RBAC (Decision 3's `ActorProvenanceGate` + `PermissionService`, same shape `BIO-MOB-005`'s `ManagePolicyPermissionKey` already uses) - Decision 6 adds nothing new to that half. Names the residual (a device's role is not per-officer accountability) and that closing it needs a not-yet-scheduled real per-officer mobile identity story, distinct from `BIO-INT-001`. ADR-0001–0005 unchanged throughout. Kemi's approval recorded in the ADR header; independent review (AC04 step 6) was pending as of this snapshot, not yet confirmed. |
| `1.1.10` | `c4e26b2431e1682ddb1ef113a8850723ebf0650d` | Corrects `1.1.9`'s "pending" status: **ADR-0006 Decision 6's independent review (AC04 step 6) is confirmed by god.** god read the full Decision 6 diff directly against the reviewed BIO-MOB-004 ruling - the authenticated-vs-asserted distinction holds and the residual (device role is not per-officer accountability) is named honestly, not overclaimed - and independently re-ran verification (`check-contract-freeze.py`, this test suite, `node --test contracts/js/`). Decision 6's content is otherwise unchanged from `1.1.9`; ADR-0001–0005 remain byte-identical. |
| `1.1.11` | `7435abf1f3540661adbe99ea695747c9979239ac` | **Superseded by 1.1.12 — see below.** Adds **ADR-0003 Decision 8** (amends an existing ADR, same shape as ADR-0002 Decision 7 at `1.1.7` and ADR-0006 Decision 6 at `1.1.9`): Seyi's BIO-OPS-002 round-3 review (`SY-OPS002-05`) found BIO-TEN-002's demo tenant/role context-change audit reopens Decision 7's residual-acceptance condition - the audited session cookie, independently HMAC-verified and held by the Next.js process plus the browser, outlives the API's own in-memory audit queue across a restart, so the pair is not same-lifetime. Names a narrow, explicit exception scoped to this one mechanism (not a general relaxation of Decision 7), reasoned on three bounded facts: no authorization decision depends on this record's presence, the audited token carries its own bounded expiry, and the behavior audited is itself the already-approved demo self-selection (TD-WEB-PROVENANCE-BRIDGE). Names the real remedy (shared transaction/outbox, or an explicit attempt-then-finalize protocol) as what any future non-synthetic case would need instead of reusing this exception. ADR-0001, ADR-0002, ADR-0004–0006 unchanged throughout. Kemi's approval recorded in the ADR header; independent review (AC04 step 6) was pending as of this snapshot, not yet confirmed. |
| `1.1.12` | `2f6826a40db537753f3d91fae867a2b26a518308` | Corrects `1.1.11`'s "pending" status: **ADR-0003 Decision 8's independent review (AC04 step 6) is confirmed by Zainab** (`evidence/BIO-ARC-006/codex/ADR-0003-DECISION8-CONFIRMATION-ZAINAB.md`, reviewer commit `0ad4339`). Zainab compared the Decision text directly against Seyi's `SY-OPS002-05` finding: it accurately records the reopened boundary without claiming technical atomicity, the three bounding constraints and the non-precedent language both hold, and the later shared `InMemoryAuditSink` replacement for the historical `InMemoryDemoSessionContextAuditSink` the review names carries the identical process-local volatility, so the reasoning stays current. Decision 8's content is otherwise unchanged from `1.1.11`; ADR-0001, ADR-0002, ADR-0004–0006 remain byte-identical. |

`v1.0.0/manifest.json` through `v1.1.11/manifest.json` are all unmodified and remain the
historical record of what each covered at the time, including three now-corrected drafts
of ADR-0003 Decision 7, and `1.1.5`'s, `1.1.7`'s, `1.1.9`'s and `1.1.11`'s own "pending"
confirmation statuses — immutability means a superseded version is superseded by a new
one, never edited to look like it always said the corrected thing.
`EXPECTED_BASELINE_COMMIT` and the default `--snapshot` path in
`scripts/check-contract-freeze.py` moved forward to `1.1.12`. `FROZEN_GLOBS` was widened
from `ADR-000[1-5]-*.md` to `ADR-000[1-6]-*.md` at `1.1.5` and needs no further change
here (`1.1.12` amends an existing frozen ADR file, same as `1.1.7`, `1.1.9`, `1.1.10` and
`1.1.11`).

## BIO-ARC-006-AC01 — versioned snapshots

`1.0.0` is the first baseline. Snapshot versions use semantic versioning:

- patch: clarification with no consumer-visible behavior change;
- minor: backward-compatible addition;
- major: breaking contract change.

A Git tag alone is not the snapshot. The versioned manifest is the durable file inventory,
size and SHA-256 binding; a tag may point at an accepted snapshot later.

## BIO-ARC-006-AC02 — what “zero open architecture conflicts” means

An architecture conflict is open when two authoritative rules contradict, two active or
future stories can write the same path, a required decision has no owner, a Critical/High
finding is unresolved, or a documented limitation could reasonably be consumed as a
stronger guarantee without an explicit consumer obligation.

The W01 conflict ledger is zero-open:

| Boundary examined | Resolution at freeze | Open conflict |
|---|---|---|
| Tenant routing/audit | ADR-0002: resolved context provenance, mandatory settled audit, fail closed | No |
| Audit/event delivery | ADR-0003: event dedupe is `(tenant_id,event_id)`; `received_at` excluded | No |
| API write vs event idempotency | ADR-0004 Decision 4.6 distinguishes the mechanisms and the emit-then-fail seam | No |
| Migration sequencing | ADR-0005: one no-argument production registry authority plus the process lock | No |
| Migration proof runner fidelity | ADR-0005 Decision 4.6 is conditional, not an unconditional proof | No — disclosed boundary with the obligation below |
| Audit ordering for audited mutations | ADR-0003 Decision 7 (snapshot 1.1.3): per-aggregate lock, audit-before-commit, `ExistsAsync` as the ambiguous-throw pattern for this tier, `CancellationToken.None` past confirmed-audit; crash/durability residual bounded to a consequence — a surviving phantom mismatch — that cannot occur while both stores are same-process volatile singletons | No — lock/audit-before-commit/ExistsAsync-pattern/crash-boundary confirmed by BIO-TEN-001 round 5 (`REREVIEW-ROUND5.md`, `03086c1`); `CancellationToken.None` confirmed separately by BIO-DEV-001 round 3 (`REREVIEW-ROUND3.md`, `1e8eccb`); reopens per the triggers the Decision names |
| Tenant-resolution review evidence before a channel ships | ADR-0002 Decision 6 (snapshot 1.1.2): test-double logic proof plus real shipped-host fail-closed evidence, bounded to a named upgrade obligation per channel | No — confirmed by Seyi and god, bounded and not a general relaxation of Decision 2 |
| Caller-asserted actor identity before real authentication ships | ADR-0006 (snapshot 1.1.6): the asserted-actor accommodation is safe only as audit attribution, never as the sole input to an authorization decision; BIO-IDN-006's interim closure is verified trusted-edge provenance, not a stronger identity-check shape; separation-of-duties stays an open, disclosed residual until BIO-INT-001 | No — confirmed by god and Zainab (AC04 step 6), bounded to BIO-INT-001 as the closing story (Decision 5); ADR confirmation is not evidence BIO-IDN-006 is remediated - round 4's own provenance-verification evidence is a separate, still-open acceptance criterion |
| Tenant resolution for a caller with no host/interactive-principal/service channel available | ADR-0002 Decision 7 (snapshot 1.1.8): device-secret authentication realizes the existing `principal` channel; tenant resolved from the authenticated device's own registered record via a narrow, internal `ICrossTenantDeviceLookup`, never from the request; explicitly not PKI/mTLS, and every future device-authenticated story must reuse this same shape with Kemi's approval for any new lookup | No — confirmed by god (AC04 step 6), bounded to the exact mechanism specified (one narrow internal lookup, gated behind device-secret authentication already having succeeded) |
| Role-gated authorization for a device-authenticated actor with no human identity of any shape | ADR-0006 Decision 6 (snapshot 1.1.10): a project-local, tenant-scoped device-role mechanism, safe outside Decisions 1-5's asserted-actor accommodation because it rests on an authenticated device principal, not a bare claim; role-assignment stays gated by real human RBAC (Decision 3); every future device-authenticated story must reuse this same shape rather than inventing its own | No — confirmed by god (AC04 step 6), bounded to the exact mechanism specified (device-role check only, never a substitute for per-officer identity) |
| Audit/effect same-lifetime condition (Decision 7) reopened by a demo-session mechanism whose effect outlives the API process | ADR-0003 Decision 8 (snapshot 1.1.12): a narrow, named exception scoped to BIO-TEN-002's demo tenant/role context-change audit only, bounded by three facts (no authorization decision depends on the record, the audited token has a bounded expiry, the audited behavior is itself already-approved self-selection) — not a general relaxation; any future non-synthetic case must implement Decision 7's named remedy or bring its own separately justified exception | No — confirmed by Zainab (AC04 step 6), bounded to the exact mechanism named, not a precedent for any audit that merely lags its effect across a restart |

The migration boundary is deliberately not flattened into “the migration ran.”
`assignSequence` proves fresh-create and upgrade-from-previous converge **if and only if**
the supplied `applyUp`/`hashState` faithfully interpret the opaque migration. It cannot
prove that fidelity. Every feature that proposes a migration must supply a trustworthy
runner; Kemi owns its selection at assignment. No feature may cite sequence assignment as
evidence that its `up` body was executed. A no-op runner remains a reproducible accepted
boundary, not a closed implementation finding.

## BIO-ARC-006-AC03 — future feature ownership

`governance/feature-path-ownership.json` declares every W02–W15 story. Each story owns six
story-keyed subtrees (`api`, `web`, `mobile`, `tests`, `docs`, `evidence`) and has read-only
access to `contracts/**`, `docs/adr/**`, and `governance/contract-freeze/**`. Story-keyed
paths cannot overlap. The ownership registry, freeze validator and this policy document are
also read-only to every feature story, so a consumer cannot grant itself access or weaken
enforcement. A story may leave an owned surface empty; ownership is permission, not a
requirement to create code on every surface.

Shared code needed by two stories is not placed in either story’s subtree by convenience.
It requires Kemi to allocate a new shared owner or approve a contract change through the
post-freeze process below.

## BIO-ARC-006-AC04 — post-freeze change control

Any change to a frozen file requires all of the following before merge:

1. A superseding or amending ADR naming the old and proposed behavior.
2. Kemi’s recorded approval.
3. An impact review listing every affected contract, generated binding, feature owner,
   stored-data/migration consequence, security/tenant consequence, compatibility class,
   rollout and rollback/repair plan.
4. Targeted contract tests plus the full frozen-contract regression suite.
5. A new snapshot version and regenerated hashes. Existing snapshot directories are
   immutable; they are never edited in place.
6. Independent review and normal story acceptance.

Emergency work does not bypass these steps. It may shorten scheduling, not evidence.
