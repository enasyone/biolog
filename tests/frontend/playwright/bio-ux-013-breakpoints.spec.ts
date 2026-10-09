// Developer tests for BIO-UX-013-AC01. SYNTHETIC: no real data.
//
// "Major pages" (god's dispatch left the exact set to this story's own judgement, given AC01-06
// span the whole app): one representative page per distinct interaction pattern already in the
// product, rather than all 31 routes — Dashboard (metrics/cards), People (a filterable list/table
// with row actions), Settings (a dense multi-section form), Document Verify (a multi-step
// journey with many cards), Cards (a table with per-row actions), Access Control (an operational
// form + the shared scenario picker), Administration, Reports. A full-route sweep already exists
// for axe (bio-ux-013-axe-baseline.spec.ts); this file checks layout at the three approved
// breakpoints from web/design-system/breakpoints.ts (BIO-UX-001-AC04), not accessibility.
//
// VIEWPORTS/MAJOR_PAGES now live in fixtures/approved-viewports.ts (extracted by BIO-UX-014, which
// reuses these exact same combinations for its visual-regression baselines) - no change to this
// file's own assertions.
import { expect, test, type Page } from '@playwright/test';
import { VIEWPORTS, MAJOR_PAGES } from './fixtures/approved-viewports';

async function hasNoHorizontalOverflow(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);
}

test.describe('BIO-UX-013-AC01 no horizontal overflow at any approved breakpoint', () => {
  for (const { route } of MAJOR_PAGES) {
    for (const [name, viewport] of Object.entries(VIEWPORTS)) {
      test(`${route} has no horizontal overflow at ${name} (${viewport.width}px)`, async ({ page }) => {
        await page.setViewportSize(viewport);
        await page.goto(route);
        expect(await hasNoHorizontalOverflow(page)).toBe(true);
      });
    }
  }
});

test.describe('BIO-UX-013-AC01 nav pattern matches web/design-system/breakpoints.ts (BIO-UX-001-AC04)', () => {
  test('below bpTablet (375px): nav is an off-canvas drawer, closed by default, opened by the header toggle', async ({
    page,
  }) => {
    await page.setViewportSize(VIEWPORTS.mobile);
    await page.goto('/');
    const toggle = page.getByRole('button', { name: /Open navigation/ });
    await expect(toggle).toBeVisible();
    const nav = page.getByRole('navigation', { name: 'Primary' });
    // Closed: the drawer's links exist in the DOM but are not part of the accessibility tree
    // (visibility: hidden), matching globals.css's own documented BIO-FND-007-QA-01 reasoning.
    await expect(nav).toBeHidden();
    await toggle.click();
    await expect(nav).toBeVisible();
    await expect(page.getByRole('link', { name: 'People' })).toBeVisible();
  });

  test('at bpTablet (768px) and above: nav is a permanent sidebar, no toggle needed', async ({ page }) => {
    for (const [name, viewport] of [
      ['tablet', VIEWPORTS.tablet],
      ['desktop', VIEWPORTS.desktop],
    ] as const) {
      await page.setViewportSize(viewport);
      await page.goto('/');
      await expect(page.getByRole('button', { name: /Open navigation/ }), `nav toggle at ${name}`).toBeHidden();
      await expect(page.getByRole('navigation', { name: 'Primary' }), `sidebar at ${name}`).toBeVisible();
      await expect(page.getByRole('link', { name: 'People' }), `nav link at ${name}`).toBeVisible();
    }
  });
});

test.describe('BIO-UX-013-AC01 the primary action on every major page stays reachable at every breakpoint', () => {
  for (const { route, primaryAction } of MAJOR_PAGES) {
    for (const [name, viewport] of Object.entries(VIEWPORTS)) {
      test(`${route}'s primary action ("${primaryAction}") is visible and enabled at ${name}`, async ({ page }) => {
        await page.setViewportSize(viewport);
        await page.goto(route);
        const action = page.getByRole('button', { name: primaryAction }).or(page.getByRole('link', { name: primaryAction }));
        await expect(action.first()).toBeVisible();
      });
    }
  }
});
