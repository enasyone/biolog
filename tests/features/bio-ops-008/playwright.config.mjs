const baseURL = process.env.BIOLOG_BASE_URL ?? 'http://127.0.0.1:3018';

export default {
  testDir: '.',
  testMatch: 'bio-ops-008.spec.ts',
  fullyParallel: false,
  retries: 0,
  workers: 1,
  use: { baseURL },
  webServer: process.env.BIOLOG_BASE_URL
    ? undefined
    : {
        command: 'pnpm --dir ../../../web dev -p 3018 -H 127.0.0.1',
        url: baseURL,
        reuseExistingServer: !process.env.CI,
        timeout: 60_000,
        env: { ...process.env, TZ: 'UTC' },
      },
};
