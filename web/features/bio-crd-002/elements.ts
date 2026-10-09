/**
 * BIO-CRD-002-AC04: "elements support move/resize/rotate/layer/lock." Every operation here is a
 * pure function - given an element (or element array) and returns a NEW one, never mutates its
 * argument - so the canvas state (`cardDesignState.ts`) can be built out of these without any
 * hidden aliasing between front/back (AC05) or between renders.
 *
 * A locked element refuses move/resize/rotate outright (returns itself unchanged) - lock is
 * meaningless otherwise. Locking/unlocking itself is never refused, including on an already
 * locked element, because there must always be a way out of a locked state.
 */

export interface CanvasElement {
  readonly id: string;
  readonly label: string;
  readonly xPx: number;
  readonly yPx: number;
  readonly widthPx: number;
  readonly heightPx: number;
  /** Degrees, normalised to [0, 360). */
  readonly rotationDeg: number;
  readonly zIndex: number;
  readonly locked: boolean;
}

export function createElement(partial: Omit<CanvasElement, 'rotationDeg' | 'locked'> & Partial<Pick<CanvasElement, 'rotationDeg' | 'locked'>>): CanvasElement {
  return { rotationDeg: 0, locked: false, ...partial };
}

function normaliseDegrees(deg: number): number {
  const wrapped = deg % 360;
  return wrapped < 0 ? wrapped + 360 : wrapped;
}

export function moveElement(element: CanvasElement, dxPx: number, dyPx: number): CanvasElement {
  if (element.locked) return element;
  return { ...element, xPx: element.xPx + dxPx, yPx: element.yPx + dyPx };
}

export function resizeElement(element: CanvasElement, widthPx: number, heightPx: number): CanvasElement {
  if (element.locked) return element;
  if (widthPx <= 0 || heightPx <= 0) {
    throw new RangeError('width and height must both be positive');
  }
  return { ...element, widthPx, heightPx };
}

export function rotateElement(element: CanvasElement, deltaDeg: number): CanvasElement {
  if (element.locked) return element;
  return { ...element, rotationDeg: normaliseDegrees(element.rotationDeg + deltaDeg) };
}

export function setLocked(element: CanvasElement, locked: boolean): CanvasElement {
  return { ...element, locked };
}

/** Reassigns zIndex 0..n-1 by array order - rendering always sorts by zIndex, so this is the one place layer order is decided. */
function reindexed(elements: readonly CanvasElement[]): CanvasElement[] {
  return elements.map((element, index) => ({ ...element, zIndex: index }));
}

export function bringToFront(elements: readonly CanvasElement[], id: string): CanvasElement[] {
  const target = elements.find((e) => e.id === id);
  if (!target) return [...elements];
  return reindexed([...elements.filter((e) => e.id !== id), target]);
}

export function sendToBack(elements: readonly CanvasElement[], id: string): CanvasElement[] {
  const target = elements.find((e) => e.id === id);
  if (!target) return [...elements];
  return reindexed([target, ...elements.filter((e) => e.id !== id)]);
}

export function bringForward(elements: readonly CanvasElement[], id: string): CanvasElement[] {
  const ordered = [...elements].sort((a, b) => a.zIndex - b.zIndex);
  const index = ordered.findIndex((e) => e.id === id);
  if (index === -1 || index === ordered.length - 1) return reindexed(ordered);
  [ordered[index], ordered[index + 1]] = [ordered[index + 1]!, ordered[index]!];
  return reindexed(ordered);
}

export function sendBackward(elements: readonly CanvasElement[], id: string): CanvasElement[] {
  const ordered = [...elements].sort((a, b) => a.zIndex - b.zIndex);
  const index = ordered.findIndex((e) => e.id === id);
  if (index <= 0) return reindexed(ordered);
  [ordered[index], ordered[index - 1]] = [ordered[index - 1]!, ordered[index]!];
  return reindexed(ordered);
}
