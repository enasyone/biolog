import { expect, test } from "@playwright/test";

test("playwright harness loads a synthetic document", async ({ page }) => {
  await page.setContent("<main data-classification='SYNTHETIC'>BIOLOG</main>");
  await expect(page.locator("main")).toHaveAttribute("data-classification", "SYNTHETIC");
});
