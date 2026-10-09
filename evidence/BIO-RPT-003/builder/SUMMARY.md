# BIO-RPT-003 — Builder evidence

**Story:** Implement immutable audit investigation views
**Result:** PASS

## Restated scope (per the story-stage prompt)

**User story.** As auditors, I need searchable investigation views for sensitive actions without
allowing audit mutation.

**Acceptance criteria:** AC01 (filters: actor/person/action/time/device/reason), AC02 (audit
records read-only to tenant users), AC03 (export permissioned and audited), AC04 (sensitive
payload fields respect redaction policy), AC05 (the approved web surface `/audit` is connected
through `ReportingService` to this story's live implementation, mapped human workflow passes
without an unapproved UI redesign).

**Dependencies (build-wave gating).** BIO-ARC-003 (ADR-0003, Accepted), BIO-IDN-006 (RBAC —
the one genuine cross-story runtime dependency).

**Conflict group.** `audit-ui`.

**Owned write paths.** `api/features/bio-rpt-003/**` (new feature project), `api/Biolog.Api/
Modules/BioRpt003Module.cs`, `tests/features/bio-rpt-003/**`, `web/features/bio-rpt-003/**`
(new), `web/app/audit/page.tsx` (additive mount only), `tests/frontend/playwright/bio-rpt-003-*.
spec.ts`, `tests/frontend/unit/bio-rpt-003-*.test.ts`, `docs/features/bio-rpt-003/**`,
`evidence/BIO-RPT-003/**`.

**Shared, unlisted entry points touched:** `api/Biolog.Api/Program.cs`, `api/Biolog.Api/
Biolog.Api.csproj`, `web/lib/module-registry.ts`, `web/lib/feature-mode.ts`,
`web/next.config.mjs`, `tests/frontend/unit/bio-ux-005-feature-mode.test.ts` (its own multi-port
guard test updated), `scripts/quality.sh`.

**Zero additive changes needed to any other story's own owned files** (unlike BIO-CRD-008, which
needed two additive fields on BIO-CRD-007's `ProductionJob`) — every one of the four wired
sibling-feature audit sources is consumed through its own already-public, already-registered
in-memory sink, composed entirely inside `BioRpt003Module.cs`.

**Read-only shared contracts respected.** `docs/adr/ADR-0003-audit-evidence-and-event-envelopes.
md` (the audit envelope/classification contract this story's design is built against, never
amended), every other feature's own `Audit/*AuditEvent.cs`/`*AuditSink.cs` (read via their
already-public surface, never modified).

**Security/privacy risks identified and addressed.**
- Tenant isolation: every source is filtered by the ambient tenant; verified by test.
- Two independent redaction gates: the producing feature's own capture-time classification (what
  is safe to CAPTURE) plus this story's own view-time gate (what is safe to SHOW the specific
  viewer asking) — see `docs/features/bio-rpt-003/README.md` "Design decisions."
- Export is permissioned (`audit.export`) and itself audited BEFORE its bytes are returned
  (ADR-0003 Decision 7's ordering rule, generalised).
- AC02 is structural: no mutation method exists on the service, no `MapPut`/`MapPatch`/
  `MapDelete` anywhere under `/api/v1/audit-investigations` — verified by a real-HTTP integration
  test (a real 405, not merely absence) and the offline structural harness.

**Tests planned and written.** See "Test evidence" below.

## Design decisions

See `docs/features/bio-rpt-003/README.md` for the full write-up. Summary: the ADR-0003 recursive
redaction engine is NOT re-implemented (every producing feature already runs a smaller,
converged-upon "right-sized local port" — flat classified detail, fail-closed on unregistered
classification); this story instead adds a SECOND, independent view-time gate
(`AuditViewRedaction`) answering a different question (safe-to-show-this-viewer, not
safe-to-capture). Four real sibling-feature audit sources are wired via a genuinely open
`IAuditEventSource` extension point, composed only inside `Biolog.Api`, with zero edits to any of
those four features' own files; the story's approved `Dependencies` field (wave-gating) and the
module's own `DependsOn` (a real DI-graph edge) are disclosed as two different, non-conflicting
concepts. The AC01 device filter is implemented and unit-tested against a synthetic fixture even
though no real source currently populates a non-null device value anywhere in this codebase
(verified directly) — a disclosed, honest boundary, not a gap.

## Issues found and fixed during development (not assumed correct, verified)

1. **A missing `using` statement resolved the wrong extension method** — the third confirmed
   instance of this exact bug class in this codebase. Fixed by adding the missing `using`.
2. **The same class of bug across four sibling namespaces at once** — `BioRpt003Module.cs`'s four
   adapters needed fully-qualified static `ToWireString()` calls (not instance-call syntax) since
   each sibling's own `ActorType`/`AuditOutcome` type is a distinct, unqualified CLR type. Caught
   by the compiler across eight call sites.
3. **A test bug**: the CSV-export test wrongly asserted a person's canonical id (`per_alice`) was
   absent from a redacted export — ADR-0003 explicitly treats a canonical identifier as a safe,
   correlatable reference, distinct from the raw sensitive value it names. Fixed the test.
4. **A test bug**: the AC02 structural regression test expected 404 for PUT/PATCH/DELETE; ASP.NET
   Core correctly returns 405 (the stronger proof). Fixed the assertion.
5. **A harness-script bug**: an offline check searching for the literal absence of
   `MapPut`/`MapPatch`/`MapDelete` matched its own doc comment, which names those exact strings
   while explaining their absence. Fixed by requiring the opening parenthesis.
6. **A Playwright strict-mode locator collision, self-caught** — `{name:'Audit'}` (no `exact:
   true`) also matched this story's own new heading. Fixed with `exact: true`.
7. **A test-authoring bug in the LIVE spec, self-caught** — a response listener registered after
   the click that produced the response it waited for. Fixed by moving it into the same
   `Promise.all`.
8. **A stale `next-server` process during evidence capture, self-caught** — a second LIVE-mode
   evidence run reused a port from an earlier attempt; confirmed via `lsof`, killed, restarted
   cleanly. The same "stale reused dev server" lesson BIO-CRD-008 documented, recurring a second
   time in a different context (evidence capture, not first-draft test authoring).

None of these were review-round findings — all eight were caught and fixed during my own
implementation pass, before any independent review.

## Test evidence

- **Backend unit/integration tests** (`dotnet test`, xunit): 37/37 passing
  (`tests/features/bio-rpt-003/`) — see `backend-unit-test-output.txt`. Covers AC01 (every filter
  dimension, pagination, tenant isolation), AC02 (structural — no mutation method, real-HTTP 405
  for every mutating verb), AC03 (export forbidden without permission; audited before returning),
  AC04 (public/internal never hidden; personal/sensitive permission-gated; secret always hidden;
  unregistered classification treated as secret; producer `Withheld` preserved).
- **API host build**: clean, 0 errors/warnings, `BioRpt003Module` registered and DI-validated at
  startup (booted the real host immediately after wiring the module — no DI-lifetime bug found)
  — see `api-host-build-output.txt`.
- **`tsc --noEmit` + `next build`**: PASS, zero errors — see `web-typecheck-output.txt`,
  `web-build-output.txt`.
- **Frontend unit tests** (vitest): 426/426 passing across 56 files, including 13 new
  `bio-rpt-003-audit-investigation-mock.test.ts` tests and the updated multi-port guard test —
  see `frontend-unit-test-output.txt`.
- **Frontend e2e tests** (Playwright + axe-core), SIMULATION mode, single-worker: 419/419 passing
  (6 new `bio-rpt-003-audit-investigation.spec.ts` tests, 13 skipped-by-design LIVE-only tests
  across the repo) — no regression across the pre-existing suite (an earlier run's one unrelated
  Document-Verify timing flake did not recur on this clean run) — see
  `frontend-e2e-test-output.txt`.
- **AC05 LIVE-mode dual-server proof, actually run, not just asserted**: real `.NET` backend +
  LIVE-built Next.js server, a tenant domain registered and verified through BIO-TEN-002's own
  control plane, a real `card_templates` audit event produced via BIO-CRD-008's own already-
  accepted endpoint, then genuinely searched for and returned through this story's own real
  `/api/v1/audit-investigations` endpoint. 3/3 Playwright cases passed
  (`frontend-live-e2e-test-output.txt`), plus direct `curl` verification of the full
  register-tenant → create-template → search (unfiltered and action-filtered) →
  export-forbidden-403 cycle against the real backend (see `docs/features/bio-rpt-003/README.md`
  "Proving AC05" for the exact commands and this run's own confirmed responses).
