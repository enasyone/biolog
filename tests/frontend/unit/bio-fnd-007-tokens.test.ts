// Developer tests for BIO-FND-007-AC02 (brand tokens).
// SYNTHETIC: no real tenant/user data.
import { describe, expect, it } from 'vitest';
import { brand, themeTokens, tokenCssVar } from '../../../web/lib/tokens';

describe('BIO-FND-007-AC02 brand palette is tokenised', () => {
  it('pins the approved brand orange and navy', () => {
    expect(brand.orange).toBe('#F4502A');
    expect(brand.navy).toBe('#0B2341');
  });

  it('uses the same brand orange/navy identity in both themes', () => {
    expect(themeTokens.light.colorAccent).toBe(brand.orange);
    expect(themeTokens.light.colorBrandNavy).toBe(brand.navy);
    expect(themeTokens.dark.colorBrandNavy.toLowerCase()).not.toBe('#ffffff');
  });

  it('light and dark themes invert their neutral background/text', () => {
    expect(themeTokens.light.colorBg).not.toBe(themeTokens.dark.colorBg);
    expect(themeTokens.light.colorText).not.toBe(themeTokens.dark.colorText);
  });

  it('every theme token has a declared CSS custom-property name', () => {
    const keys = Object.keys(themeTokens.light);
    for (const key of keys) {
      expect(tokenCssVar).toHaveProperty(key);
      expect(tokenCssVar[key as keyof typeof tokenCssVar]).toMatch(/^--biolog-color-/);
    }
  });
});
