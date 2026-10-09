/**
 * Feature-mode registry (BIO-UX-005-AC02, AC03, AC06).
 *
 * The source of truth for which adapter (mock or live) each module's ports
 * resolve to. A page never reads this directly — it calls `resolvePort`
 * (below) or, in practice, the module's own exported port instance, which
 * already resolved once at import time. This file is what the dev-only
 * feature-mode panel (`web/dev-tools/feature-mode-panel.tsx`) reads to
 * display current state (AC03's "exposes current module mode").
 */
import type { ServicePort } from './route-manifest';
import type { ModulePort } from './module-port';

export type FeatureMode = 'SIMULATION' | 'LIVE';
export type ModuleStatus = 'SIMULATION' | 'HYBRID' | 'LIVE';

function isValidMode(value: unknown): value is FeatureMode {
  return value === 'SIMULATION' || value === 'LIVE';
}

/**
 * Every module currently registered and the port keys it exposes. Most
 * modules have exactly one ('primaryAction' — see `module-port.ts`'s own
 * header comment for why); a domain story adds a key here when its
 * `ports.ts` grows a second real port.
 *
 * `ReportingService` already has two: `/reports` ("Run report") and
 * `/audit` ("View audit trail") are genuinely distinct actions
 * `route-manifest.ts` already named separately, not one generic action
 * reused across routes like every other module today. Their independent
 * mode resolution (see `features/reporting/index.ts`) is what lets
 * `getModuleStatus('ReportingService')` actually reach `HYBRID`
 * (BIO-UX-005-AC02 remediation, evidence `e2dd368` — Chioma's option (a):
 * a real multi-port module with executable evidence, not a scope change).
 *
 * BIO-UX-010 follows that exact same multi-port pattern for its five
 * operational modules, keyed after each route's own `primaryActions`/
 * `secondaryActions` in `route-manifest.ts` (each additional key below is one
 * of those named secondary actions, not an invented port): Access gets
 * `viewAccessEvents`; Attendance gets `flagException`; Visitor gets
 * `checkOutVisitor` and `viewVisitHistory` (two secondary actions,
 * `route-manifest.ts`'s `/visitors` entry names both); Transport gets
 * `assignRoute`; Device gets `viewConnectivityStatus`.
 *
 * `DocumentVerifyService` gains a second key, `authenticity`, with BIO-DOC-009 (the first BIO-DOC
 * story to expose a real HTTP endpoint - `POST /api/v1/documents/authenticity`). Independent from
 * `primaryAction` for the same reason `ReportingService`'s own two ports are: BIO-DOC-009's
 * backend orchestrator exists and is connectable, but the other seven Document Verify steps
 * (capture, MRZ, barcode, OCR, document identification, NFC, selfie comparison) have no backend of
 * their own yet - switching only `authenticity` to LIVE lets the module genuinely reach `HYBRID`
 * (BIO-UX-005-AC02) rather than forcing an all-or-nothing choice across steps with very different
 * readiness.
 *
 * A third key, `documentIdentification`, is added with BIO-DOC-010 - `POST /api/v1/documents/identify`
 * (previously mapped in `live.ts`'s own `LIVE_ENDPOINTS` since BIO-UX-009, but always unconnected
 * until this story). Independent from `primaryAction` for exactly the reason `authenticity` already
 * is, plus one more: `identifyDocument` is gated on `capture` having already succeeded
 * (`DocumentVerifyApp.tsx`'s own `requiresCapture` rule) - if this key shared `primaryAction`'s
 * mode, switching it LIVE would also switch `capture` LIVE, and `capture` has no backend of its own
 * yet, so it would always fail and permanently block `identifyDocument` from ever running. Keeping
 * it a separate key lets `capture` stay on the SIMULATION mock (always succeeds) while
 * `documentIdentification` alone reaches the real governed-pack backend.
 *
 * `ReportingService` gains four more keys with BIO-RPT-002: `generateReport`/`getReportStatus`/
 * `cancelReport`/`exportReport`, its own dedicated create→poll→(cancel|export) connection to a
 * real governed report engine. `primaryAction`/`viewAuditTrail` are untouched and stay
 * BIO-RPT-001's own real notification-demo routes — a genuine two-story collision at those exact
 * paths was resolved (god's ruling, 2026-09-24) by giving BIO-RPT-002 new, non-colliding routes/
 * ports rather than either story losing its own already-accepted work; see
 * `web/features/reporting/ports.ts`'s own header and docs/features/bio-rpt-002/README.md.
 *
 * `ReportingService` gains two more keys with BIO-RPT-003: `searchAuditEvents`/
 * `exportAuditEvents`, mounted on `/audit` ALONGSIDE (not replacing) BIO-RPT-001's own
 * `viewAuditTrail` card — see `web/features/bio-rpt-003/ports.ts`'s own header and
 * docs/features/bio-rpt-003/README.md.
 *
 * `CredentialService` gains eight more keys with BIO-CRD-008: `createTemplateDraft`/
 * `listTemplateLineages`/`submitTemplateForValidation`/`recordTemplateValidation`/
 * `rejectTemplate`/`publishTemplate`/`retireTemplate`/`createNextTemplateVersion` - the real
 * Draft->Validation->Approval->Published->Retired card-template lifecycle
 * (`web/features/bio-crd-008/`), independent of `saveDesign` (BIO-UX-007's own stable seam,
 * untouched) and every other existing `CredentialService` port. See
 * `web/features/bio-crd-008/ports.ts`'s own header.
 *
 * `BiometricService` gains a second key, `checkLiveness`, with BIO-BIO-008 (the first
 * `BiometricService` action to expose a real HTTP endpoint - `POST /api/v1/biometrics/face/liveness`).
 * Independent from `primaryAction` for the same reason `DocumentVerifyService`'s own `authenticity`
 * split is: BIO-BIO-008's liveness provider/policy backend exists and is connectable, but the
 * other three `BiometricService` actions (fingerprint/face capture, 1:1 verify, 1:N identify) have
 * no backend of their own yet - switching only `checkLiveness` to LIVE lets the module genuinely
 * reach `HYBRID` (BIO-UX-005-AC02) rather than forcing an all-or-nothing choice.
 *
 * `BiometricService` gains two more keys with BIO-BIO-009 (round 3, Seyi's BIO-BIO-009-F03):
 * `manageRetentionPolicy`/`deleteBiometricRecord`, each its own real endpoint
 * (`POST /api/v1/biometrics/retention/policy`, `POST /api/v1/biometrics/retention/delete`) -
 * the same independent-per-port shape `checkLiveness` already established, mounted on
 * `/fingerprints` (`web/features/bio-bio-009/RetentionSection.tsx`).
 *
 * `IntegrationService` gains four more keys with BIO-INT-003: `listServiceAccounts`/
 * `createServiceAccount`/`rotateServiceAccountSecret`/`revokeServiceAccount` - a real tenant-
 * scoped API service-account lifecycle (`web/features/bio-int-003/`), independent of
 * `primaryAction` (BIO-INT-001's own demo-seed route) and of BIO-INT-002's own `listMappings`/
 * `createMapping`/`previewImport`/`applyImport` (which still ride `primaryAction`'s mode - an
 * earlier choice this story does not change). Mounted as a new "API Credentials" panel on
 * `/administration/integrations` ALONGSIDE BIO-INT-002's `ImportConsole`, the same "new keys,
 * new panel, existing panel untouched" shape `ReportingService`'s own `searchAuditEvents`/
 * `exportAuditEvents` pair already established for `/audit`.
 *
 * `IntegrationService` gains three more keys with BIO-INT-004: `listWebhookSubscriptions`/
 * `createWebhookSubscription`/`setWebhookSubscriptionEnabled` - tenant-scoped outbound webhook
 * subscriptions (`web/features/bio-int-004/`), independent of BIO-INT-003's own service-account
 * keys above (a different sub-surface of the same module), mounted as a new "Webhooks" panel on
 * `/administration/integrations` ALONGSIDE both existing panels there.
 *
 * `IntegrationService` gains three more keys with BIO-INT-005: `scanConflicts`/`listConflicts`/
 * `decideConflict` - external import conflict review (`web/features/bio-int-005/`), independent
 * of every key above (a different sub-surface of the same module), mounted as a new "Conflict
 * Review" panel on `/administration/integrations` ALONGSIDE all three existing panels there.
 * While extending this module, also closed a pre-existing gap left by BIO-INT-003/BIO-INT-004:
 * `web/lib/module-registry.ts`'s own `IntegrationService` entry had never actually registered
 * their seven port keys (only `primaryAction` was present) - `bio-ux-005-module-registry.test.ts`'s
 * own "registry and feature-mode agree" check was failing for this module before this story (not
 * a new failure introduced here); fixed for all ten keys at once while already touching this file.
 *
 * `TransportService` gains four more keys with BIO-TRN-003: `listTransportNotificationRules`/
 * `createTransportNotificationRule`/`setTransportNotificationRuleEnabled`/
 * `listTransportNotificationDeliveries` - tenant-scoped board/alight/missed-alight/exception
 * notification rules and their delivery trail (`web/features/bio-trn-003/`), independent of
 * `primaryAction`/`assignRoute` above (a different sub-surface of the same module), mounted as a
 * new "Transport notifications" panel on `/transport` ALONGSIDE the existing boarding/alighting
 * log and route-assignment panels there.
 *
 * `DeviceService` gains four more keys with BIO-DEV-004: `listDevices`/`pollDeviceHealth`/
 * `getRemoteJobCapabilities`/`runRemoteJob` - tenant-scoped device health polling/heartbeat and
 * capability-gated remote configuration jobs (`web/features/bio-dev-004/`), independent of
 * `primaryAction`/`viewConnectivityStatus` above, mounted as a new "Device operations" panel on
 * `/devices` ALONGSIDE the existing register/connectivity-status panels there.
 */
export const MODULE_PORT_KEYS: Readonly<Record<ServicePort, readonly string[]>> = Object.freeze({
  IdentityService: ['primaryAction'],
  VisitorService: ['primaryAction', 'checkOutVisitor', 'viewVisitHistory'],
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
  BiometricService: ['primaryAction', 'checkLiveness', 'manageRetentionPolicy', 'deleteBiometricRecord'],
  PhotoService: ['primaryAction'],
  DocumentVerifyService: ['primaryAction', 'authenticity', 'documentIdentification'],
  AccessService: ['primaryAction', 'viewAccessEvents'],
  AttendanceService: ['primaryAction', 'flagException', 'workforceAttendance'],
  TransportService: [
    'primaryAction', 'assignRoute',
    'listTransportNotificationRules', 'createTransportNotificationRule', 'setTransportNotificationRuleEnabled', 'listTransportNotificationDeliveries',
  ],
  DeviceService: [
    'primaryAction', 'viewConnectivityStatus',
    'listDevices', 'pollDeviceHealth', 'getRemoteJobCapabilities', 'runRemoteJob',
  ],
  ReportingService: [
    'primaryAction', 'viewAuditTrail', 'generateReport', 'getReportStatus', 'cancelReport', 'exportReport',
    'searchAuditEvents', 'exportAuditEvents', 'operationalHealth',
  ],
  TenantService: ['primaryAction'],
  IntegrationService: [
    'primaryAction', 'listServiceAccounts', 'createServiceAccount', 'rotateServiceAccountSecret', 'revokeServiceAccount',
    'listWebhookSubscriptions', 'createWebhookSubscription', 'setWebhookSubscriptionEnabled',
    'scanConflicts', 'listConflicts', 'decideConflict',
  ],
  FrontendRuntime: [],
  Unassigned: [],
});

/**
 * Environment-driven default (BIO-UX-005-AC06 "auditable"): set
 * `NEXT_PUBLIC_FEATURE_MODE_<MODULE>` (module name upper-cased, e.g.
 * `NEXT_PUBLIC_FEATURE_MODE_IDENTITYSERVICE=LIVE`) in `.env.local` to flip a
 * module's default without touching code.
 *
 * Every reference below is a literal `process.env.NEXT_PUBLIC_...` property
 * access, deliberately not a computed/bracket-notation lookup
 * (`process.env[\`...${module}\`]`) — Next.js's bundler only inlines
 * `NEXT_PUBLIC_*` values it can statically see referenced this way at build
 * time; in a client bundle, `process.env` is not the real runtime
 * dictionary, so a computed key silently always resolves to `undefined`
 * there (this was a real bug here, caught by manually verifying the
 * end-to-end LIVE-mode switch against a real dev server rather than trusting
 * the code — see `docs/features/bio-ux-005/README.md` "Proving AC04").
 */
const ENV_DEFAULTS: Readonly<Record<ServicePort, string | undefined>> = {
  IdentityService: process.env.NEXT_PUBLIC_FEATURE_MODE_IDENTITYSERVICE,
  VisitorService: process.env.NEXT_PUBLIC_FEATURE_MODE_VISITORSERVICE,
  CredentialService: process.env.NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE,
  BiometricService: process.env.NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE,
  PhotoService: process.env.NEXT_PUBLIC_FEATURE_MODE_PHOTOSERVICE,
  DocumentVerifyService: process.env.NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE,
  AccessService: process.env.NEXT_PUBLIC_FEATURE_MODE_ACCESSSERVICE,
  AttendanceService: process.env.NEXT_PUBLIC_FEATURE_MODE_ATTENDANCESERVICE,
  TransportService: process.env.NEXT_PUBLIC_FEATURE_MODE_TRANSPORTSERVICE,
  DeviceService: process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE,
  ReportingService: process.env.NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE,
  TenantService: process.env.NEXT_PUBLIC_FEATURE_MODE_TENANTSERVICE,
  IntegrationService: process.env.NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE,
  FrontendRuntime: undefined,
  Unassigned: undefined,
};

/**
 * Per-port env overrides, for the handful of ports that need to diverge
 * from their module's own whole-module default above (currently just
 * Reporting's second port — see `MODULE_PORT_KEYS`'s comment). Falls back to
 * `ENV_DEFAULTS[module]` when unset, so every other module's simple
 * single-var configuration is unaffected.
 */
const PER_PORT_ENV_DEFAULTS: Readonly<Record<string, string | undefined>> = {
  'ReportingService:viewAuditTrail': process.env.NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_VIEWAUDITTRAIL,
  'ReportingService:searchAuditEvents': process.env.NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_SEARCHAUDITEVENTS,
  'ReportingService:exportAuditEvents': process.env.NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_EXPORTAUDITEVENTS,
  'ReportingService:operationalHealth': process.env.NEXT_PUBLIC_FEATURE_MODE_REPORTINGSERVICE_OPERATIONALHEALTH,
  'DocumentVerifyService:authenticity': process.env.NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY,
  'DocumentVerifyService:documentIdentification': process.env.NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_DOCUMENTIDENTIFICATION,
  'AccessService:viewAccessEvents': process.env.NEXT_PUBLIC_FEATURE_MODE_ACCESSSERVICE_VIEWACCESSEVENTS,
  'AttendanceService:flagException': process.env.NEXT_PUBLIC_FEATURE_MODE_ATTENDANCESERVICE_FLAGEXCEPTION,
  'AttendanceService:workforceAttendance': process.env.NEXT_PUBLIC_FEATURE_MODE_ATTENDANCESERVICE_WORKFORCEATTENDANCE,
  'VisitorService:checkOutVisitor': process.env.NEXT_PUBLIC_FEATURE_MODE_VISITORSERVICE_CHECKOUTVISITOR,
  'VisitorService:viewVisitHistory': process.env.NEXT_PUBLIC_FEATURE_MODE_VISITORSERVICE_VIEWVISITHISTORY,
  'TransportService:assignRoute': process.env.NEXT_PUBLIC_FEATURE_MODE_TRANSPORTSERVICE_ASSIGNROUTE,
  'DeviceService:viewConnectivityStatus': process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE_VIEWCONNECTIVITYSTATUS,
  'BiometricService:checkLiveness': process.env.NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_CHECKLIVENESS,
  'BiometricService:manageRetentionPolicy': process.env.NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_MANAGERETENTIONPOLICY,
  'BiometricService:deleteBiometricRecord': process.env.NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_DELETEBIOMETRICRECORD,
  'IntegrationService:listServiceAccounts': process.env.NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_LISTSERVICEACCOUNTS,
  'IntegrationService:createServiceAccount': process.env.NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_CREATESERVICEACCOUNT,
  'IntegrationService:rotateServiceAccountSecret': process.env.NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_ROTATESERVICEACCOUNTSECRET,
  'IntegrationService:revokeServiceAccount': process.env.NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_REVOKESERVICEACCOUNT,
  'IntegrationService:listWebhookSubscriptions': process.env.NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_LISTWEBHOOKSUBSCRIPTIONS,
  'IntegrationService:createWebhookSubscription': process.env.NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_CREATEWEBHOOKSUBSCRIPTION,
  'IntegrationService:setWebhookSubscriptionEnabled': process.env.NEXT_PUBLIC_FEATURE_MODE_INTEGRATIONSERVICE_SETWEBHOOKSUBSCRIPTIONENABLED,
  'TransportService:listTransportNotificationRules': process.env.NEXT_PUBLIC_FEATURE_MODE_TRANSPORTSERVICE_LISTTRANSPORTNOTIFICATIONRULES,
  'TransportService:createTransportNotificationRule': process.env.NEXT_PUBLIC_FEATURE_MODE_TRANSPORTSERVICE_CREATETRANSPORTNOTIFICATIONRULE,
  'TransportService:setTransportNotificationRuleEnabled': process.env.NEXT_PUBLIC_FEATURE_MODE_TRANSPORTSERVICE_SETTRANSPORTNOTIFICATIONRULEENABLED,
  'TransportService:listTransportNotificationDeliveries': process.env.NEXT_PUBLIC_FEATURE_MODE_TRANSPORTSERVICE_LISTTRANSPORTNOTIFICATIONDELIVERIES,
  'DeviceService:listDevices': process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE_LISTDEVICES,
  'DeviceService:pollDeviceHealth': process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE_POLLDEVICEHEALTH,
  'DeviceService:getRemoteJobCapabilities': process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE_GETREMOTEJOBCAPABILITIES,
  'DeviceService:runRemoteJob': process.env.NEXT_PUBLIC_FEATURE_MODE_DEVICESERVICE_RUNREMOTEJOB,
};

function envDefault(module: ServicePort, portKey: string): FeatureMode | undefined {
  const perPort = PER_PORT_ENV_DEFAULTS[`${module}:${portKey}`];
  if (isValidMode(perPort)) return perPort;

  const raw = ENV_DEFAULTS[module];
  return isValidMode(raw) ? raw : undefined;
}

/** Dev/test-only in-memory overrides — never the production source of truth. See `setPortModeOverride`. */
const overrides = new Map<string, FeatureMode>();

function overrideKey(module: ServicePort, portKey: string): string {
  return `${module}:${portKey}`;
}

/**
 * Get a port's current mode. Fails safe (BIO-UX-005-AC06): an unknown
 * module/port-key pair, or an invalid override/env value, resolves to
 * `'SIMULATION'` rather than throwing or defaulting to `'LIVE'` (the safer
 * direction to fail toward — a stuck-in-simulation module blocks nothing;
 * an accidentally-live module could call a real, unbuilt backend).
 */
export function getPortMode(module: ServicePort, portKey = 'primaryAction'): FeatureMode {
  const knownKeys = MODULE_PORT_KEYS[module];
  if (!knownKeys || !knownKeys.includes(portKey)) return 'SIMULATION';

  const override = overrides.get(overrideKey(module, portKey));
  if (override !== undefined) return isValidMode(override) ? override : 'SIMULATION';

  const fromEnv = envDefault(module, portKey);
  if (fromEnv !== undefined) return fromEnv;

  return 'SIMULATION';
}

/**
 * Aggregated module status (BIO-UX-005-AC02): `SIMULATION` if every known
 * port on the module is in simulation, `LIVE` if every one is live,
 * `HYBRID` if mixed. A module with no registered ports (`FrontendRuntime`,
 * `Unassigned`) reports `SIMULATION` — there is nothing to be live about.
 */
export function getModuleStatus(module: ServicePort): ModuleStatus {
  const keys = MODULE_PORT_KEYS[module] ?? [];
  if (keys.length === 0) return 'SIMULATION';
  const modes = keys.map((key) => getPortMode(module, key));
  const allSimulation = modes.every((mode) => mode === 'SIMULATION');
  const allLive = modes.every((mode) => mode === 'LIVE');
  if (allSimulation) return 'SIMULATION';
  if (allLive) return 'LIVE';
  return 'HYBRID';
}

/**
 * Dev/test-only override (BIO-UX-005-AC06). Throws outside development so a
 * misplaced call can never silently flip a module live in production — the
 * same fail-safe direction as `getPortMode`'s default, enforced at the
 * write path too.
 */
export function setPortModeOverride(module: ServicePort, portKey: string, mode: FeatureMode): void {
  if (process.env.NODE_ENV === 'production') {
    throw new Error('setPortModeOverride is development/test-only and is disabled in production.');
  }
  overrides.set(overrideKey(module, portKey), mode);
}

export function clearPortModeOverrides(): void {
  overrides.clear();
}

/** Resolve which adapter a call site should use for one port (BIO-UX-005-AC01, AC04). */
export function resolvePort<T extends ModulePort>(mock: T, live: T, portKey = 'primaryAction'): T {
  return getPortMode(mock.module, portKey) === 'LIVE' ? live : mock;
}

export interface ModuleAuditEntry {
  module: ServicePort;
  status: ModuleStatus;
  ports: ReadonlyArray<{ portKey: string; mode: FeatureMode }>;
}

/** Full registry snapshot (BIO-UX-005-AC03, AC06) — what the dev-only panel renders. */
export function auditFeatureModes(): readonly ModuleAuditEntry[] {
  return (Object.keys(MODULE_PORT_KEYS) as ServicePort[])
    .filter((module) => MODULE_PORT_KEYS[module].length > 0)
    .map((module) => ({
      module,
      status: getModuleStatus(module),
      ports: MODULE_PORT_KEYS[module].map((portKey) => ({ portKey, mode: getPortMode(module, portKey) })),
    }));
}
