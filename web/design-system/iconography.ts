/**
 * Iconography tokens (BIO-UX-001-AC02).
 *
 * No specific icon library is chosen by this story — see `README.md` "Iconography: metrics
 * frozen, library still open" — but AC02 requires iconography itself to be tokenised the
 * same way colour/typography/spacing/radii/shadows already are. These are technology-neutral
 * metrics and rules: sizes, stroke weight, an optical-alignment grid, and semantic usage
 * rules any icon set (inline SVG, an icon font, a component library) must conform to. A later
 * story choosing a concrete icon library sizes/aligns its icons against these values rather
 * than inventing new ones.
 */

/** Rendered icon sizes, matched to context (BIO-UX-001-AC02). */
export const iconSize = Object.freeze({
  /** Inline with `typography.size.sm`/`.base` text. */
  iconSizeSm: '1rem',
  /** Default: nav items, buttons, table/list rows — matches `density.compact.controlHeight`. */
  iconSizeMd: '1.25rem',
  /** Page headers, empty states, dialogs. */
  iconSizeLg: '1.5rem',
});

export type IconSizeKey = keyof typeof iconSize;

/** SVG `stroke-width` for stroke-based icon sets, in user units against `iconAlignment.iconGrid`. */
export const iconStrokeWeight = Object.freeze({
  iconStrokeRegular: 1.5,
  iconStrokeBold: 2,
});

export type IconStrokeWeightKey = keyof typeof iconStrokeWeight;

/**
 * Optical alignment (BIO-UX-001-AC02). Every icon in any chosen set is designed against the
 * same square grid regardless of its rendered `iconSize`, so mixed icons in one row/toolbar
 * stay visually aligned; a small optical inset compensates for glyphs that fill more of their
 * box (e.g. a filled circle/square) so they don't read oversized next to thinner line icons.
 */
export const iconAlignment = Object.freeze({
  /** Base SVG viewBox (square, N×N) every icon is authored against, independent of `iconSize`. */
  iconGrid: 24,
  /** Percent of the grid trimmed inward for visually "full" glyphs, so they read the same weight as line icons at the same box size. */
  iconOpticalInsetPercent: 8,
  /** CSS `vertical-align` value when an icon sits inline within a line of text. */
  iconBaselineAlign: 'text-bottom',
});

export type IconAlignmentKey = keyof typeof iconAlignment;

export interface IconUsageRule {
  description: string;
}

/** Semantic usage rules (BIO-UX-001-AC02) — how any chosen icon set is applied, not which one. */
export const iconUsageRules: Readonly<Record<string, IconUsageRule>> = Object.freeze({
  colorInheritance: Object.freeze({
    description:
      "Icons use currentColor by default, inheriting colorText/colorTextMuted from context. Only a status icon (success/warning/error/info/offline) uses its matching statusColorTokens value instead of inheriting.",
  }),
  accessibleName: Object.freeze({
    description:
      'An icon that is the only content of an interactive control (e.g. an icon-only button) must carry an accessible name (aria-label or equivalent); an icon placed beside visible text conveying the same meaning is aria-hidden.',
  }),
  sizeBySurface: Object.freeze({
    description:
      'iconSizeSm inline with sm/base body text; iconSizeMd for nav items, buttons and table/list rows; iconSizeLg for page, empty-state and dialog headers.',
  }),
});
