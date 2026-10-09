import { defineConfig } from "@playwright/test";

const baseURL = process.env.BIOLOG_BASE_URL ?? "http://127.0.0.1:3000";

export default defineConfig({
  testDir: "./playwright",
  fullyParallel: true,
  retries: 0,
  use: { baseURL },
  // Auto-starts the web/ app (BIO-FND-007) so `pnpm --dir tests/frontend test:e2e`
  // is self-contained. Skipped when BIOLOG_BASE_URL points at a server started
  // some other way.
  webServer: process.env.BIOLOG_BASE_URL
    ? undefined
    : {
        command: "pnpm --dir ../../web dev -p 3000 -H 127.0.0.1",
        url: baseURL,
        reuseExistingServer: !process.env.CI,
        timeout: 60_000,
        // BIO-UX-014-CR-01: without a pinned server timezone, any spec that renders a
        // server-formatted date/time (e.g. the Dashboard's "recent activity" list) can hit a
        // real React hydration mismatch whenever the machine running this suite isn't already on
        // UTC - the server formats in its local timezone, any browser context configured with an
        // explicit timezoneId (as bio-ux-014-visual-regression.spec.ts is, for deterministic
        // screenshots) formats differently, and the two disagree. UTC here costs nothing for
        // every other spec (none assert exact wall-clock text) and removes this whole class of
        // flake for this and any future spec.
        env: { ...process.env, TZ: "UTC" },
      },
});
