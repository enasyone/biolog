# BIOLOG manual

One manual for three audiences — end users, operators and administrators. It documents
what exists **today** accurately and completely, and nothing that doesn't exist yet. See
[§4 What's not here yet](#4-whats-not-here-yet) for the honest map of what remains.

This is a living document. As each wave closes, the story that owns the new capability
should extend the relevant section below — the same pattern `docs/adr/` and
`docs/architecture/` already use. This edit is a catch-up rewrite (the manual had drifted
badly stale, documenting only through W02 while the product had reached W12); going
forward, extend it incrementally rather than letting it drift again. If a section here
looks stale against `governance/runtime-state.json`, that file is authoritative — open an
issue against this file rather than trusting it blindly.

**Current state, checked against `governance/runtime-state.json` at time of writing:**
Waves W00 through W10 are complete (84 stories), plus all of W01.5 (the UI-first
interactive-simulation wave that wires real screens to real backends). W11 is partially
done (5 of 9 stories) and W12 is early (4 of 18). W13 (cross-module acceptance), W14
(hardening) and W15 (first GitHub publication) haven't started. See §4 for the exact list
of what's still missing.

**The single most important caveat in this manual, stated once here so every later section
can assume it:** there is no real login or session authentication anywhere in the platform
yet. Every backend endpoint that needs to know "who is calling" reads it from `X-Actor-Id`,
a header the caller supplies — asserted, not authenticated. `docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md`
covers this in full; the short version is that this is safe for audit attribution (a forged
identity only changes who gets blamed) but not yet safe as the basis for an authorization
decision. Real authentication is `BIO-INT-001` (W12), still in review. Nothing in this
manual should be read as implying real login, sessions, or enforced RBAC exist — where a
capability looks like RBAC (BIO-IDN-006's maker-checker), that section says exactly what
mitigates the gap and what doesn't.

---

## 1. User section — what you can actually do today

### The shell, and previewing tenants/roles

Run the web app locally (see [§2](#2-operations-section)) and you get a real application
shell: light/dark theme, BIOLOG's brand palette, and primary navigation to every domain
below — every nav target listed in this manual resolves to a real, live-mounted route
(`web/lib/route-manifest.ts`), not a 404 placeholder.

A tenant/role selector in the shell (`BIO-UX-002`) lets you preview the app as different
synthetic tenants and roles — `System Administrator`, `Registration Staff`, `Report
Viewer` — with `?tenant=`/`?as=` URL overrides still available for deep links.
**This is still not authentication or real RBAC.** `web/context/session-context.tsx`'s own
header comment says so directly: it's a placeholder session pending a later story replacing
its body with a real resolved one. Every tenant/role/permission shown is synthetic and
labelled as such, held only in client state.

### Identity & people

PERSON records, profiles, lifecycle, and duplicate-review are real (`BIO-IDN-001`–`005`).
`/people`, `/students`, `/staff` and `/duplicate-review` are live routes backed by a real
`IdentityService`. `BIO-IDN-006` adds real RBAC/maker-checker enforcement — see the
authentication caveat above and its own paragraph in [§3](#3-administrator-section) for
exactly what it does and doesn't guarantee yet.

### Tenants

Real tenant control-plane administration exists (`BIO-TEN-001`, `BIO-TEN-002`) at
`/administration/tenants` and `/settings`: create, read, list, update, change status, and
soft-delete a tenant, each change audited. Tenant data is deliberately fixed-schema and
control-plane-only — no operational PII field can land there — and is in-memory only, since
no story has wired persistence yet. Full detail: `docs/features/bio-ten-001/README.md`.

### Photos & biometrics

Photo capture is real and durable (`BIO-PHO-001`): a captured `PhotoMaster` is structurally
immutable and versioned — nothing overwrites it, later derivatives (print, display) only
reference it. Photo-booth kiosk integration (`BIO-PHO-002`) is a real device-authenticated
contract: a booth proves itself with a device id/secret header pair against the device
registry, and a capture only promotes to a real `PhotoMaster` after a quality gate and
explicit approval. `/photographs` is live.

Fingerprint and face enrollment, 1:1 verification, and 1:N identification are real
(`BIO-BIO-001`–`007`) at `/fingerprints` and `/facial-recognition`. Identification only
searches a gallery of previously-approved references (a Chioma-found gap where raw
unapproved samples leaked in has since been fixed) and returns ranked candidates only —
never an authorization decision by itself. Liveness/anti-spoofing (`BIO-BIO-008`) is now
accepted and real: a provider-neutral liveness contract and policy hooks, connected through
`BiometricService`'s own independently-resolved `checkLiveness` port so the other three
still-unbacked biometric actions aren't falsely claimed LIVE alongside it.

### Documents

MRZ, barcode, OCR, document-type identification, and NFC read are real individual capture
steps (`BIO-DOC-001`–`008`) at `/document-verify`. `BIO-DOC-009` adds a real authenticity
orchestrator combining six checks (structural, cross-source, cryptographic) into one
explainable disposition — deliberately engineered so a barcode decode alone can never
produce "document authentic" on its own, only cross-source comparison and passive
authentication can. As a whole module this reaches HYBRID, not LIVE: the `authenticity`
step has a real connectable backend, the other seven steps don't yet.

### Cards & credentials

Card design, the card-designer canvas, encode/verify-read, issue, replace, mark-lost,
recover, revoke, and mobile-credential revoke are all real (`BIO-CRD-001`–`007`) at
`/cards`, `/credentials`, `/cards/issue`, `/cards/replace`, `/card-designer`,
`/nfc-uhf`, and `/mobile-credentials`. `BIO-CRD-008` adds a real card-template lifecycle
(Draft → Validation → Approval → Published → Retired, immutable published versions) — the
most rigorously proven LIVE capability in the platform, with an executed dual-server
Playwright proof (real API + a LIVE-mode Next.js build against a real tenant). That LIVE
proof requires an explicit build-time env var; it is not what ships by default — "provably
capable of LIVE," not "runs LIVE out of the box."

### Devices

Device registration, health/status tracking, and a certification matrix
(supported/experimental/deprecated combinations) are real (`BIO-DEV-001`–`003`), including
a vendor-adapter framework isolating SDK code behind typed interfaces. The `/devices` UI
register-device form was SIMULATION-only as of `BIO-DEV-003`'s own acceptance evidence (it
only collected a device name against a real endpoint needing vendor/model/firmware/SDK/
site/network) — a follow-up story (`BIO-UX-010-DEVICE-FORM-FOLLOWUP`, DONE) has since closed
that field-parity gap.

### Access control

A tenant security administrator can author real WHO+CREDENTIAL+WHERE+WHEN+STATUS access
policies with draft/publish versioning and maker-checker on publish (`BIO-ACC-001`).
Evaluation is fixed and explicit: deny always wins, then a matching allow only if presented
assurance is sufficient, otherwise fail-closed default deny. The online decision service
(`BIO-ACC-002`) enforces a genuine separation — a face/fingerprint match is evidence only,
never itself the decision; the policy evaluator is the sole source of ALLOW/DENY and its
reason code. Offline, edge devices cache a revocation delta feed and make the allow/deny
call locally against it before the server ever sees the event (`BIO-ACC-003`), using the
same device-secret pattern as photo booths and mobile sync. `/access-control` is live.

### Attendance

One attendance event model covers education/workforce/presence interpretations
(`BIO-ATT-001`) at `/attendance`, with a hard separation between the immutable raw event and
a derived status computed at read time, never persisted stale. Exception flagging and
further attendance policy (`BIO-ATT-002`/`003`) are **not built yet**.

### Visitor management

Preregistration (optionally linked to a PERSON, never forcing one), auditable host approval,
and per-tenant configurable check-in fields (vehicle, NDA, safety briefing, watchlist,
temporary-credential options) are real (`BIO-VIS-001`) at `/visitors`. **Actual temporary
credential issuance and check-out/revocation (`BIO-VIS-002`) is not built yet** — despite the
config fields existing today, there is no live badge issuance flow.

### Transport

Versioned route/vehicle/stop lifecycle, person assignments with validity windows, and
vehicle-device linkage are real (`BIO-TRN-001`) at `/transport`. `BIO-TRN-002` adds real
boarding/alighting events with configurable evidence (UHF/NFC/face), idempotent debounced
capture, and read-time-only presence/exception evaluation with feature-local notifications.

### Mobile & offline

The Flutter mobile app has real feature areas for tenant selection, enrollment, scan,
verify, bus, and offline sync. A durable local offline event queue with idempotency
(`BIO-MOB-002`), real central sync with conflict handling (`BIO-MOB-003` — identity,
credential and biometric conflicts always route to a human Sync Resolution Queue, never
silent last-write-wins), and a real, tenant-policy-gated limited offline enrollment package
with encrypted local storage (`BIO-MOB-005`) are all live. **Known, disclosed limitation:**
there is no self-service device enrollment/pairing flow yet — a device secret must be
provisioned out of band before mobile sync can authenticate at all
(`mobile/lib/core/sync/device_credential_store.dart`'s own header comment; a dedicated
enrollment/pairing story is recommended but not yet scheduled). Offline *verification*
(`BIO-MOB-004`) is not built yet.

### Reporting

Configurable notification rules across email/SMS/push/Teams/webhook with allowlist-based
payload minimization and delivery audit (`BIO-RPT-001`), a governed report builder with a
static, code-defined whitelist of dimensions/measures — no arbitrary tenant SQL reachable
anywhere — and real CSV export (`BIO-RPT-002`), and immutable, searchable, redaction-aware
audit investigation views (`BIO-RPT-003`) are all real, at `/reports` and `/audit`.

### How to read "real" precisely — LIVE, HYBRID and SIMULATION

Every module above resolves through one consistent mechanism
(`web/lib/feature-mode.ts`): a module with one simple action shares one
`NEXT_PUBLIC_FEATURE_MODE_<MODULE>` environment variable; a module where only *some* of its
actions have a real backend gets that action its own, independently-resolved port key (e.g.
`DocumentVerifyService:authenticity`, `BiometricService:checkLiveness`), so switching one
real action to LIVE never falsely claims LIVE for a sibling action that still has no
backend. `getModuleStatus()` reports `SIMULATION`, `HYBRID` or `LIVE` per module
accordingly — check the dev-only feature-mode panel (`web/dev-tools/feature-mode-panel.tsx`)
against a running app rather than trusting this manual's prose alone if you need the exact
current default for a specific environment.

---

## 2. Operations section

### Running it locally

Web app:

```sh
corepack pnpm --dir web install
corepack pnpm --dir web dev      # http://127.0.0.1:3000
```

API:

```sh
dotnet run --project api/Biolog.Api
```

Boots against `ASPNETCORE_ENVIRONMENT=Development` by default. Full detail —
`typecheck`/`build` commands, project layout, module registration pattern — is in
`docs/API-SHELL.md` and `web/README.md`; this manual doesn't duplicate them.

Local infrastructure (MySQL x2, Redis, RabbitMQ, Azurite, Mailpit — all Docker, all
synthetic credentials, zero required configuration) is documented in full in
`docs/LOCAL-INFRASTRUCTURE.md`:

```sh
scripts/infra-up.sh --wait     # bring the stack up, block until healthy
scripts/infra-down.sh          # stop, keep data
scripts/infra-reset.sh         # stop, wipe named volumes
```

### Health endpoints

Still exactly two, deliberately outside the versioned `/api/v1` surface (ADR-0004 governs
the business API, not infrastructure probes) — unchanged since W02, reverified directly
against `api/Biolog.Api/Modules/HealthModule.cs`:

| Endpoint | Meaning |
|---|---|
| `GET /health/live` | Liveness only — the process responded. Never evaluates a dependency, so a slow dependency can't get a healthy process restarted. |
| `GET /health/ready` | Readiness — evaluates every registered health check. Still none registered as of this writing (`services.AddHealthChecks()` with no `.AddCheck` calls), so it reports `Healthy` by construction — this shell still has no dependency-level check wired in, even though real dependencies (tenant registry, identity store, etc.) now exist in-memory. |

Full detail in `docs/API-SHELL.md`.

### Observability

Real now (`BIO-FND-009`, done — this manual previously documented it as unmerged, that's no
longer true). Structured JSON logs with correlation/tenant scoping, OpenTelemetry hooks, and
secret/biometric exclusion from logs are live. Full detail, deliberately not duplicated
here: `docs/OBSERVABILITY.md`.

### Quality gate

```sh
scripts/quality.sh          # offline-safe: foundation/contract test harnesses,
                             # traceability inventory, tracked-secret scan
scripts/quality.sh --full   # + .NET unit/integration tests, Vitest, Playwright,
                             # Flutter, .NET vulnerability audit
```

`--full` fails rather than silently skipping when a required toolchain isn't installed.
Full detail, including the security/dependency-scan commands run separately, is in
`docs/TESTING.md`.

Every implemented story's requirement→AC→test→evidence graph is machine-checked:

```sh
python3 scripts/check-traceability.py --all       # every implemented story
python3 scripts/check-evidence.py <STORY-KEY>      # one story's evidence
```

A gap (missing test, missing evidence, uncovered acceptance criterion) prints
`ACCEPTANCE: BLOCKED` and returns non-zero. Full mechanism in `docs/TRACEABILITY.md`.

### The contract-freeze mechanism

`scripts/check-contract-freeze.py` refuses any changed, missing or unlisted file under
`contracts/` or `docs/adr/ADR-0001` through `ADR-0006` against the accepted baseline. It
protects the shared architecture contracts — canonical identifiers, tenant routing,
audit/event envelopes, API conventions, migration sequencing, actor-identity boundary —
that every later feature story depends on without being allowed to change. Changing a
frozen file requires a superseding or amending ADR, Kemi's recorded approval, an impact
review, targeted tests plus the full frozen-contract regression suite, and a new versioned
snapshot (current: `v1.1.8`). Full policy, including exactly what "zero open architecture
conflicts" means and the current conflict ledger, is in
`docs/architecture/SHARED-CONTRACT-FREEZE.md` — this manual references it rather than
duplicating it, since it's the authoritative record and changes under its own
change-control process.

---

## 3. Administrator section

Real tenant control-plane administration exists today: create/read/list/update a tenant,
change its status, soft-delete it, at `/administration/tenants` and `/settings`
(`BIO-TEN-001`/`002`). Every change is audited.

**Read the caveat at the top of this manual before relying on anything below as access
control.** In short: there is no real login anywhere yet (`BIO-INT-001`, W12, not done), and
every "who did this" in the system today comes from a caller-supplied `X-Actor-Id` header —
asserted, not authenticated.

`BIO-IDN-006` adds real RBAC and maker-checker logic — role assignment, permission checks,
a second-approver requirement on sensitive actions. Its round-4 remediation added
`ActorProvenanceGate`: a cryptographic (HMAC-SHA256) proof header
(`X-Actor-Provenance`), bound to the specific asserted actor id, required on the four
highest-risk RBAC/maker-checker endpoints. **This is explicitly an interim mitigation, not
authentication and not real separation-of-duties** — anyone holding the shared provenance
secret can still compute a valid proof for *any* actor id, including self-approving a
maker-checker request under two different asserted identities. `ADR-0006` (docs/adr/) covers
the full reasoning, including why this is judged safe enough for the audit-attribution uses
elsewhere in the platform but not a substitute for real authentication. The real fix —
binding `X-Actor-Id` to a genuinely authenticated principal — is `BIO-INT-001`'s job, not
yet done.

Beyond tenant administration and the RBAC boundary above, there's no further admin surface
yet: no SIS/HR/ERP import configuration, no authentication-provider setup (SAML/OIDC/Entra),
no webhook/API-service-account management, no tenant-level feature configuration UI. All of
that is W12's remaining scope (`BIO-INT-001`–`005`, `BIO-TEN-003`) — see §4.

---

## 4. What's not here yet

Sourced from `governance/runtime-state.json` at time of writing. Nothing below is built;
don't assume a capability exists because it sounds plausible — check here or the governance
file first.

| Wave | Status | Remaining |
|---|---|---|
| W11 | 5 of 9 done | `BIO-MOB-004` (offline verification), `BIO-ATT-002` (attendance exceptions), `BIO-VIS-002` (temporary credential issuance/check-out), `BIO-CRD-009` (photo element on card-designer canvas) |
| W12 | 4 of 18 done | `BIO-INT-001` (authentication providers — SAML/OIDC/Entra/password, in review), `BIO-INT-002`–`005` (SIS/HR/ERP sync, service auth/API keys, webhooks, conflict resolution UI), `BIO-TEN-003` (further tenant configuration), `BIO-DOC-010`/`011`, `BIO-ATT-003`, `BIO-TRN-003`, `BIO-RPT-004`, `BIO-DEV-004` (remote device jobs), `BIO-BIO-009`, `BIO-PHO-003` |
| W13 | 0 of 5 | Cross-module acceptance: end-to-end enrolment, card issuance, document verify, biometrics, access/attendance, offline recovery, all exercised locally — no new features |
| W14 | 0 of 4 | Security/performance hardening, accessibility, backup/restore, device-lab readiness — fixes only |
| W15 | 0 of 1 | First GitHub publication gate — remote created only after local MVP acceptance |

The practical consequence for administrators: real login/authentication, SIS/HR/ERP
integration, and full tenant self-service configuration are all still ahead (W12). The
practical consequence for end users: offline verification, visitor badge issuance, and
attendance exception handling are still ahead (W11-W12) — everything else described in §1,
including liveness-checked biometrics, is real today.

---

*Owner: Kemi — Chief Architect & Integrator. Extend the relevant section when your story
lands; don't create a second manual. If a claim here is wrong or stale, fix it in the same
commit as the story that makes it wrong, the same discipline `docs/adr/` uses.*
