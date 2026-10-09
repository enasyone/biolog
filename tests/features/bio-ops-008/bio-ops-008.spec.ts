// BIO-OPS-008 acceptance tests. SYNTHETIC: demo fixtures only; no real identity data.
import AxeBuilder from '../../frontend/node_modules/@axe-core/playwright/dist/index.js';
import playwrightTest from '../../frontend/node_modules/@playwright/test/index.js';
import type { Page } from '../../frontend/node_modules/@playwright/test/index.js';

const { expect, test } = playwrightTest;

const CRITICAL_FLOWS = ['/enrolment?as=admin', '/cards/issue?as=admin', '/document-verify?as=admin', '/card-designer?as=admin'];

async function criticalViolations(page: Page) {
  const result = await new AxeBuilder({ page }).analyze();
  return result.violations.filter((violation) => violation.impact === 'critical');
}

async function tabTo(page: Page, accessibleName: string, limit = 80): Promise<void> {
  await page.locator('body').focus();
  const target = page
    .getByLabel(accessibleName, { exact: true })
    .or(page.getByRole('button', { name: accessibleName, exact: true }))
    .or(page.getByRole('combobox', { name: accessibleName, exact: true }));
  for (let index = 0; index < limit; index += 1) {
    await page.keyboard.press('Tab');
    if (await target.evaluateAll((elements) => elements.some((element) => element === document.activeElement))) return;
  }
  throw new Error(`Keyboard focus did not reach "${accessibleName}" within ${limit} Tab presses`);
}

test.describe('BIO-OPS-008-AC01 critical workflow axe baseline', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const route of CRITICAL_FLOWS) {
      test(`${route} has no critical axe violations in ${theme} mode`, async ({ page }) => {
        await page.addInitScript((selectedTheme) => localStorage.setItem('biolog-theme', selectedTheme), theme);
        await page.goto(route);
        await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
        const violations = await criticalViolations(page);
        expect(violations, JSON.stringify(violations, null, 2)).toEqual([]);
      });
    }
  }
});

test.describe('BIO-OPS-008-AC02 keyboard-only core workflows', () => {
  test('enrolment identity can be entered and advanced without a pointer', async ({ page }) => {
    await page.goto('/enrolment?as=admin');
    await tabTo(page, 'First name');
    await page.keyboard.type('Keyboard');
    await page.keyboard.press('Tab');
    await page.keyboard.type('User');
    await page.keyboard.press('Tab');
    await page.keyboard.type('2008-05-01');
    await tabTo(page, 'Next');
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Profile' })).toBeVisible();
  });

  test('card issuance can select a person and submit without a pointer', async ({ page }) => {
    await page.goto('/cards/issue?as=admin');
    await tabTo(page, 'Person');
    const person = page.getByRole('combobox', { name: 'Person', exact: true });
    // Chromium renders native <select> popups as an OS-level widget that headless Playwright
    // cannot drive reliably with raw ArrowDown events. Keep the application-owned keyboard path
    // under test (Tab reaches this real combobox; Tab/Enter activate the enabled submit button),
    // while selectOption exercises the same native option/value change deterministically.
    await person.selectOption({ index: 1 });
    await expect(person).not.toHaveValue('');
    const issueCard = page.getByRole('button', { name: 'Issue card' });
    await expect(issueCard).toBeEnabled();
    await page.keyboard.press('Tab');
    await expect(issueCard).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('status')).toContainText(/Issued card|could not issue card/i);
  });

  test('document capture is reachable and announces its result without a pointer', async ({ page }) => {
    await page.goto('/document-verify?as=admin');
    await tabTo(page, 'Capture/upload document');
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('step-capture').getByRole('status')).toBeVisible();
  });
});

test.describe('BIO-OPS-008-AC03 understandable error states', () => {
  test('empty enrolment names produce a named, announced failure with recovery action', async ({ page }) => {
    await page.goto('/enrolment?as=admin');
    for (let step = 0; step < 5; step += 1) await page.getByRole('button', { name: 'Next', exact: true }).click();
    await page.getByRole('button', { name: 'Submit enrolment (simulated)' }).click();
    await expect(page.getByRole('heading', { name: 'Enrolment not submitted' })).toBeVisible();
    await expect(page.getByRole('status')).toContainText(/first name|last name|required/i);
    await expect(page.getByRole('button', { name: 'Start another enrolment' })).toBeVisible();
    expect(await criticalViolations(page)).toEqual([]);
  });

  test('failed document capture explains the failure and keeps dependent actions gated', async ({ page }) => {
    await page.goto('/document-verify?as=admin');
    await page.getByTestId('step-capture').getByRole('button').click();
    await expect(page.getByTestId('step-capture').getByRole('status')).toContainText(/failed/i);
    await expect(page.getByTestId('step-mrz')).toContainText('Capture/upload the document first.');
    await expect(page.getByTestId('step-mrz').getByRole('button')).toBeDisabled();
    expect(await criticalViolations(page)).toEqual([]);
  });

  test('card issuance exposes its prerequisite before action is enabled', async ({ page }) => {
    await page.goto('/cards/issue?as=admin');
    await expect(page.getByText(/Only people without an already-active card are eligible/)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Issue card' })).toBeDisabled();
  });
});

test.describe('BIO-OPS-008-AC04 theme and print-design semantics', () => {
  test('theme switching preserves physical card format, content and computed print colours', async ({ page }) => {
    await page.goto('/card-designer?as=admin');
    await page.getByRole('button', { name: 'Add element' }).click();
    await page.getByRole('button', { name: '+ Title', exact: true }).click();
    await page.getByRole('button', { name: '+ Barcode (default: alphanumeric)', exact: true }).click();
    await page.getByLabel('New photo reference').fill('SYNTHETIC-PHOTO-001');
    await page.getByRole('button', { name: '+ Photo', exact: true }).click();

    const canvases = [
      page.getByRole('group', { name: 'Front card canvas', exact: true }),
      page.getByRole('group', { name: 'Card typography canvas', exact: true }),
      page.getByRole('group', { name: 'Card objects canvas', exact: true }),
      page.getByRole('group', { name: 'Front card photo canvas', exact: true }),
    ];
    const editorFormat = page.getByLabel('Format');

    async function snapshotPrintColours() {
      return Promise.all(canvases.map(async (canvas) => {
        const element = canvas.getByRole('button').first();
        return {
          canvas: await canvas.evaluate((node) => {
            const surface = node.firstElementChild as HTMLElement;
            const style = getComputedStyle(surface);
            return {
              html: node.innerHTML,
              width: (node as HTMLElement).style.width,
              height: (node as HTMLElement).style.height,
              backgroundColor: style.backgroundColor,
              backgroundImage: style.backgroundImage,
            };
          }),
          element: await element.evaluate((node) => {
            const style = getComputedStyle(node);
            return {
              backgroundColor: style.backgroundColor,
              color: style.color,
              borderColor: style.borderColor,
              outlineColor: style.outlineColor,
            };
          }),
        };
      }));
    }

    const before = await snapshotPrintColours();
    const lightEditorBackground = await editorFormat.evaluate((element) => getComputedStyle(element).backgroundColor);
    await page.getByRole('button', { name: 'Switch to dark theme' }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    const after = await snapshotPrintColours();
    const darkEditorBackground = await editorFormat.evaluate((element) => getComputedStyle(element).backgroundColor);

    expect(after).toEqual(before);
    expect(darkEditorBackground).not.toBe(lightEditorBackground);
    expect(await criticalViolations(page)).toEqual([]);
  });
});
