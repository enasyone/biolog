/**
 * Central module/port-key → resolved-port lookup (BIO-UX-005-AC01).
 *
 * `PlaceholderPage` is a Client Component; the 30 Server Component pages
 * that render it can only pass it serialisable props (a `ModulePort` object
 * carries a function — `primaryAction` — which the React Server Components
 * boundary rejects). Pages instead pass the plain `ServicePort` string
 * naming their module (and, for the one module with more than one port,
 * which `portKey`), and `PlaceholderPage` resolves the actual port from
 * this registry itself, entirely client-side — no function ever crosses the
 * server/client boundary.
 *
 * Indexed by module then port key (not a flat `Record<ServicePort, ModulePort>`)
 * so a module with more than one port — currently only `ReportingService`,
 * see `feature-mode.ts`'s `MODULE_PORT_KEYS` comment — is expressible without
 * `PlaceholderPage` (shell-owned, BIO-UX-002-AC05: no feature-specific logic)
 * needing to special-case any one module by name.
 */
import { identityPort } from '../features/identity';
import { visitorsCheckOutPort, visitorsPort, visitorsViewHistoryPort } from '../features/visitors';
import {
  cardsPort,
  cardsIssuePort,
  cardsReplacePort,
  cardsMarkLostPort,
  cardsRecoverPort,
  cardsRevokePort,
  cardsEncodeTagPort,
  cardsVerifyReadPort,
  cardsRevokeMobilePort,
  cardsSaveDesignPort,
} from '../features/cards';
import {
  cardTemplateCreateDraftPort,
  cardTemplateListLineagesPort,
  cardTemplateSubmitForValidationPort,
  cardTemplateRecordValidationPort,
  cardTemplateRejectPort,
  cardTemplatePublishPort,
  cardTemplateRetirePort,
  cardTemplateCreateNextVersionPort,
} from '../features/bio-crd-008';
import {
  biometricsPort,
  biometricsCheckLivenessPort,
  biometricsManageRetentionPolicyPort,
  biometricsDeleteBiometricRecordPort,
} from '../features/biometrics';
import { photosPort } from '../features/photos';
import { documentsAuthenticityPort, documentsDocumentIdentificationPort, documentsPort } from '../features/documents';
import { accessPort, accessViewAccessEventsPort } from '../features/access';
import { attendanceFlagExceptionPort, attendancePort, workforceAttendanceModulePort } from '../features/attendance';
import { transportAssignRoutePort, transportPort } from '../features/transport';
import {
  createTransportNotificationRulePort,
  listTransportNotificationDeliveriesPort,
  listTransportNotificationRulesPort,
  setTransportNotificationRuleEnabledPort,
} from '../features/bio-trn-003';
import { devicesPort, devicesViewConnectivityPort } from '../features/devices';
import {
  getRemoteJobCapabilitiesPort,
  listDevicesPort,
  pollDeviceHealthPort,
  runRemoteJobPort,
} from '../features/bio-dev-004';
import {
  reportingPort,
  reportingViewAuditTrailPort,
  reportingGenerateReportPort,
  reportingGetReportStatusPort,
  reportingCancelReportPort,
  reportingExportReportPort,
} from '../features/reporting';
import { searchAuditEventsPort, exportAuditEventsPort } from '../features/bio-rpt-003';
import { operationalHealthPort } from '../features/bio-rpt-004';
import { tenantsPort } from '../features/tenants';
import { integrationsPort } from '../features/integrations';
import {
  createServiceAccountPort,
  listServiceAccountsPort,
  revokeServiceAccountPort,
  rotateServiceAccountSecretPort,
} from '../features/bio-int-003';
import {
  createWebhookSubscriptionPort,
  listWebhookSubscriptionsPort,
  setWebhookSubscriptionEnabledPort,
} from '../features/bio-int-004';
import { decideConflictPort, listConflictsPort, scanConflictsPort } from '../features/bio-int-005';
import { UNASSIGNED_MOCK_PORT, type ModulePort } from './module-port';
import type { ServicePort } from './route-manifest';

export const MODULE_PORTS: Readonly<Record<ServicePort, Readonly<Record<string, ModulePort>>>> =
  Object.freeze({
    IdentityService: Object.freeze({ primaryAction: identityPort }),
    VisitorService: Object.freeze({
      primaryAction: visitorsPort,
      checkOutVisitor: visitorsCheckOutPort,
      viewVisitHistory: visitorsViewHistoryPort,
    }),
    CredentialService: Object.freeze({
      primaryAction: cardsPort,
      issueCard: cardsIssuePort,
      replaceCard: cardsReplacePort,
      markLost: cardsMarkLostPort,
      recoverCard: cardsRecoverPort,
      revokeCard: cardsRevokePort,
      encodeTag: cardsEncodeTagPort,
      verifyRead: cardsVerifyReadPort,
      revokeMobileCredential: cardsRevokeMobilePort,
      saveDesign: cardsSaveDesignPort,
      createTemplateDraft: cardTemplateCreateDraftPort,
      listTemplateLineages: cardTemplateListLineagesPort,
      submitTemplateForValidation: cardTemplateSubmitForValidationPort,
      recordTemplateValidation: cardTemplateRecordValidationPort,
      rejectTemplate: cardTemplateRejectPort,
      publishTemplate: cardTemplatePublishPort,
      retireTemplate: cardTemplateRetirePort,
      createNextTemplateVersion: cardTemplateCreateNextVersionPort,
    }),
    BiometricService: Object.freeze({
      primaryAction: biometricsPort,
      checkLiveness: biometricsCheckLivenessPort,
      manageRetentionPolicy: biometricsManageRetentionPolicyPort,
      deleteBiometricRecord: biometricsDeleteBiometricRecordPort,
    }),
    PhotoService: Object.freeze({ primaryAction: photosPort }),
    DocumentVerifyService: Object.freeze({
      primaryAction: documentsPort,
      authenticity: documentsAuthenticityPort,
      documentIdentification: documentsDocumentIdentificationPort,
    }),
    AccessService: Object.freeze({ primaryAction: accessPort, viewAccessEvents: accessViewAccessEventsPort }),
    AttendanceService: Object.freeze({ primaryAction: attendancePort, flagException: attendanceFlagExceptionPort, workforceAttendance: workforceAttendanceModulePort }),
    TransportService: Object.freeze({
      primaryAction: transportPort,
      assignRoute: transportAssignRoutePort,
      listTransportNotificationRules: listTransportNotificationRulesPort,
      createTransportNotificationRule: createTransportNotificationRulePort,
      setTransportNotificationRuleEnabled: setTransportNotificationRuleEnabledPort,
      listTransportNotificationDeliveries: listTransportNotificationDeliveriesPort,
    }),
    DeviceService: Object.freeze({
      primaryAction: devicesPort,
      viewConnectivityStatus: devicesViewConnectivityPort,
      listDevices: listDevicesPort,
      pollDeviceHealth: pollDeviceHealthPort,
      getRemoteJobCapabilities: getRemoteJobCapabilitiesPort,
      runRemoteJob: runRemoteJobPort,
    }),
    ReportingService: Object.freeze({
      primaryAction: reportingPort,
      viewAuditTrail: reportingViewAuditTrailPort,
      generateReport: reportingGenerateReportPort,
      getReportStatus: reportingGetReportStatusPort,
      cancelReport: reportingCancelReportPort,
      exportReport: reportingExportReportPort,
      searchAuditEvents: searchAuditEventsPort,
      exportAuditEvents: exportAuditEventsPort,
      operationalHealth: operationalHealthPort,
    }),
    TenantService: Object.freeze({ primaryAction: tenantsPort }),
    IntegrationService: Object.freeze({
      primaryAction: integrationsPort,
      listServiceAccounts: listServiceAccountsPort,
      createServiceAccount: createServiceAccountPort,
      rotateServiceAccountSecret: rotateServiceAccountSecretPort,
      revokeServiceAccount: revokeServiceAccountPort,
      listWebhookSubscriptions: listWebhookSubscriptionsPort,
      createWebhookSubscription: createWebhookSubscriptionPort,
      setWebhookSubscriptionEnabled: setWebhookSubscriptionEnabledPort,
      scanConflicts: scanConflictsPort,
      listConflicts: listConflictsPort,
      decideConflict: decideConflictPort,
    }),
    FrontendRuntime: Object.freeze({ primaryAction: UNASSIGNED_MOCK_PORT }),
    Unassigned: Object.freeze({ primaryAction: UNASSIGNED_MOCK_PORT }),
  });
