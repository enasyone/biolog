'use client';

import { useEffect, useState } from 'react';
import { StatusBadge, type StatusBadgeTone } from '../../components/status-badge';
import type { CardDesignState } from '../bio-crd-002/cardDesignState';
import { isNameFieldElement, type NameFieldElement } from '../bio-crd-003/nameFields';
import { isWatermarkElement, WATERMARK_PRESETS, type WatermarkElement, type WatermarkPresetKind } from '../bio-crd-003/watermarks';
import { isBarcodeElement, type BarcodeElement, type BarcodeProfile } from '../bio-crd-004/barcodeObjects';
import { isSignatureElement, type SignatureElement } from '../bio-crd-004/signatureObjects';
import { isLogoElement, type LogoElement } from '../bio-crd-004/logoObjects';
import type { PhotoDesignState } from '../bio-crd-009/photoDesignState';
import {
  cardTemplateCreateDraftPort,
  cardTemplateCreateNextVersionPort,
  cardTemplateListLineagesPort,
  cardTemplatePublishPort,
  cardTemplateRecordValidationPort,
  cardTemplateRejectPort,
  cardTemplateRetirePort,
  cardTemplateSubmitForValidationPort,
} from '.';
import type { CardTemplateActionResult, CardTemplateElement, CardTemplateLineageSummary, CardTemplateSnapshot, CardTemplateSnapshotRecord } from './ports';

type ActionState = { status: 'idle' } | { status: 'loading' } | ({ status: 'done' } & CardTemplateActionResult);

const STATUS_TONE: Readonly<Record<string, StatusBadgeTone>> = {
  draft: 'neutral',
  validation: 'info',
  approval: 'warning',
  published: 'success',
  retired: 'neutral',
};

const BARCODE_WIRE: Readonly<Record<BarcodeProfile, CardTemplateSnapshot['barcode']>> = {
  alphanumeric: 'alphanumeric',
  qr: 'qr',
  pdf417: 'pdf417',
  dataMatrix: 'data_matrix',
  aztec: 'aztec',
};

function watermarkKindFor(content: string): CardTemplateSnapshot['watermark'] {
  const found = (Object.keys(WATERMARK_PRESETS) as WatermarkPresetKind[]).find((k) => WATERMARK_PRESETS[k].text === content);
  return found ?? 'none';
}

function toTemplateElement(e: { id: string; xPx: number; yPx: number; widthPx: number; heightPx: number; rotationDeg: number; zIndex: number; locked: boolean }, kind: CardTemplateElement['kind'], label: string, extra?: Partial<CardTemplateElement>): CardTemplateElement {
  return { id: e.id, kind, label, xPx: e.xPx, yPx: e.yPx, widthPx: e.widthPx, heightPx: e.heightPx, rotationDeg: e.rotationDeg, zIndex: e.zIndex, locked: e.locked, ...extra };
}

/**
 * BIO-CRD-008-AC03: builds one governed snapshot out of the THREE independent canvases
 * (BIO-CRD-002/003/004) this route already mounts side by side. Until a later story merges them
 * into one true front/back surface, this story places every BIO-CRD-003/004 element on the
 * FRONT side and BIO-CRD-002's own front/back elements as-is - a disclosed simplification (see
 * docs/features/bio-crd-008/README.md "Building one snapshot from three canvases"), not a hidden
 * data-loss bug: every field AC03 names is still captured, just not yet per-side for the two
 * newer canvases.
 *
 * BIO-CRD-009-AC04: `photoElements` is a genuine, real inclusion (not a HYBRID boundary) - unlike
 * BIO-CRD-003/004, BIO-CRD-009's own `CardPhotoApp` already splits by front/back itself
 * (`PhotoDesignState`), so `photoElements.front`/`.back` map straight onto
 * `frontElements`/`backElements` with `kind: 'photo'`, the same way a `LogoElement` already maps
 * to `kind: 'image'`. The pre-existing `photo` parameter is untouched - it stays the
 * `TemplateLifecycleBar`-owned "requires a photo" spec checkbox (metadata, BIO-CRD-008's own
 * concern), while `photoElements` is the actual placed, positioned photo(s) BIO-CRD-009 adds.
 * `photoElements` defaults to empty so every pre-existing caller (every test written before this
 * story) is unaffected - the same additive-optional-prop shape every sibling canvas prop here
 * already uses.
 */
export function buildSnapshot(
  design: CardDesignState,
  typography: readonly (NameFieldElement | WatermarkElement)[],
  objects: readonly (BarcodeElement | SignatureElement | LogoElement)[],
  photo: CardTemplateSnapshot['photo'],
  photoElements: PhotoDesignState = { front: [], back: [] },
): CardTemplateSnapshot {
  const designFront = design.front.elements.map((e) => toTemplateElement(e, 'image', e.label));
  const designBack = design.back.elements.map((e) => toTemplateElement(e, 'image', e.label));

  const typographyFront = typography.map((e) => {
    if (isNameFieldElement(e)) {
      return toTemplateElement(e, 'text', `Name field (${e.binding})`, { fontFamily: e.typography.fontFamily, fontSizePx: e.typography.fontSizePt, text: `{{${e.binding}}}` });
    }
    return toTemplateElement(e, 'watermark', 'Watermark', { text: e.content });
  });

  const logoFront = objects.filter(isLogoElement).map((e) => toTemplateElement(e, 'image', `Logo (${e.content})`, { text: e.content }));
  const photoFront = photoElements.front.map((e) => toTemplateElement(e, 'photo', `Photo (${e.content})`, { text: e.content }));
  const photoBack = photoElements.back.map((e) => toTemplateElement(e, 'photo', `Photo (${e.content})`, { text: e.content }));

  const barcodeElement = objects.find(isBarcodeElement);
  const signatureElement = objects.find(isSignatureElement);
  const watermarkElement = typography.find(isWatermarkElement);

  return {
    formatKind: design.formatKind,
    orientation: design.orientation,
    customSizeMm: design.customSizeMm ? { longEdgeMm: design.customSizeMm.widthMm, shortEdgeMm: design.customSizeMm.heightMm } : undefined,
    frontElements: [...designFront, ...typographyFront, ...logoFront, ...photoFront],
    backElements: [...designBack, ...photoBack],
    watermark: watermarkElement ? watermarkKindFor(watermarkElement.content) : 'none',
    photo,
    barcode: barcodeElement ? BARCODE_WIRE[barcodeElement.profile] : 'none',
    signature: signatureElement ? { required: true, lineageId: signatureElement.lineageId, version: signatureElement.version } : { required: false },
  };
}

export interface TemplateLifecycleBarProps {
  designState: CardDesignState;
  typographyElements: readonly (NameFieldElement | WatermarkElement)[];
  objectElements: readonly (BarcodeElement | SignatureElement | LogoElement)[];
  /** BIO-CRD-009-AC04: additive, optional - see `buildSnapshot`'s own header for the full rationale. */
  photoElements?: PhotoDesignState;
}

/**
 * BIO-CRD-008-AC01-AC05: the real Draft->Validation->Approval->Published->Retired lifecycle,
 * connected through `CredentialService`'s own new dedicated ports (`./index.ts`). Mounted on
 * `/card-designer` alongside (not replacing) BIO-UX-007's own `SaveDesignBar` - that component
 * stays exactly as it was, still backing `route-manifest.ts`'s own "Save design" primary action;
 * this is a genuinely new, additional set of actions for this story's own lifecycle.
 */
export function TemplateLifecycleBar({ designState, typographyElements, objectElements, photoElements }: TemplateLifecycleBarProps) {
  const [name, setName] = useState('');
  const [photoRequired, setPhotoRequired] = useState(false);
  const [lineages, setLineages] = useState<CardTemplateLineageSummary[]>([]);
  const [current, setCurrent] = useState<CardTemplateSnapshotRecord | null>(null);
  const [action, setAction] = useState<ActionState>({ status: 'idle' });

  async function refreshLineages() {
    const result = await cardTemplateListLineagesPort.primaryAction();
    if (result.outcome === 'success' && result.lineages) setLineages(result.lineages);
  }

  useEffect(() => {
    void refreshLineages();
  }, []);

  function run(promise: Promise<CardTemplateActionResult>) {
    setAction({ status: 'loading' });
    void promise.then((result) => {
      setAction({ status: 'done', ...result });
      if (result.template) setCurrent(result.template);
      void refreshLineages();
    });
  }

  function createDraft() {
    if (!name.trim()) return;
    const snapshot = buildSnapshot(designState, typographyElements, objectElements, { required: photoRequired, aspectRatio: photoRequired ? '3:4' : undefined }, photoElements);
    run(cardTemplateCreateDraftPort.primaryAction({ name, snapshot }));
  }

  function submit() {
    if (!current) return;
    run(cardTemplateSubmitForValidationPort.primaryAction({ templateId: current.templateId, version: current.version, expectedRevision: current.revision }));
  }

  function recordValidation(passed: boolean) {
    if (!current) return;
    run(cardTemplateRecordValidationPort.primaryAction({ templateId: current.templateId, version: current.version, expectedRevision: current.revision, passed }));
  }

  function reject() {
    if (!current) return;
    run(cardTemplateRejectPort.primaryAction({ templateId: current.templateId, version: current.version, expectedRevision: current.revision }));
  }

  function publish() {
    if (!current) return;
    run(cardTemplatePublishPort.primaryAction({ templateId: current.templateId, version: current.version, expectedRevision: current.revision }));
  }

  function retire() {
    if (!current) return;
    run(cardTemplateRetirePort.primaryAction({ templateId: current.templateId, version: current.version, expectedRevision: current.revision }));
  }

  function createNextVersion() {
    if (!current) return;
    run(cardTemplateCreateNextVersionPort.primaryAction({ templateId: current.templateId, fromVersion: current.version }));
  }

  return (
    <section className="biolog-card" aria-label="Card template lifecycle" data-testid="template-lifecycle-bar">
      <h2>Publish this design</h2>
      <p>
        Draft → Validation → Approval → Published → Retired, with immutable published versions
        (BIO-CRD-008).{' '}
        <StatusBadge tone={cardTemplateCreateDraftPort.mode === 'live' ? 'success' : 'neutral'}>
          {cardTemplateCreateDraftPort.mode === 'live' ? 'LIVE' : 'SIMULATION'}
        </StatusBadge>
      </p>

      <div className="biolog-field-row">
        <label htmlFor="template-lifecycle-name">Template name</label>
        <input id="template-lifecycle-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Staff ID v1" />
      </div>
      <div className="biolog-field-row">
        <label htmlFor="template-lifecycle-photo-required">Requires a photo</label>
        <input id="template-lifecycle-photo-required" type="checkbox" checked={photoRequired} onChange={(e) => setPhotoRequired(e.target.checked)} />
      </div>
      <button type="button" className="biolog-button-primary" disabled={!name.trim() || action.status === 'loading'} onClick={createDraft}>
        Create draft from current canvas
      </button>

      <div className="biolog-field-row">
        <label htmlFor="template-lifecycle-existing">Existing templates</label>
        <select
          id="template-lifecycle-existing"
          value={current ? `${current.templateId}:${current.version}` : ''}
          onChange={(e) => {
            const lineage = lineages.find((l) => e.target.value.startsWith(l.templateId));
            if (!lineage) return setCurrent(null);
            // The lineage summary alone is enough to drive the lifecycle buttons below; a real
            // load-by-version call would refine this further, out of scope for this control.
            setCurrent({
              templateId: lineage.templateId,
              version: lineage.latestVersion,
              revision: 1,
              status: lineage.latestStatus,
              name: lineage.name,
              snapshot: buildSnapshot(designState, typographyElements, objectElements, { required: photoRequired }, photoElements),
              createdAt: '',
              updatedAt: '',
            });
          }}
        >
          <option value="">Select a template…</option>
          {lineages.map((l) => (
            <option key={l.templateId} value={`${l.templateId}:${l.latestVersion}`}>
              {l.name} v{l.latestVersion} ({l.latestStatus}){l.publishedVersion ? ` — published v${l.publishedVersion}` : ''}
            </option>
          ))}
        </select>
      </div>

      {current && (
        <div role="status" aria-label="Selected template status">
          <p>
            {current.name} v{current.version} — <StatusBadge tone={STATUS_TONE[current.status] ?? 'neutral'}>{current.status}</StatusBadge>
          </p>

          {current.status === 'draft' && (
            <button type="button" disabled={action.status === 'loading'} onClick={submit}>
              Submit for validation
            </button>
          )}
          {current.status === 'validation' && (
            <>
              <button type="button" disabled={action.status === 'loading'} onClick={() => recordValidation(true)}>
                Validation passed
              </button>
              <button type="button" disabled={action.status === 'loading'} onClick={() => recordValidation(false)}>
                Validation failed
              </button>
            </>
          )}
          {current.status === 'approval' && (
            <>
              <button type="button" className="biolog-button-primary" disabled={action.status === 'loading'} onClick={publish}>
                Publish
              </button>
              <button type="button" className="biolog-button-secondary" disabled={action.status === 'loading'} onClick={reject}>
                Reject
              </button>
            </>
          )}
          {current.status === 'published' && (
            <>
              <button type="button" className="biolog-button-secondary" disabled={action.status === 'loading'} onClick={retire}>
                Retire
              </button>
              <button type="button" className="biolog-button-secondary" disabled={action.status === 'loading'} onClick={createNextVersion}>
                Create new version
              </button>
            </>
          )}
          {current.status === 'retired' && (
            <button type="button" className="biolog-button-secondary" disabled={action.status === 'loading'} onClick={createNextVersion}>
              Create new version
            </button>
          )}
        </div>
      )}

      {action.status === 'done' && <p role="alert">{action.message}</p>}
    </section>
  );
}
