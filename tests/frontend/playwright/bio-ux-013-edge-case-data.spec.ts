// Developer tests for BIO-UX-013-AC05. SYNTHETIC: no real data.
import { expect, test, type Page } from '@playwright/test';

async function hasNoHorizontalOverflow(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);
}

test.describe('BIO-UX-013-AC05 a genuinely long, unbroken Nigerian name does not break layout', () => {
  test('a 30+ character single-token first/last name renders on the Enrolment review step without page overflow, at mobile width', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/enrolment');
    // Deliberately unbroken (no spaces/hyphens) — the worst case for wrapping, and a real,
    // plausible Yoruba compound given name pattern, not an artificial stress string.
    await page.getByLabel('First name').fill('Oluwadamilareoluwaseunfunmilayo');
    await page.getByLabel('Last name').fill('Adegboyegabalogun');
    await page.getByRole('button', { name: 'Next' }).click(); // Identity -> Profile
    await page.getByRole('button', { name: 'Next' }).click(); // Profile -> Photo
    await page.getByRole('button', { name: 'Next' }).click(); // Photo -> Biometric
    await page.getByRole('button', { name: 'Next' }).click(); // Biometric -> Credential
    await page.getByRole('button', { name: 'Next' }).click(); // Credential -> Review
    await expect(page.getByRole('heading', { name: 'Review' })).toBeVisible();
    await expect(page.getByText('Oluwadamilareoluwaseunfunmilayo Adegboyegabalogun')).toBeVisible();
    expect(await hasNoHorizontalOverflow(page)).toBe(true);
  });

  test('the longest name already in the synthetic pool renders without overflow on People, at mobile width', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/people?as=admin');
    expect(await hasNoHorizontalOverflow(page)).toBe(true);
  });
});

test.describe('BIO-UX-013-AC05 missing optional fields render gracefully, not as "undefined"/"null"', () => {
  test('a credential with no statusReason (the common case — only lost/revoked ones have one) shows no literal "undefined"', async ({
    page,
  }) => {
    await page.goto('/cards');
    const bodyText = await page.locator('body').innerText();
    expect(bodyText).not.toContain('undefined');
    expect(bodyText).not.toContain('null');
  });

  test('a person with no date of birth entered completes the Enrolment review step showing an em dash, not blank/undefined', async ({
    page,
  }) => {
    await page.goto('/enrolment');
    await page.getByLabel('First name').fill('Ada');
    await page.getByLabel('Last name').fill('Eze');
    // Date of birth left empty on purpose.
    await page.getByRole('button', { name: 'Next' }).click();
    await page.getByRole('button', { name: 'Next' }).click();
    await page.getByRole('button', { name: 'Next' }).click();
    await page.getByRole('button', { name: 'Next' }).click();
    await page.getByRole('button', { name: 'Next' }).click();
    const dobRow = page.locator('dt', { hasText: 'Date of birth' }).locator('xpath=following-sibling::dd[1]');
    await expect(dobRow).toHaveText('—');
  });
});

test.describe('BIO-UX-013-AC05 a dense table (many rows) does not break layout', () => {
  test('Audit\'s access-event table, which lists every event for the tenant, has no horizontal page overflow at mobile width', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/audit');
    const rows = page.locator('table tbody tr');
    expect(await rows.count()).toBeGreaterThan(5); // confirms this is actually a non-trivial table, not an empty state
    expect(await hasNoHorizontalOverflow(page)).toBe(true);
  });
});
