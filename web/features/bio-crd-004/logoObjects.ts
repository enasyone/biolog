/**
 * A logo card object (named in the user story alongside barcode/QR/signature; no AC tests it
 * independently of AC05's conditional-visibility requirement, which applies uniformly to every
 * card object this story defines). `content` is an opaque asset reference string, never resolved
 * or validated here - the same "BIO-CRD-001 owns real asset validation" boundary BIO-CRD-003's
 * own watermark image content already established.
 */
import { normaliseDegrees, type BaseElement } from './canvasElementOps';
import { ALWAYS_VISIBLE, type VisibilityRule } from './visibility';

export interface LogoElement extends BaseElement {
  readonly elementKind: 'logo';
  readonly content: string;
  readonly visibilityRule: VisibilityRule;
}

export function isLogoElement(element: BaseElement): element is LogoElement {
  return (element as { elementKind?: unknown }).elementKind === 'logo';
}

export function createLogoElement(
  id: string,
  content: string,
  position: { xPx: number; yPx: number; widthPx: number; heightPx: number },
  zIndex: number,
): LogoElement {
  return {
    elementKind: 'logo',
    id,
    content,
    visibilityRule: ALWAYS_VISIBLE,
    xPx: position.xPx,
    yPx: position.yPx,
    widthPx: position.widthPx,
    heightPx: position.heightPx,
    rotationDeg: normaliseDegrees(0),
    zIndex,
    locked: false,
  };
}

export function setContent(element: LogoElement, content: string): LogoElement {
  return { ...element, content };
}

export function setVisibilityRule(element: LogoElement, visibilityRule: VisibilityRule): LogoElement {
  return { ...element, visibilityRule };
}
