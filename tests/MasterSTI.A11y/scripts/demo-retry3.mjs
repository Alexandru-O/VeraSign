// Retry S8 on Uploaded doc (Cancel allowed per code: canCancel=true for Uploaded).
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const a11yRoot = path.resolve(__dirname, '..');
const outDir = path.join(a11yRoot, 'test-results', 'demo');
const baseUrl = process.env.WEB_BASE_URL ?? 'https://localhost:7165';

function record(n, s, d, e) { console.log('SCENARIO ' + JSON.stringify({ name: n, status: s, details: d ?? null, error: e?.message ?? null })); }

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
const snap = async n => { try { await page.screenshot({ path: path.join(outDir, n + '.png') }); } catch {} };

async function gotoBlazor(url) { await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 }); await page.waitForTimeout(800); }

await gotoBlazor(baseUrl + '/login');
await page.locator('input[autocomplete="email"]').click();
await page.locator('input[autocomplete="email"]').fill('admin@verasign.demo');
await page.locator('input[autocomplete="current-password"]').click();
await page.locator('input[autocomplete="current-password"]').fill('Demo!2025');
await page.locator('input[autocomplete="current-password"]').press('Tab');
await page.getByRole('button', { name: /Continuă/i }).first().click();
await page.waitForURL(/\/dashboard/, { timeout: 30000 });

try {
  await gotoBlazor(baseUrl + '/documents?status=uploaded');
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  const rowsBefore = await page.locator('table.vs-docs__table tbody tr').count();
  if (rowsBefore === 0) throw new Error('No Uploaded docs');
  await page.locator('.vs-docs__kebab').first().click();
  await page.waitForTimeout(400);
  const cancelItem = page.locator('.vs-docs__kebab-menu button.vs-docs__kebab-item').filter({ hasText: /Anulează/i }).first();
  if (await cancelItem.count() === 0) throw new Error('Cancel menu item missing');
  await cancelItem.click();
  await page.waitForTimeout(600);
  await snap('S8_retry3_modal');
  const reasonTa = page.locator('#vs-docs-cancel-reason');
  if (await reasonTa.count() > 0) await reasonTa.fill('Test prezentare');
  const confirmBtn = page.getByRole('button', { name: /Confirmă anularea/i }).last();
  await confirmBtn.click();
  await page.waitForTimeout(2000);
  await snap('S8_retry3_done');
  // List cancelled
  await gotoBlazor(baseUrl + '/documents?status=cancelled');
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  const cancelledAfter = await page.locator('table.vs-docs__table tbody tr').count();
  record('S8 — Cancel Uploaded doc (retry3)', 'PASS', { rowsBefore, cancelledAfter });
} catch (e) {
  await snap('S8_retry3_FAIL');
  record('S8 — Cancel Uploaded doc (retry3)', 'FAIL', null, e);
}

await browser.close();
