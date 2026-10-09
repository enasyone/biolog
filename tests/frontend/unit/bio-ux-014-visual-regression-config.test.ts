// Developer tests for BIO-UX-014-AC02, AC05.
// SYNTHETIC: no real tenant/user data.
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { VIEWPORTS, MAJOR_PAGES } from '../playwright/fixtures/approved-viewports';
import { PRIMARY_NAV_ITEMS } from '../../../web/lib/nav-items';

const here = dirname(fileURLToPath(import.meta.url));
const breakpointsPath = resolve(here, '../../../web/design-system/breakpoints.ts');
const specPath = resolve(here, '../playwright/bio-ux-014-visual-regression.spec.ts');
const playwrightConfigPath = resolve(here, '../playwright.config.mjs');

describe('BIO-UX-014-AC02 approved viewport combinations are pinned to the real breakpoints', () => {
  const breakpointsSource = readFileSync(breakpointsPath, 'utf-8');

  it('the tablet viewport width matches bpTablet exactly', () => {
    expect(breakpointsSource).toMatch(/bpTablet:\s*768/);
    expect(VIEWPORTS.tablet.width).toBe(768);
  });

  it('the mobile viewport is below bpTablet and the desktop viewport is above bpDesktop', () => {
    expect(breakpointsSource).toMatch(/bpDesktop:\s*1024/);
    expect(VIEWPORTS.mobile.width).toBeLessThan(768);
    expect(VIEWPORTS.desktop.width).toBeGreaterThan(1024);
  });

  it('exactly three approved viewports are named (mobile, tablet, desktop)', () => {
    expect(Object.keys(VIEWPORTS).sort()).toEqual(['desktop', 'mobile', 'tablet']);
  });
});

describe('BIO-UX-014-AC01 the representative page list covers every nav group', () => {
  it('every distinct web/lib/nav-items.ts group has at least one route baselined', () => {
    const groups = new Set(PRIMARY_NAV_ITEMS.map((item) => item.group).filter(Boolean));
    const majorRoutes = new Set(MAJOR_PAGES.map((p) => p.route.split('?')[0]));
    for (const group of groups) {
      const groupRoutes = PRIMARY_NAV_ITEMS.filter((item) => item.group === group).map((item) => item.href);
      expect(
        groupRoutes.some((href) => majorRoutes.has(href)),
        `no MAJOR_PAGES entry represents nav group "${group}" (routes: ${groupRoutes.join(', ')})`,
      ).toBe(true);
    }
  });

  it('the dashboard ("/") is included for full shell coverage', () => {
    expect(MAJOR_PAGES.some((p) => p.route === '/')).toBe(true);
  });
});

describe('BIO-UX-014-AC05 visual regression uses only local/synthetic sources, no external dependency', () => {
  it('the visual-regression spec never references an external (non-relative) URL', () => {
    const source = readFileSync(specPath, 'utf-8');
    expect(source).not.toMatch(/https?:\/\/(?!127\.0\.0\.1|localhost)/);
  });

  it("playwright.config.mjs's default base URL is localhost-only", () => {
    const source = readFileSync(playwrightConfigPath, 'utf-8');
    expect(source).toMatch(/127\.0\.0\.1|localhost/);
    expect(source).not.toMatch(/https?:\/\/(?!127\.0\.0\.1|localhost)[^"'`\s]*\.[a-z]{2,}/i);
  });

  it('every MAJOR_PAGES route is a relative path, never an absolute external URL', () => {
    for (const { route } of MAJOR_PAGES) {
      expect(route.startsWith('/')).toBe(true);
    }
  });
});
