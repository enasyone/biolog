// Developer tests for BIO-CRD-002-AC04. SYNTHETIC: no real card designs.
import { describe, expect, it } from 'vitest';
import {
  bringForward,
  bringToFront,
  createElement,
  moveElement,
  resizeElement,
  rotateElement,
  sendBackward,
  sendToBack,
  setLocked,
} from '../../../web/features/bio-crd-002/elements';

function el(id: string, zIndex: number, overrides: Partial<Parameters<typeof createElement>[0]> = {}) {
  return createElement({ id, label: id, xPx: 0, yPx: 0, widthPx: 50, heightPx: 50, zIndex, ...overrides });
}

describe('BIO-CRD-002-AC04 move', () => {
  it('moves an unlocked element by the given delta', () => {
    const moved = moveElement(el('a', 0, { xPx: 10, yPx: 10 }), 5, -3);
    expect(moved.xPx).toBe(15);
    expect(moved.yPx).toBe(7);
  });

  it('never mutates the original element', () => {
    const original = el('a', 0, { xPx: 10, yPx: 10 });
    moveElement(original, 5, 5);
    expect(original.xPx).toBe(10);
    expect(original.yPx).toBe(10);
  });

  it('refuses to move a locked element', () => {
    const locked = el('a', 0, { xPx: 10, yPx: 10, locked: true });
    expect(moveElement(locked, 5, 5)).toBe(locked);
  });
});

describe('BIO-CRD-002-AC04 resize', () => {
  it('resizes an unlocked element', () => {
    const resized = resizeElement(el('a', 0), 120, 80);
    expect(resized.widthPx).toBe(120);
    expect(resized.heightPx).toBe(80);
  });

  it('refuses to resize a locked element', () => {
    const locked = el('a', 0, { locked: true });
    expect(resizeElement(locked, 999, 999)).toBe(locked);
  });

  it('rejects a non-positive width or height', () => {
    expect(() => resizeElement(el('a', 0), 0, 10)).toThrow(RangeError);
    expect(() => resizeElement(el('a', 0), 10, -1)).toThrow(RangeError);
  });
});

describe('BIO-CRD-002-AC04 rotate', () => {
  it('rotates by the given delta, normalised to [0, 360)', () => {
    expect(rotateElement(el('a', 0, { rotationDeg: 350 }), 20).rotationDeg).toBe(10);
    expect(rotateElement(el('a', 0, { rotationDeg: 10 }), -20).rotationDeg).toBe(350);
  });

  it('refuses to rotate a locked element', () => {
    const locked = el('a', 0, { locked: true, rotationDeg: 0 });
    expect(rotateElement(locked, 45)).toBe(locked);
  });
});

describe('BIO-CRD-002-AC04 lock', () => {
  it('locks and unlocks regardless of current state', () => {
    const unlocked = el('a', 0);
    const locked = setLocked(unlocked, true);
    expect(locked.locked).toBe(true);
    expect(setLocked(locked, false).locked).toBe(false);
  });

  it('locking an already-locked element is a harmless no-op, never refused', () => {
    const locked = el('a', 0, { locked: true });
    expect(setLocked(locked, true).locked).toBe(true);
  });
});

describe('BIO-CRD-002-AC04 layer ordering', () => {
  const stack = [el('a', 0), el('b', 1), el('c', 2)];

  it('bringToFront moves the element to the highest zIndex, preserving the relative order of the rest', () => {
    const result = bringToFront(stack, 'a');
    expect(result.map((e) => e.id)).toEqual(['b', 'c', 'a']);
    expect(result.map((e) => e.zIndex)).toEqual([0, 1, 2]);
  });

  it('sendToBack moves the element to the lowest zIndex', () => {
    const result = sendToBack(stack, 'c');
    expect(result.map((e) => e.id)).toEqual(['c', 'a', 'b']);
  });

  it('bringForward swaps with the next element up, and is a no-op at the top', () => {
    expect(bringForward(stack, 'a').map((e) => e.id)).toEqual(['b', 'a', 'c']);
    expect(bringForward(stack, 'c').map((e) => e.id)).toEqual(['a', 'b', 'c']);
  });

  it('sendBackward swaps with the next element down, and is a no-op at the bottom', () => {
    expect(sendBackward(stack, 'c').map((e) => e.id)).toEqual(['a', 'c', 'b']);
    expect(sendBackward(stack, 'a').map((e) => e.id)).toEqual(['a', 'b', 'c']);
  });

  it('every layer operation returns a freshly reindexed array, never mutating the input', () => {
    const before = stack.map((e) => e.zIndex);
    bringToFront(stack, 'a');
    expect(stack.map((e) => e.zIndex)).toEqual(before);
  });
});
