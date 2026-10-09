# BIO-INT-003 — Builder evidence

**Story:** Implement API service accounts, OAuth2 and rate limits
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-INT-003`, branch `feature/BIO-INT-003` off `develop`
**Dependencies:** BIO-ARC-004 (ADR-0004, documentation-only, no code to depend on), BIO-IDN-006 (RBAC role model, accepted)
**Commit:** see `metadata.json` (original builder `6903622`/`6b44153`, round 1 `46fa855`/`bd66442`, round 2 `c5e2f9d`/`e193f44`, round 3 `93a0241`/`1ae52d6`, round 4 below)

## Remediation (round 4): ZB-INT003-07 (Zainab, High - SuperAdmin as a service-account scope)

Round 4 confirmed round 3's `IRolePermissionGateway` fix correct for every ordinary role, then
FAILed on one new finding: round 3 mirrored `RoleType.SuperAdmin`'s own BIO-IDN-006 domain
invariant ("never needs or has an explicit grant record") into
`RolePermissionGatewayAdapter` - correct for a HUMAN role assignment under
`RoleAssignmentService`'s own tightly-gated endpoint, but unsafe for a service account: any actor
holding only the narrow `ManagePermission` could mint a `SuperAdmin`-scoped token that
auto-passed every current and future domain-permission check with no tenant grant ever
consulted - a blanket tenant-admin token for integrations, which the API architecture's own rule
explicitly forbids. The round-3 real-HTTP ALLOW proof relied on exactly this shortcut.

**Fixed at the root, not after the fact**: `KnownRoles` no longer includes `SuperAdmin` in its
closed scope vocabulary (ten roles remain), so creation-time validation (`IsKnown`) rejects it
before a token can ever be issued - not merely constrained once issued. The adapter's own
now-unreachable `SuperAdmin` short-circuit was removed too, rather than left as misleading dead
code. Four tests that relied on the SuperAdmin free pass were re-pointed to real, already-valid
scopes or new assertions; two new tests prove the root fix
(`Creating_an_account_rejects_SuperAdmin_as_a_scope`,
`A_service_account_manager_cannot_mint_a_token_for_a_permission_they_were_never_authorised_to_delegate`)
plus one new real-HTTP test
(`ZB07_SuperAdmin_is_rejected_as_a_service_account_scope_at_the_real_http_layer`, expects `400
unknown_scope`).

**Consequence, disclosed rather than worked around**: removing the shortcut also removes the
only production-real way this project had to prove a real-HTTP permission ALLOW without a
seeding fixture - see `docs/features/bio-int-003/README.md`'s own "Round 4" section. The ALLOW
path is now proven only at the unit level via `FakeRolePermissionGateway`; the real-HTTP ALLOW
path for a genuine tenant grant remains undemonstrated end-to-end pending a narrow,
production-real seeding fixture this project does not yet have.

## Remediation (round 3): ZB-INT003-03 (Zainab, High - round-2 fix found necessary but insufficient)

Round 3 confirmed ZB-01/02/04/05/06 all closed - and specifically validated the ZB-05
explicit-branch design (no dummy key, `MinimumSecretLength`) as BETTER than the floor-wide
sweep's random-key pattern (it also catches a weak/typo'd secret the sweep's version would
silently accept as a real key). Kemi is normalizing the other 13 swept files to this pattern -
no action needed here.

The one remaining finding: round 2's `AuthorizeAsync` fix (live account/revocation re-read) was
**necessary but not sufficient** - it checked a scope LABEL against itself
(`effectiveScopes.Contains("ReadOnly")`), never consulting whether that role actually CARRIES the
permission a protected resource requires via BIO-IDN-006's own tenant-scoped, independently-
configurable `RolePermissionGrant`. Fixed: `/whoami` now requires a real namespaced domain
permission (`WhoamiPermission`), resolved against every live scope via a new
`IRolePermissionGateway` seam - `BioInt003Module.cs` implements it by calling BIO-IDN-006's own
`IRolePermissionGrantRepository.GetAsync` DIRECTLY, bypassing `PermissionService`'s
ambient-tenant-bound convenience wrapper (which cannot serve a non-Host-channel OAuth2
bearer-token caller - see `docs/features/bio-int-003/README.md`'s own "Round 3" section for the
full architectural reasoning, including why `ITenantContextAccessor`'s read-only design from
ADR-0002 Decision 3 ruled out the alternative of swapping the ambient tenant mid-request).

**Disclosed test limitation, not a shortcut**: this project has no way to seed a real
`RolePermissionGrant` row from any test project (same disclosed limitation BIO-ACC-001's/
BIO-IDN-006's own host-channel tests already have - a tenant's first privileged grant is, by
BIO-IDN-006's own design, impossible to create through any public endpoint). The full
allow/deny/same-role-different-grant/cross-tenant proof matrix lives at the unit level via a
trusted test double; the real HTTP layer proves the honest default-deny path (meaningfully
different from round 2, since "ReadOnly" no longer auto-passes) and the allow path via
`SuperAdmin`'s own implicit-grant domain invariant - real production code, no fake, end to end.

## Remediation (round 2): ZB-INT003-03/05/06 (Zainab, High/Critical/Medium), fixed

Zainab's round-2 review confirmed round 1's ZB-01/02/04 closed, then FAILed on three findings:

- **ZB-05 (Critical)**: my own ZB-01 fix's copied `ActorProvenanceGate.cs` (byte-for-byte from
  BIO-ACC-001/BIO-IDN-006) carried THEIR identical dummy-key bug forward -
  `SHA256("no-provenance-secret-configured")` is a literal checked into source, computable by
  anyone to forge a provenance proof for ANY actor id, including a privileged one. God confirmed
  this exact bug is copy-pasted across ~12 other already-merged stories, escalated floor-wide
  separately. Fixed: same fail-closed discipline as ZB-01 - no fallback key, `Verify()` always
  `false` below 32 configured characters. **Lesson**: copying an "accepted" pattern verbatim does
  not mean the pattern itself was ever actually secure - it means nobody's review happened to
  stress it yet. Reusing code is not the same as reusing a verified property.
- **ZB-03 (High, reopened)**: round 1's "named follow-up per ADR-0006" framing was explicitly
  rejected - an approved acceptance criterion cannot be waived by an ADR citation. Actually fixed:
  `AuthorizeAsync` now re-reads live account state (revocation, current granted scopes) on every
  call instead of trusting a token's embedded claims; `/whoami` requires a real scope among the
  live effective set.
- **ZB-06 (Medium)**: rotate/revoke wrote a "completed" audit event before the repository's own
  existence/version check ran - a 404/409 left a false durable record. Fixed: both now check
  existence/version first, writing an audit event only once the mutation is actually going to
  happen.

Full detail, including the disclosed narrow residual race in ZB-06's fix and the precise
mechanism-vs-endpoint distinction in ZB-03's "removed grant" proof, is in
`docs/features/bio-int-003/README.md`'s own "Round 2" section.

## Remediation (round 1): ZB-INT003-01/02/04 (Zainab, Critical/Critical/High), fixed

Zainab's security review FAILed commit `6b44153`: full finding text in
`evidence/features/bio-int-003/codex/SECURITY-REVIEW-ZAINAB.md`. See
`docs/features/bio-int-003/README.md`'s own "Security-review fixes" section for the full
rationale; condensed here:

- **ZB-01 (Critical)**: the access-token codec and secret hasher fell back to a public,
  deterministic dummy key when unconfigured - her own test proved two unconfigured instances
  accepted each other's tokens (full token/secret-hash forgery). Fixed: both now fail closed
  (throw on issue/hash, always reject on validate/verify) below a 32-character configured
  secret, with no secret ever committed to source.
- **ZB-02 (Critical)**: create/list/rotate/revoke had no actor or authorization gate at all -
  any caller reaching a tenant plane could mint a SuperAdmin-scoped account. Fixed: every one
  now resolves a provenance-verified actor and checks a live BIO-IDN-006 permission grant,
  copying BIO-ACC-001's own `ActorResolver`/`ActorProvenanceGate`/`IPermissionGateway` seam
  byte-for-byte (this project can reference neither sibling feature project to share one copy).
- **ZB-04 (High)**: a failed audit write after a repository mutation could invalidate a secret
  the caller never received (true lockout). Fixed: audit now runs BEFORE the mutation, using
  BIO-INT-001's own write-or-throw-with-`ExistsAsync`-recheck discipline; a genuine audit
  failure now means nothing was mutated at all.
- **ZB-03 (High)**: scope unchanged, confirmed and re-documented as a named follow-up (not
  addressed this round, per ADR-0006 Decision 5's own precedent for BIO-INT-001).

`ServiceAccountService` is now `internal` (its constructor takes the internal
`IPermissionGateway`), matching `AccessPolicyService`'s own precedent. Integration tests were
redesigned to prove unauthenticated/forged-provenance/unprivileged-actor negatives against the
REAL `PermissionService` (same disclosed no-`InternalsVisibleTo`-access limitation BIO-ACC-001's
own host-channel tests have); the OAuth2/rate-limit/whoami tests now seed an account directly
into a substituted `IServiceAccountRepository`, bypassing only the gated CREATE endpoint.

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — service accounts are tenant-scoped | `IServiceAccountRepository`, every management method takes an explicit `tenantId`; not-found-not-forbidden across tenants | `ServiceAccountServiceTests.cs` (3 tests), `ApiSecurityEndpointsTests.cs` (3 tests) |
| AC02 — OAuth2/token scopes map to permissions | `POST /api/v1/oauth/token` (client_credentials grant, RFC 6749 §4.4.2 wire fields); scopes are BIOLOG's own closed 11-role vocabulary | `ApiAccessTokenCodecTests.cs` (9 tests), `ServiceAccountServiceTests.cs` (7 tests), `ApiSecurityEndpointsTests.cs` (2 tests) |
| AC03 — rate limits and API audit exist | `InMemoryApiRateLimiter` (tenant+client+route fixed-window, value-struct key); `InMemoryApiAuditSink` (every token issuance/rotation/revocation/rate-limit-rejection audited) | `RateLimitingTests.cs` (6 tests), `ApiAuditTests.cs` (3 tests), `ApiSecurityEndpointsTests.cs` (2 tests) |
| AC04 — API keys hashed/rotatable, never logged | `HmacApiSecretHasher` (pepper-HMAC, plaintext never stored); rotate-secret invalidates the old secret atomically under optimistic concurrency; plaintext returned exactly once | `ApiAccessTokenCodecTests.cs`, `ServiceAccountServiceTests.cs`, `ApiSecurityEndpointsTests.cs` (4 tests) |
| AC05 — `/administration/integrations` connected via `IntegrationService` | New, additive "API Credentials" panel (`ServiceAccountsPanel.tsx`) mounted alongside BIO-INT-002's own `ImportConsole`; four new independently mode-switchable port keys | See `docs/features/bio-int-003/README.md`'s own "AC05" section |

## Design decisions worth flagging

- **Scopes are BIOLOG role names, not a second taxonomy.** Rather than inventing a bespoke
  per-endpoint permission string catalogue with no existing fine-grained model to map onto,
  a service account's OAuth2 scopes are drawn directly from BIO-IDN-006's own closed 11-role
  vocabulary (duplicated by value, same boundary pattern BIO-INT-001's own `KnownRoles.cs`
  already established - this is a THIRD independent copy, since this project can reference
  neither bio-idn-006 nor bio-int-001). Real enforcement against BIO-IDN-006's live permission
  grants is a documented, named follow-up (ADR-0006 Decision 5's own precedent for BIO-INT-001),
  not performed by this story.
- **Rate limiting is genuinely new ground** - confirmed by searching the whole repository
  before designing (no `RateLimiter`/`RateLimit*` precedent anywhere). The bucket key is a
  value-equality `record struct`, not a joined string, applying BIO-INT-002's own remediation
  lesson (a naive joined-string key can let two different logical buckets collide) before any
  reviewer had to find it.
- **Three independent HMAC keys** (access-token signing, secret-hash pepper, and BIO-INT-001's
  own separate session-token secret) - a leaked key in one must not compromise the others.
- **`/settings/authentication` deliberately left untouched** - its own primary actions are
  BIO-INT-001's own sign-in/session-policy domain (already disclosed by BIO-INT-002-AC06's own
  HYBRID note), not this story's API service-account concern.
- **A model-binding bug was caught and fixed before considering this done**: the OAuth2 token
  endpoint initially bound its request body using this host's default camelCase JSON
  convention, which silently rejected a real OAuth2 client's actual RFC 6749 snake_case fields
  (`grant_type`/`client_id`/`client_secret`) with an opaque 400 - caught by the integration
  tests sending genuine spec-shaped requests, not by a shortcut that would have matched the
  bug. Fixed with explicit `[JsonPropertyName]` attributes on `OAuthTokenRequest` rather than
  changing the tests to send non-standard field names.

## Verification (round 4, post-remediation)

- `dotnet build api/Biolog.Api/Biolog.Api.csproj` - clean, 0 warnings, 0 errors, full feature-project graph.
- `dotnet test tests/features/bio-int-003/Biolog.Features.ApiSecurity.Tests.csproj` - 62/62 (was 61, +2/-1 net: removed the SuperAdmin-free-pass test, added the root-fix proof and the manager-cannot-delegate proof).
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj` - 207/207 (re-pointed four SuperAdmin-reliant tests to real, already-valid scopes or new ZB-07 assertions; one unrelated, pre-existing `ObservabilityTests` failure reproduced once in a 207-test run, passed cleanly both in isolation and on a full-suite re-run - a flake, not a regression from this fix).
- `dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj` - 200/200 (unchanged, no regression).
- `bash tests/features/bio-int-003/BIO-INT-003.test.sh` - 64/64 (was 59, +5 new ZB-07 checks, 1 repointed to the renamed AC03 test).
- `bash scripts/check-module-project-references.sh` - PASS.
- `python3 scripts/check-traceability.py --all` - PASS (35 implemented stories).
- `bash scripts/secret-scan.sh --all` - clean.
- `bash scripts/check-dotnet-vulnerabilities.sh` (both new projects) - PASS, no vulnerable packages.
- Web TypeScript: NOT run through `npm run typecheck` - `node_modules` is not installed in this
  worktree, and BIO-INT-002's own builder `metadata.json` shows no such command in its own
  `test_command` either (checked before relying on that precedent). The new files closely
  mirror the already-typechecked `ReportingService`/`BIO-RPT-003` multi-port pattern. Unaffected
  by this remediation round (no web/ files changed).
