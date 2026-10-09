// Developer tests for BIO-UX-005-AC02, AC03, AC06.
// SYNTHETIC: no real tenant/user data.
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  auditFeatureModes,
  clearPortModeOverrides,
  getModuleStatus,
  getPortMode,
  MODULE_PORT_KEYS,
  resolvePort,
  setPortModeOverride,
} from '../../../web/lib/feature-mode';
import { createMockAdapter } from '../../../web/lib/module-port';

afterEach(() => {
  clearPortModeOverrides();
  delete process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE;
});

/**
 * Env-var defaults are captured once, at module import time (`ENV_DEFAULTS`
 * in `feature-mode.ts`) — deliberately, matching how Next.js actually
 * inlines `NEXT_PUBLIC_*` values into a browser bundle at build time, not a
 * dynamic runtime read. Testing that requires setting the env var *before*
 * a fresh import, via `vi.resetModules()`, not just setting
 * `process.env` mid-test against the already-imported module.
 */
async function importWithEnv(devicesMode: string): Promise<typeof import('../../../web/lib/feature-mode')> {
  vi.resetModules();
  process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE = devicesMode;
  const fresh = await import('../../../web/lib/feature-mode');
  delete process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE;
  return fresh;
}

/** Same pattern, for the one per-port env var (Reporting's viewAuditTrail — see feature-mode.ts). */
async function importWithReportingAuditTrailEnv(
  mode: string,
): Promise<typeof import('../../../web/lib/feature-mode')> {
  vi.resetModules();
  process.env.NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_VIEWAUDITTRAIL = mode;
  const fresh = await import('../../../web/lib/feature-mode');
  delete process.env.NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_VIEWAUDITTRAIL;
  return fresh;
}

describe('BIO-UX-005-AC06 getPortMode fails safe', () => {
  it('defaults to SIMULATION with nothing configured', () => {
    expect(getPortMode('DeviceService')).toBe('SIMULATION');
  });

  it('falls back to SIMULATION for an unknown port key on a known module', () => {
    expect(getPortMode('DeviceService', 'thisKeyDoesNotExist')).toBe('SIMULATION');
  });

  it('falls back to SIMULATION for a module with no registered ports', () => {
    expect(getPortMode('Unassigned')).toBe('SIMULATION');
  });

  it('an invalid override value falls back to SIMULATION rather than propagating garbage', () => {
    setPortModeOverride('DeviceService', 'primaryAction', 'LIVE');
    // Simulate a corrupted override bypassing the type system (BIO-UX-005-AC06 "fails safe when invalid").
    (setPortModeOverride as unknown as (m: string, k: string, mode: string) => void)(
      'DeviceService',
      'primaryAction',
      'not-a-real-mode',
    );
    expect(getPortMode('DeviceService')).toBe('SIMULATION');
  });

  it('an invalid NEXT_PUBLIC_FEATURE_MODE_* env value is ignored, not trusted', async () => {
    const fresh = await importWithEnv('not-a-real-mode');
    expect(fresh.getPortMode('DeviceService')).toBe('SIMULATION');
  });

  it('a valid env default is honoured when no override is set', async () => {
    const fresh = await importWithEnv('LIVE');
    expect(fresh.getPortMode('DeviceService')).toBe('LIVE');
  });

  it('an explicit override takes precedence over the env default', async () => {
    const fresh = await importWithEnv('LIVE');
    fresh.setPortModeOverride('DeviceService', 'primaryAction', 'SIMULATION');
    expect(fresh.getPortMode('DeviceService')).toBe('SIMULATION');
  });
});

describe('BIO-UX-005-AC02 getModuleStatus aggregates a module\'s ports', () => {
  it('reports SIMULATION when every port is in simulation (the default)', () => {
    expect(getModuleStatus('DeviceService')).toBe('SIMULATION');
  });

  it('reports LIVE when every known port on the module is live', () => {
    // IdentityService, not DeviceService: BIO-UX-010 gave DeviceService a second port
    // (viewConnectivityStatus), so "every known port" now needs both set live to reach LIVE —
    // exercised by this same describe block's HYBRID test below. IdentityService still has
    // exactly one port, so setting it live is still sufficient here on its own.
    setPortModeOverride('IdentityService', 'primaryAction', 'LIVE');
    expect(getModuleStatus('IdentityService')).toBe('LIVE');
  });

  it('reports SIMULATION for a module with no registered ports (nothing to be live about)', () => {
    expect(getModuleStatus('FrontendRuntime')).toBe('SIMULATION');
  });

  it('reports HYBRID when a module\'s two ports are in different modes (BIO-UX-005-AC02 remediation)', () => {
    // ReportingService is the one module with two real, independently-named actions
    // (route-manifest.ts: /reports "Run report", /audit "View audit trail") rather than one
    // generic action reused across routes - see MODULE_PORT_KEYS's own comment. Setting only
    // one of its two ports live is genuine mixed-mode evidence, not a synthetic module invented
    // solely to make this assertion pass.
    setPortModeOverride('ReportingService', 'viewAuditTrail', 'LIVE');
    expect(getPortMode('ReportingService', 'primaryAction')).toBe('SIMULATION');
    expect(getPortMode('ReportingService', 'viewAuditTrail')).toBe('LIVE');
    expect(getModuleStatus('ReportingService')).toBe('HYBRID');
  });

  it('the per-port env var alone (no override call) is enough to reach HYBRID', async () => {
    // A config-level proof, not just a JS-call proof (BIO-UX-005-AC06 "auditable in local
    // development"): setting NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_VIEWAUDITTRAIL in
    // .env.local alone flips ReportingService to HYBRID, with zero code/override calls.
    const fresh = await importWithReportingAuditTrailEnv('LIVE');
    expect(fresh.getPortMode('ReportingService', 'primaryAction')).toBe('SIMULATION');
    expect(fresh.getPortMode('ReportingService', 'viewAuditTrail')).toBe('LIVE');
    expect(fresh.getModuleStatus('ReportingService')).toBe('HYBRID');
  });

  it('every module with more than one port key is a deliberate, noticed multi-port module', () => {
    // Every module except FrontendRuntime/Unassigned (no ports at all, nothing to be live
    // about) has one or more port keys. Originally only ReportingService had more than one;
    // BIO-UX-007 gave CredentialService a real key per Cards/Credentials/NFC-UHF/Mobile
    // Credentials/Card Designer screen (web/features/cards/ports.ts's own header note), and
    // BIO-UX-010 gave five operational modules (Access/Attendance/Visitor/Transport/Device)
    // a second key each, keyed after route-manifest.ts's own named secondary actions;
    // BIO-DOC-009 gave DocumentVerifyService a second key, 'authenticity', once its own real
    // backend existed to connect (see MODULE_PORT_KEYS's own comment); BIO-RPT-002 gave
    // ReportingService four more keys ('generateReport'/'getReportStatus'/'cancelReport'/
    // 'exportReport'), its own dedicated connection distinct from BIO-RPT-001's own
    // 'primaryAction'/'viewAuditTrail' (a genuine two-story route collision resolved by giving
    // BIO-RPT-002 new, non-colliding routes/ports rather than either story losing its own
    // already-accepted work - see MODULE_PORT_KEYS's own comment); BIO-CRD-008 gave
    // CredentialService eight more keys ('createTemplateDraft'/'listTemplateLineages'/
    // 'submitTemplateForValidation'/'recordTemplateValidation'/'rejectTemplate'/
    // 'publishTemplate'/'retireTemplate'/'createNextTemplateVersion'), the real card-template
    // lifecycle, independent of 'saveDesign' and every other existing CredentialService port;
    // BIO-RPT-003 gave ReportingService two more keys ('searchAuditEvents'/'exportAuditEvents'),
    // mounted on /audit alongside BIO-RPT-001's own 'viewAuditTrail' card - asserted explicitly
    // (module name + exact key list) so any future change here is deliberate and noticed, not
    // silent.
    // BIO-BIO-008 gave BiometricService a second key ('checkLiveness'), its real liveness
    // provider/policy backend, independent of 'primaryAction' (fingerprint/face capture, 1:1
    // verify, 1:N identify - still SIMULATION-only) - asserted explicitly (module name + exact
    // key list) so any future change here is deliberate and noticed, not silent; BIO-BIO-009
    // added 'manageRetentionPolicy'/'deleteBiometricRecord', its real retention-policy and
    // audited-deletion endpoints.
    // BIO-RPT-004 gave ReportingService one more key ('operationalHealth'), the dashboard's own
    // real identity/access/production-state snapshot, independent of every existing
    // ReportingService key - asserted explicitly for the same reason.
    // IntegrationService gained its first extra keys with BIO-INT-003 ('listServiceAccounts'/
    // 'createServiceAccount'/'rotateServiceAccountSecret'/'revokeServiceAccount') and BIO-INT-004
    // ('listWebhookSubscriptions'/'createWebhookSubscription'/'setWebhookSubscriptionEnabled') -
    // this module was already multi-port before this story, but this assertion's own expected
    // literal had never been updated to say so (a pre-existing gap, not introduced here).
    // BIO-INT-005 adds three more ('scanConflicts'/'listConflicts'/'decideConflict'); all ten
    // non-primaryAction keys are asserted together here for the first time.
    const multiPortModules = Object.entries(MODULE_PORT_KEYS).filter(([, keys]) => keys.length > 1);
    expect(Object.fromEntries(multiPortModules)).toEqual({
      CredentialService: [
        'primaryAction',
        'issueCard',
        'replaceCard',
        'markLost',
        'recoverCard',
        'revokeCard',
        'encodeTag',
        'verifyRead',
        'revokeMobileCredential',
        'saveDesign',
        'createTemplateDraft',
        'listTemplateLineages',
        'submitTemplateForValidation',
        'recordTemplateValidation',
        'rejectTemplate',
        'publishTemplate',
        'retireTemplate',
        'createNextTemplateVersion',
      ],
      ReportingService: [
        'primaryAction', 'viewAuditTrail', 'generateReport', 'getReportStatus', 'cancelReport', 'exportReport',
        'searchAuditEvents', 'exportAuditEvents', 'operationalHealth',
      ],
      AccessService: ['primaryAction', 'viewAccessEvents'],
      AttendanceService: ['primaryAction', 'flagException', 'workforceAttendance'],
      VisitorService: ['primaryAction', 'checkOutVisitor', 'viewVisitHistory'],
      // TransportService's own four BIO-TRN-003 keys were never synced into this literal when
      // that story merged (a pre-existing TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP-class gap,
      // not introduced by BIO-DEV-004) - closed here since it was found while this exact test
      // was being run for this story's own DeviceService addition, and the fix is a one-line,
      // zero-judgment sync to BIO-TRN-003's own already-accepted MODULE_PORT_KEYS value.
      TransportService: [
        'primaryAction', 'assignRoute',
        'listTransportNotificationRules', 'createTransportNotificationRule', 'setTransportNotificationRuleEnabled', 'listTransportNotificationDeliveries',
      ],
      DeviceService: ['primaryAction', 'viewConnectivityStatus', 'listDevices', 'pollDeviceHealth', 'getRemoteJobCapabilities', 'runRemoteJob'],
      DocumentVerifyService: ['primaryAction', 'authenticity', 'documentIdentification'],
      BiometricService: ['primaryAction', 'checkLiveness', 'manageRetentionPolicy', 'deleteBiometricRecord'],
      IntegrationService: [
        'primaryAction', 'listServiceAccounts', 'createServiceAccount', 'rotateServiceAccountSecret', 'revokeServiceAccount',
        'listWebhookSubscriptions', 'createWebhookSubscription', 'setWebhookSubscriptionEnabled',
        'scanConflicts', 'listConflicts', 'decideConflict',
      ],
    });
  });
});

describe('BIO-UX-005-AC01 resolvePort picks mock vs live from the registry', () => {
  it('returns the mock adapter in SIMULATION mode (the default)', () => {
    const mock = createMockAdapter('DeviceService', { simulatedLatencyMs: 0 });
    const live = createMockAdapter('DeviceService', { simulatedLatencyMs: 0 });
    expect(resolvePort(mock, live)).toBe(mock);
  });

  it('returns the live adapter once the module is set to LIVE', () => {
    setPortModeOverride('DeviceService', 'primaryAction', 'LIVE');
    const mock = createMockAdapter('DeviceService', { simulatedLatencyMs: 0 });
    const live = createMockAdapter('DeviceService', { simulatedLatencyMs: 0 });
    expect(resolvePort(mock, live)).toBe(live);
  });
});

describe('BIO-UX-005-AC03 auditFeatureModes is the read-only registry snapshot the dev panel renders', () => {
  it('includes every module that has at least one port, excludes those with none', () => {
    const entries = auditFeatureModes();
    const modules = entries.map((e) => e.module);
    expect(modules).toContain('DeviceService');
    expect(modules).toContain('IdentityService');
    expect(modules).not.toContain('Unassigned');
    expect(modules).not.toContain('FrontendRuntime');
  });

  it('every entry\'s status matches getModuleStatus for that module', () => {
    setPortModeOverride('DeviceService', 'primaryAction', 'LIVE');
    const entry = auditFeatureModes().find((e) => e.module === 'DeviceService');
    expect(entry?.status).toBe(getModuleStatus('DeviceService'));
    // DeviceService has six ports since BIO-DEV-004 (primaryAction, viewConnectivityStatus,
    // listDevices, pollDeviceHealth, getRemoteJobCapabilities, runRemoteJob) - only the first
    // is set live above, so the entry lists all six, and status is HYBRID.
    expect(entry?.ports).toEqual([
      { portKey: 'primaryAction', mode: 'LIVE' },
      { portKey: 'viewConnectivityStatus', mode: 'SIMULATION' },
      { portKey: 'listDevices', mode: 'SIMULATION' },
      { portKey: 'pollDeviceHealth', mode: 'SIMULATION' },
      { portKey: 'getRemoteJobCapabilities', mode: 'SIMULATION' },
      { portKey: 'runRemoteJob', mode: 'SIMULATION' },
    ]);
    expect(entry?.status).toBe('HYBRID');
  });
});

describe('BIO-UX-005-AC06 setPortModeOverride is disabled in production', () => {
  it('throws when NODE_ENV is production, so a misplaced call can never flip a module live there', () => {
    const original = process.env.NODE_ENV;
    // @ts-expect-error - NODE_ENV is typed readonly in this TS config; test needs to set it.
    process.env.NODE_ENV = 'production';
    try {
      expect(() => setPortModeOverride('DeviceService', 'primaryAction', 'LIVE')).toThrow(/development\/test-only/);
    } finally {
      // @ts-expect-error - see above.
      process.env.NODE_ENV = original;
    }
  });
});
