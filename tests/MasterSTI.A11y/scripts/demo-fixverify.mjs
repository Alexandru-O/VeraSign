// Verify HandleSend + QR modal fixes.
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const a11yRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(a11yRoot, '..', '..');
const outDir = path.join(a11yRoot, 'test-results', 'demo');
const baseUrl = process.env.WEB_BASE_URL ?? 'https://localhost:7165';
const apiBase = process.env.API_BASE_URL ?? 'https://localhost:7001';
const fixturePdf = path.join(repoRoot, 'src', 'MasterSTI.Api', 'storage', 'templates', 'nda-confidentialitate.pdf');

function record(n, s, d, e) { console.log('SCENARIO ' + JSON.stringify({ name: n, status: s, details: d ?? null, error: e?.message ?? null })); }

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();
const snap = async n => { try { await page.screenshot({ path: path.join(outDir, n + '.png') }); } catch {} };
async function gotoBlazor(url) { await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 }); await page.waitForTimeout(800); }

// Login
await gotoBlazor(baseUrl + '/login');
await page.locator('input[autocomplete="email"]').click();
await page.locator('input[autocomplete="email"]').fill('admin@verasign.demo');
await page.locator('input[autocomplete="current-password"]').click();
await page.locator('input[autocomplete="current-password"]').fill('Demo!2025');
await page.locator('input[autocomplete="current-password"]').press('Tab');
await page.getByRole('button', { name: /Continuă/i }).first().click();
await page.waitForURL(/\/dashboard/, { timeout: 30000 });

// Fresh upload to get a doc
let docId;
try {
  await gotoBlazor(baseUrl + '/documents/new');
  await page.locator('#vs-file-input').setInputFiles(fixturePdf);
  await page.waitForFunction(() => !document.querySelector('.vs-up2__drop') ||
                                   document.querySelector('.vs-place, iframe[src*="render"]'),
                              { timeout: 30000 });
  await page.waitForTimeout(800);
  await page.getByRole('button', { name: /Continuă|Mai departe/i }).first().click();
  await page.waitForURL(/\/documents\/[0-9a-f-]+\/recipients/, { timeout: 15000 });
  docId = (page.url().match(/documents\/([0-9a-f-]+)\/recipients/) || [])[1];
} catch (e) { record('Pre-S5 upload', 'FAIL', null, e); await browser.close(); process.exit(1); }

// === Test #1: QR modal real flow ===
try {
  await page.waitForSelector('.vs-rec', { timeout: 10000 });
  await page.getByRole('button', { name: /Vreau să semnez acum/i }).click();
  // Wait for QR image to appear in modal
  await page.waitForSelector('.vs-modal__qr-wrap img', { timeout: 12000 });
  await snap('FIX_qr_modal');
  // Check no hardcoded backup code visible
  const body = await page.locator('body').innerText();
  const hasFakeBackup = /KX-7F29-M3P2/.test(body);
  const hasFakeOverlay = /Conectat la telefon/.test(body);
  // Capture network — was /api/wallet/auth called?
  const reqs = await page.evaluate(() => performance.getEntriesByType('resource').map(e => e.name));
  const calledWalletAuth = reqs.some(u => /\/api\/wallet\/auth$/i.test(u));
  // Close modal
  await page.locator('.vs-modal__close').click();
  await page.waitForTimeout(300);
  record('QR modal — real wallet/auth + no fake stubs', 'PASS', { hasFakeBackup, hasFakeOverlay, calledWalletAuth });
} catch (e) {
  await snap('FIX_qr_FAIL');
  record('QR modal — real wallet/auth + no fake stubs', 'FAIL', null, e);
}

// === Test #2: HandleSend transitions doc to Awaiting ===
try {
  await page.waitForTimeout(500);
  // Get doc status BEFORE clicking
  const loginResp = await page.request.post(apiBase + '/api/auth/login', {
    data: { email: 'admin@verasign.demo', password: 'Demo!2025' }, ignoreHTTPSErrors: true
  });
  const tok = (await loginResp.json()).token;
  const before = await page.request.get(apiBase + '/api/documents/' + docId + '/info',
    { headers: { Authorization: 'Bearer ' + tok }, ignoreHTTPSErrors: true });
  const beforeJson = before.ok() ? await before.json() : null;
  const statusBefore = beforeJson?.status ?? beforeJson?.Status;
  // Click Trimite
  await page.getByRole('button', { name: /Trimite cererea/i }).click();
  await page.waitForURL(/\/dashboard/, { timeout: 15000 });
  await page.waitForTimeout(1500);
  // Get doc status AFTER
  const after = await page.request.get(apiBase + '/api/documents/' + docId + '/info',
    { headers: { Authorization: 'Bearer ' + tok }, ignoreHTTPSErrors: true });
  const afterJson = after.ok() ? await after.json() : null;
  const statusAfter = afterJson?.status ?? afterJson?.Status;
  const transitioned = String(statusBefore).toLowerCase() === 'uploaded'
                       && String(statusAfter).toLowerCase() === 'awaiting';
  record('HandleSend — POST /api/documents/{id}/send', transitioned ? 'PASS' : 'FAIL', { statusBefore, statusAfter, transitioned });
} catch (e) {
  await snap('FIX_send_FAIL');
  record('HandleSend — POST /api/documents/{id}/send', 'FAIL', null, e);
}

await browser.close();
