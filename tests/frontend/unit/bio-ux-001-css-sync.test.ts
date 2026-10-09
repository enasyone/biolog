// Developer tests for BIO-UX-001-AC02, AC03 (globals.css mirrors design-system/tokens.ts exactly).
// SYNTHETIC: no real tenant/user data.
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import {
  radii,
  spacing,
  statusColorCssVar,
  statusColorTokens,
} from '../../../web/design-system/tokens';
import { iconSize, iconStrokeWeight } from '../../../web/design-system/iconography';

const here = dirname(fileURLToPath(import.meta.url));
const globalsCssPath = resolve(here, '../../../web/app/globals.css');
const css = readFileSync(globalsCssPath, 'utf8');

describe('BIO-UX-001 globals.css mirrors design-system/tokens.ts', () => {
  it('every light-theme status colour hex value appears under :root[data-theme=\'light\']', () => {
    const lightBlockMatch = css.match(/:root,\s*\n:root\[data-theme='light'\] \{([\s\S]*?)\n\}/);
    expect(lightBlockMatch).not.toBeNull();
    const lightBlock = lightBlockMatch![1]!.toLowerCase();
    for (const key of Object.keys(statusColorTokens.light) as (keyof typeof statusColorTokens.light)[]) {
      const cssVar = statusColorCssVar[key];
      const hex = statusColorTokens.light[key].toLowerCase();
      expect(lightBlock, `${cssVar} should be ${hex} in the light block`).toContain(
        `${cssVar}: ${hex};`,
      );
    }
  });

  it("every dark-theme status colour hex value appears under :root[data-theme='dark']", () => {
    const darkBlockMatch = css.match(/:root\[data-theme='dark'\] \{([\s\S]*?)\n\}/);
    expect(darkBlockMatch).not.toBeNull();
    const darkBlock = darkBlockMatch![1]!.toLowerCase();
    for (const key of Object.keys(statusColorTokens.dark) as (keyof typeof statusColorTokens.dark)[]) {
      const cssVar = statusColorCssVar[key];
      const hex = statusColorTokens.dark[key].toLowerCase();
      expect(darkBlock, `${cssVar} should be ${hex} in the dark block`).toContain(
        `${cssVar}: ${hex};`,
      );
    }
  });

  it('every spacing token value appears as a CSS custom property', () => {
    const order: (keyof typeof spacing)[] = [
      'space0',
      'space1',
      'space2',
      'space3',
      'space4',
      'space5',
      'space6',
      'space7',
      'space8',
    ];
    order.forEach((key, index) => {
      expect(css).toContain(`--biolog-space-${index}: ${spacing[key]};`);
    });
  });

  it('every radius token value appears as a CSS custom property', () => {
    expect(css).toContain(`--biolog-radius-sm: ${radii.radiusSm};`);
    expect(css).toContain(`--biolog-radius-md: ${radii.radiusMd};`);
    expect(css).toContain(`--biolog-radius-lg: ${radii.radiusLg};`);
    expect(css).toContain(`--biolog-radius-pill: ${radii.radiusPill};`);
  });

  it('every icon size and stroke-weight token value appears as a CSS custom property', () => {
    expect(css).toContain(`--biolog-icon-size-sm: ${iconSize.iconSizeSm};`);
    expect(css).toContain(`--biolog-icon-size-md: ${iconSize.iconSizeMd};`);
    expect(css).toContain(`--biolog-icon-size-lg: ${iconSize.iconSizeLg};`);
    expect(css).toContain(`--biolog-icon-stroke-regular: ${iconStrokeWeight.iconStrokeRegular};`);
    expect(css).toContain(`--biolog-icon-stroke-bold: ${iconStrokeWeight.iconStrokeBold};`);
  });

  it("the prefers-color-scheme no-JS fallback matches the dark theme block's status colours", () => {
    const fallbackMatch = css.match(
      /:root:not\(\[data-theme='light'\]\):not\(\[data-theme='dark'\]\) \{([\s\S]*?)\n {2}\}/,
    );
    expect(fallbackMatch).not.toBeNull();
    const fallbackBlock = fallbackMatch![1]!.toLowerCase();
    expect(fallbackBlock).toContain(
      `--biolog-color-success: ${statusColorTokens.dark.colorSuccess.toLowerCase()};`,
    );
    expect(fallbackBlock).toContain(
      `--biolog-color-error: ${statusColorTokens.dark.colorError.toLowerCase()};`,
    );
  });
});
