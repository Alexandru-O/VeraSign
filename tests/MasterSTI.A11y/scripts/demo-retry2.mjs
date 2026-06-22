// Retry S8 with correct kebab selector + login first.
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const a11yRoot = path.resolve(__dirname, '..');
const outDir = path.join(a11yRoot, 'test-results', 'demo');
const baseUrl = process.env.WEB_BASE_URL ?? 'https://localhost:7165';

function record(name, status, details, error) {
  console.log('SCENARIO ' + JSON.stringify({ name, status, details: details ?? null, error: error?.message ?? null }));
}

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
const snap = async n => { try { await page.screenshot({ path: path.join(outDir, n + '.png') }); } catch {} };

async function gotoBlazor(url) {
  await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(800);
}

async function login() {
  await gotoBlazor(baseUrl + '/login');
  await page.locator('input[autocomplete="email"]').click();
  await page.locator('input[autocomplete="email"]').fill('admin@verasign.demo');
  await page.locator('input[autocomplete="current-password"]').click();
  await page.locator('input[autocomplete="current-password"]').fill('Demo!2025');
  await page.locator('input[autocomplete="current-password"]').press('Tab');
  await page.waitForTimeout(300);
  await page.getByRole('button', { name: /Continuă/i }).first().click();
  await page.waitForURL(/\/dashboard/, { timeout: 30000 });
  await page.waitForSelector('.vs-dash2', { timeout: 10000 });
}

try {
  await login();
  // List Awaiting docs
  await gotoBlazor(baseUrl + '/documents?status=awaiting');
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  const rows = await page.locator('table.vs-docs__table tbody tr').count();
  if (rows === 0) throw new Error('No Awaiting docs found in list');
  // Click first kebab
  const kebab = page.locator('.vs-docs__kebab').first();
  await kebab.click();
  await page.waitForTimeout(400);
  const cancelItem = page.locator('.vs-docs__kebab-menu button.vs-docs__kebab-item').filter({ hasText: /Anulează/i }).first();
  if (await cancelItem.count() === 0) throw new Error('Cancel menu item not visible');
  await cancelItem.click();
  await page.waitForTimeout(600);
  await snap('S8_retry2_modal');
  // Modal confirm — the cancel modal in razor has Variant=danger button "Anulează cererea"
  const confirmBtn = page.getByRole('button', { name: /^Anulează cererea$|^Confirmă$|^Anulează$/i }).last();
  await confirmBtn.click();
  await page.waitForTimeout(2000);
  await snap('S8_retry2_after');
  // Re-list cancelled
  await gotoBlazor(baseUrl + '/documents?status=cancelled');
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  const cancelledRows = await page.locator('table.vs-docs__table tbody tr').count();
  record('S8 — Cancel (retry2)', 'PASS', { awaitingRowsBefore: rows, cancelledRowsAfter: cancelledRows });
} catch (e) {
  await snap('S8_retry2_FAIL');
  record('S8 — Cancel (retry2)', 'FAIL', null, e);
}

await browser.close();
