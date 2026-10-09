// Developer tests for BIO-CRD-003-AC02. SYNTHETIC: no real card designs.
import { describe, expect, it } from 'vitest';
import { DEFAULT_TYPOGRAPHY, updateTypography } from '../../../web/features/bio-crd-003/typography';

describe('BIO-CRD-003-AC02 font family, size, colour, bold, italic, underline, case, alignment, spacing and opacity are editable per field', () => {
  it('updates font family independently of every other property', () => {
    const next = updateTypography(DEFAULT_TYPOGRAPHY, { fontFamily: 'Georgia' });
    expect(next.fontFamily).toBe('Georgia');
    expect(next.fontSizePt).toBe(DEFAULT_TYPOGRAPHY.fontSizePt);
  });

  it('updates font size', () => {
    expect(updateTypography(DEFAULT_TYPOGRAPHY, { fontSizePt: 24 }).fontSizePt).toBe(24);
  });

  it('updates colour', () => {
    expect(updateTypography(DEFAULT_TYPOGRAPHY, { color: '#F4502A' }).color).toBe('#F4502A');
  });

  it('toggles bold, italic and underline independently', () => {
    const bold = updateTypography(DEFAULT_TYPOGRAPHY, { bold: true });
    expect(bold.bold).toBe(true);
    expect(bold.italic).toBe(false);
    expect(bold.underline).toBe(false);

    const all = updateTypography(bold, { italic: true, underline: true });
    expect(all.bold).toBe(true);
    expect(all.italic).toBe(true);
    expect(all.underline).toBe(true);
  });

  it('updates text case', () => {
    expect(updateTypography(DEFAULT_TYPOGRAPHY, { textCase: 'uppercase' }).textCase).toBe('uppercase');
  });

  it('updates alignment', () => {
    expect(updateTypography(DEFAULT_TYPOGRAPHY, { textAlign: 'center' }).textAlign).toBe('center');
  });

  it('updates letter spacing', () => {
    expect(updateTypography(DEFAULT_TYPOGRAPHY, { letterSpacingPx: 2.5 }).letterSpacingPx).toBe(2.5);
  });

  it('updates opacity within 0-100', () => {
    expect(updateTypography(DEFAULT_TYPOGRAPHY, { opacityPercent: 0 }).opacityPercent).toBe(0);
    expect(updateTypography(DEFAULT_TYPOGRAPHY, { opacityPercent: 100 }).opacityPercent).toBe(100);
    expect(updateTypography(DEFAULT_TYPOGRAPHY, { opacityPercent: 42 }).opacityPercent).toBe(42);
  });

  it('rejects an opacity outside 0-100', () => {
    expect(() => updateTypography(DEFAULT_TYPOGRAPHY, { opacityPercent: -1 })).toThrow(RangeError);
    expect(() => updateTypography(DEFAULT_TYPOGRAPHY, { opacityPercent: 101 })).toThrow(RangeError);
  });

  it('rejects a non-positive font size', () => {
    expect(() => updateTypography(DEFAULT_TYPOGRAPHY, { fontSizePt: 0 })).toThrow(RangeError);
    expect(() => updateTypography(DEFAULT_TYPOGRAPHY, { fontSizePt: -5 })).toThrow(RangeError);
  });

  it('rejects a blank font family', () => {
    expect(() => updateTypography(DEFAULT_TYPOGRAPHY, { fontFamily: '   ' })).toThrow(RangeError);
  });

  it('never mutates the original style', () => {
    const original = { ...DEFAULT_TYPOGRAPHY };
    updateTypography(DEFAULT_TYPOGRAPHY, { bold: true, fontSizePt: 99 });
    expect(DEFAULT_TYPOGRAPHY).toEqual(original);
  });
});
