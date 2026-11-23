const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    for (const kind of ['query', 'hash', 'edited', 'disabled', 'detached', 'valid', 'rapid']) {
    const page = await browser.newPage();
    try {
    await page.route('https://teams.microsoft.com/**', route => route.fulfill({ contentType: 'text/html', body:
      '<section data-automation-id="questionItem"><h2>Explica brevemente qué es una prueba de regresión.</h2><textarea></textarea></section>' }));
    await page.goto('https://teams.microsoft.com/?assignment=first');
    await page.evaluate(() => {
      globalThis.pending = [];
      globalThis.chrome = { runtime: {
        onMessage: { addListener(fn) { globalThis.arm = fn; }, removeListener() {} },
        async sendMessage(message) { if (message.type !== 'AIVSP_ASSIST_REQUEST') return {};
          globalThis.payload = message.payload; return new Promise(resolve => { globalThis.pending.push({ payload: message.payload, resolve }); }); }
      } };
    });
    await page.addScriptTag({ path: path.join(__dirname, '../chrome-extension/content-script.js') });
    await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', diagnosticMode: true }));
    await page.locator('h2').click();
    await page.waitForFunction(() => globalThis.payload);
    if (kind === 'rapid') {
      await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', diagnosticMode: true }));
      await page.locator('h2').click();
      await page.waitForFunction(() => pending.length === 2);
    }
    if (kind === 'edited') await page.locator('textarea').fill('Texto propio');
    await page.evaluate(kind => {
      if (kind === 'query') history.pushState({}, '', '?assignment=second');
      if (kind === 'hash') history.pushState({}, '', '#assignment-second');
      if (kind === 'disabled') document.querySelector('textarea').disabled = true;
      if (kind === 'detached') { const section = document.querySelector('section'); section.replaceWith(section.cloneNode(true)); }
      const message = pending[0];
      message.resolve({ ok: true, applyAnswers: true, result: { contextId: message.payload.contextId, mode: 2,
        answer: 'Respuesta verificada', confidence: 0.95, overlayProfile: 1 } });
    }, kind);
    if (kind === 'rapid') {
      await page.waitForTimeout(100);
      assert.equal(await page.locator('textarea').inputValue(), '', 'Discard old generation');
      await page.evaluate(() => pending[1].resolve({ ok: true, applyAnswers: true, result: {
        contextId: pending[1].payload.contextId, mode: 2, answer: 'Respuesta nueva', confidence: 0.95, overlayProfile: 1 } }));
    }
    await page.waitForFunction(() => !document.getElementById('aivsp-dom-status')?.className.includes('processing'));
    const expected = kind === 'edited' ? 'Texto propio' : kind === 'valid' ? 'Respuesta verificada' : kind === 'rapid' ? 'Respuesta nueva' : '';
    assert.equal(await page.locator('textarea').inputValue(), expected, kind);
    if (!['valid', 'rapid'].includes(kind)) assert.match(await page.locator('#aivsp-dom-status').textContent(), /cambió/);
    console.log(`PASS Teams SPA safety ${kind}. Synthetic page, not real Teams.`);
    } finally { await page.close(); }
    }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
