/**
 * BIO-CRD-003-AC02: "Font family, size, colour, bold, italic, underline, case, alignment,
 * spacing and opacity are editable per field." `TypographyStyle` names all ten properties;
 * `updateTypography` is the one place a change to any of them is applied, so validation
 * (opacity/size/spacing bounds) can never be bypassed by editing a field directly.
 */

export type TextCase = 'none' | 'uppercase' | 'lowercase' | 'titlecase';
export type TextAlign = 'left' | 'center' | 'right';

export interface TypographyStyle {
  readonly fontFamily: string;
  readonly fontSizePt: number;
  readonly color: string;
  readonly bold: boolean;
  readonly italic: boolean;
  readonly underline: boolean;
  readonly textCase: TextCase;
  readonly textAlign: TextAlign;
  readonly letterSpacingPx: number;
  /** 0-100 inclusive. */
  readonly opacityPercent: number;
}

export const DEFAULT_TYPOGRAPHY: TypographyStyle = Object.freeze({
  fontFamily: 'Arial',
  fontSizePt: 12,
  color: '#000000',
  bold: false,
  italic: false,
  underline: false,
  textCase: 'none',
  textAlign: 'left',
  letterSpacingPx: 0,
  opacityPercent: 100,
});

/** Applies a partial change, validating the result - never a partial, half-applied update. */
export function updateTypography(style: TypographyStyle, changes: Partial<TypographyStyle>): TypographyStyle {
  const next: TypographyStyle = { ...style, ...changes };

  if (!Number.isFinite(next.fontSizePt) || next.fontSizePt <= 0) {
    throw new RangeError('fontSizePt must be a positive, finite number');
  }
  if (!Number.isFinite(next.letterSpacingPx)) {
    throw new RangeError('letterSpacingPx must be a finite number');
  }
  if (!Number.isFinite(next.opacityPercent) || next.opacityPercent < 0 || next.opacityPercent > 100) {
    throw new RangeError('opacityPercent must be between 0 and 100');
  }
  if (next.fontFamily.trim().length === 0) {
    throw new RangeError('fontFamily must not be blank');
  }

  return next;
}
