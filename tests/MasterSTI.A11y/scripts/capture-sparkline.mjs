// One-off screenshot of the Dashboard InfraHealthBar with 7d sparkline strip
// populated. Assumes the stack is running (start-all.ps1 or docker compose).
// Run: node scripts/capture-sparkline.mjs

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
// Give the live polling + sparkline render a beat to settle.
await page.waitForTimeout(2000);

const fullPath = path.join(outDir, '05b-infra-sparkline.png');
await page.locator('.vs-infra').screenshot({ path: fullPath });
console.log(`saved: ${fullPath}`);

await browser.close();
