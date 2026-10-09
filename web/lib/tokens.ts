/**
 * BIOLOG design tokens (BIO-FND-007-AC02).
 *
 * Single source of truth for the brand palette and theme values. `globals.css`
 * mirrors these as CSS custom properties by hand — there is no build step that
 * generates one from the other, so `tokens.test.ts` (tests/frontend/unit) pins
 * these exact values and a change here must be mirrored there in the same
 * commit.
 *
 * Brand colours are taken from the approved BIOLOG mark
 * (reference/biolog-wordmark-light.svg icon fill `#F4502A`) and the Card
 * Designer v0.3 specification's navy text colour (`#0B2341`,
 * reference/CARD_DESIGNER_V03_SPECIFICATION.md section on typography). No
 * other brand colour source exists in the repository at time of writing.
 */

export const brand = Object.freeze({
  orange: '#F4502A',
  navy: '#0B2341',
});

export type ThemeName = 'light' | 'dark';

export interface ThemeTokens {
  /** Page background. */
  colorBg: string;
  /** Raised surface (header, side nav, cards). */
  colorSurface: string;
  /** Primary body text. */
  colorText: string;
  /** De-emphasised text (captions, disabled nav labels). */
  colorTextMuted: string;
  /** Hairline borders between surfaces. */
  colorBorder: string;
  /** Brand accent used for active/interactive states. Always the brand orange. */
  colorAccent: string;
  /** Text colour to place on top of `colorAccent`. */
  colorAccentContrast: string;
  /** Brand navy, used for the header/side-nav surface. */
  colorBrandNavy: string;
  /** Text colour to place on top of `colorBrandNavy`. */
  colorOnNavy: string;
}

/**
 * Light and dark theme token sets. Both keep the same brand orange/navy so the
 * brand reads consistently; only neutrals invert (BIO-FND-007-AC01).
 */
export const themeTokens: Readonly<Record<ThemeName, ThemeTokens>> = Object.freeze({
  light: Object.freeze({
    colorBg: '#F7F8FA',
    colorSurface: '#FFFFFF',
    colorText: '#161B22',
    colorTextMuted: '#5B6570',
    colorBorder: '#DDE2E8',
    colorAccent: brand.orange,
    colorAccentContrast: '#FFFFFF',
    colorBrandNavy: brand.navy,
    colorOnNavy: '#FFFFFF',
  }),
  dark: Object.freeze({
    colorBg: '#11151B',
    colorSurface: '#181D25',
    colorText: '#EDEFF2',
    colorTextMuted: '#98A2AE',
    colorBorder: '#2A313C',
    colorAccent: '#FF7A52',
    colorAccentContrast: '#161200',
    colorBrandNavy: '#132A4A',
    colorOnNavy: '#F2F5F9',
  }),
});

/** CSS custom-property name for each token, matching `globals.css`. */
export const tokenCssVar: Readonly<Record<keyof ThemeTokens, string>> = Object.freeze({
  colorBg: '--biolog-color-bg',
  colorSurface: '--biolog-color-surface',
  colorText: '--biolog-color-text',
  colorTextMuted: '--biolog-color-text-muted',
  colorBorder: '--biolog-color-border',
  colorAccent: '--biolog-color-accent',
  colorAccentContrast: '--biolog-color-accent-contrast',
  colorBrandNavy: '--biolog-color-brand-navy',
  colorOnNavy: '--biolog-color-on-navy',
});
