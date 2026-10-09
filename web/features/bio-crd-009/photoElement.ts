/**
 * BIO-CRD-009-AC01: a real, positionable photo element on the card designer canvas - extends
 * the SHARED `BaseElement` from `web/features/bio-crd-003/canvasElementOps.ts` (not a fresh
 * per-feature copy like BIO-CRD-004's own `canvasElementOps.ts` forked) because the story's own
 * approved AC01 text names that exact file as the base to extend; move/resize/rotate/layer/lock
 * are inherited from it unchanged, no new canvas mechanics invented here.
 *
 * BIO-CRD-009-AC02: `content` is an opaque reference string only - never resolved, uploaded or
 * validated here. BIO-PHO-001 owns real photo capture/bytes; this mirrors `LogoElement`/
 * `WatermarkElement`'s own identical boundary exactly (see `bio-crd-004/logoObjects.ts`,
 * `bio-crd-003/watermarks.ts`).
 */
import { normaliseDegrees, type BaseElement } from '../bio-crd-003/canvasElementOps';

export interface PhotoElement extends BaseElement {
  readonly elementKind: 'photo';
  /** An opaque reference string (e.g. a future BIO-PHO-001 asset id) - never resolved or validated here. */
  readonly content: string;
}

export function isPhotoElement(element: BaseElement): element is PhotoElement {
  return (element as { elementKind?: unknown }).elementKind === 'photo';
}

export function createPhotoElement(
  id: string,
  content: string,
  position: { xPx: number; yPx: number; widthPx: number; heightPx: number },
  zIndex: number,
  rotationDeg = 0,
): PhotoElement {
  return {
    elementKind: 'photo',
    id,
    content,
    xPx: position.xPx,
    yPx: position.yPx,
    widthPx: position.widthPx,
    heightPx: position.heightPx,
    rotationDeg: normaliseDegrees(rotationDeg),
    zIndex,
    locked: false,
  };
}

export function setContent(element: PhotoElement, content: string): PhotoElement {
  return { ...element, content };
}
