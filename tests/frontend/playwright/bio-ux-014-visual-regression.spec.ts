/**
 * Visual-regression baselines (BIO-UX-014-AC01, AC02).
 *
 * Scope, deliberately not a full page x viewport x theme matrix (that would
 * be 8 pages x 3 viewports x 2 themes = 48 screenshots - slow to run and
 * painful to review on real drift):
 *
 * - The SHELL (Dashboard, "/") gets full coverage across every approved
 *   viewport AND theme combination (AC02's concern is shell-level responsive/
 *   theme correctness, which is uniform shell behaviour, not per-page).
 * - Every other representative page (BIO-UX-013's own MAJOR_PAGES list,
 *   reused via fixtures/approved-viewports.ts) is baselined once, at the
 *   single canonical desktop+light combination (AC01's concern is per-module
 *   content coverage, not re-proving the shell's own responsiveness at every
 *   single page - that's already covered by the Dashboard sweep plus
 *   BIO-UX-013's own no-overflow sweep at all three viewports).
 *
 * Baselines are the committed PNGs under bio-ux-014-visual-regression.spec.ts-snapshots/
 * (Playwright's default location - not gitignored, unlike test-results/). See
 * docs/features/bio-ux-014/README.md for the update procedure (BIO-UX-014-AC04).
 */
import { expect, test } from '@playwright/test';
import { VIEWPORTS, MAJOR_PAGES, type ViewportName } from './fixtures/approved-viewports';
import { THEME_STORAGE_KEY } from '../../../web/components/theme-script';

export const THEMES = ['light', 'dark'] as const;
export type Theme = (typeof THEMES)[number];

// Pin timezone/locale so any rendered date/time (e.g. the dashboard's synthetic "as of" values)
// is byte-identical regardless of which machine runs this file - scoped to this spec only, not
// the shared playwright.config.mjs, so no other story's already-accepted specs are affected.
//
// The server this spec runs against MUST also run with TZ=UTC (tests/frontend/package.json's
// test:visual/test:visual:update scripts set this) - otherwise the Dashboard's server-rendered
// "recent activity" timestamps use the server process's own local timezone while this browser
// context renders in UTC, producing different text and a genuine React hydration mismatch
// (error #418) that was found while fixing BIO-UX-014-CR-01. That mismatch is a real, separate
// application defect (any real user whose browser timezone differs from wherever the app is
// built/deployed would hit it too) - reported separately, out of this story's scope to fix.
test.use({ timezoneId: 'UTC', locale: 'en-GB' });

// Run this whole file's tests serially, one worker, regardless of which command invokes it -
// reduces (but does not by itself eliminate - see beforeAll below) contention against the one
// dev server every worker in a full test:e2e run shares.
test.describe.configure({ mode: 'serial' });

// Next.js dev mode (Turbopack) compiles each route on demand, on its FIRST request - not before
// the server reports "ready". Inside the full test:e2e run, this file can be the very first spec
// to request "/", and that cold-compile can push the response slow enough to disrupt the
// init-script/hydration timing this spec depends on (observed as html[data-theme] never getting
// set, deterministically, only ever on the first test run against a freshly-started server).
// Warming every route this file visits once, before any timed test runs, moves that one-time
// compile cost out of the tests themselves.
test.beforeAll(async ({ browser, baseURL }) => {
  const page = await browser.newPage({ baseURL });
  const routes = new Set(['/', ...MAJOR_PAGES.map((p) => p.route)]);
  for (const route of routes) {
    await page.goto(route).catch(() => {});
  }
  await page.close();
});

/** Sets the theme deterministically before the app's own inline theme-script reads it on first paint. */
async function setTheme(page: import('@playwright/test').Page, theme: Theme) {
  await page.addInitScript(
    ([key, value]) => localStorage.setItem(key, value),
    [THEME_STORAGE_KEY, theme] as const,
  );
}

/** Waits for the mock-adapter loading state (BIO-UX-005-AC05) to resolve before capturing a stable frame. */
async function waitForSettled(page: import('@playwright/test').Page) {
  await expect(page.getByText('Loading…')).toHaveCount(0, { timeout: 10_000 });
}

async function captureBaseline(
  page: import('@playwright/test').Page,
  name: string,
  viewport: (typeof VIEWPORTS)[ViewportName],
  theme: Theme,
  route: string,
) {
  // Order matters: page.setViewportSize() called AFTER page.addInitScript() but before
  // page.goto() can cause the init script not to fire for that navigation (verified directly -
  // swapping the order alone flips data-theme from consistently null to consistently correct).
  // setViewportSize must come first.
  await page.setViewportSize(viewport);
  await setTheme(page, theme);
  await page.goto(route);
  await waitForSettled(page);
  // BIO-UX-014-CR-01 (Chioma round 1): assert the theme actually took effect before capturing -
  // a silent no-op here previously made every "dark" baseline byte-identical to "light". The
  // reload below is defense-in-depth, not the primary fix (that's the beforeAll warm-up above,
  // for the Turbopack cold-compile cause) - if some other transient hiccup still leaves the
  // attribute unset, one reload deterministically re-triggers the init script against a fresh
  // navigation; if it's STILL wrong after that, it's a real regression and the test correctly
  // fails rather than retrying indefinitely.
  const html = page.locator('html');
  if ((await html.getAttribute('data-theme')) !== theme) {
    await page.reload();
    await waitForSettled(page);
  }
  await expect(html).toHaveAttribute('data-theme', theme, { timeout: 5_000 });
  await expect(page).toHaveScreenshot(`${name}.png`, {
    fullPage: true,
    animations: 'disabled',
    timeout: 10_000,
    // Next.js 16's dev overlay (<nextjs-portal>) isn't fully suppressed by next.config.mjs's
    // devIndicators: false - an "N issues" badge can still render (and its expanded/collapsed
    // state, or the count itself, isn't deterministic run-to-run). It's dev-only chrome, never
    // part of the approved BIOLOG interface this baseline exists to protect, so it's masked out
    // of the comparison entirely rather than chased with more config flags.
    mask: [page.locator('nextjs-portal')],
  });
}

test.describe('BIO-UX-014-AC01/AC02 shell baseline across every approved viewport and theme', () => {
  for (const [viewportName, viewport] of Object.entries(VIEWPORTS) as [ViewportName, (typeof VIEWPORTS)[ViewportName]][]) {
    for (const theme of THEMES) {
      test(`dashboard at ${viewportName} (${viewport.width}px), ${theme} theme`, async ({ page }) => {
        await captureBaseline(page, `dashboard-${viewportName}-${theme}`, viewport, theme, '/');
      });
    }
  }
});

test.describe('BIO-UX-014-AC01 representative page from every major module (desktop, light theme)', () => {
  for (const { route } of MAJOR_PAGES) {
    if (route === '/') continue; // Dashboard already gets full coverage above.
    test(`${route}`, async ({ page }) => {
      const id = route.replace(/^\//, '').replace(/[/?=&]/g, '-') || 'dashboard';
      await captureBaseline(page, id, VIEWPORTS.desktop, 'light', route);
    });
  }
});
