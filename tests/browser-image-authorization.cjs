const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage();
    await page.route('https://quiz.example.org/**', route => route.fulfill({ contentType: 'text/html', body:
      '<h2>Formulario de prueba</h2><a href="/must-not-open"><img id="target" width="160" height="100" src="https://cdn.example.net/a.svg"></a>' }));
    await page.route('https://cdn.example.net/a.svg', route => route.fulfill({ contentType: 'image/svg+xml', body:
      '<svg xmlns="http://www.w3.org/2000/svg" width="160" height="100"><rect width="160" height="100" fill="navy"/></svg>' }));
    await page.goto('https://quiz.example.org/form');
    await page.evaluate(() => {
      globalThis.calls = []; globalThis.pendingMode = false;
      globalThis.chrome = { runtime: {
        onMessage: { addListener(fn) { globalThis.arm = fn; }, removeListener() {} },
        async sendMessage(message) {
          calls.push(message);
          if (message.type === 'AIVSP_AUTHORIZE_IMAGE') return { ok: true, pending: pendingMode };
          return {};
        }
      } };
    });
    await page.addScriptTag({ path: path.join(__dirname, '../chrome-extension/content-script.js') });
    for (const automatic of [false, true]) {
      await page.evaluate(automatic => { globalThis.calls = []; globalThis.pendingMode = !automatic;
        arm({ type: 'AIVSP_BEGIN_IMAGE_AUTHORIZATION', ticket: 'synthetic', automatic }); }, automatic);
      assert.equal(await page.locator('#aivsp-dom-status').textContent(), automatic ? '+' : 'Autorización de imagen: haz clic directamente en la imagen. No se consultará IA.');
      await page.locator('#target').click();
      await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('success'));
      const calls = await page.evaluate(() => globalThis.calls);
      assert.equal(calls.filter(value => value.type === 'AIVSP_AUTHORIZE_IMAGE').length, 1);
      assert.equal(calls.filter(value => ['AIVSP_ASSIST_REQUEST', 'AIVSP_FETCH_IMAGE'].includes(value.type)).length, 0);
      assert.equal(page.url(), 'https://quiz.example.org/form', 'Image link must not navigate');
      if (automatic) {
        assert.equal(await page.locator('#aivsp-dom-status').textContent(), '✓');
        assert.equal(await page.locator('#aivsp-dom-status').getAttribute('data-aivsp-diagnostic'), null);
      } else assert.match(await page.locator('#aivsp-dom-status').textContent(), /Confirma/);
    }
    await page.evaluate(() => { globalThis.calls = []; arm({ type: 'AIVSP_BEGIN_IMAGE_AUTHORIZATION', ticket: 'new', automatic: true }); });
    await page.locator('h2').click();
    assert.equal(await page.locator('#aivsp-dom-status').textContent(), '!');
    assert.equal(await page.evaluate(() => calls.filter(value => value.type === 'AIVSP_AUTHORIZE_IMAGE').length), 0);
    await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_IMAGE_AUTHORIZATION', ticket: 'escape', automatic: true }));
    await page.keyboard.press('Escape'); assert.equal(await page.locator('#aivsp-dom-status').count(), 0);
    console.log('PASS authorization UI: manual messages visible, automatic glyphs only, no inference/download/navigation, image-only target and Escape. Mock worker.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
