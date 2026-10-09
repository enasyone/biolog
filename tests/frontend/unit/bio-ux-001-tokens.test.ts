// Developer tests for BIO-UX-001-AC02, AC03 (status colour/typography/spacing/radii/shadow tokens).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import {
  brand,
  themeTokens,
  radii,
  shadows,
  spacing,
  statusColorCssVar,
  statusColorTokens,
  typography,
} from '../../../web/design-system/tokens';

describe('BIO-UX-001-AC02 status colours are tokenised for both themes', () => {
  it('defines success/warning/error/info/offline for light and dark', () => {
    for (const themeName of ['light', 'dark'] as const) {
      const theme = statusColorTokens[themeName];
      expect(theme.colorSuccess).toMatch(/^#[0-9A-Fa-f]{6}$/);
      expect(theme.colorWarning).toMatch(/^#[0-9A-Fa-f]{6}$/);
      expect(theme.colorError).toMatch(/^#[0-9A-Fa-f]{6}$/);
      expect(theme.colorInfo).toMatch(/^#[0-9A-Fa-f]{6}$/);
      expect(theme.colorOffline).toMatch(/^#[0-9A-Fa-f]{6}$/);
    }
  });

  it('every status token has a declared CSS custom-property name', () => {
    const keys = Object.keys(statusColorTokens.light) as (keyof typeof statusColorTokens.light)[];
    for (const key of keys) {
      expect(statusColorCssVar).toHaveProperty(key);
      expect(statusColorCssVar[key]).toMatch(/^--biolog-color-/);
    }
  });

  it('light and dark status colours differ (theme-sensitive, unlike brand identity)', () => {
    expect(statusColorTokens.light.colorSuccess).not.toBe(statusColorTokens.dark.colorSuccess);
    expect(statusColorTokens.light.colorError).not.toBe(statusColorTokens.dark.colorError);
  });
});

describe('BIO-UX-001-AC02 typography/spacing/radii/shadows are tokenised', () => {
  it('typography names weight and a full size scale with line-heights', () => {
    expect(typography.weight.regular).toBe(400);
    expect(typography.weight.bold).toBe(700);
    for (const key of ['xs', 'sm', 'base', 'lg', 'xl', '2xl', '3xl'] as const) {
      expect(typography.size[key].fontSize).toMatch(/rem$/);
      expect(typography.size[key].lineHeight).toMatch(/rem$/);
    }
  });

  it('spacing is a monotonically increasing rem scale', () => {
    const values = Object.values(spacing).map((v) => Number.parseFloat(v));
    for (let i = 1; i < values.length; i += 1) {
      expect(values[i]).toBeGreaterThan(values[i - 1]!);
    }
  });

  it('radii include a pill value for fully-rounded controls', () => {
    expect(radii.radiusPill).toBe('999px');
    expect(radii.radiusSm).toMatch(/rem$/);
  });

  it('shadows escalate from sm to lg and dark uses higher alpha than light', () => {
    expect(shadows.light.shadowSm).not.toBe(shadows.light.shadowLg);
    const lightAlpha = Number(shadows.light.shadowMd.match(/[\d.]+\)$/)?.[0].replace(')', ''));
    const darkAlpha = Number(shadows.dark.shadowMd.match(/[\d.]+\)$/)?.[0].replace(')', ''));
    expect(darkAlpha).toBeGreaterThan(lightAlpha);
  });
});

describe('BIO-UX-001-AC03 semantic tokens use one shared system across themes', () => {
  it('re-exports the same brand/themeTokens BIO-FND-007 pinned, unmodified', () => {
    expect(brand.orange).toBe('#F4502A');
    expect(brand.navy).toBe('#0B2341');
    expect(themeTokens.light.colorAccent).toBe(brand.orange);
  });

  it('status token keys are identical across light and dark (semantic parity)', () => {
    expect(Object.keys(statusColorTokens.light).sort()).toEqual(
      Object.keys(statusColorTokens.dark).sort(),
    );
  });
});
