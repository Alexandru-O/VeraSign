import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright config for VeraSign a11y scans.
 *
 * Base URL points at the Blazor Web project. CI overrides via WEB_BASE_URL
 * env var (compose binds the web container to localhost:7165).
 *
 * No webServer block — the harness assumes the stack is already running
 * (either start-all.ps1 or docker compose up). This keeps the test
 * independent of how the app was launched.
 */
export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI
    ? [['github'], ['list'], ['json', { outputFile: 'a11y-report.json' }]]
    : [['list']],
  use: {
    baseURL: process.env.WEB_BASE_URL ?? 'http://localhost:7165',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ignoreHTTPSErrors: true,
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
});
