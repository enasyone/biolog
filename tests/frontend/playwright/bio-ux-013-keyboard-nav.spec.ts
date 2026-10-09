// Developer tests for BIO-UX-013-AC02. SYNTHETIC: no real data.
import { expect, test } from '@playwright/test';

test.describe('BIO-UX-013-AC02 landmarks are present and correctly named on every major page', () => {
  const PAGES = ['/', '/people?as=admin', '/settings?as=admin', '/document-verify', '/cards'];

  for (const route of PAGES) {
    test(`${route} has a banner, a "Primary" navigation and a main landmark`, async ({ page }) => {
      await page.goto(route);
      await expect(page.getByRole('banner')).toBeVisible();
      await expect(page.getByRole('navigation', { name: 'Primary' })).toBeVisible();
      await expect(page.getByRole('main')).toBeVisible();
    });
  }
});

test.describe('BIO-UX-013-AC02 skip link', () => {
  test('is the first focusable element and jumps focus to main content on activation', async ({ page }) => {
    await page.goto('/');
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.locator('#biolog-main')).toBeFocused();
  });
});

test.describe('BIO-UX-013-AC02 focus-visible indicator applies to keyboard-focused controls', () => {
  test('a Tab-focused control has a visible focus outline (not suppressed)', async ({ page }) => {
    await page.goto('/');
    await page.keyboard.press('Tab'); // skip link
    await page.keyboard.press('Tab'); // brand link
    const outline = await page.evaluate(() => {
      const el = document.activeElement as HTMLElement | null;
      if (!el) return null;
      const style = getComputedStyle(el);
      return { outlineStyle: style.outlineStyle, outlineWidth: style.outlineWidth };
    });
    expect(outline?.outlineStyle).not.toBe('none');
    expect(outline?.outlineWidth).not.toBe('0px');
  });
});

test.describe('BIO-UX-013-AC02 keyboard-only tab order reaches every primary action, no traps', () => {
  test('Settings: tabbing from the top reaches the General section\'s Save button without a trap', async ({ page }) => {
    await page.goto('/settings?as=admin');
    // Tab a generous, bounded number of times from the very top of the document — if focus
    // never reaches the Save button, either something traps focus earlier or the button isn't
    // keyboard-reachable at all; both are real AC02 failures, not just this test being wrong.
    await page.locator('body').evaluate((el) => el.focus());
    let reached = false;
    for (let i = 0; i < 40; i += 1) {
      await page.keyboard.press('Tab');
      const name = await page.evaluate(() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.textContent || '');
      if (name.includes('Save (SIMULATION)')) {
        reached = true;
        break;
      }
    }
    expect(reached).toBe(true);
  });

  test('Document Verify: the multi-step journey has no keyboard trap across the whole page', async ({ page }) => {
    await page.goto('/document-verify');
    await page.locator('body').evaluate((el) => el.focus());
    const visitedTags = new Set<string>();
    let previousMarker: string | null = null;
    let stuckCount = 0;
    for (let i = 0; i < 60; i += 1) {
      await page.keyboard.press('Tab');
      // A stable per-step identity marker computed inside the SAME evaluate call each time
      // (tag + id + a snippet of accessible text) — avoids passing a live element handle across
      // separate evaluate() round-trips, which Playwright does not dereference inside a plain array.
      const marker = await page.evaluate(() => {
        const el = document.activeElement as HTMLElement | null;
        if (!el) return null;
        return `${el.tagName}#${el.id}:${(el.getAttribute('aria-label') || el.textContent || '').slice(0, 40)}`;
      });
      stuckCount = marker !== null && marker === previousMarker ? stuckCount + 1 : 0;
      // Three consecutive Tabs landing on the exact same control is a real trap; a handful of
      // buttons sharing a tag name (but not the same marker) is expected and fine.
      expect(stuckCount, `keyboard trap detected at iteration ${i} (${marker})`).toBeLessThan(3);
      previousMarker = marker;
      const tag = await page.evaluate(() => document.activeElement?.tagName ?? '');
      if (tag) visitedTags.add(tag);
    }
    // A real multi-step journey should reach more than just <body>/<a> — buttons and selects too.
    expect(visitedTags.has('BUTTON')).toBe(true);
    expect(visitedTags.has('SELECT') || visitedTags.has('INPUT')).toBe(true);
  });
});
