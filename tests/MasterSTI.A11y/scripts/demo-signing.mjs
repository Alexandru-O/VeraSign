// S9 alt — drive the legacy /signing/{id} flow end-to-end (the working path).
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const a11yRoot = path.resolve(__dirname, '..');
const outDir = path.join(a11yRoot, 'test-results', 'demo');
const baseUrl = process.env.WEB_BASE_URL ?? 'https://localhost:7165';

const signingRequestId = process.argv[2];
if (!signingRequestId) { console.error('Pass signingRequestId as arg'); process.exit(1); }

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
  await gotoBlazor(baseUrl + '/signing/' + signingRequestId + '/wallet-auth');
  await snap('S9b_walletauth_page');
  const txt = await page.locator('body').innerText();
  // Click "Simulate EUDIW Response (Dev)"
  const simBtn = page.getByRole('button', { name: /Simulate|Simulează/i }).first();
  if (await simBtn.count() === 0) throw new Error('No simulate button on wallet-auth page. Body excerpt: ' + txt.slice(0, 300));
  await simBtn.click();
  // Wait for redirect to /credentials
  await page.waitForURL(/\/signing\/.+\/credentials/, { timeout: 30000 });
  await snap('S9b_credentials');
  await page.waitForTimeout(1500);
  // PIN + sign
  const pinInput = page.locator('input[type="password"], input[name*="pin" i], input[placeholder*="PIN" i]').first();
  if (await pinInput.count() > 0) await pinInput.fill('123456');
  // Click first credential card
  const credCard = page.locator('.card.cursor-pointer, .card').first();
  await credCard.click();
  await page.waitForTimeout(800);
  // Fill PIN (>=4 chars required)
  const pin = page.locator('input[type="password"]').first();
  await pin.click();
  await pin.fill('123456');
  await pin.press('Tab');
  await page.waitForTimeout(600);
  const signBtn = page.getByRole('button', { name: /Sign Document|Semnează|^Sign$/i }).first();
  await signBtn.click();
  await page.waitForURL(/\/signed\//, { timeout: 60000 });
  await snap('S9b_signed');
  const signedId = (page.url().match(/signed\/([0-9a-f-]+)/) || [])[1];
  // S10 validation report
  await gotoBlazor(baseUrl + '/signed/' + signedId + '/validate');
  await page.waitForTimeout(1500);
  await snap('S10b_validate');
  record('S9 — Legacy /signing flow end-to-end', 'PASS', { signedId, signedUrl: page.url() });
} catch (e) {
  await snap('S9b_FAIL');
  record('S9 — Legacy /signing flow end-to-end', 'FAIL', null, e);
}

await browser.close();
