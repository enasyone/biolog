// Developer tests for BIO-UX-001-AC04 (responsive breakpoints and density rules).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { breakpoints, density, layoutPrinciples } from '../../../web/design-system/breakpoints';

describe('BIO-UX-001-AC04 responsive breakpoints are documented', () => {
  it('pins the shell breakpoint already in globals.css at 768', () => {
    expect(breakpoints.bpTablet).toBe(768);
  });

  it('breakpoints increase monotonically mobile -> tablet -> desktop -> wide', () => {
    expect(breakpoints.bpDesktop).toBeGreaterThan(breakpoints.bpTablet);
    expect(breakpoints.bpWide).toBeGreaterThan(breakpoints.bpDesktop);
  });

  it('defines compact and comfortable density with comfortable using larger controls', () => {
    const compactHeight = Number.parseFloat(density.compact.controlHeight);
    const comfortableHeight = Number.parseFloat(density.comfortable.controlHeight);
    expect(comfortableHeight).toBeGreaterThan(compactHeight);
  });

  it('documents a nav/content/density principle for every breakpoint tier', () => {
    for (const tier of ['mobile', 'tablet', 'desktop'] as const) {
      expect(layoutPrinciples[tier].nav.length).toBeGreaterThan(0);
      expect(layoutPrinciples[tier].content.length).toBeGreaterThan(0);
      expect(['compact', 'comfortable']).toContain(layoutPrinciples[tier].density);
    }
  });
});
