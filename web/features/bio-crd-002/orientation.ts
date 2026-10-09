import type { CanvasElement } from './elements';

/**
 * BIO-CRD-002-AC02: "landscape/portrait switch preserves geometry without cumulative drift."
 *
 * `switchOrientation` is a real 90° rotation of the canvas and every element on it - not an
 * approximation. It uses only addition/subtraction against the canvas's own current pixel
 * dimensions, never a trigonometric rotation matrix (`sin`/`cos` of 90° are exactly 0/±1 in
 * theory, but computed in IEEE-754 floating point they are not exactly 0/±1 in practice, and
 * repeated multiplication by that near-zero/near-one noise is exactly the "cumulative drift"
 * this AC forbids). Because the transform is exact integer/rational arithmetic, switching
 * orientation twice reproduces the ORIGINAL state bit-for-bit, not merely "close enough" -
 * `orientation.test.ts` asserts exact equality, not a tolerance.
 *
 * The two directions use different (not identical) formulas - landscape->portrait and
 * portrait->landscape are each other's exact algebraic inverse, verified by direct derivation
 * (see the story's builder evidence for the worked proof). A single shared formula applied both
 * ways would produce a 180° rotation on the second switch, not a return to the original layout,
 * which is what a designer actually expects from a one-click "switch back" action.
 */

export type Orientation = 'landscape' | 'portrait';

export interface CardCanvasSide {
  readonly widthPx: number;
  readonly heightPx: number;
  readonly elements: readonly CanvasElement[];
}

function normaliseDegrees(deg: number): number {
  const wrapped = deg % 360;
  return wrapped < 0 ? wrapped + 360 : wrapped;
}

function rotateElementLandscapeToPortrait(element: CanvasElement, currentWidthPx: number): CanvasElement {
  return {
    ...element,
    xPx: element.yPx,
    yPx: currentWidthPx - element.xPx - element.widthPx,
    widthPx: element.heightPx,
    heightPx: element.widthPx,
    rotationDeg: normaliseDegrees(element.rotationDeg - 90),
  };
}

function rotateElementPortraitToLandscape(element: CanvasElement, currentHeightPx: number): CanvasElement {
  return {
    ...element,
    xPx: currentHeightPx - element.yPx - element.heightPx,
    yPx: element.xPx,
    widthPx: element.heightPx,
    heightPx: element.widthPx,
    rotationDeg: normaliseDegrees(element.rotationDeg + 90),
  };
}

/** Switches a single side (front or back) between landscape and portrait. `currentOrientation` names which orientation `side` is in NOW - the direction of the switch, not merely "toggle," is what makes this exactly reversible. */
export function switchOrientation(side: CardCanvasSide, currentOrientation: Orientation): CardCanvasSide {
  const rotate = currentOrientation === 'landscape'
    ? (e: CanvasElement) => rotateElementLandscapeToPortrait(e, side.widthPx)
    : (e: CanvasElement) => rotateElementPortraitToLandscape(e, side.heightPx);

  return {
    widthPx: side.heightPx,
    heightPx: side.widthPx,
    elements: side.elements.map(rotate),
  };
}

export function otherOrientation(orientation: Orientation): Orientation {
  return orientation === 'landscape' ? 'portrait' : 'landscape';
}
