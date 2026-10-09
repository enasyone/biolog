// Developer tests for BIO-CRD-003-AC01/AC03. SYNTHETIC: no real card designs.
import { describe, expect, it } from 'vitest';
import { DEFAULT_TYPOGRAPHY, updateTypography } from '../../../web/features/bio-crd-003/typography';
import {
  NAME_FIELD_BINDINGS,
  copyStyleTo,
  createNameFieldElement,
  isNameFieldElement,
  setBinding,
  setTypography,
} from '../../../web/features/bio-crd-003/nameFields';

const POSITION = { xPx: 10, yPx: 10, widthPx: 200, heightPx: 40 };

describe('BIO-CRD-003-AC01 each name field binds independently', () => {
  it('names all six independent objects: Title, First, Middle, Last, Full and Display Name', () => {
    expect(NAME_FIELD_BINDINGS).toEqual(['title', 'firstName', 'middleName', 'lastName', 'fullName', 'displayName']);
  });

  it('two fields with different bindings are wholly independent objects', () => {
    const first = createNameFieldElement('f1', 'firstName', POSITION, 0);
    const last = createNameFieldElement('f2', 'lastName', POSITION, 1);
    expect(first.binding).toBe('firstName');
    expect(last.binding).toBe('lastName');
  });

  it('changing one field binding never changes a different field object', () => {
    const first = createNameFieldElement('f1', 'firstName', POSITION, 0);
    const rebound = setBinding(first, 'displayName');
    expect(rebound.binding).toBe('displayName');
    expect(first.binding).toBe('firstName'); // the original object is untouched - a pure function
  });

  it('is recognised by the shared type guard', () => {
    const field = createNameFieldElement('f1', 'title', POSITION, 0);
    expect(isNameFieldElement(field)).toBe(true);
  });
});

describe('BIO-CRD-003-AC03 copy style does not copy data binding', () => {
  it("copies the source's typography onto the target, verbatim", () => {
    const source = createNameFieldElement('src', 'firstName', POSITION, 0, updateTypography(DEFAULT_TYPOGRAPHY, { bold: true, fontSizePt: 20 }));
    const target = createNameFieldElement('tgt', 'lastName', POSITION, 1);

    const result = copyStyleTo(source, target);

    expect(result.typography).toEqual(source.typography);
  });

  it("never changes the target's binding, even when copying from a field with a different binding", () => {
    const source = createNameFieldElement('src', 'firstName', POSITION, 0);
    const target = createNameFieldElement('tgt', 'lastName', POSITION, 1);

    const result = copyStyleTo(source, target);

    expect(result.binding).toBe('lastName'); // NOT 'firstName' - this is the exact defect AC03 forbids
  });

  it("never changes the target's id, position, size, rotation, layer or lock state", () => {
    const source = createNameFieldElement('src', 'firstName', { xPx: 999, yPx: 999, widthPx: 999, heightPx: 999 }, 5);
    const target = createNameFieldElement('tgt', 'lastName', POSITION, 1);

    const result = copyStyleTo(source, target);

    expect(result.id).toBe('tgt');
    expect(result.xPx).toBe(POSITION.xPx);
    expect(result.yPx).toBe(POSITION.yPx);
    expect(result.zIndex).toBe(1);
    expect(result.locked).toBe(false);
  });

  it('a chain of copies (A -> B -> C) never accumulates a binding change anywhere', () => {
    const fieldA = createNameFieldElement('a', 'title', POSITION, 0, updateTypography(DEFAULT_TYPOGRAPHY, { color: '#111111' }));
    const fieldB = createNameFieldElement('b', 'middleName', POSITION, 1);
    const fieldC = createNameFieldElement('c', 'fullName', POSITION, 2);

    const copiedB = copyStyleTo(fieldA, fieldB);
    const copiedC = copyStyleTo(copiedB, fieldC);

    expect(copiedB.binding).toBe('middleName');
    expect(copiedC.binding).toBe('fullName');
    expect(copiedC.typography.color).toBe('#111111');
  });

  it('setTypography (a direct style replacement, not via copyStyleTo) also leaves binding untouched', () => {
    const field = createNameFieldElement('f1', 'displayName', POSITION, 0);
    const restyled = setTypography(field, updateTypography(DEFAULT_TYPOGRAPHY, { italic: true }));
    expect(restyled.binding).toBe('displayName');
  });
});
