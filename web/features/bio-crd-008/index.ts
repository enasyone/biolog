import { resolvePort } from '../../lib/feature-mode';
import {
  cardTemplateCreateDraftMockPort,
  cardTemplateListLineagesMockPort,
  cardTemplateSubmitForValidationMockPort,
  cardTemplateRecordValidationMockPort,
  cardTemplateRejectMockPort,
  cardTemplatePublishMockPort,
  cardTemplateRetireMockPort,
  cardTemplateCreateNextVersionMockPort,
} from './mock';
import {
  cardTemplateCreateDraftLivePort,
  cardTemplateListLineagesLivePort,
  cardTemplateSubmitForValidationLivePort,
  cardTemplateRecordValidationLivePort,
  cardTemplateRejectLivePort,
  cardTemplatePublishLivePort,
  cardTemplateRetireLivePort,
  cardTemplateCreateNextVersionLivePort,
} from './live';

export type {
  CardTemplateStatus,
  CardTemplateElement,
  CardTemplateSnapshot,
  CardTemplateSnapshotRecord,
  CardTemplateLineageSummary,
  CardTemplateActionResult,
  CreateTemplateDraftInput,
  TemplateVersionInput,
  RecordTemplateValidationInput,
  RejectTemplateInput,
  CreateNextTemplateVersionInput,
} from './ports';

/**
 * The card-template lifecycle module's resolved ports (BIO-CRD-008-AC01-AC05). Pages/components
 * import these, never `./mock` or `./live` directly — switching
 * `NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE` (module-wide) or a per-port
 * `NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE_<PORTKEY>` override (`web/lib/feature-mode.ts`)
 * from SIMULATION to LIVE changes what these resolve to without any importer changing. Same
 * `CredentialService` servicePort `/card-designer` already declares - a new dedicated set of
 * keys, following `saveDesign`'s own precedent (BIO-UX-007), not a new module.
 */
export const cardTemplateCreateDraftPort = resolvePort(cardTemplateCreateDraftMockPort, cardTemplateCreateDraftLivePort, 'createTemplateDraft');
export const cardTemplateListLineagesPort = resolvePort(cardTemplateListLineagesMockPort, cardTemplateListLineagesLivePort, 'listTemplateLineages');
export const cardTemplateSubmitForValidationPort = resolvePort(cardTemplateSubmitForValidationMockPort, cardTemplateSubmitForValidationLivePort, 'submitTemplateForValidation');
export const cardTemplateRecordValidationPort = resolvePort(cardTemplateRecordValidationMockPort, cardTemplateRecordValidationLivePort, 'recordTemplateValidation');
export const cardTemplateRejectPort = resolvePort(cardTemplateRejectMockPort, cardTemplateRejectLivePort, 'rejectTemplate');
export const cardTemplatePublishPort = resolvePort(cardTemplatePublishMockPort, cardTemplatePublishLivePort, 'publishTemplate');
export const cardTemplateRetirePort = resolvePort(cardTemplateRetireMockPort, cardTemplateRetireLivePort, 'retireTemplate');
export const cardTemplateCreateNextVersionPort = resolvePort(cardTemplateCreateNextVersionMockPort, cardTemplateCreateNextVersionLivePort, 'createNextTemplateVersion');
