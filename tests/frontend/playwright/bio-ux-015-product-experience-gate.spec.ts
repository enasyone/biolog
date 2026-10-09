import { expect, test, type Page } from '@playwright/test';
import { PRODUCT_EXPERIENCE_GATE, type GateAction } from './fixtures/product-experience-gate';

test.describe.configure({ mode: 'serial' });

async function runCanonicalAction(page: Page, action: GateAction) {
  const main = page.locator('main');
  const beforeText = await main.innerText();

  if (action.kind === 'link') {
    await page.getByRole('link', { name: action.name, exact: true }).first().click();
    await expect(page).toHaveURL(new RegExp(action.destination.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
    return;
  }

  if (action.kind === 'select-detail') {
    await page.getByRole('combobox', { name: action.label, exact: true }).selectOption({ index: 1 });
    await expect.poll(async () => main.innerText()).not.toBe(beforeText);
    return;
  }

  if (action.kind === 'select-button-status') {
    await page.getByRole('combobox', { name: action.label, exact: true }).selectOption({ index: 1 });
    await page.getByRole('button', { name: action.button, exact: true }).click();
    await expect(page.getByRole('status').last()).not.toBeEmpty();
    return;
  }

  if (action.kind === 'fill-button-status') {
    await page.getByLabel(action.label, { exact: true }).fill(action.value);
    await page.getByRole('button', { name: action.button, exact: true }).click();
    await expect(page.getByRole('status').last()).not.toBeEmpty();
    return;
  }

  const button = page.getByRole('button', { name: action.name, exact: true });
  const target = action.kind === 'first-button-change' ? button.first() : button;
  await expect(target).toBeVisible();
  await expect(target).toBeEnabled();
  await target.click();

  if (action.kind === 'button-status') {
    await expect(page.getByRole('status').last()).not.toBeEmpty();
  } else {
    await expect.poll(async () => main.innerText()).not.toBe(beforeText);
  }
}

test.describe('BIO-UX-015-AC01 every approved route opens and its canonical primary action works', () => {
  for (const entry of PRODUCT_EXPERIENCE_GATE) {
    test(`${entry.id}: ${entry.href}`, async ({ page }) => {
      await page.goto(entry.href);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(entry.heading);
      await expect(page.getByRole('heading', { level: 1 })).not.toHaveText('Not yet available');
      await runCanonicalAction(page, entry.action);
    });
  }
});
