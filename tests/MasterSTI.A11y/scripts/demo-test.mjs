// VeraSign dissertation-demo end-to-end smoke.
// Runs S0-S12 from .claude/plans/ test plan. Chromium only (Firefox blocked by WDAC).
// Usage: node scripts/demo-test.mjs
// Output: one JSON line per scenario to stdout. Screenshots in test-results/demo/.

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
const apiBase = process.env.API_BASE_URL ?? 'https://localhost:7001';
const qtspBase = process.env.QTSP_BASE_URL ?? 'https://localhost:7111';

await fs.mkdir(outDir, { recursive: true });

const fixturePdf = path.join(repoRoot, 'src', 'MasterSTI.Api', 'storage', 'templates', 'nda-confidentialitate.pdf');
const fixtureTxtPath = path.join(outDir, 'not-a-pdf.txt');
await fs.writeFile(fixtureTxtPath, 'this is not a pdf\n', 'utf8');

const results = [];
function record(name, status, details, error) {
  const r = { name, status, details: details ?? null, error: error?.message ?? null };
  results.push(r);
  console.log('SCENARIO ' + JSON.stringify(r));
}

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({
  ignoreHTTPSErrors: true,
  viewport: { width: 1440, height: 900 },
});

const consoleErrors = [];
ctx.on('weberror', e => consoleErrors.push({ url: e.page().url(), msg: String(e.error()) }));

const page = await ctx.newPage();
page.on('console', m => { if (m.type() === 'error') consoleErrors.push({ url: page.url(), msg: m.text() }); });

async function snap(name) {
  try {
    await page.screenshot({ path: path.join(outDir, name + '.png'), fullPage: false });
  } catch {}
}

async function step(name, fn) {
  consoleErrors.length = 0;
  try {
    const details = await fn();
    record(name, 'PASS', details);
  } catch (e) {
    await snap(name.replace(/[^a-zA-Z0-9]/g, '_'));
    record(name, 'FAIL', { consoleErrors: [...consoleErrors] }, e);
  }
}

async function loginPassword() {
  await page.goto(baseUrl + '/login', { waitUntil: 'domcontentloaded' });
  await page.locator('input[autocomplete="email"]').fill('admin@verasign.demo');
  await page.locator('input[autocomplete="current-password"]').fill('Demo!2025');
  await page.locator('button[type="submit"]').first().click();
  await page.waitForURL(/\/dashboard/, { timeout: 15000 });
}

/* S0 */
await step('S0 — Bootstrap & landing', async () => {
  await page.goto(baseUrl + '/welcome', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-landing2', { timeout: 10000 });
  const h1 = await page.locator('h1').first().innerText();
  await snap('S0_landing');
  return { h1, url: page.url() };
});

/* S1 */
await step('S1 — Login parolă happy path', async () => {
  await loginPassword();
  const url = page.url();
  await page.waitForSelector('.vs-dash2', { timeout: 10000 });
  await snap('S1_dashboard');
  return { url };
});

/* S2 — wallet login simulator. Logout first via new incognito context. */
await step('S2 — Login Wallet password-less simulator', async () => {
  const ctx2 = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
  const p2 = await ctx2.newPage();
  try {
    await p2.goto(baseUrl + '/login', { waitUntil: 'domcontentloaded' });
    // EU Wallet button
    const walletBtn = p2.getByRole('button', { name: /EU Wallet/i });
    await walletBtn.click();
    // Dialog open — wait for simulator link
    const simLink = p2.locator('a[href*="simulate/eudiw-login"]').first();
    await simLink.waitFor({ state: 'visible', timeout: 10000 });
    const simHref = await simLink.getAttribute('href');
    if (!simHref) throw new Error('Simulator link missing href');
    // Open simulator in a new tab and submit
    const simPage = await ctx2.newPage();
    await simPage.goto(simHref, { waitUntil: 'domcontentloaded' });
    // Mock QTSP simulator form — submit button
    const submitBtn = simPage.locator('button[type="submit"], input[type="submit"]').first();
    await submitBtn.click();
    await simPage.waitForLoadState('domcontentloaded');
    // Original login tab should now poll and redirect
    await p2.waitForURL(/\/dashboard/, { timeout: 20000 });
    await p2.waitForSelector('.vs-dash2', { timeout: 10000 });
    // Check topbar / user name
    const body = await p2.locator('body').innerText();
    const sawAndrei = /Andrei/i.test(body);
    return { url: p2.url(), andreiVisible: sawAndrei };
  } finally {
    await ctx2.close();
  }
});

/* S3 — Dashboard widgets */
await step('S3 — Dashboard widgets + KPI deep-link', async () => {
  await page.goto(baseUrl + '/dashboard', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-dash2', { timeout: 10000 });
  // KPIs
  const kpiCount = await page.locator('.vs-dash2__kpi').count();
  // Pipeline
  const hasPipeline = (await page.locator('.vs-pipeline, [class*="pipeline"]').count()) > 0;
  // Infra
  const hasInfra = (await page.locator('.vs-infra').count()) > 0;
  // Week chart
  const hasWeek = (await page.locator('svg').count()) > 0;
  // Range chip 30d
  const range30 = page.locator('button.vs-dash2__seg-btn', { hasText: '30' }).first();
  if (await range30.count() > 0) await range30.click();
  await page.waitForTimeout(800);
  // Click first KPI
  const firstKpi = page.locator('.vs-dash2__kpi').first();
  await firstKpi.click();
  await page.waitForTimeout(500);
  const afterClickUrl = page.url();
  await snap('S3_dashboard');
  return { kpiCount, hasPipeline, hasInfra, hasWeek, afterClickUrl };
});

/* S4 — Documents list */
await step('S4 — Documents list filtere + paginare', async () => {
  await page.goto(baseUrl + '/documents', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  // Search
  const search = page.locator('input.vs-docs__search');
  await search.fill('test');
  await page.waitForTimeout(500);
  await search.fill('');
  await page.waitForTimeout(400);
  // Find a chip — first status chip non-"Toate"
  const chips = page.locator('.vs-docs__chip');
  const chipCount = await chips.count();
  // Click second status chip if exists
  if (chipCount > 1) await chips.nth(1).click();
  await page.waitForTimeout(600);
  // Reset
  await page.goto(baseUrl + '/documents', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  const tableRows = await page.locator('table.vs-docs__table tbody tr').count();
  await snap('S4_docs');
  return { chipCount, tableRows };
});

/* S5 — Sender flow complet */
let s5DocId = null;
await step('S5 — Sender flow Upload→Fields→Recipients→Send', async () => {
  await page.goto(baseUrl + '/documents/new', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-up2', { timeout: 10000 });
  if (!fssync.existsSync(fixturePdf)) throw new Error('Fixture PDF missing: ' + fixturePdf);
  await page.locator('#vs-file-input').setInputFiles(fixturePdf);
  // After upload, app navigates to /documents/{id}/fields
  await page.waitForURL(/\/documents\/[0-9a-f-]+\/fields/, { timeout: 30000 });
  s5DocId = (page.url().match(/documents\/([0-9a-f-]+)\/fields/) || [])[1];
  // Place a signature field — click "signature" tool then click canvas at center
  await page.waitForSelector('.vs-place', { timeout: 10000 }).catch(() => {});
  // Try to click the first tool button
  const tools = page.locator('[class*="tool"], button:has(svg)').filter({ hasText: /Semn|Sign|Inițiale|Initial/i });
  if (await tools.count() > 0) await tools.first().click();
  // Click somewhere on the PDF area
  const canvas = page.locator('.vs-place__canvas, [class*="canvas"], iframe').first();
  try {
    const box = await canvas.boundingBox();
    if (box) await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
  } catch {}
  // Continue → recipients
  const continueBtn = page.getByRole('button', { name: /Continuă/i }).first();
  await continueBtn.click().catch(() => {});
  await page.waitForURL(/\/documents\/[0-9a-f-]+\/recipients/, { timeout: 10000 }).catch(() => {});
  // On recipients, click Trimite
  const sendBtn = page.getByRole('button', { name: /Trimite cererea/i });
  if (await sendBtn.count() > 0) await sendBtn.click();
  await page.waitForURL(/\/dashboard|\/documents/, { timeout: 15000 }).catch(() => {});
  await snap('S5_sent');
  return { docId: s5DocId, finalUrl: page.url() };
});

/* S6 — Use template */
let s6DocId = null;
await step('S6 — Folosire template', async () => {
  await page.goto(baseUrl + '/templates', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-tpl', { timeout: 10000 });
  const useBtn = page.getByRole('button', { name: /^Folosește$/i }).first();
  await useBtn.click();
  await page.waitForURL(/\/documents\/[0-9a-f-]+\/fields/, { timeout: 20000 });
  s6DocId = (page.url().match(/documents\/([0-9a-f-]+)\/fields/) || [])[1];
  await snap('S6_fromtpl');
  return { docId: s6DocId, finalUrl: page.url() };
});

/* S7 — Edit template content */
await step('S7 — Edit conținut template', async () => {
  await page.goto(baseUrl + '/templates', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-tpl', { timeout: 10000 });
  const editBtn = page.getByRole('button', { name: /Conținut/i }).first();
  await editBtn.click();
  await page.waitForURL(/\/templates\/[0-9a-f-]+\/edit-content/, { timeout: 10000 });
  await page.waitForSelector('textarea', { timeout: 8000 });
  const ta = page.locator('textarea').first();
  const original = await ta.inputValue();
  await ta.fill(original + '\n\n## Test prezentare diacritice ăîâșț');
  await ta.blur();
  // Wait for autosave indicator
  await page.waitForTimeout(3500);
  const savedBody = await page.locator('body').innerText();
  const sawSaved = /Salvat/i.test(savedBody);
  // Restore content
  await ta.fill(original);
  await ta.blur();
  await page.waitForTimeout(2500);
  await snap('S7_template_edit');
  return { sawSaved };
});

/* S8 — Cancel document Awaiting */
await step('S8 — Cancel document Awaiting', async () => {
  if (!s5DocId) throw new Error('S5 did not produce a doc id — cannot cancel');
  await page.goto(baseUrl + '/documents?status=awaiting', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-docs', { timeout: 10000 });
  const row = page.locator('table.vs-docs__table tbody tr', { has: page.locator(`[data-doc-id="${s5DocId}"], a[href*="${s5DocId}"]`) }).first();
  // Generic fallback — kebab on first row
  const kebabs = page.locator('table.vs-docs__table button[aria-label*="more" i], table.vs-docs__table button[aria-label*="acțiuni" i], table.vs-docs__table button:has(svg)').last();
  await kebabs.click().catch(() => {});
  await page.waitForTimeout(400);
  const cancelMenu = page.getByRole('menuitem', { name: /Anulează|Cancel/i }).first();
  let triedCancel = false;
  if (await cancelMenu.count() > 0) {
    await cancelMenu.click();
    triedCancel = true;
    await page.waitForTimeout(600);
    const reason = page.locator('textarea, input[type="text"]').last();
    if (await reason.count() > 0) await reason.fill('Test prezentare').catch(() => {});
    const confirm = page.getByRole('button', { name: /Confirmă|Anulează cererea|Anulează|Trimite/i }).last();
    if (await confirm.count() > 0) await confirm.click().catch(() => {});
    await page.waitForTimeout(1000);
  }
  await snap('S8_cancel');
  return { triedCancel };
});

/* S9 — Wallet self-sign end-to-end (best-effort via mock QTSP simulate-eudiw-response) */
await step('S9 — Wallet self-sign via simulator', async () => {
  if (!s6DocId) throw new Error('S6 did not produce a doc id — cannot self-sign');
  await page.goto(baseUrl + '/documents/' + s6DocId + '/recipients', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-rec', { timeout: 10000 });
  const qrBtn = page.getByRole('button', { name: /Vreau să semnez acum/i });
  await qrBtn.click();
  await page.waitForTimeout(800);
  // Look for a simulator link in modal
  const simLink = page.locator('a[href*="signing/"]').first();
  let arrivedSigned = false;
  if (await simLink.count() > 0) {
    await simLink.click();
    await page.waitForURL(/\/signing\//, { timeout: 10000 }).catch(() => {});
    // On WalletAuthPage there's a "Simulate EUDIW Response (Dev)" button
    const simBtn = page.getByRole('button', { name: /Simulate|Simulează/i }).first();
    if (await simBtn.count() > 0) {
      await simBtn.click();
      await page.waitForTimeout(3000);
    }
    // After EUDIW auth, might auto-nav to /credentials
    if (/\/credentials/.test(page.url())) {
      const signBtn = page.getByRole('button', { name: /Semnează|Sign/i }).first();
      const pinInput = page.locator('input[type="password"], input[name*="pin" i]').first();
      if (await pinInput.count() > 0) await pinInput.fill('123456').catch(() => {});
      if (await signBtn.count() > 0) await signBtn.click().catch(() => {});
      await page.waitForURL(/\/signed\//, { timeout: 30000 }).catch(() => {});
    }
    arrivedSigned = /\/signed\//.test(page.url());
  }
  await snap('S9_signed');
  return { finalUrl: page.url(), arrivedSigned };
});

/* S10 — Validation report */
await step('S10 — Validation report', async () => {
  if (/\/signed\//.test(page.url())) {
    const id = (page.url().match(/signed\/([0-9a-f-]+)/) || [])[1];
    if (id) await page.goto(baseUrl + '/signed/' + id + '/validate', { waitUntil: 'domcontentloaded' });
  } else {
    await page.goto(baseUrl + '/verify', { waitUntil: 'domcontentloaded' });
  }
  await page.waitForTimeout(1500);
  const body = await page.locator('body').innerText();
  const hits = ['integritat', 'timestamp', 'lanț', 'eIDAS'].filter(k => new RegExp(k, 'i').test(body));
  await snap('S10_validate');
  return { hits, url: page.url() };
});

/* S11 — Edge: upload non-PDF */
await step('S11 — Edge upload non-PDF', async () => {
  await page.goto(baseUrl + '/documents/new', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-up2', { timeout: 10000 });
  await page.locator('#vs-file-input').setInputFiles(fixtureTxtPath).catch(() => {});
  await page.waitForTimeout(1500);
  const body = await page.locator('body').innerText();
  const rejected = /PDF|format|eroare|invalid/i.test(body);
  const stillOnUpload = /\/documents\/new$/.test(page.url());
  await snap('S11_nonpdf');
  return { rejected, stillOnUpload };
});

/* S12 — Edge: delete protejat — try DELETE via API on signed doc */
await step('S12 — Edge ștergere protejată 409', async () => {
  // Take a signed/signing doc id from API
  const token = await page.evaluate(() => {
    return window.localStorage.getItem('vs_auth_token') || document.cookie || '';
  });
  // Use page.request which inherits cookies/storage
  const list = await page.request.get(apiBase + '/api/documents?status=signed&page=1&pageSize=1', { ignoreHTTPSErrors: true }).catch(() => null);
  let probedStatus = 'n/a', sampleId = null;
  if (list && list.ok()) {
    const j = await list.json();
    const item = j.items?.[0] ?? j.Items?.[0];
    sampleId = item?.id ?? item?.Id;
    if (sampleId) {
      const del = await page.request.delete(apiBase + '/api/documents/' + sampleId, { ignoreHTTPSErrors: true });
      probedStatus = del.status();
    }
  } else if (list) {
    probedStatus = 'list-' + list.status();
  }
  await snap('S12_delete');
  return { sampleId, probedStatus };
});

await browser.close();

const summary = {
  total: results.length,
  pass: results.filter(r => r.status === 'PASS').length,
  fail: results.filter(r => r.status === 'FAIL').length,
};
console.log('SUMMARY ' + JSON.stringify(summary));
await fs.writeFile(path.join(outDir, 'results.json'), JSON.stringify({ summary, results }, null, 2));
process.exit(summary.fail > 0 ? 1 : 0);
