// Developer tests for BIO-CRD-009-AC03. SYNTHETIC: no real cardholder photos.
import { describe, expect, it } from 'vitest';
import {
  addPhotoOn,
  bringPhotoToFrontOn,
  createInitialPhotoState,
  movePhotoOn,
  removePhotoOn,
  rotatePhotoOn,
  setPhotoLockedOn,
} from '../../../web/features/bio-crd-009/photoDesignState';
import { createPhotoElement } from '../../../web/features/bio-crd-009/photoElement';

const POSITION = { xPx: 10, yPx: 10, widthPx: 90, heightPx: 110 };

function stateWithOnePerSide() {
  const state = createInitialPhotoState();
  const withFront = addPhotoOn(state, 'front', createPhotoElement('front-1', 'front-photo', POSITION, 0));
  return addPhotoOn(withFront, 'back', createPhotoElement('back-1', 'back-photo', POSITION, 0));
}

describe('BIO-CRD-009-AC03 the photo element is addable/removable on both front and back independently', () => {
  it('a fresh state starts with no photos on either side', () => {
    const state = createInitialPhotoState();
    expect(state.front).toEqual([]);
    expect(state.back).toEqual([]);
  });

  it('adding a photo to front never appears on back, and vice versa', () => {
    const state = stateWithOnePerSide();
    expect(state.front.map((e) => e.id)).toEqual(['front-1']);
    expect(state.back.map((e) => e.id)).toEqual(['back-1']);
  });

  it('moving a photo on front leaves the back side array completely untouched - same reference', () => {
    const state = stateWithOnePerSide();
    const next = movePhotoOn(state, 'front', 'front-1', 5, 5);
    expect(next.back).toBe(state.back);
  });

  it('rotating a photo on back leaves the front side array completely untouched', () => {
    const state = stateWithOnePerSide();
    const next = rotatePhotoOn(state, 'back', 'back-1', 45);
    expect(next.front).toBe(state.front);
  });

  it('locking a photo on one side does not affect the other side at all', () => {
    const state = stateWithOnePerSide();
    const next = setPhotoLockedOn(state, 'front', 'front-1', true);
    expect(next.back).toBe(state.back);
    expect(next.front[0]!.locked).toBe(true);
  });

  it('reordering layers on one side does not touch the other side', () => {
    const state = stateWithOnePerSide();
    const next = bringPhotoToFrontOn(state, 'front', 'front-1');
    expect(next.back).toBe(state.back);
  });

  it('removing a photo from front never removes the identically-id\'d photo from back', () => {
    const base = createInitialPhotoState();
    const withBoth = addPhotoOn(
      addPhotoOn(base, 'front', createPhotoElement('shared-id', 'front-photo', POSITION, 0)),
      'back',
      createPhotoElement('shared-id', 'back-photo', POSITION, 0),
    );
    const next = removePhotoOn(withBoth, 'front', 'shared-id');
    expect(next.front).toEqual([]);
    expect(next.back.map((e) => e.id)).toEqual(['shared-id']);
  });

  it('removing an unknown id is a no-op', () => {
    const state = stateWithOnePerSide();
    const next = removePhotoOn(state, 'front', 'does-not-exist');
    expect(next.front).toEqual(state.front);
  });
});
