/**
 * BIO-CRD-009-AC03: "the photo element is addable/removable on both front and back
 * independently (BIO-CRD-002-AC05's front/back independence still holds)." Mirrors
 * `bio-crd-002/cardDesignState.ts`'s own front/back split pattern (`sideOf`/`withSide` helpers,
 * each operation explicitly parameterised by side and only ever constructing a new object for
 * THAT side) - the exact same invariant BIO-CRD-002-AC05 already established and tests with
 * `toBe` (reference equality on the untouched side), applied here to `PhotoElement` instead of
 * BIO-CRD-002's own generic `CanvasElement`. Neither `CardTypographyApp` (BIO-CRD-003) nor
 * `CardObjectsApp` (BIO-CRD-004) split by side at all - this story needs the split because its
 * own AC03 explicitly asks for it, unlike its siblings' ACs.
 */
import {
  bringForward,
  bringToFront,
  moveElement,
  resizeElement,
  rotateElement,
  sendBackward,
  sendToBack,
  setLocked,
} from '../bio-crd-003/canvasElementOps';
import type { PhotoElement } from './photoElement';

export type PhotoSide = 'front' | 'back';

export interface PhotoDesignState {
  readonly front: readonly PhotoElement[];
  readonly back: readonly PhotoElement[];
}

export function createInitialPhotoState(): PhotoDesignState {
  return { front: [], back: [] };
}

function sideOf(state: PhotoDesignState, side: PhotoSide): readonly PhotoElement[] {
  return side === 'front' ? state.front : state.back;
}

function withSide(state: PhotoDesignState, side: PhotoSide, elements: readonly PhotoElement[]): PhotoDesignState {
  return side === 'front' ? { ...state, front: elements } : { ...state, back: elements };
}

export function addPhotoOn(state: PhotoDesignState, side: PhotoSide, element: PhotoElement): PhotoDesignState {
  return withSide(state, side, [...sideOf(state, side), element]);
}

export function removePhotoOn(state: PhotoDesignState, side: PhotoSide, id: string): PhotoDesignState {
  return withSide(state, side, sideOf(state, side).filter((e) => e.id !== id));
}

function updateOne(
  state: PhotoDesignState,
  side: PhotoSide,
  id: string,
  update: (element: PhotoElement) => PhotoElement,
): PhotoDesignState {
  return withSide(state, side, sideOf(state, side).map((e) => (e.id === id ? update(e) : e)));
}

export function movePhotoOn(state: PhotoDesignState, side: PhotoSide, id: string, dxPx: number, dyPx: number): PhotoDesignState {
  return updateOne(state, side, id, (e) => moveElement(e, dxPx, dyPx));
}

export function resizePhotoOn(state: PhotoDesignState, side: PhotoSide, id: string, widthPx: number, heightPx: number): PhotoDesignState {
  return updateOne(state, side, id, (e) => resizeElement(e, widthPx, heightPx));
}

export function rotatePhotoOn(state: PhotoDesignState, side: PhotoSide, id: string, deltaDeg: number): PhotoDesignState {
  return updateOne(state, side, id, (e) => rotateElement(e, deltaDeg));
}

export function setPhotoLockedOn(state: PhotoDesignState, side: PhotoSide, id: string, locked: boolean): PhotoDesignState {
  return updateOne(state, side, id, (e) => setLocked(e, locked));
}

export function bringPhotoToFrontOn(state: PhotoDesignState, side: PhotoSide, id: string): PhotoDesignState {
  return withSide(state, side, bringToFront(sideOf(state, side), id));
}

export function sendPhotoToBackOn(state: PhotoDesignState, side: PhotoSide, id: string): PhotoDesignState {
  return withSide(state, side, sendToBack(sideOf(state, side), id));
}

export function bringPhotoForwardOn(state: PhotoDesignState, side: PhotoSide, id: string): PhotoDesignState {
  return withSide(state, side, bringForward(sideOf(state, side), id));
}

export function sendPhotoBackwardOn(state: PhotoDesignState, side: PhotoSide, id: string): PhotoDesignState {
  return withSide(state, side, sendBackward(sideOf(state, side), id));
}
