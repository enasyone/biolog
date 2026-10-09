// Developer tests for BIO-UX-007. SYNTHETIC: demo tenant/role/synthetic fixture data only.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

test.describe('BIO-UX-007-AC01 Cards registry is real and interactive', () => {
  test('renders a populated table with holder/status/actions, not a placeholder shell', async ({ page }) => {
    await page.goto('/cards?as=admin');
    await expect(page.getByRole('heading', { name: 'Cards', level: 1 })).toBeVisible();
    const rows = page.locator('table tbody tr');
    await expect(rows.first()).toBeVisible();
    // The status cell, not a free-text search across the whole row: "Mark lost" (an action
    // button in a later cell) also contains the substring "lost" and would otherwise match too.
    await expect(rows.first().locator('td').nth(2)).toContainText(/active|lost|recovered|replaced|revoked/i);
  });

  test('the related-links landmark still navigates to Issue Card (preserves BIO-UX-002-AC04 contract)', async ({ page }) => {
    await page.goto('/cards?as=admin');
    await page.getByRole('navigation', { name: 'Cards related pages' }).getByRole('link', { name: 'Issue Card' }).click();
    await expect(page).toHaveURL(/\/cards\/issue/);
  });
});

test.describe('BIO-UX-007-AC01/AC04 Issue Card', () => {
  test('issuing a card for an eligible person shows a success result with simulated evidence', async ({ page }) => {
    await page.goto('/cards/issue?as=admin');
    await expect(page.getByRole('heading', { name: 'Issue Card', level: 1 })).toBeVisible();
    const select = page.getByLabel('Person');
    await select.selectOption({ index: 1 });
    await page.getByRole('button', { name: 'Issue card' }).click();
    await expect(page.getByRole('status')).toContainText(/Issued card/);
    await expect(page.getByRole('status')).toContainText('SIMULATED');
    await expect(page.getByText(/Evidence: SIM-EVD-issue-/)).toBeVisible();
  });

  test('a person who already holds an active card is not offered in the list', async ({ page }) => {
    await page.goto('/cards?as=admin');
    const firstHolder = await page.locator('table tbody tr').first().locator('td').nth(1).textContent();
    await page.goto('/cards/issue?as=admin');
    const options = await page.getByLabel('Person').locator('option').allTextContents();
    expect(options.some((o) => firstHolder && o.startsWith(firstHolder))).toBe(false);
  });
});

test.describe('BIO-UX-007-AC03 lost/recovered/replaced/revoked status transitions', () => {
  test('mark a card lost from the registry, then recover it, updating the visible badge both times', async ({ page }) => {
    await page.goto('/cards?as=admin');
    const row = page.locator('table tbody tr').first();
    const cardNumber = await row.locator('td').first().textContent();

    await row.getByRole('button', { name: 'Mark lost' }).click();
    await expect(page.getByRole('status')).toContainText(/is now lost/);
    const rowAfterLost = page.locator('table tbody tr', { hasText: cardNumber! });
    await expect(rowAfterLost.getByText('lost', { exact: true })).toBeVisible();

    await rowAfterLost.getByRole('button', { name: 'Recover' }).click();
    await expect(page.getByRole('status')).toContainText(/is now recovered/);
    const rowAfterRecover = page.locator('table tbody tr', { hasText: cardNumber! });
    await expect(rowAfterRecover.getByText('recovered', { exact: true })).toBeVisible();
  });

  test('revoking a card from the registry is terminal - no further action buttons remain for that row', async ({ page }) => {
    await page.goto('/cards?as=admin');
    const row = page.locator('table tbody tr').first();
    const cardNumber = await row.locator('td').first().textContent();
    await row.getByRole('button', { name: 'Revoke card' }).click();
    await expect(page.getByRole('status')).toContainText(/is now revoked/);
    const rowAfter = page.locator('table tbody tr', { hasText: cardNumber! });
    await expect(rowAfter.getByRole('button')).toHaveCount(0);
  });

  test('Replace Card issues a new card and marks the old one replaced', async ({ page }) => {
    await page.goto('/cards/replace?as=admin');
    await expect(page.getByRole('heading', { name: 'Replace Card', level: 1 })).toBeVisible();
    await page.getByLabel('Card to replace').selectOption({ index: 1 });
    await page.getByLabel('Reason').selectOption('damaged');
    await page.getByRole('button', { name: 'Replace card' }).click();
    await expect(page.getByRole('status')).toContainText(/Card replaced/);
    await expect(page.getByText(/Evidence: SIM-EVD-replace-/)).toBeVisible();
  });
});

test.describe('BIO-UX-007-AC01/AC06 NFC/UHF', () => {
  test('encoding a tag for an active card succeeds and shows evidence, then verify read succeeds', async ({ page }) => {
    await page.goto('/nfc-uhf?as=admin');
    await expect(page.getByRole('heading', { name: 'NFC / UHF', level: 1 })).toBeVisible();
    // getByRole('combobox', ...), not getByLabel: the shell's own global search input's
    // accessible name ("Search people, cards, devices or actions…") otherwise substring-matches
    // "Card" too under getByLabel's default (case-insensitive substring, not exact) matching.
    await page.getByRole('combobox', { name: 'Card', exact: true }).selectOption({ index: 1 });
    await page.getByLabel('Tag type').selectOption('UHF');
    await page.getByRole('button', { name: 'Encode tag' }).click();
    await expect(page.getByRole('status')).toContainText(/UHF tag encoded/);

    await page.getByRole('button', { name: 'Verify read' }).click();
    await expect(page.getByRole('status')).toContainText(/Read verified/);
  });
});

test.describe('BIO-UX-007-AC01 Mobile Credentials', () => {
  test('viewing a person shows their credential status, and revoke works', async ({ page }) => {
    await page.goto('/mobile-credentials?as=admin');
    await expect(page.getByRole('heading', { name: 'Mobile Credentials', level: 1 })).toBeVisible();
    await page.getByLabel('View status').selectOption({ index: 1 });
    await expect(page.getByText(/Mobile credential status:/)).toBeVisible();
    await page.getByRole('button', { name: 'Revoke mobile credential' }).click();
    await expect(page.getByRole('status')).toContainText(/Mobile credential revoked/);
  });
});

test.describe('BIO-UX-007-AC01 Credentials view', () => {
  test('selecting a credential shows its detail panel with status and revoke action', async ({ page }) => {
    await page.goto('/credentials?as=admin');
    await expect(page.getByRole('heading', { name: 'Credentials', level: 1 })).toBeVisible();
    await page.getByLabel('View credential').selectOption({ index: 1 });
    await expect(page.getByRole('heading', { level: 2 })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Revoke credential' })).toBeVisible();
  });
});

test.describe('BIO-UX-007-AC06 Card Designer gains a real Save design action', () => {
  test('Save design is a real, working primary action alongside the existing canvas, which stays unchanged', async ({ page }) => {
    await page.goto('/card-designer?as=admin');
    // The existing BIO-CRD-002 canvas is still present and untouched.
    await expect(page.locator('[aria-label="Front card canvas"]')).toBeVisible();

    await page.getByLabel('Design name').fill('Staff ID v2 (SYNTHETIC)');
    await page.getByRole('button', { name: 'Save design' }).click();
    await expect(page.getByRole('status')).toContainText(/saved \(simulated\)/);
  });
});

test.describe('BIO-UX-007 accessibility baseline', () => {
  for (const route of ['/cards', '/cards/issue', '/cards/replace', '/nfc-uhf', '/mobile-credentials', '/credentials']) {
    test(`${route} has no axe violations on first load`, async ({ page }) => {
      await page.goto(`${route}?as=admin`);
      const results = await new AxeBuilder({ page }).analyze();
      expect(results.violations, JSON.stringify(results.violations, null, 2)).toEqual([]);
    });
  }
});
