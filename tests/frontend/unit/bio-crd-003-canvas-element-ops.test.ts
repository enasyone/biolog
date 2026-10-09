// Developer tests for the shared element ops BIO-CRD-003's own element kinds compose with.
// SYNTHETIC: no real card designs.
import { describe, expect, it } from 'vitest';
import { bringForward, moveElement, resizeElement, sendBackward, setLocked } from '../../../web/features/bio-crd-003/canvasElementOps';
import { createNameFieldElement } from '../../../web/features/bio-crd-003/nameFields';
import { createWatermarkElement } from '../../../web/features/bio-crd-003/watermarks';

const POSITION = { xPx: 10, yPx: 10, widthPx: 50, heightPx: 50 };

describe('shared element ops are generic across BIO-CRD-003s own element kinds', () => {
  it('moves a name field and preserves its binding/typography', () => {
    const field = createNameFieldElement('f1', 'firstName', POSITION, 0);
    const moved = moveElement(field, 5, -3);
    expect(moved.xPx).toBe(15);
    expect(moved.yPx).toBe(7);
    expect(moved.binding).toBe('firstName');
  });

  it('resizes a watermark and preserves its content/opacity', () => {
    const watermark = createWatermarkElement('w1', 'text', 'STAFF', POSITION, 0, 30);
    const resized = resizeElement(watermark, 400, 100);
    expect(resized.widthPx).toBe(400);
    expect(resized.content).toBe('STAFF');
    expect(resized.opacityPercent).toBe(30);
  });

  it('refuses to resize a locked element', () => {
    const field = setLocked(createNameFieldElement('f1', 'title', POSITION, 0), true);
    expect(resizeElement(field, 999, 999)).toBe(field);
  });

  it('rejects a non-positive width or height for any element kind', () => {
    const watermark = createWatermarkElement('w1', 'text', 'X', POSITION, 0);
    expect(() => resizeElement(watermark, 0, 10)).toThrow(RangeError);
  });

  it('layer reordering works identically for a mix of name fields and watermarks in the same array', () => {
    const field = createNameFieldElement('field', 'firstName', POSITION, 0);
    const watermark = createWatermarkElement('watermark', 'text', 'STAFF', POSITION, 1);
    const back = bringForward([field, watermark], 'field');
    expect(back.map((e) => e.id)).toEqual(['watermark', 'field']);
    const restored = sendBackward(back, 'field');
    expect(restored.map((e) => e.id)).toEqual(['field', 'watermark']);
  });

  it('locking and unlocking is never refused, including for an already-locked element', () => {
    const field = createNameFieldElement('f1', 'lastName', POSITION, 0);
    const locked = setLocked(field, true);
    expect(setLocked(locked, true).locked).toBe(true);
    expect(setLocked(locked, false).locked).toBe(false);
  });
});
