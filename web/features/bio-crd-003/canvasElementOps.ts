/**
 * Generic move/resize/rotate/lock/layer operations for BIO-CRD-003's own element types
 * (`NameFieldElement`, `WatermarkElement`). Deliberately a fresh, independent copy of the same
 * logic `web/features/bio-crd-002/elements.ts` already implements for its own generic
 * `CanvasElement` - BIO-CRD-003 does not modify BIO-CRD-002's own files (outside this story's
 * declared write scope, `api`/`web`/`tests`/`docs`/`evidence`/`mobile`+`features/bio-crd-003/**`
 * only), so each feature project/directory owns its own small primitives rather than reaching
 * into a sibling story's, the same convention every backend feature project in this repository
 * already follows for an identical reason.
 *
 * Generic over `T extends BaseElement` so BOTH `NameFieldElement` and `WatermarkElement` share
 * ONE copy of this logic within BIO-CRD-003 itself - only the cross-story boundary is
 * duplicated, not this story's own two element kinds against each other.
 */

export interface BaseElement {
  readonly id: string;
  readonly xPx: number;
  readonly yPx: number;
  readonly widthPx: number;
  readonly heightPx: number;
  /** Degrees, normalised to [0, 360). */
  readonly rotationDeg: number;
  readonly zIndex: number;
  readonly locked: boolean;
}

/** Exported so any element-creation function can normalise a starting rotation the same way every mutation already does - the "always normalised to [0, 360)" invariant on `BaseElement.rotationDeg` must hold from construction, not just after the first `rotateElement` call. */
export function normaliseDegrees(deg: number): number {
  const wrapped = deg % 360;
  return wrapped < 0 ? wrapped + 360 : wrapped;
}

export function moveElement<T extends BaseElement>(element: T, dxPx: number, dyPx: number): T {
  if (element.locked) return element;
  return { ...element, xPx: element.xPx + dxPx, yPx: element.yPx + dyPx };
}

export function resizeElement<T extends BaseElement>(element: T, widthPx: number, heightPx: number): T {
  if (element.locked) return element;
  if (widthPx <= 0 || heightPx <= 0) {
    throw new RangeError('width and height must both be positive');
  }
  return { ...element, widthPx, heightPx };
}

/** BIO-CRD-003-AC04: rotation, for a watermark or any other element - inherited from this shared base, not reimplemented per element kind. */
export function rotateElement<T extends BaseElement>(element: T, deltaDeg: number): T {
  if (element.locked) return element;
  return { ...element, rotationDeg: normaliseDegrees(element.rotationDeg + deltaDeg) };
}

export function setLocked<T extends BaseElement>(element: T, locked: boolean): T {
  return { ...element, locked };
}

function reindexed<T extends BaseElement>(elements: readonly T[]): T[] {
  return elements.map((element, index) => ({ ...element, zIndex: index }));
}

/** BIO-CRD-003-AC04: layer control, for a watermark or any other element. */
export function bringToFront<T extends BaseElement>(elements: readonly T[], id: string): T[] {
  const target = elements.find((e) => e.id === id);
  if (!target) return [...elements];
  return reindexed([...elements.filter((e) => e.id !== id), target]);
}

export function sendToBack<T extends BaseElement>(elements: readonly T[], id: string): T[] {
  const target = elements.find((e) => e.id === id);
  if (!target) return [...elements];
  return reindexed([target, ...elements.filter((e) => e.id !== id)]);
}

export function bringForward<T extends BaseElement>(elements: readonly T[], id: string): T[] {
  const ordered = [...elements].sort((a, b) => a.zIndex - b.zIndex);
  const index = ordered.findIndex((e) => e.id === id);
  if (index === -1 || index === ordered.length - 1) return reindexed(ordered);
  [ordered[index], ordered[index + 1]] = [ordered[index + 1]!, ordered[index]!];
  return reindexed(ordered);
}

export function sendBackward<T extends BaseElement>(elements: readonly T[], id: string): T[] {
  const ordered = [...elements].sort((a, b) => a.zIndex - b.zIndex);
  const index = ordered.findIndex((e) => e.id === id);
  if (index <= 0) return reindexed(ordered);
  [ordered[index], ordered[index - 1]] = [ordered[index - 1]!, ordered[index]!];
  return reindexed(ordered);
}
