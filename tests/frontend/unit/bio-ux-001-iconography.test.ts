// Developer tests for BIO-UX-001-AC02 (iconography tokens/rules).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import {
  iconAlignment,
  iconSize,
  iconStrokeWeight,
  iconUsageRules,
} from '../../../web/design-system/iconography';

describe('BIO-UX-001-AC02 iconography is tokenised (technology-neutral, no library chosen)', () => {
  it('defines an sm/md/lg size scale that increases monotonically', () => {
    const sm = Number.parseFloat(iconSize.iconSizeSm);
    const md = Number.parseFloat(iconSize.iconSizeMd);
    const lg = Number.parseFloat(iconSize.iconSizeLg);
    expect(md).toBeGreaterThan(sm);
    expect(lg).toBeGreaterThan(md);
  });

  it('defines regular and bold stroke weights, bold heavier than regular', () => {
    expect(iconStrokeWeight.iconStrokeBold).toBeGreaterThan(iconStrokeWeight.iconStrokeRegular);
  });

  it('defines an optical-alignment grid and inset independent of rendered size', () => {
    expect(iconAlignment.iconGrid).toBeGreaterThan(0);
    expect(iconAlignment.iconOpticalInsetPercent).toBeGreaterThan(0);
    expect(iconAlignment.iconOpticalInsetPercent).toBeLessThan(50);
    expect(iconAlignment.iconBaselineAlign.length).toBeGreaterThan(0);
  });

  it('specifies semantic usage rules: colour inheritance, accessible naming, size-by-surface', () => {
    expect(iconUsageRules.colorInheritance.description).toMatch(/currentColor/i);
    expect(iconUsageRules.accessibleName.description).toMatch(/aria-label/i);
    expect(iconUsageRules.sizeBySurface.description).toMatch(/iconSizeSm/);
  });
});
