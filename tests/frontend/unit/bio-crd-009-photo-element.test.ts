// Developer tests for BIO-CRD-009-AC01/AC02. SYNTHETIC: no real cardholder photos.
import { describe, expect, it } from 'vitest';
import { bringToFront, rotateElement, resizeElement, moveElement, setLocked } from '../../../web/features/bio-crd-003/canvasElementOps';
import { createPhotoElement, isPhotoElement, setContent } from '../../../web/features/bio-crd-009/photoElement';

const POSITION = { xPx: 10, yPx: 10, widthPx: 90, heightPx: 110 };

describe('BIO-CRD-009-AC01 PhotoElement extends the shared BaseElement, same operations as Logo/Watermark', () => {
  it('creates a photo element with the given content and geometry', () => {
    const photo = createPhotoElement('p1', 'cardholder-photo-1', POSITION, 0);
    expect(photo.elementKind).toBe('photo');
    expect(photo.content).toBe('cardholder-photo-1');
    expect(photo.xPx).toBe(10);
    expect(photo.widthPx).toBe(90);
    expect(photo.locked).toBe(false);
  });

  it('move is inherited from the shared canvasElementOps, not reimplemented', () => {
    const photo = createPhotoElement('p1', 'ref', POSITION, 0);
    const moved = moveElement(photo, 5, -5);
    expect(moved.xPx).toBe(15);
    expect(moved.yPx).toBe(5);
    expect(isPhotoElement(moved)).toBe(true);
  });

  it('resize is inherited and rejects non-positive dimensions', () => {
    const photo = createPhotoElement('p1', 'ref', POSITION, 0);
    expect(resizeElement(photo, 40, 50).widthPx).toBe(40);
    expect(() => resizeElement(photo, 0, 10)).toThrow(RangeError);
    expect(() => resizeElement(photo, 10, -1)).toThrow(RangeError);
  });

  it('rotation is inherited and normalised to [0, 360)', () => {
    const photo = createPhotoElement('p1', 'ref', POSITION, 0, -30);
    expect(photo.rotationDeg).toBe(330);
    expect(rotateElement(photo, 45).rotationDeg).toBe(15);
  });

  it('layer control (bring to front) is inherited', () => {
    const back = createPhotoElement('back', 'ref', POSITION, 0);
    const front = createPhotoElement('front', 'ref', POSITION, 1);
    const reordered = bringToFront([back, front], 'back');
    expect(reordered.map((p) => p.id)).toEqual(['front', 'back']);
  });

  it('a locked photo refuses move/resize/rotate, like any other locked element', () => {
    const locked = setLocked(createPhotoElement('p1', 'ref', POSITION, 0), true);
    expect(moveElement(locked, 5, 5)).toBe(locked);
    expect(rotateElement(locked, 90)).toBe(locked);
    expect(resizeElement(locked, 10, 10)).toBe(locked);
  });

  it('setContent replaces the opaque reference without touching geometry', () => {
    const photo = createPhotoElement('p1', 'cardholder-photo-1', POSITION, 0);
    const updated = setContent(photo, 'cardholder-photo-2');
    expect(updated.content).toBe('cardholder-photo-2');
    expect(updated.xPx).toBe(photo.xPx);
  });

  it('isPhotoElement distinguishes a photo from an unrelated element shape', () => {
    expect(isPhotoElement(createPhotoElement('p1', 'ref', POSITION, 0))).toBe(true);
    expect(isPhotoElement({ ...POSITION, id: 'x', rotationDeg: 0, zIndex: 0, locked: false } as never)).toBe(false);
  });
});
