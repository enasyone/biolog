/**
 * BIO-CRD-004-AC01/AC02/AC03: barcode card objects. Mirrors
 * `api/features/bio-crd-004/Domain/BarcodeProfile.cs`/`BarcodeProfileCatalog.cs`/
 * `BarcodeValidation.cs` by hand - the same "mirrored by hand, pinned by a test" pattern
 * `web/lib/tokens.ts` and every prior card-designer sibling's own catalogue already
 * established. See the backend files' own headers for why these specific threshold values were
 * chosen (no in-repo barcode specification exists).
 */
import { normaliseDegrees, type BaseElement } from './canvasElementOps';
import { ALWAYS_VISIBLE, type VisibilityRule } from './visibility';

export type BarcodeProfile = 'alphanumeric' | 'qr' | 'pdf417' | 'dataMatrix' | 'aztec';

/** AC01: the only profile a new barcode object starts with unless a designer explicitly picks one of the optional profiles. */
export const DEFAULT_BARCODE_PROFILE: BarcodeProfile = 'alphanumeric';

/** AC02: every profile other than the default - never auto-selected. */
export const OPTIONAL_BARCODE_PROFILES: readonly BarcodeProfile[] = Object.freeze(['qr', 'pdf417', 'dataMatrix', 'aztec']);

export const ALL_BARCODE_PROFILES: readonly BarcodeProfile[] = Object.freeze([DEFAULT_BARCODE_PROFILE, ...OPTIONAL_BARCODE_PROFILES]);

export function isDefaultProfile(profile: BarcodeProfile): boolean {
  return profile === DEFAULT_BARCODE_PROFILE;
}

export const MIN_QUIET_ZONE_MM = 2.0;
export const MIN_DIMENSION_MM = 5.0;
export const MAX_DIMENSION_MM = 100.0;
export const MIN_CONTRAST_RATIO = 4.5;

export interface BarcodeValidationRequest {
  readonly quietZoneMm: number;
  readonly widthMm: number;
  readonly heightMm: number;
  readonly foregroundColorHex: string;
  readonly backgroundColorHex: string;
}

export interface BarcodeValidationResult {
  readonly isValid: boolean;
  readonly violations: readonly string[];
}

function parseHexColor(hex: string): { r: number; g: number; b: number } | null {
  if (hex.length !== 7 || hex[0] !== '#') return null;
  const r = Number.parseInt(hex.slice(1, 3), 16);
  const g = Number.parseInt(hex.slice(3, 5), 16);
  const b = Number.parseInt(hex.slice(5, 7), 16);
  if ([r, g, b].some((c) => Number.isNaN(c))) return null;
  return { r, g, b };
}

function relativeLuminance({ r, g, b }: { r: number; g: number; b: number }): number {
  const channel = (c: number) => {
    const srgb = c / 255;
    return srgb <= 0.03928 ? srgb / 12.92 : ((srgb + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/** WCAG 2.x contrast ratio: (L1 + 0.05) / (L2 + 0.05), lighter over darker. */
export function contrastRatio(a: { r: number; g: number; b: number }, b: { r: number; g: number; b: number }): number {
  const la = relativeLuminance(a);
  const lb = relativeLuminance(b);
  const lighter = Math.max(la, lb);
  const darker = Math.min(la, lb);
  return (lighter + 0.05) / (darker + 0.05);
}

/** AC03: never throws for a well-formed request - an invalid configuration is DATA (named violations), never an exception, mirroring the backend's own convention. */
export function validateBarcode(request: BarcodeValidationRequest): BarcodeValidationResult {
  const violations: string[] = [];

  if (!Number.isFinite(request.quietZoneMm) || request.quietZoneMm < MIN_QUIET_ZONE_MM) {
    violations.push(`quiet_zone_mm must be at least ${MIN_QUIET_ZONE_MM}`);
  }
  if (!Number.isFinite(request.widthMm) || request.widthMm < MIN_DIMENSION_MM || request.widthMm > MAX_DIMENSION_MM) {
    violations.push(`width_mm must be between ${MIN_DIMENSION_MM} and ${MAX_DIMENSION_MM}`);
  }
  if (!Number.isFinite(request.heightMm) || request.heightMm < MIN_DIMENSION_MM || request.heightMm > MAX_DIMENSION_MM) {
    violations.push(`height_mm must be between ${MIN_DIMENSION_MM} and ${MAX_DIMENSION_MM}`);
  }

  const foreground = parseHexColor(request.foregroundColorHex);
  if (!foreground) violations.push('foreground_color_hex must be a well-formed #rrggbb colour');
  const background = parseHexColor(request.backgroundColorHex);
  if (!background) violations.push('background_color_hex must be a well-formed #rrggbb colour');

  if (foreground && background) {
    const ratio = contrastRatio(foreground, background);
    if (ratio < MIN_CONTRAST_RATIO) {
      violations.push(`contrast_ratio (${ratio.toFixed(2)}:1) must be at least ${MIN_CONTRAST_RATIO}:1`);
    }
  }

  return { isValid: violations.length === 0, violations };
}

export interface BarcodeElement extends BaseElement {
  readonly elementKind: 'barcode';
  readonly profile: BarcodeProfile;
  readonly quietZoneMm: number;
  readonly widthMm: number;
  readonly heightMm: number;
  readonly foregroundColorHex: string;
  readonly backgroundColorHex: string;
  readonly visibilityRule: VisibilityRule;
}

export function isBarcodeElement(element: BaseElement): element is BarcodeElement {
  return (element as { elementKind?: unknown }).elementKind === 'barcode';
}

export function createBarcodeElement(
  id: string,
  position: { xPx: number; yPx: number; widthPx: number; heightPx: number },
  zIndex: number,
  profile: BarcodeProfile = DEFAULT_BARCODE_PROFILE,
): BarcodeElement {
  return {
    elementKind: 'barcode',
    id,
    profile,
    quietZoneMm: MIN_QUIET_ZONE_MM,
    widthMm: MIN_DIMENSION_MM * 4,
    heightMm: MIN_DIMENSION_MM * 2,
    foregroundColorHex: '#000000',
    backgroundColorHex: '#ffffff',
    visibilityRule: ALWAYS_VISIBLE,
    xPx: position.xPx,
    yPx: position.yPx,
    widthPx: position.widthPx,
    heightPx: position.heightPx,
    rotationDeg: normaliseDegrees(0),
    zIndex,
    locked: false,
  };
}

export function setProfile(element: BarcodeElement, profile: BarcodeProfile): BarcodeElement {
  return { ...element, profile };
}

export function setQuietZoneMm(element: BarcodeElement, quietZoneMm: number): BarcodeElement {
  return { ...element, quietZoneMm };
}

export function setSizeMm(element: BarcodeElement, widthMm: number, heightMm: number): BarcodeElement {
  return { ...element, widthMm, heightMm };
}

export function setColors(element: BarcodeElement, foregroundColorHex: string, backgroundColorHex: string): BarcodeElement {
  return { ...element, foregroundColorHex, backgroundColorHex };
}

export function setVisibilityRule(element: BarcodeElement, visibilityRule: VisibilityRule): BarcodeElement {
  return { ...element, visibilityRule };
}

export function validate(element: BarcodeElement): BarcodeValidationResult {
  return validateBarcode({
    quietZoneMm: element.quietZoneMm,
    widthMm: element.widthMm,
    heightMm: element.heightMm,
    foregroundColorHex: element.foregroundColorHex,
    backgroundColorHex: element.backgroundColorHex,
  });
}
