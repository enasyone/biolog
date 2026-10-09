// Developer tests for BIO-CRD-002-AC01/AC03. SYNTHETIC: no real card designs.
import { describe, expect, it } from 'vitest';
import {
  CANVAS_DPI,
  CUSTOM_SIZE_BOUNDS_MM,
  STANDARD_FORMATS,
  mmToPx,
  physicalSizeMm,
  validateCustomSize,
} from '../../../web/features/bio-crd-002/cardFormats';

describe('BIO-CRD-002-AC01 standard sizes use correct physical dimensions', () => {
  it('pins CR80 to the ISO/IEC 7810 ID-1 dimensions', () => {
    expect(STANDARD_FORMATS.cr80).toEqual({ longEdgeMm: 85.6, shortEdgeMm: 53.98 });
  });

  it('pins CR79 to its published industry dimensions', () => {
    expect(STANDARD_FORMATS.cr79).toEqual({ longEdgeMm: 83.9, shortEdgeMm: 51.9 });
  });

  it('pins CR100 to its published industry dimensions', () => {
    expect(STANDARD_FORMATS.cr100).toEqual({ longEdgeMm: 98.5, shortEdgeMm: 67.0 });
  });

  it('every standard format has a long edge strictly greater than its short edge', () => {
    for (const size of Object.values(STANDARD_FORMATS)) {
      expect(size.longEdgeMm).toBeGreaterThan(size.shortEdgeMm);
    }
  });

  it('landscape orientation puts the long edge on the width', () => {
    const { widthMm, heightMm } = physicalSizeMm('cr80', 'landscape');
    expect(widthMm).toBe(85.6);
    expect(heightMm).toBe(53.98);
  });

  it('portrait orientation puts the long edge on the height', () => {
    const { widthMm, heightMm } = physicalSizeMm('cr80', 'portrait');
    expect(widthMm).toBe(53.98);
    expect(heightMm).toBe(85.6);
  });

  it('a custom format requires an explicit width/height and is not looked up from STANDARD_FORMATS', () => {
    expect(() => physicalSizeMm('custom', 'landscape')).toThrow();
    const { widthMm, heightMm } = physicalSizeMm('custom', 'landscape', { widthMm: 120, heightMm: 80 });
    expect(widthMm).toBe(120);
    expect(heightMm).toBe(80);
  });

  it('converts millimetres to pixels at the documented canvas DPI', () => {
    expect(CANVAS_DPI).toBe(300);
    // 1 inch (25.4mm) at 300 DPI is exactly 300px.
    expect(mmToPx(25.4)).toBe(300);
  });
});

describe('BIO-CRD-002-AC03 custom width/height validation exists', () => {
  it('accepts a size within bounds', () => {
    expect(validateCustomSize(90, 60)).toEqual({ isValid: true, errors: [] });
  });

  it('accepts the bounds themselves, inclusive', () => {
    expect(validateCustomSize(CUSTOM_SIZE_BOUNDS_MM.min, CUSTOM_SIZE_BOUNDS_MM.max).isValid).toBe(true);
  });

  it('rejects a width just below the minimum', () => {
    const result = validateCustomSize(CUSTOM_SIZE_BOUNDS_MM.min - 0.01, 50);
    expect(result.isValid).toBe(false);
    expect(result.errors).toHaveLength(1);
  });

  it('rejects a height just above the maximum', () => {
    const result = validateCustomSize(50, CUSTOM_SIZE_BOUNDS_MM.max + 0.01);
    expect(result.isValid).toBe(false);
  });

  it('rejects zero, negative, NaN and Infinity, never silently accepting them', () => {
    expect(validateCustomSize(0, 50).isValid).toBe(false);
    expect(validateCustomSize(-10, 50).isValid).toBe(false);
    expect(validateCustomSize(Number.NaN, 50).isValid).toBe(false);
    expect(validateCustomSize(Number.POSITIVE_INFINITY, 50).isValid).toBe(false);
  });

  it('reports both dimensions when both are out of bounds', () => {
    const result = validateCustomSize(1, 1000);
    expect(result.errors).toHaveLength(2);
  });
});
