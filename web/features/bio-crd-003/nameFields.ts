/**
 * BIO-CRD-003-AC01: "Each name field binds independently." BIO-CRD-003-AC03: "Copy style does
 * not copy data binding."
 */
import type { BaseElement } from './canvasElementOps';
import { DEFAULT_TYPOGRAPHY, type TypographyStyle } from './typography';

/** The six independent name objects the story names - Title, First, Middle, Last, Full and Display Name. */
export type NameFieldBinding = 'title' | 'firstName' | 'middleName' | 'lastName' | 'fullName' | 'displayName';

export const NAME_FIELD_BINDINGS: readonly NameFieldBinding[] = Object.freeze([
  'title',
  'firstName',
  'middleName',
  'lastName',
  'fullName',
  'displayName',
]);

export interface NameFieldElement extends BaseElement {
  readonly elementKind: 'nameField';
  /** BIO-CRD-003-AC01: which name component this field displays - independent of every other field's binding and of this field's own typography. */
  readonly binding: NameFieldBinding;
  readonly typography: TypographyStyle;
}

export function isNameFieldElement(element: BaseElement): element is NameFieldElement {
  return (element as { elementKind?: unknown }).elementKind === 'nameField';
}

export function createNameFieldElement(
  id: string,
  binding: NameFieldBinding,
  position: { xPx: number; yPx: number; widthPx: number; heightPx: number },
  zIndex: number,
  typography: TypographyStyle = DEFAULT_TYPOGRAPHY,
): NameFieldElement {
  return {
    elementKind: 'nameField',
    id,
    binding,
    typography,
    xPx: position.xPx,
    yPx: position.yPx,
    widthPx: position.widthPx,
    heightPx: position.heightPx,
    rotationDeg: 0,
    zIndex,
    locked: false,
  };
}

export function setBinding(element: NameFieldElement, binding: NameFieldBinding): NameFieldElement {
  return { ...element, binding };
}

export function setTypography(element: NameFieldElement, typography: TypographyStyle): NameFieldElement {
  return { ...element, typography };
}

/**
 * BIO-CRD-003-AC03's real mechanism: this function's own signature never reads or writes
 * `target.binding` at all - there is no code path through which a binding could leak from
 * `source` to `target`, structurally, not merely by convention. Every other property of
 * `target` (position, size, rotation, layer, lock, id) is equally untouched; only `typography`
 * changes.
 */
export function copyStyleTo(source: NameFieldElement, target: NameFieldElement): NameFieldElement {
  return { ...target, typography: source.typography };
}
