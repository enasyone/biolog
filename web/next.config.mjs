/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  // Next.js 16 otherwise (re)writes AGENTS.md/CLAUDE.md into web/ on every `next dev`.
  // This repo already has its own agent-instruction files; don't let the framework
  // add a second, differently-scoped one.
  agentRules: false,
  // Next.js 16's dev-mode overlay renders a real, focusable button with
  // aria-label "Open Next.js Dev Tools" over every page. Playwright's
  // getByRole('button', { name }) substring-matches by default, so any spec
  // targeting a short button name that happens to contain "Next" (a wizard's
  // own "Next" step button, for example) hits a strict-mode violation against
  // this injected element instead of - or as well as - the intended one.
  // Dev-only chrome, zero effect on production builds or the approved design;
  // disabling it removes the interference at its source rather than needing
  // every current and future spec to defensively scope its selectors.
  devIndicators: false,
  // BIO-FND-007: shell foundation only. Feature routes/rewrites are added by the
  // stories that own them, not here.
  //
  // BIO-DOC-009-AC06: `web/features/documents/live.ts`'s live adapters `fetch()` a bare relative
  // path (e.g. `/api/v1/documents/authenticity`), which — with no rewrite — would resolve against
  // this Next.js server's OWN origin, not the separate `Biolog.Api` (.NET) host, and 404 even once
  // a real backend exists. This rewrite forwards every `/api/v1/documents/*` call server-side to
  // the API host, so the browser sees one origin and no CORS configuration is needed on either
  // side. Scoped to this story's own owned route family (`DocumentVerifyService`'s
  // `LIVE_ENDPOINTS`) only — no other module's `/api/v1/*` path is touched. `BIOLOG_API_BASE_URL`
  // defaults to `Biolog.Api`'s own documented dev port (`api/Biolog.Api/Properties/launchSettings.json`).
  //
  // BIO-ATT-001-AC05: identical reasoning, for `web/features/attendance/live.ts`'s own two
  // adapters (`attendanceLivePort`/`attendanceFlagExceptionLivePort`, already BIO-UX-010's own
  // scaffolding — unchanged by this story) — they already POST to `/api/v1/attendance/primary-action`
  // and `/api/v1/attendance/flag-exception`; only this rewrite entry and the real `Biolog.Api`
  // endpoints (`api/features/bio-att-001/Api/AttendanceEndpoints.cs`) were needed to connect them.
  //
  // BIO-VIS-001-AC05: identical reasoning, for `web/features/visitors/live.ts`'s own three
  // adapters (`visitorsLivePort`/`visitorsCheckOutLivePort`/`visitorsViewHistoryLivePort`,
  // already BIO-UX-005/010's own scaffolding — unchanged by this story) — they already POST to
  // `/api/v1/visitors/primary-action`, `/api/v1/visitors/check-out-visitor` and
  // `/api/v1/visitors/view-visit-history`; only this rewrite entry and the real `Biolog.Api`
  // endpoints (`api/features/bio-vis-001/Api/VisitorEndpoints.cs`) were needed to connect them.
  //
  // BIO-ACC-003-AC06: identical reasoning, for `web/features/access/live.ts`'s own
  // `accessViewAccessEventsLivePort` (already BIO-UX-010's own scaffolding — unchanged by this
  // story) — it already POSTs to `/api/v1/access/view-access-events`; only this rewrite entry
  // and the real `Biolog.Api` endpoint (`api/features/bio-acc-003/Api/AccessCacheEndpoints.cs`,
  // `ViewAccessEventsAsync`) were needed to connect it.
  // BIO-ACC-002-AC05: `accessLivePort` ("create policy") already POSTs to
  // `/api/v1/access/primary-action`; only this rewrite entry and the real `Biolog.Api` endpoint
  // (`api/features/bio-acc-002/Api/AccessDecisionEndpoints.cs`, `PrimaryActionAsync`) were needed
  // to connect it — this story is the one BIO-ACC-003-AC06's own comment above named as not yet
  // built. Both `AccessService` ports are now LIVE, reaching a genuine `LIVE` status.
  // BIO-RPT-002-AC06: the reporting prefix also forwards this story's dedicated governed-report
  // create/status/cancel/export routes while preserving BIO-RPT-001's existing primary-action and
  // view-audit-trail endpoints.
  // BIO-TRN-001-AC05: `web/features/transport/live.ts`'s own two
  // adapters (`transportLivePort`/`transportAssignRouteLivePort`, already BIO-UX-005/010's own
  // scaffolding — unchanged by this story) — they already POST to `/api/v1/transport/primary-action`
  // and `/api/v1/transport/assign-route`; only this rewrite entry and the real `Biolog.Api`
  // endpoints (`api/features/bio-trn-001/Api/TransportEndpoints.cs`) were needed to connect them.
  //
  // BIO-BIO-008-AC05: `web/features/biometrics/live.ts`'s own `checkLiveness` adapter (already
  // BIO-UX-008's own scaffolding — unchanged by this story) already POSTs to
  // `/api/v1/biometrics/face/liveness`; only this rewrite entry and the real `Biolog.Api` endpoint
  // (`api/Biolog.Api/Modules/BioBio008Module.cs`) were needed to connect it. Scoped to
  // `/api/v1/biometrics/face/liveness` only, not a `:path*` wildcard — `BiometricService`'s other
  // four actions (fingerprint/face capture, 1:1 verify, 1:N identify) have no real backend yet
  // (`primaryAction` stays SIMULATION-only), and `checkLiveness`'s own independent port key
  // (`feature-mode.ts`) is the only one of the five with anywhere real to forward to today.
  async rewrites() {
    const apiBaseUrl = process.env.BIOLOG_API_BASE_URL ?? 'http://127.0.0.1:5080';
    return [
      {
        source: '/api/v1/documents/:path*',
        destination: `${apiBaseUrl}/api/v1/documents/:path*`,
      },
      {
        source: '/api/v1/attendance/:path*',
        destination: `${apiBaseUrl}/api/v1/attendance/:path*`,
      },
      {
        // BIO-ATT-003-AC05: this story's own shift/overtime/exception routes
        // (`api/features/bio-att-003/Api/WorkforceAttendanceEndpoints.cs`) - a deliberately
        // separate prefix from BIO-ATT-001's own `/api/v1/attendance/*` above, never reused by
        // any other module's own routes.
        source: '/api/v1/workforce-attendance/:path*',
        destination: `${apiBaseUrl}/api/v1/workforce-attendance/:path*`,
      },
      {
        source: '/api/v1/visitors/:path*',
        destination: `${apiBaseUrl}/api/v1/visitors/:path*`,
      },
      {
        source: '/api/v1/access/view-access-events',
        destination: `${apiBaseUrl}/api/v1/access/view-access-events`,
      },
      {
        source: '/api/v1/access/primary-action',
        destination: `${apiBaseUrl}/api/v1/access/primary-action`,
      },
      {
        source: '/api/v1/reporting/:path*',
        destination: `${apiBaseUrl}/api/v1/reporting/:path*`,
      },
      {
        source: '/api/v1/transport/:path*',
        destination: `${apiBaseUrl}/api/v1/transport/:path*`,
      },
      {
        // BIO-CRD-008-AC05: the card-template lifecycle's own dedicated routes
        // (`api/features/bio-crd-008/Api/CardTemplateEndpoints.cs`) - never reused by any other
        // module's own routes, checked before mapping (see docs/features/bio-crd-008/README.md).
        source: '/api/v1/card-templates/:path*',
        destination: `${apiBaseUrl}/api/v1/card-templates/:path*`,
      },
      {
        // BIO-RPT-003-AC05: the audit investigation view's own dedicated routes
        // (`api/features/bio-rpt-003/Api/AuditInvestigationEndpoints.cs`) - never reused by any
        // other module's own routes, checked before mapping (see docs/features/bio-rpt-003/README.md).
        source: '/api/v1/audit-investigations/:path*',
        destination: `${apiBaseUrl}/api/v1/audit-investigations/:path*`,
      },
      {
        source: '/api/v1/biometrics/face/liveness',
        destination: `${apiBaseUrl}/api/v1/biometrics/face/liveness`,
      },
      {
        // BIO-RPT-004-AC05: the operational dashboard's own dedicated route
        // (`api/features/bio-rpt-004/Api/DashboardEndpoints.cs`) - never reused by any other
        // module's own routes, checked before mapping (see docs/features/bio-rpt-004/README.md).
        source: '/api/v1/dashboard/:path*',
        destination: `${apiBaseUrl}/api/v1/dashboard/:path*`,
      },
      {
        // BIO-PHO-003-AC05: the photo-quality capture review's own dedicated routes
        // (`api/features/bio-pho-003/Api/PhotoQualityEndpoints.cs`) - never reused by any other
        // module's own routes, checked before mapping (see docs/features/bio-pho-003/README.md).
        source: '/api/v1/photo-quality/:path*',
        destination: `${apiBaseUrl}/api/v1/photo-quality/:path*`,
      },
      {
        // BIO-BIO-009-AC01/AC05 (round 3): `web/features/biometrics/live.ts`'s own
        // `manageRetentionPolicy`/`requestBiometricDeletion` adapters already POST to these two
        // paths; only these rewrite entries and the real `Biolog.Api` endpoints
        // (`api/Biolog.Api/Modules/BioBio009Module.cs`) were needed to connect them. Scoped to
        // these two exact paths, not a `:path*` wildcard - the same deliberate narrowing
        // `checkLiveness`'s own entry above uses.
        source: '/api/v1/biometrics/retention/policy',
        destination: `${apiBaseUrl}/api/v1/biometrics/retention/policy`,
      },
      {
        source: '/api/v1/biometrics/retention/delete',
        destination: `${apiBaseUrl}/api/v1/biometrics/retention/delete`,
      },
      {
        // BIO-TEN-003-AC05: this story's own tenant-plane feature-configuration routes
        // (`api/features/bio-ten-003/Api/TenantFeatureConfigurationEndpoints.cs`) - never
        // reused by any other module's own routes, checked before mapping. Previously missing
        // entirely (round-2 remediation finding): `web/features/tenants/live.ts`'s fetch calls
        // were resolving against this Next.js server's own origin with no forwarding rule, so
        // they would 404 before ever reaching `Biolog.Api`, independent of and prior to this
        // round's provenance/permission fix. Scoped to `:path*` under `/api/v1/tenants/` the
        // same way every sibling story's own entry above is scoped to its own prefix; does not
        // touch `/api/v1/control-plane/tenants/*` (BIO-TEN-001's own, unrelated, route family).
        source: '/api/v1/tenants/:path*',
        destination: `${apiBaseUrl}/api/v1/tenants/:path*`,
      },
    ];
  },
};

export default nextConfig;
