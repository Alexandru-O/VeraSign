// Recipients keyboard reorder a11y check (Phase 4, two-wallet-demo-plan).
// Focuses each row's drag handle and presses ArrowUp/ArrowDown — assert that
// the source-of-truth order column (.vs-rec__order) updates in sync.
// Usage: node scripts/recipients-reorder.mjs
// Requires: API + Web running (see start-all.ps1), admin@verasign.demo seeded.

import { chromium } from 'playwright';
import path from 'node:path';
import fs from 'node:fs/promises';
import fssync from 'node:fs';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const a11yRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(a11yRoot, '..', '..');
const outDir = path.join(a11yRoot, 'test-results', 'recipients-reorder');
const baseUrl = process.env.WEB_BASE_URL ?? 'https://localhost:7165';
await fs.mkdir(outDir, { recursive: true });

const fixturePdf = path.join(repoRoot, 'src', 'MasterSTI.Api', 'storage', 'templates', 'nda-confidentialitate.pdf');
if (!fssync.existsSync(fixturePdf)) throw new Error('Fixture PDF missing: ' + fixturePdf);

const results = [];
function record(name, status, details, error) {
  const r = { name, status, details: details ?? null, error: error?.message ?? null };
  results.push(r);
  console.log('SCENARIO ' + JSON.stringify(r));
}

const browser = await chromium.launch({ headless: true });
const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 900 } });
const page = await ctx.newPage();

async function snap(name) {
  try { await page.screenshot({ path: path.join(outDir, name + '.png'), fullPage: false }); } catch {}
}

async function step(name, fn) {
  try {
    const details = await fn();
    record(name, 'PASS', details);
  } catch (e) {
    await snap(name.replace(/[^a-zA-Z0-9]/g, '_'));
    record(name, 'FAIL', null, e);
  }
}

async function readOrderedNames() {
  return await page.locator('.vs-rec__row').evaluateAll(rows => rows.map(r => {
    const ord = r.querySelector('.vs-rec__order')?.textContent?.trim();
    const nm = r.querySelector('.vs-rec__name')?.textContent?.trim();
    return { ord, nm };
  }));
}

await step('Login', async () => {
  await page.goto(baseUrl + '/login', { waitUntil: 'domcontentloaded' });
  await page.locator('input[autocomplete="email"]').fill('admin@verasign.demo');
  await page.locator('input[autocomplete="current-password"]').fill('Demo!2025');
  await page.locator('button[type="submit"]').first().click();
  await page.waitForURL(/\/dashboard/, { timeout: 15000 });
});

let docId = null;
await step('Upload PDF → recipients page', async () => {
  await page.goto(baseUrl + '/documents/new', { waitUntil: 'domcontentloaded' });
  await page.locator('#vs-file-input').setInputFiles(fixturePdf);
  await page.waitForURL(/\/documents\/[0-9a-f-]+\/fields/, { timeout: 30000 });
  docId = (page.url().match(/documents\/([0-9a-f-]+)\/fields/) || [])[1];
  await page.goto(baseUrl + '/documents/' + docId + '/recipients', { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.vs-rec__list', { timeout: 10000 });
  // Need at least 2 recipients — default seed should provide; ensure via chips otherwise.
  const rowCount = await page.locator('.vs-rec__row').count();
  if (rowCount < 2) {
    const chips = page.locator('.vs-rec__chip:not(.vs-rec__chip--disabled)');
    const needed = 2 - rowCount;
    for (let i = 0; i < needed && i < (await chips.count()); i++) {
      await chips.nth(i).click();
      await page.waitForTimeout(400);
    }
  }
  await page.waitForFunction(() => document.querySelectorAll('.vs-rec__row').length >= 2);
  return { docId };
});

await step('Drag handles present + aria-labelled', async () => {
  const handles = page.locator('.vs-rec__handle');
  const n = await handles.count();
  if (n < 2) throw new Error('expected >= 2 drag handles, got ' + n);
  const aria = await handles.first().getAttribute('aria-label');
  if (!aria || !/Mută/i.test(aria)) throw new Error('handle missing aria-label, got: ' + aria);
  return { handles: n, ariaSample: aria };
});

await step('Reorder banner visible', async () => {
  const banner = page.locator('.vs-rec__banner');
  if ((await banner.count()) === 0) throw new Error('reorder banner missing');
  const txt = (await banner.innerText()).trim();
  if (!/Ordinea/i.test(txt)) throw new Error('banner text unexpected: ' + txt);
  return { banner: txt };
});

await step('Keyboard ArrowUp swaps rows', async () => {
  const before = await readOrderedNames();
  if (before.length < 2) throw new Error('need 2+ rows, got ' + before.length);

  // Focus 2nd row's handle, press ArrowUp — expect it to become 1st.
  const handle2 = page.locator('.vs-rec__handle').nth(1);
  await handle2.focus();
  await page.keyboard.press('ArrowUp');
  await page.waitForTimeout(800); // persist roundtrip

  const after = await readOrderedNames();
  if (after.length !== before.length) throw new Error('row count changed unexpectedly');
  if (after[0].nm !== before[1].nm) {
    throw new Error(`ArrowUp did not swap: before[0]=${before[0].nm} before[1]=${before[1].nm} after[0]=${after[0].nm} after[1]=${after[1].nm}`);
  }
  if (after[0].ord !== '1' || after[1].ord !== '2') {
    throw new Error('Order badges out of sync: ' + JSON.stringify(after.map(a => a.ord)));
  }
  return { before: before.map(b => b.nm), after: after.map(a => a.nm) };
});

await step('Keyboard ArrowDown restores order', async () => {
  const before = await readOrderedNames();
  // Focus 1st row's handle, press ArrowDown — should swap back.
  const handle1 = page.locator('.vs-rec__handle').nth(0);
  await handle1.focus();
  await page.keyboard.press('ArrowDown');
  await page.waitForTimeout(800);

  const after = await readOrderedNames();
  if (after[0].nm !== before[1].nm) {
    throw new Error(`ArrowDown did not swap: before=${JSON.stringify(before.map(b => b.nm))} after=${JSON.stringify(after.map(a => a.nm))}`);
  }
  return { before: before.map(b => b.nm), after: after.map(a => a.nm) };
});

await snap('final');
await browser.close();

await fs.writeFile(path.join(outDir, 'results.json'), JSON.stringify(results, null, 2));
const failed = results.filter(r => r.status !== 'PASS');
if (failed.length > 0) {
  console.error('FAILED scenarios:', failed.length);
  process.exit(1);
}
console.log('PASS — ' + results.length + ' scenarios green');
