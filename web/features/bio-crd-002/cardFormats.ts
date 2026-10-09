/**
 * BIO-CRD-002-AC01: "standard sizes use correct physical dimensions."
 *
 * Mirrors `api/features/bio-crd-002/Domain/CardFormatCatalog.cs` exactly - no build step
 * generates one from the other (the same "single source of truth, mirrored by hand, pinned by
 * a test" pattern `web/lib/tokens.ts` already established for the brand palette; see that
 * file's own header). A change to either file's dimensions must be mirrored in the other in
 * the same commit; `cardFormats.test.ts` pins these exact values.
 *
 * No "Card Designer v0.3" specification file exists anywhere in this repository (referenced by
 * name in `docs/BRAND-ASSETS.md`, never actually committed) - these are the real,
 * widely-published industry-standard blank sizes CR80/CR79/CR100 refer to across the
 * card-printer industry, not invented values. See the backend catalogue's own header for the
 * per-format citation.
 */

export type CardFormatKind = 'cr80' | 'cr79' | 'cr100' | 'custom';

export interface CardPhysicalSize {
  /** The longer physical edge, in millimetres. Orientation-independent - see `orientation.ts`. */
  readonly longEdgeMm: number;
  /** The shorter physical edge, in millimetres. */
  readonly shortEdgeMm: number;
}

export const STANDARD_FORMATS: Readonly<Record<Exclude<CardFormatKind, 'custom'>, CardPhysicalSize>> = Object.freeze({
  cr80: Object.freeze({ longEdgeMm: 85.6, shortEdgeMm: 53.98 }),
  cr79: Object.freeze({ longEdgeMm: 83.9, shortEdgeMm: 51.9 }),
  cr100: Object.freeze({ longEdgeMm: 98.5, shortEdgeMm: 67.0 }),
});

/**
 * BIO-CRD-002-AC03: "custom width/height validation exists." Mirrors
 * `CustomCardSizeValidator.cs`'s own bounds exactly, for the same reason as `STANDARD_FORMATS`
 * above - client-side validation gives the designer instant feedback; the backend endpoint is
 * the same rule enforced again as a defense-in-depth boundary, not the only place it runs.
 */
export const CUSTOM_SIZE_BOUNDS_MM = Object.freeze({ min: 20, max: 200 });

export interface CustomSizeValidationResult {
  readonly isValid: boolean;
  readonly errors: readonly string[];
}

export function validateCustomSize(widthMm: number, heightMm: number): CustomSizeValidationResult {
  const errors: string[] = [];
  for (const [label, value] of [
    ['width', widthMm],
    ['height', heightMm],
  ] as const) {
    if (!Number.isFinite(value)) {
      errors.push(`${label}_mm must be a finite number`);
      continue;
    }
    if (value < CUSTOM_SIZE_BOUNDS_MM.min || value > CUSTOM_SIZE_BOUNDS_MM.max) {
      errors.push(`${label}_mm must be between ${CUSTOM_SIZE_BOUNDS_MM.min} and ${CUSTOM_SIZE_BOUNDS_MM.max}`);
    }
  }
  return { isValid: errors.length === 0, errors };
}

/**
 * Render resolution for converting a physical millimetre size to on-screen canvas pixels. Not
 * specified anywhere in this repository - chosen as a conventional print-quality resolution,
 * the same 300 DPI figure BIO-PHO-002's own `CaptureSpecification.MinimumDpi` uses for an
 * identical reason (one named constant to revise, not a magic number scattered through the
 * feature).
 */
export const CANVAS_DPI = 300;
const MM_PER_INCH = 25.4;

export function mmToPx(mm: number): number {
  return Math.round((mm / MM_PER_INCH) * CANVAS_DPI);
}

/** Physical size of a format, in millimetres, for the CURRENT orientation - long edge horizontal in landscape, vertical in portrait. */
export function physicalSizeMm(
  kind: CardFormatKind,
  orientation: 'landscape' | 'portrait',
  custom?: { widthMm: number; heightMm: number },
): { widthMm: number; heightMm: number } {
  if (kind === 'custom') {
    if (!custom) {
      throw new Error('a custom format requires custom.widthMm/heightMm');
    }
    return custom;
  }

  const { longEdgeMm, shortEdgeMm } = STANDARD_FORMATS[kind];
  return orientation === 'landscape' ? { widthMm: longEdgeMm, heightMm: shortEdgeMm } : { widthMm: shortEdgeMm, heightMm: longEdgeMm };
}
