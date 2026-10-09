/**
 * BIO-CRD-002-AC05: "front and back are independent." A card has one physical size (both faces
 * are printed on the same physical blank), but front and back carry wholly independent element
 * arrays - every operation below is explicitly parameterised by `side` and only ever
 * constructs a new object for THAT side, leaving the other side's object reference untouched
 * (not merely equal-by-value - the untouched side is the exact same object reference after the
 * call, which `cardDesignState.test.ts` asserts directly with `toBe`, not `toEqual`).
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
  type CanvasElement,
} from './elements';
import { switchOrientation, otherOrientation, type CardCanvasSide, type Orientation } from './orientation';
import { mmToPx, physicalSizeMm, type CardFormatKind } from './cardFormats';

export type CardSide = 'front' | 'back';

export interface CardDesignState {
  readonly formatKind: CardFormatKind;
  readonly orientation: Orientation;
  readonly customSizeMm?: { widthMm: number; heightMm: number };
  readonly front: CardCanvasSide;
  readonly back: CardCanvasSide;
}

export function createInitialDesignState(
  formatKind: CardFormatKind,
  orientation: Orientation,
  customSizeMm?: { widthMm: number; heightMm: number },
): CardDesignState {
  const { widthMm, heightMm } = physicalSizeMm(formatKind, orientation, customSizeMm);
  const side: CardCanvasSide = { widthPx: mmToPx(widthMm), heightPx: mmToPx(heightMm), elements: [] };
  return { formatKind, orientation, customSizeMm, front: side, back: side };
}

function sideOf(state: CardDesignState, side: CardSide): CardCanvasSide {
  return side === 'front' ? state.front : state.back;
}

function withSide(state: CardDesignState, side: CardSide, next: CardCanvasSide): CardDesignState {
  return side === 'front' ? { ...state, front: next } : { ...state, back: next };
}

function withElements(state: CardDesignState, side: CardSide, elements: readonly CanvasElement[]): CardDesignState {
  return withSide(state, side, { ...sideOf(state, side), elements });
}

export function addElement(state: CardDesignState, side: CardSide, element: CanvasElement): CardDesignState {
  return withElements(state, side, [...sideOf(state, side).elements, element]);
}

export function removeElement(state: CardDesignState, side: CardSide, id: string): CardDesignState {
  return withElements(state, side, sideOf(state, side).elements.filter((e) => e.id !== id));
}

function updateOneElement(
  state: CardDesignState,
  side: CardSide,
  id: string,
  update: (element: CanvasElement) => CanvasElement,
): CardDesignState {
  return withElements(state, side, sideOf(state, side).elements.map((e) => (e.id === id ? update(e) : e)));
}

export function moveElementOn(state: CardDesignState, side: CardSide, id: string, dxPx: number, dyPx: number): CardDesignState {
  return updateOneElement(state, side, id, (e) => moveElement(e, dxPx, dyPx));
}

export function resizeElementOn(state: CardDesignState, side: CardSide, id: string, widthPx: number, heightPx: number): CardDesignState {
  return updateOneElement(state, side, id, (e) => resizeElement(e, widthPx, heightPx));
}

export function rotateElementOn(state: CardDesignState, side: CardSide, id: string, deltaDeg: number): CardDesignState {
  return updateOneElement(state, side, id, (e) => rotateElement(e, deltaDeg));
}

export function setLockedOn(state: CardDesignState, side: CardSide, id: string, locked: boolean): CardDesignState {
  return updateOneElement(state, side, id, (e) => setLocked(e, locked));
}

export function bringToFrontOn(state: CardDesignState, side: CardSide, id: string): CardDesignState {
  return withElements(state, side, bringToFront(sideOf(state, side).elements, id));
}

export function sendToBackOn(state: CardDesignState, side: CardSide, id: string): CardDesignState {
  return withElements(state, side, sendToBack(sideOf(state, side).elements, id));
}

export function bringForwardOn(state: CardDesignState, side: CardSide, id: string): CardDesignState {
  return withElements(state, side, bringForward(sideOf(state, side).elements, id));
}

export function sendBackwardOn(state: CardDesignState, side: CardSide, id: string): CardDesignState {
  return withElements(state, side, sendBackward(sideOf(state, side).elements, id));
}

/** BIO-CRD-002-AC02: switches BOTH sides (one physical card, two faces, always the same size), each independently transformed from its own current elements. */
export function switchDesignOrientation(state: CardDesignState): CardDesignState {
  const nextOrientation = otherOrientation(state.orientation);
  return {
    ...state,
    orientation: nextOrientation,
    front: switchOrientation(state.front, state.orientation),
    back: switchOrientation(state.back, state.orientation),
  };
}
