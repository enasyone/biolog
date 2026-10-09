/**
 * BIO-CRD-003-AC04: "Text and image watermarks support 0-100% opacity, rotation and layer
 * control." BIO-CRD-003-AC05: "STAFF/STUDENT/VISITOR starter watermark behaviors are
 * available."
 *
 * Rotation and layer control are inherited from `canvasElementOps.ts`'s shared base - a
 * watermark is a `BaseElement` like any other, so `rotateElement`/`bringToFront`/etc. already
 * apply to it with no watermark-specific code. Opacity is watermark-specific (a generic
 * `CanvasElement`/`BaseElement` carries no opacity at all - only elements that render visible
 * content, like a watermark or a name field's own typography, need one).
 */
import { normaliseDegrees, type BaseElement } from './canvasElementOps';

export type WatermarkKind = 'text' | 'image';

/** Mirrors `api/features/bio-crd-003/Domain/WatermarkPresetKind.cs` - the backend catalogue is the source of truth this is fetched from; this local union pins the three starter categories for the frontend's own type-checked switches. */
export type WatermarkPresetKind = 'staff' | 'student' | 'visitor';

export interface WatermarkPreset {
  readonly text: string;
  readonly defaultOpacityPercent: number;
  readonly defaultRotationDeg: number;
}

/** BIO-CRD-003-AC05: mirrors `WatermarkPresetCatalog.cs` exactly - see that file's own header for why each value was chosen. No build step generates one from the other; `bio-crd-003-watermarks.test.ts` pins these values, the same "mirrored by hand, pinned by a test" pattern `web/lib/tokens.ts` and BIO-CRD-002's own `cardFormats.ts` already established. */
export const WATERMARK_PRESETS: Readonly<Record<WatermarkPresetKind, WatermarkPreset>> = Object.freeze({
  staff: Object.freeze({ text: 'STAFF', defaultOpacityPercent: 20, defaultRotationDeg: -30 }),
  student: Object.freeze({ text: 'STUDENT', defaultOpacityPercent: 20, defaultRotationDeg: -30 }),
  visitor: Object.freeze({ text: 'VISITOR', defaultOpacityPercent: 35, defaultRotationDeg: -30 }),
});

export interface WatermarkElement extends BaseElement {
  readonly elementKind: 'watermark';
  readonly watermarkKind: WatermarkKind;
  /** The watermark's own text, or an opaque reference to an image asset - never resolved or validated here (BIO-CRD-001 owns real asset validation; this story only positions and styles a watermark on the canvas). */
  readonly content: string;
  /** 0-100 inclusive. */
  readonly opacityPercent: number;
}

export function isWatermarkElement(element: BaseElement): element is WatermarkElement {
  return (element as { elementKind?: unknown }).elementKind === 'watermark';
}

function validateOpacity(opacityPercent: number): void {
  if (!Number.isFinite(opacityPercent) || opacityPercent < 0 || opacityPercent > 100) {
    throw new RangeError('opacityPercent must be between 0 and 100');
  }
}

export function createWatermarkElement(
  id: string,
  watermarkKind: WatermarkKind,
  content: string,
  position: { xPx: number; yPx: number; widthPx: number; heightPx: number },
  zIndex: number,
  opacityPercent = 100,
  rotationDeg = 0,
): WatermarkElement {
  validateOpacity(opacityPercent);
  return {
    elementKind: 'watermark',
    id,
    watermarkKind,
    content,
    opacityPercent,
    xPx: position.xPx,
    yPx: position.yPx,
    widthPx: position.widthPx,
    heightPx: position.heightPx,
    rotationDeg: normaliseDegrees(rotationDeg),
    zIndex,
    locked: false,
  };
}

/** BIO-CRD-003-AC05: a starter watermark from the fixed STAFF/STUDENT/VISITOR catalogue - a starting point, not locked; `setOpacity`/`rotateElement` still apply afterwards. */
export function createWatermarkFromPreset(
  id: string,
  presetKind: WatermarkPresetKind,
  position: { xPx: number; yPx: number; widthPx: number; heightPx: number },
  zIndex: number,
): WatermarkElement {
  const preset = WATERMARK_PRESETS[presetKind];
  return createWatermarkElement(id, 'text', preset.text, position, zIndex, preset.defaultOpacityPercent, preset.defaultRotationDeg);
}

export function setOpacity(element: WatermarkElement, opacityPercent: number): WatermarkElement {
  validateOpacity(opacityPercent);
  return { ...element, opacityPercent };
}

export function setContent(element: WatermarkElement, content: string): WatermarkElement {
  return { ...element, content };
}
