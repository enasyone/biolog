/**
 * BIOLOG design-system tokens (BIO-UX-001-AC02, AC03).
 *
 * `web/lib/tokens.ts` (BIO-FND-007) already froze the brand palette and the
 * light/dark surface/text/border/accent colours; that file stays the pinned
 * single source for those colours (`tests/frontend/unit/bio-fnd-007-tokens.test.ts`
 * pins its exact values, and `theme-toggle.tsx` plus three Card Designer
 * feature modules already import it). This module does not duplicate or
 * replace it — it re-exports it and adds the token categories BIO-UX-001
 * requires that BIO-FND-007 did not cover: status colours, typography,
 * spacing, radii and shadows. `web/app/globals.css` mirrors every value
 * below as a CSS custom property by hand, the same pattern BIO-FND-007
 * established for the colour tokens; `bio-ux-001-css-sync.test.ts` pins that
 * the two stay equal.
 */

export { brand, themeTokens, tokenCssVar } from '../lib/tokens';
export type { ThemeName, ThemeTokens } from '../lib/tokens';

/**
 * Status/semantic colours (BIO-UX-001-AC02). Not part of BIO-FND-007's
 * original palette. `On` values are the text colour for solid-fill uses;
 * `Bg` values are a low-emphasis tinted background for banners/badges.
 */
export interface StatusColorTokens {
  colorSuccess: string;
  colorSuccessOn: string;
  colorSuccessBg: string;
  colorWarning: string;
  colorWarningOn: string;
  colorWarningBg: string;
  colorError: string;
  colorErrorOn: string;
  colorErrorBg: string;
  colorInfo: string;
  colorInfoOn: string;
  colorInfoBg: string;
  /** Used for the offline/degraded-connectivity state (BIO-UX-001-AC05). */
  colorOffline: string;
  colorOfflineOn: string;
  colorOfflineBg: string;
}

export const statusColorTokens: Readonly<Record<'light' | 'dark', StatusColorTokens>> =
  Object.freeze({
    light: Object.freeze({
      colorSuccess: '#1E8E5A',
      colorSuccessOn: '#FFFFFF',
      colorSuccessBg: '#E6F5EC',
      colorWarning: '#B7791F',
      colorWarningOn: '#FFFFFF',
      colorWarningBg: '#FBF0DD',
      colorError: '#C4321E',
      colorErrorOn: '#FFFFFF',
      colorErrorBg: '#FBE6E2',
      colorInfo: '#2364AA',
      colorInfoOn: '#FFFFFF',
      colorInfoBg: '#E4EEF9',
      colorOffline: '#5B6570',
      colorOfflineOn: '#FFFFFF',
      colorOfflineBg: '#EAECEF',
    }),
    dark: Object.freeze({
      colorSuccess: '#3FC17E',
      colorSuccessOn: '#0B1F14',
      colorSuccessBg: '#132A1E',
      colorWarning: '#E0A83F',
      colorWarningOn: '#241A05',
      colorWarningBg: '#2C2410',
      colorError: '#F0604A',
      colorErrorOn: '#2A0B05',
      colorErrorBg: '#2E1512',
      colorInfo: '#5B9BE0',
      colorInfoOn: '#081726',
      colorInfoBg: '#152232',
      colorOffline: '#98A2AE',
      colorOfflineOn: '#14181D',
      colorOfflineBg: '#20252C',
    }),
  });

export const statusColorCssVar: Readonly<Record<keyof StatusColorTokens, string>> = Object.freeze(
  {
    colorSuccess: '--biolog-color-success',
    colorSuccessOn: '--biolog-color-success-on',
    colorSuccessBg: '--biolog-color-success-bg',
    colorWarning: '--biolog-color-warning',
    colorWarningOn: '--biolog-color-warning-on',
    colorWarningBg: '--biolog-color-warning-bg',
    colorError: '--biolog-color-error',
    colorErrorOn: '--biolog-color-error-on',
    colorErrorBg: '--biolog-color-error-bg',
    colorInfo: '--biolog-color-info',
    colorInfoOn: '--biolog-color-info-on',
    colorInfoBg: '--biolog-color-info-bg',
    colorOffline: '--biolog-color-offline',
    colorOfflineOn: '--biolog-color-offline-on',
    colorOfflineBg: '--biolog-color-offline-bg',
  },
);

/** Typography scale (BIO-UX-001-AC02). Matches `globals.css` body font stack. */
export const typography = Object.freeze({
  fontFamily:
    "Satoshi, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif",
  weight: Object.freeze({
    regular: 400,
    medium: 500,
    semibold: 600,
    bold: 700,
  }),
  size: Object.freeze({
    xs: Object.freeze({ fontSize: '0.75rem', lineHeight: '1.05rem' }),
    sm: Object.freeze({ fontSize: '0.8125rem', lineHeight: '1.2rem' }),
    base: Object.freeze({ fontSize: '0.9375rem', lineHeight: '1.4rem' }),
    lg: Object.freeze({ fontSize: '1.0625rem', lineHeight: '1.55rem' }),
    xl: Object.freeze({ fontSize: '1.25rem', lineHeight: '1.75rem' }),
    '2xl': Object.freeze({ fontSize: '1.5rem', lineHeight: '2rem' }),
    '3xl': Object.freeze({ fontSize: '1.875rem', lineHeight: '2.375rem' }),
  }),
});

export type TypographySizeKey = keyof typeof typography.size;

/** 4px-base spacing scale (BIO-UX-001-AC02). */
export const spacing = Object.freeze({
  space0: '0',
  space1: '0.25rem',
  space2: '0.5rem',
  space3: '0.75rem',
  space4: '1rem',
  space5: '1.5rem',
  space6: '2rem',
  space7: '3rem',
  space8: '4rem',
});

export type SpacingKey = keyof typeof spacing;

/** Border radii (BIO-UX-001-AC02) — "rounded but restrained" per the approved visual direction. */
export const radii = Object.freeze({
  radiusSm: '0.25rem',
  radiusMd: '0.5rem',
  radiusLg: '0.75rem',
  radiusPill: '999px',
});

export type RadiusKey = keyof typeof radii;

/**
 * Elevation/shadow scale (BIO-UX-001-AC02). Dark-theme shadows use a higher
 * alpha than light-theme shadows: a light-opacity shadow is nearly invisible
 * against the dark surface colours in `themeTokens.dark`, so parity here
 * means "reads as the same elevation," not "identical rgba values" — the
 * same principle `themeTokens` already applies to colour (BIO-FND-007's
 * neutrals invert; the brand identity does not).
 */
export const shadows: Readonly<Record<'light' | 'dark', Readonly<Record<string, string>>>> =
  Object.freeze({
    light: Object.freeze({
      shadowSm: '0 1px 2px rgba(15, 23, 42, 0.08)',
      shadowMd: '0 4px 10px rgba(15, 23, 42, 0.10)',
      shadowLg: '0 12px 24px rgba(15, 23, 42, 0.14)',
    }),
    dark: Object.freeze({
      shadowSm: '0 1px 2px rgba(0, 0, 0, 0.32)',
      shadowMd: '0 4px 10px rgba(0, 0, 0, 0.40)',
      shadowLg: '0 12px 24px rgba(0, 0, 0, 0.48)',
    }),
  });

export type ShadowKey = keyof typeof shadows.light;
