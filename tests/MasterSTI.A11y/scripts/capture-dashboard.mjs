// Full-page screenshot of the authenticated Dashboard for the README hero.
// Assumes the stack is running (start-all.ps1 or docker compose).
// Run: node scripts/capture-dashboard.mjs

import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..', '..', '..');
const outDir = path.join(repoRoot, 'docs', 'screenshots');
const baseUrl = process.env.WEB_BASE_URL ?? 'https://localhost:7165';

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({
  ignoreHTTPSErrors: true,
  viewport: { width: 1440, height: 900 },
});
const page = await ctx.newPage();

await page.goto(`${baseUrl}/login`, { waitUntil: 'networkidle' });

await page.locator('input[autocomplete="email"]').fill('admin@verasign.demo');
await page.locator('input[autocomplete="current-password"]').fill('Demo!2025');
await page.locator('button[type="submit"]').first().click();

await page.waitForURL(/\/dashboard/, { timeout: 15_000 });
await page.waitForSelector('.vs-infra', { timeout: 15_000 });
// Let live polling + sparkline render settle before capturing.
await page.waitForTimeout(2500);

const fullPath = path.join(outDir, 'dashboard.png');
await page.screenshot({ path: fullPath, fullPage: true });
console.log(`saved: ${fullPath}`);

await browser.close();
