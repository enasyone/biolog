import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * Card-template lifecycle ports (BIO-CRD-008-AC01-AC05), following the exact precedent
 * `features/reporting/ports.ts` (BIO-RPT-002) already established: a module with more than one
 * real action gets one independently-resolvable port per action (each switchable on its own
 * `NEXT_PUBLIC_FEATURE_MODE_CREDENTIALSERVICE_*` override), all under the SAME `CredentialService`
 * servicePort `/card-designer` already declares (`route-manifest.ts`) - not a new module.
 *
 * These are new, dedicated keys alongside `CredentialService`'s existing nine (BIO-UX-007) plus
 * `saveDesign` (also BIO-UX-007, BIO-CRD-008-AC05's own stable seam - see `SaveDesignBar.tsx`'s
 * header). `saveDesign` itself is left untouched; this story's own `TemplateLifecycleBar` uses
 * these new ports instead, the same "new dedicated ports, not a route fight" shape BIO-RPT-002
 * established for its own conflict with BIO-RPT-001.
 *
 * BIO-CRD-009-AC04: `kind: 'photo'` is a purely additive literal added to the union below so a
 * placed `PhotoElement` (`web/features/bio-crd-009/photoElement.ts`) can be captured into
 * `frontElements`/`backElements` the same way a `LogoElement` already is (`kind: 'image'`) -
 * mirrors `api/features/bio-crd-008/Domain/CardTemplateElement.cs`'s own additive
 * `CardTemplateElementKind.Photo` enum member exactly. No existing consumer of this union breaks
 * (widening a string-literal union is non-breaking to every prior switch/map over it).
 */

export type CardTemplateStatus = 'draft' | 'validation' | 'approval' | 'published' | 'retired';

export interface CardTemplateElement {
  id: string;
  kind: 'text' | 'image' | 'watermark' | 'photo';
  label: string;
  xPx: number;
  yPx: number;
  widthPx: number;
  heightPx: number;
  rotationDeg?: number;
  zIndex: number;
  locked?: boolean;
  fontFamily?: string;
  fontSizePx?: number;
  text?: string;
}

export interface CardTemplateSnapshot {
  formatKind: 'cr80' | 'cr79' | 'cr100' | 'custom';
  orientation: 'portrait' | 'landscape';
  customSizeMm?: { longEdgeMm: number; shortEdgeMm: number };
  frontElements: CardTemplateElement[];
  backElements: CardTemplateElement[];
  watermark: 'none' | 'staff' | 'student' | 'visitor';
  photo: { required: boolean; aspectRatio?: string; minWidthPx?: number; minHeightPx?: number };
  barcode: 'none' | 'alphanumeric' | 'qr' | 'pdf417' | 'data_matrix' | 'aztec';
  signature: { required: boolean; lineageId?: string; version?: number };
}

export interface CardTemplateSnapshotRecord {
  templateId: string;
  version: number;
  revision: number;
  status: CardTemplateStatus;
  name: string;
  snapshot: CardTemplateSnapshot;
  createdAt: string;
  updatedAt: string;
  publishedAt?: string;
}

export interface CardTemplateLineageSummary {
  templateId: string;
  name: string;
  latestVersion: number;
  latestStatus: CardTemplateStatus;
  publishedVersion?: number;
}

export interface CardTemplateActionResult extends PrimaryActionResult {
  template?: CardTemplateSnapshotRecord;
}

export interface CreateTemplateDraftInput {
  name: string;
  snapshot: CardTemplateSnapshot;
}
export interface CreateTemplateDraftPort extends ModulePort {
  primaryAction(input?: CreateTemplateDraftInput): Promise<CardTemplateActionResult>;
}

export interface ListTemplateLineagesResult extends PrimaryActionResult {
  lineages?: CardTemplateLineageSummary[];
}
export interface ListTemplateLineagesPort extends ModulePort {
  primaryAction(): Promise<ListTemplateLineagesResult>;
}

export interface TemplateVersionInput {
  templateId: string;
  version: number;
  expectedRevision: number;
}
export interface SubmitTemplateForValidationPort extends ModulePort {
  primaryAction(input?: TemplateVersionInput): Promise<CardTemplateActionResult>;
}

export interface RecordTemplateValidationInput extends TemplateVersionInput {
  passed: boolean;
  note?: string;
}
export interface RecordTemplateValidationPort extends ModulePort {
  primaryAction(input?: RecordTemplateValidationInput): Promise<CardTemplateActionResult>;
}

export interface RejectTemplateInput extends TemplateVersionInput {
  note?: string;
}
export interface RejectTemplatePort extends ModulePort {
  primaryAction(input?: RejectTemplateInput): Promise<CardTemplateActionResult>;
}

export interface PublishTemplatePort extends ModulePort {
  primaryAction(input?: TemplateVersionInput): Promise<CardTemplateActionResult>;
}

export interface RetireTemplatePort extends ModulePort {
  primaryAction(input?: TemplateVersionInput): Promise<CardTemplateActionResult>;
}

export interface CreateNextTemplateVersionInput {
  templateId: string;
  fromVersion: number;
}
export interface CreateNextTemplateVersionPort extends ModulePort {
  primaryAction(input?: CreateNextTemplateVersionInput): Promise<CardTemplateActionResult>;
}
