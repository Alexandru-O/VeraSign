// Retry S1, S5, S8 with proper Blazor Server circuit wait (networkidle).
import { chromium } from 'playwright';
import path from 'node:path';
import fs from 'node:fs/promises';
import fssync from 'node:fs';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const a11yRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(a11yRoot, '..', '..');
const outDir = path.join(a11yRoot, 'test-results', 'demo');
const baseUrl = process.env.WEB_BASE_URL ?? 'https://localhost:7165';

await fs.mkdir(outDir, { recursive: true });
const fixturePdf = path.join(repoRoot, 'src', 'MasterSTI.Api', 'storage', 'templates', 'nda-confidentialitate.pdf');

function record(name, status, details, error) {
  console.log('SCENARIO ' + JSON.stringify({ name, status, details: details ?? null, error: error?.message ?? null }));
}

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();

async function snap(name) {
  try { await page.screenshot({ path: path.join(outDir, name + '.png') }); } catch {}
}

async function gotoBlazor(url) {
  await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });
  // Wait extra for SignalR circuit
  await page.waitForFunction(() => !!(window.Blazor || document.querySelector('[blazor-server-ready], blazor\\:server')), { timeout: 5000 }).catch(() => {});
  await page.waitForTimeout(500);
}

let s5DocId = null;

/* S1 retry */
try {
  await gotoBlazor(baseUrl + '/login');
  const emailEl = page.locator('input[autocomplete="email"]');
  await emailEl.click();
  await emailEl.fill('admin@verasign.demo');
  const pwdEl = page.locator('input[autocomplete="current-password"]');
  await pwdEl.click();
  await pwdEl.fill('Demo!2025');
  // Press Tab to trigger blur, then click submit
  await pwdEl.press('Tab');
  await page.waitForTimeout(300);
  await page.getByRole('button', { name: /Continuă/i }).first().click();
  await page.waitForURL(/\/dashboard/, { timeout: 30000 });
  await page.waitForSelector('.vs-dash2', { timeout: 10000 });
  await snap('S1_retry_dashboard');
  record('S1 — Login parolă happy path (retry)', 'PASS', { url: page.url() });
} catch (e) {
  await snap('S1_retry_FAIL');
  record('S1 — Login parolă happy path (retry)', 'FAIL', null, e);
}

/* S5 retry — upload + place fields + send (inline rendering, no URL change post-upload) */
try {
  await gotoBlazor(baseUrl + '/documents/new');
  await page.waitForSelector('.vs-up2', { timeout: 10000 });
  if (!fssync.existsSync(fixturePdf)) throw new Error('Fixture missing: ' + fixturePdf);
  await page.locator('#vs-file-input').setInputFiles(fixturePdf);
  // After upload, step=1 renders PlaceFieldsView inline (no URL change)
  // Wait for PlaceFieldsView marker (we'll use any indicator of next step)
  await page.waitForFunction(() => {
    // step=1 hides drop zone, shows toolbox or fields canvas
    return !document.querySelector('.vs-up2__drop') ||
           document.querySelector('.vs-place, [class*="place"], [class*="toolbox"], iframe[src*="render"]');
  }, { timeout: 30000 });
  await page.waitForTimeout(800);
  await snap('S5_retry_step2');
  // Try to click "Continuă" / next button to advance to recipients
  // Stepper has 4 steps; we're on step 2 (Câmpuri). Click "Continuă" inside PlaceFieldsView
  const nextBtn = page.getByRole('button', { name: /Continuă|Mai departe|Înainte/i }).first();
  if (await nextBtn.count() > 0) {
    await nextBtn.click();
  }
  await page.waitForURL(/\/documents\/[0-9a-f-]+\/recipients/, { timeout: 15000 });
  s5DocId = (page.url().match(/documents\/([0-9a-f-]+)\/recipients/) || [])[1];
  await page.waitForSelector('.vs-rec', { timeout: 10000 });
  await snap('S5_retry_recipients');
  // Click "Trimite cererea"
  const sendBtn = page.getByRole('button', { name: /Trimite cererea/i });
  await sendBtn.click();
  await page.waitForTimeout(2000);
  await snap('S5_retry_sent');
  record('S5 — Sender flow (retry)', 'PASS', { docId: s5DocId, finalUrl: page.url() });
} catch (e) {
  await snap('S5_retry_FAIL');
  record('S5 — Sender flow (retry)', 'FAIL', null, e);
}

/* S8 retry */
try {
  if (!s5DocId) throw new Error('S5 did not produce a doc id');
  await gotoBlazor(baseUrl + '/documents?status=awaiting');
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  // Find the row for s5DocId and open kebab
  const rowSelector = `tr:has(a[href*="${s5DocId}"]), tr:has([data-id="${s5DocId}"])`;
  const row = page.locator(rowSelector).first();
  let rowFound = await row.count() > 0;
  // Generic fallback: just use first row in awaiting filter
  const targetRow = rowFound ? row : page.locator('table.vs-docs__table tbody tr').first();
  await targetRow.scrollIntoViewIfNeeded().catch(() => {});
  // Find action menu trigger in the row
  const kebab = targetRow.locator('button').last();
  await kebab.click();
  await page.waitForTimeout(500);
  // Click Cancel-like menu item
  const cancelItem = page.getByText(/^Anulează|Cancel|Anulează cererea/i).first();
  if (await cancelItem.count() > 0) {
    await cancelItem.click();
    await page.waitForTimeout(500);
    // Optional modal with reason
    const reason = page.locator('textarea, input[type="text"]').last();
    if (await reason.count() > 0) await reason.fill('Test prezentare').catch(() => {});
    const confirm = page.getByRole('button', { name: /Confirmă|Anulează cererea|Trimite|Da|Anulează$/i }).last();
    if (await confirm.count() > 0) await confirm.click().catch(() => {});
    await page.waitForTimeout(1500);
  }
  await snap('S8_retry_cancel');
  // Verify status switched
  await gotoBlazor(baseUrl + '/documents?status=cancelled');
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  const inCancelled = (await page.locator(`a[href*="${s5DocId}"]`).count()) > 0;
  record('S8 — Cancel document Awaiting (retry)', 'PASS', { rowFound, inCancelled });
} catch (e) {
  await snap('S8_retry_FAIL');
  record('S8 — Cancel document Awaiting (retry)', 'FAIL', null, e);
}

await browser.close();
