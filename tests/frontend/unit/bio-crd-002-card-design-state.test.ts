// Developer tests for BIO-CRD-002-AC05 (and AC02's state-level wiring). SYNTHETIC: no real card designs.
import { describe, expect, it } from 'vitest';
import {
  addElement,
  bringToFrontOn,
  createInitialDesignState,
  moveElementOn,
  removeElement,
  rotateElementOn,
  setLockedOn,
  switchDesignOrientation,
} from '../../../web/features/bio-crd-002/cardDesignState';
import { createElement } from '../../../web/features/bio-crd-002/elements';

function stateWithOneElementPerSide() {
  const state = createInitialDesignState('cr80', 'landscape');
  const withFront = addElement(state, 'front', createElement({ id: 'front-1', label: 'Photo', xPx: 10, yPx: 10, widthPx: 50, heightPx: 50, zIndex: 0 }));
  return addElement(withFront, 'back', createElement({ id: 'back-1', label: 'Barcode', xPx: 20, yPx: 20, widthPx: 60, heightPx: 20, zIndex: 0 }));
}

describe('BIO-CRD-002-AC05 front and back are independent', () => {
  it('adding an element to front never appears on back, and vice versa', () => {
    const state = stateWithOneElementPerSide();
    expect(state.front.elements.map((e) => e.id)).toEqual(['front-1']);
    expect(state.back.elements.map((e) => e.id)).toEqual(['back-1']);
  });

  it('moving an element on front leaves the back side object completely untouched - same reference, not merely equal', () => {
    const state = stateWithOneElementPerSide();
    const next = moveElementOn(state, 'front', 'front-1', 5, 5);
    expect(next.back).toBe(state.back);
  });

  it('rotating an element on back leaves the front side object completely untouched', () => {
    const state = stateWithOneElementPerSide();
    const next = rotateElementOn(state, 'back', 'back-1', 45);
    expect(next.front).toBe(state.front);
  });

  it('locking an element on one side does not affect the other side at all', () => {
    const state = stateWithOneElementPerSide();
    const next = setLockedOn(state, 'front', 'front-1', true);
    expect(next.back.elements[0]!.id).toBe('back-1');
    expect(next.back).toBe(state.back);
  });

  it('reordering layers on one side does not touch the other side', () => {
    const state = stateWithOneElementPerSide();
    const next = bringToFrontOn(state, 'front', 'front-1');
    expect(next.back).toBe(state.back);
  });

  it('removing an element from front never removes the identically-shaped element from back', () => {
    const front = createElement({ id: 'shared-id', label: 'Same id on both sides', xPx: 0, yPx: 0, widthPx: 10, heightPx: 10, zIndex: 0 });
    const back = createElement({ id: 'shared-id', label: 'Same id on both sides', xPx: 0, yPx: 0, widthPx: 10, heightPx: 10, zIndex: 0 });
    const base = createInitialDesignState('cr80', 'landscape');
    const withBoth = addElement(addElement(base, 'front', front), 'back', back);

    const next = removeElement(withBoth, 'front', 'shared-id');

    expect(next.front.elements).toHaveLength(0);
    expect(next.back.elements).toHaveLength(1);
  });

  it('front and back start at the exact same physical size, since both are the same card blank', () => {
    const state = createInitialDesignState('cr80', 'landscape');
    expect(state.front.widthPx).toBe(state.back.widthPx);
    expect(state.front.heightPx).toBe(state.back.heightPx);
  });
});

describe('BIO-CRD-002-AC02 switching orientation applies to both sides independently, each from its own current elements', () => {
  it('switches both sides to the swapped canvas size', () => {
    const state = stateWithOneElementPerSide();
    const switched = switchDesignOrientation(state);
    expect(switched.orientation).toBe('portrait');
    expect(switched.front.widthPx).toBe(state.front.heightPx);
    expect(switched.back.widthPx).toBe(state.back.heightPx);
  });

  it('switching twice reproduces the exact original state for both sides', () => {
    const state = stateWithOneElementPerSide();
    const twice = switchDesignOrientation(switchDesignOrientation(state));
    expect(twice.front).toEqual(state.front);
    expect(twice.back).toEqual(state.back);
    expect(twice.orientation).toBe(state.orientation);
  });
});
