// Developer tests for BIO-CRD-002-AC02. SYNTHETIC: no real card designs.
import { describe, expect, it } from 'vitest';
import { createElement } from '../../../web/features/bio-crd-002/elements';
import { switchOrientation, otherOrientation, type CardCanvasSide } from '../../../web/features/bio-crd-002/orientation';

const SIDE: CardCanvasSide = {
  widthPx: 1011, // ~85.6mm at 300dpi, rounded - deliberately not a "nice" number, to stress rounding
  heightPx: 638, // ~53.98mm at 300dpi
  elements: [
    createElement({ id: 'a', label: 'Photo', xPx: 40, yPx: 30, widthPx: 200, heightPx: 260, zIndex: 0 }),
    createElement({ id: 'b', label: 'Name', xPx: 260, yPx: 40, widthPx: 400, heightPx: 60, zIndex: 1, rotationDeg: 15 }),
    createElement({ id: 'c', label: 'Locked logo', xPx: 800, yPx: 500, widthPx: 100, heightPx: 100, zIndex: 2, locked: true }),
  ],
};

describe('BIO-CRD-002-AC02 landscape/portrait switch preserves geometry without cumulative drift', () => {
  it('swaps the canvas width and height', () => {
    const portrait = switchOrientation(SIDE, 'landscape');
    expect(portrait.widthPx).toBe(SIDE.heightPx);
    expect(portrait.heightPx).toBe(SIDE.widthPx);
  });

  it('every element keeps its own width/height swapped and its rotation shifted by exactly 90 degrees', () => {
    const portrait = switchOrientation(SIDE, 'landscape');
    for (const [before, after] of zip(SIDE.elements, portrait.elements)) {
      expect(after.widthPx).toBe(before.heightPx);
      expect(after.heightPx).toBe(before.widthPx);
      expect(after.rotationDeg).toBe((((before.rotationDeg - 90) % 360) + 360) % 360);
    }
  });

  it('switching landscape -> portrait -> landscape reproduces the EXACT original state, not merely an approximately equal one', () => {
    const portrait = switchOrientation(SIDE, 'landscape');
    const backToLandscape = switchOrientation(portrait, 'portrait');

    expect(backToLandscape).toEqual(SIDE);
  });

  it('switching orientation 50 times (25 round trips) never drifts from the original state', () => {
    let side = SIDE;
    let orientation: 'landscape' | 'portrait' = 'landscape';
    for (let i = 0; i < 50; i += 1) {
      side = switchOrientation(side, orientation);
      orientation = otherOrientation(orientation);
    }

    // 50 switches is an even number of switches, so this must land back on the exact original.
    expect(side).toEqual(SIDE);
  });

  it('an odd number of switches lands on portrait with dimensions swapped, and is itself exactly reversible', () => {
    const portrait = switchOrientation(SIDE, 'landscape');
    const landscapeAgain = switchOrientation(portrait, 'portrait');
    const portraitAgain = switchOrientation(landscapeAgain, 'landscape');

    expect(portraitAgain).toEqual(portrait);
  });

  it('a locked element still has its geometry transformed by the switch - lock blocks move/resize/rotate operations, not the orientation switch', () => {
    const portrait = switchOrientation(SIDE, 'landscape');
    const lockedBefore = SIDE.elements.find((e) => e.id === 'c')!;
    const lockedAfter = portrait.elements.find((e) => e.id === 'c')!;
    expect(lockedAfter.widthPx).toBe(lockedBefore.heightPx);
    expect(lockedAfter.locked).toBe(true);
  });

  it('uses no floating-point rotation matrix - the transform is exact even for non-round pixel dimensions', () => {
    // A regression guard against reintroducing sin/cos: exact integer inputs must produce
    // exact integer outputs, never a value with residual floating-point noise.
    const oddSide: CardCanvasSide = {
      widthPx: 1013,
      heightPx: 641,
      elements: [createElement({ id: 'x', label: 'Field', xPx: 17, yPx: 23, widthPx: 97, heightPx: 53, zIndex: 0 })],
    };
    const result = switchOrientation(oddSide, 'landscape');
    for (const value of [result.widthPx, result.heightPx, result.elements[0]!.xPx, result.elements[0]!.yPx]) {
      expect(Number.isInteger(value)).toBe(true);
    }
  });
});

function zip<A, B>(a: readonly A[], b: readonly B[]): Array<[A, B]> {
  return a.map((value, index) => [value, b[index] as B]);
}
