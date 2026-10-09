// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only.
import { describe, expect, it } from 'vitest';
import {
  ALL_BARCODE_PROFILES,
  DEFAULT_BARCODE_PROFILE,
  MAX_DIMENSION_MM,
  MIN_CONTRAST_RATIO,
  MIN_DIMENSION_MM,
  MIN_QUIET_ZONE_MM,
  OPTIONAL_BARCODE_PROFILES,
  contrastRatio,
  createBarcodeElement,
  isBarcodeElement,
  isDefaultProfile,
  setColors,
  setProfile,
  setQuietZoneMm,
  setSizeMm,
  validate,
  validateBarcode,
} from '../../../web/features/bio-crd-004/barcodeObjects';

describe('BIO-CRD-004-AC01 default profile is alphanumeric', () => {
  it('is the default', () => {
    expect(DEFAULT_BARCODE_PROFILE).toBe('alphanumeric');
    expect(isDefaultProfile('alphanumeric')).toBe(true);
  });

  it('a newly-created barcode element starts on the default profile', () => {
    const element = createBarcodeElement('b1', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 0);
    expect(element.profile).toBe('alphanumeric');
  });
});

describe('BIO-CRD-004-AC02 QR/PDF417/Data Matrix/Aztec are optional, never default', () => {
  it.each(OPTIONAL_BARCODE_PROFILES)('%s is optional and not the default', (profile) => {
    expect(isDefaultProfile(profile)).toBe(false);
  });

  it('names exactly the four optional 2D profiles', () => {
    expect(OPTIONAL_BARCODE_PROFILES).toEqual(['qr', 'pdf417', 'dataMatrix', 'aztec']);
    expect(ALL_BARCODE_PROFILES).toEqual(['alphanumeric', 'qr', 'pdf417', 'dataMatrix', 'aztec']);
  });

  it('setProfile can move a barcode element to an optional profile, and back', () => {
    let element = createBarcodeElement('b1', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 0);
    element = setProfile(element, 'qr');
    expect(element.profile).toBe('qr');
    element = setProfile(element, 'alphanumeric');
    expect(element.profile).toBe('alphanumeric');
  });
});

describe('BIO-CRD-004-AC03 quiet zone / size / contrast validation', () => {
  const valid = { quietZoneMm: 3, widthMm: 20, heightMm: 10, foregroundColorHex: '#000000', backgroundColorHex: '#ffffff' };

  it('a well-formed configuration is valid', () => {
    expect(validateBarcode(valid)).toEqual({ isValid: true, violations: [] });
  });

  it('a quiet zone below the minimum is rejected', () => {
    const result = validateBarcode({ ...valid, quietZoneMm: MIN_QUIET_ZONE_MM - 0.1 });
    expect(result.isValid).toBe(false);
    expect(result.violations.some((v) => v.includes('quiet_zone_mm'))).toBe(true);
  });

  it('a quiet zone exactly at the minimum is accepted', () => {
    expect(validateBarcode({ ...valid, quietZoneMm: MIN_QUIET_ZONE_MM }).isValid).toBe(true);
  });

  it.each([
    [1, 10],
    [200, 10],
    [10, 1],
    [10, 200],
  ])('a size outside the %d x %d bounds is rejected', (widthMm, heightMm) => {
    expect(validateBarcode({ ...valid, widthMm, heightMm }).isValid).toBe(false);
  });

  it('boundary dimensions are accepted', () => {
    expect(validateBarcode({ ...valid, widthMm: MIN_DIMENSION_MM, heightMm: MAX_DIMENSION_MM }).isValid).toBe(true);
  });

  it('identical foreground/background colours fail the contrast check', () => {
    const result = validateBarcode({ ...valid, foregroundColorHex: '#808080', backgroundColorHex: '#808080' });
    expect(result.isValid).toBe(false);
    expect(result.violations.some((v) => v.includes('contrast_ratio'))).toBe(true);
  });

  it('black on white is the maximum possible contrast ratio', () => {
    expect(contrastRatio({ r: 0, g: 0, b: 0 }, { r: 255, g: 255, b: 255 })).toBeCloseTo(21, 1);
  });

  it('is never below the documented minimum contrast ratio constant when valid', () => {
    const result = validateBarcode(valid);
    expect(result.isValid).toBe(true);
    expect(contrastRatio({ r: 0, g: 0, b: 0 }, { r: 255, g: 255, b: 255 })).toBeGreaterThanOrEqual(MIN_CONTRAST_RATIO);
  });

  it('a malformed colour is rejected without throwing', () => {
    const result = validateBarcode({ ...valid, foregroundColorHex: 'not-a-colour' });
    expect(result.isValid).toBe(false);
    expect(result.violations.some((v) => v.includes('foreground_color_hex'))).toBe(true);
  });

  it('multiple violations are all reported together', () => {
    const result = validateBarcode({ ...valid, quietZoneMm: 0, widthMm: 0 });
    expect(result.violations.length).toBeGreaterThanOrEqual(2);
  });

  it('validate(element) mirrors validateBarcode on the element\'s own fields', () => {
    let element = createBarcodeElement('b1', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 0);
    element = setColors(element, '#808080', '#808080');
    expect(validate(element).isValid).toBe(false);
    element = setColors(element, '#000000', '#ffffff');
    expect(validate(element).isValid).toBe(true);
  });
});

describe('barcode element shape', () => {
  it('createBarcodeElement produces a normalised rotation and is recognised by isBarcodeElement', () => {
    const element = createBarcodeElement('b1', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 0);
    expect(element.rotationDeg).toBe(0);
    expect(isBarcodeElement(element)).toBe(true);
  });

  it('setQuietZoneMm and setSizeMm update only the targeted fields', () => {
    let element = createBarcodeElement('b1', { xPx: 0, yPx: 0, widthPx: 10, heightPx: 10 }, 0);
    element = setQuietZoneMm(element, 5);
    element = setSizeMm(element, 30, 15);
    expect(element).toMatchObject({ quietZoneMm: 5, widthMm: 30, heightMm: 15, profile: 'alphanumeric' });
  });
});
