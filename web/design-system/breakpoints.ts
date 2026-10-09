/**
 * Responsive breakpoints and density rules (BIO-UX-001-AC04).
 *
 * The shell (BIO-FND-007) already switches from a mobile nav drawer to a
 * static sidebar at 768px in `globals.css`'s `.biolog-shell` rules; `bpTablet`
 * below pins that existing value rather than introducing a second one. Only
 * `bpDesktop` and `bpWide` are new.
 */
export const breakpoints = Object.freeze({
  /** Mobile-first default: single column, nav collapses to a drawer. */
  bpTablet: 768,
  /** Sidebar nav is permanent; content gains a second column where useful. */
  bpDesktop: 1024,
  /** Wide desktop: tables/forms may use additional horizontal space, still capped. */
  bpWide: 1440,
});

export type BreakpointKey = keyof typeof breakpoints;

/**
 * Compact enterprise density (BIO-UX-001-AC04, PRODUCT-EXPERIENCE.md
 * "compact enterprise density with generous whitespace"). `row` heights are
 * for data-table rows; `control` heights are for inputs/buttons.
 */
export const density = Object.freeze({
  compact: Object.freeze({
    rowHeight: '2.25rem',
    controlHeight: '2rem',
  }),
  comfortable: Object.freeze({
    rowHeight: '2.75rem',
    controlHeight: '2.5rem',
  }),
});

export type DensityKey = keyof typeof density;

/**
 * Layout principle per breakpoint (BIO-UX-001-AC04). Documents intent; the
 * shell CSS is the implementation. `density` is the default row/control
 * sizing for that breakpoint — pages may opt into the other density where a
 * screen's own story requires it.
 */
export const layoutPrinciples: Readonly<
  Record<'mobile' | 'tablet' | 'desktop', { nav: string; content: string; density: DensityKey }>
> = Object.freeze({
  mobile: Object.freeze({
    nav: 'Off-canvas drawer, opened by the header toggle; closes on navigation.',
    content: 'Single column; primary actions stack full-width.',
    density: 'comfortable',
  }),
  tablet: Object.freeze({
    nav: 'Permanent sidebar at bpTablet and above.',
    content: 'Single main column with room for inline secondary actions.',
    density: 'compact',
  }),
  desktop: Object.freeze({
    nav: 'Permanent sidebar; header gains room for global search.',
    content: 'Main column may add a secondary panel (detail/inspector) at bpDesktop+.',
    density: 'compact',
  }),
});
