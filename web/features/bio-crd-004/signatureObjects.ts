/**
 * BIO-CRD-004-AC04: "Signature assets are versioned." A `SignatureElement` on the canvas
 * references a specific, already-appended version by `(lineageId, version)` - it never carries
 * signature content itself (the backend owns real asset bytes and versioning via
 * `api/features/bio-crd-004/Domain/SignatureAsset.cs`/`ISignatureAssetRepository`). Pinning a
 * specific version rather than "always the latest" is what makes a printed card's signature
 * reproducible even if a newer version is appended later - the same "reference a specific
 * version" trustworthiness BIO-PHO-001's own photo-assignment-by-version established.
 */
import { normaliseDegrees, type BaseElement } from './canvasElementOps';
import { ALWAYS_VISIBLE, type VisibilityRule } from './visibility';

export interface SignatureElement extends BaseElement {
  readonly elementKind: 'signature';
  readonly lineageId: string;
  readonly version: number;
  readonly visibilityRule: VisibilityRule;
}

export function isSignatureElement(element: BaseElement): element is SignatureElement {
  return (element as { elementKind?: unknown }).elementKind === 'signature';
}

export function createSignatureElement(
  id: string,
  lineageId: string,
  version: number,
  position: { xPx: number; yPx: number; widthPx: number; heightPx: number },
  zIndex: number,
): SignatureElement {
  if (version < 1) {
    throw new RangeError('a signature version starts at 1');
  }

  return {
    elementKind: 'signature',
    id,
    lineageId,
    version,
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

/** Repoints this element at a different (already-appended) version of the SAME signature lineage - never changes `lineageId`. */
export function setVersion(element: SignatureElement, version: number): SignatureElement {
  if (version < 1) {
    throw new RangeError('a signature version starts at 1');
  }
  return { ...element, version };
}

export function setVisibilityRule(element: SignatureElement, visibilityRule: VisibilityRule): SignatureElement {
  return { ...element, visibilityRule };
}
