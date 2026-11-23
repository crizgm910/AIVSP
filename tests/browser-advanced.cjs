const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage(); const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('http://127.0.0.1:8765', { waitUntil: 'networkidle' });
    await page.evaluate(() => { globalThis.chrome = { runtime: {
      onMessage: { addListener(fn) { globalThis.arm = fn; }, removeListener() {} },
      async sendMessage(message) { if (message.type !== 'AIVSP_ASSIST_REQUEST') return {};
        globalThis.request = message.payload; return new Promise(resolve => { globalThis.reply = resolve; }); }
    } }; });
    await page.addScriptTag({ path: path.join(__dirname, '../chrome-extension/content-script.js') });
    async function begin(id, images = true) {
      await page.evaluate(images => { globalThis.request = null; globalThis.arm({ type: 'AIVSP_BEGIN_SELECTION', allowBrowserImages: images }); }, images);
      await page.locator(`[data-case-id="${id}"] h2`).click();
      await page.waitForFunction(() => globalThis.request !== null);
      return page.evaluate(() => globalThis.request);
    }
    async function reply(keys, answer = 'Respuesta de prueba', confidence = 0.95) {
      await page.evaluate(({ keys, answer, confidence }) => globalThis.reply({ ok: true, applyAnswers: true, result: {
        contextId: request.contextId, mode: request.mode, optionKey: keys[0] || null, optionKeys: keys, answer, confidence, overlayProfile: 1
      } }), { keys, answer, confidence });
      await page.waitForFunction(() => !document.getElementById('aivsp-dom-status')?.className.includes('processing'));
    }
    let request = await begin('check'); assert.equal(request.multipleAnswers, true);
    await reply(['A', 'B', 'D']); assert.equal(await page.locator('[data-case-id="check"] article input:checked').count(), 3);
    await begin('dropdown'); await reply(['B']); assert.equal(await page.locator('[data-case-id="dropdown"] article select').inputValue(), '12');
    await begin('multi-select'); await reply(['A','B','C']); assert.equal(await page.locator('[data-case-id="multi-select"] article option:checked').count(), 3);
    request = await begin('image-dropdown'); assert.equal(request.images.length, 1); assert.equal(request.images[0].mimeType, 'image/jpeg');
    await fs.mkdir(path.join(__dirname, '../outputs'), { recursive: true });
    await fs.writeFile(path.join(__dirname, '../outputs/acp-browser-fixture.jpg'), Buffer.from(request.images[0].data, 'base64'));
    await reply(['C'], 'Saturno'); assert.equal(await page.locator('[data-case-id="image-dropdown"] [role="combobox"]').textContent(), 'C) Saturno');
    const first = await begin('implicit-center'); await reply(['B'], 'Saturno');
    const second = await begin('implicit-below'); assert.equal(second.images[0].data, first.images[0].data);
    await reply([], 'Mercurio'); assert.equal(await page.locator('#implicit-below').inputValue(), 'Mercurio');
    const third = await begin('implicit-row'); assert.equal(third.images[0].data, first.images[0].data);
    await reply(['A','B','C'], 'Sol, Venus y Júpiter'); assert.equal(await page.locator('[data-case-id="implicit-row"] article input:checked').count(), 3);
    await begin('short'); await page.locator('#short').fill('Escrito por el usuario'); await reply([], 'Roma');
    assert.equal(await page.locator('#short').inputValue(), 'Escrito por el usuario');
    assert.match(await page.locator('#aivsp-dom-status').getAttribute('aria-label'), /cambió/);
    await begin('boolean'); await page.locator('#boolean-1').check(); await reply(['A']);
    assert.equal(await page.locator('#boolean-1').isChecked(), true, 'Do not overwrite a choice changed during inference');
    await begin('boolean'); await reply(['A'], 'Baja confianza', 0.4);
    assert.equal(await page.locator('#boolean-1').isChecked(), true, 'Low confidence must not change a choice');
    await begin('implicit-below');
    await page.evaluate(() => {
      document.querySelectorAll('img').forEach(img => { if (img.src.includes('space-grid.svg')) img.src += '?changed=1'; }); });
    await reply([], 'No debe pegarse');
    assert.equal(await page.locator('#implicit-below').inputValue(), 'Mercurio', 'A changed shared image cancels application');
    assert.match(await page.locator('#aivsp-dom-status').getAttribute('aria-label'), /cambió/);
    await page.evaluate(() => { globalThis.request = null; arm({ type: 'AIVSP_BEGIN_SELECTION', allowBrowserImages: false }); });
    await page.locator('[data-case-id="image-dropdown"] h2').click();
    await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('error'));
    assert.equal(await page.evaluate(() => globalThis.request), null);
    assert.match(await page.locator('#aivsp-dom-status').getAttribute('aria-label'), /Activa Adjuntar/);
    await page.evaluate(() => { globalThis.request = null; arm({ type: 'AIVSP_BEGIN_SELECTION' }); });
    await page.locator('[data-case-id="two-fields"] h2').click();
    await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('error'));
    assert.equal(await page.evaluate(() => globalThis.request), null);
    assert.equal(await page.locator('#aivsp-dom-status').textContent(), '!');
    await page.evaluate(() => { globalThis.request = null; arm({ type: 'AIVSP_BEGIN_SELECTION', diagnosticMode: true }); });
    await page.locator('[data-case-id="two-fields"] h2').click();
    await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('error'));
    assert.match(await page.locator('#aivsp-dom-status').textContent(), /varios campos/);
    assert.equal(await page.evaluate(() => globalThis.request), null);
    await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', diagnosticMode: true }));
    await page.locator('[data-case-id="rubric"] tr').nth(1).click();
    await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('warning'));
    assert.match(await page.locator('#aivsp-dom-status').textContent(), /requiere revisar/);
    assert.equal(await page.locator('[data-case-id="rubric"] input:checked').count(), 0, 'Diagnostics must not mark a fixed matrix answer');
    await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION' }));
    assert.equal(await page.locator('#aivsp-dom-status').textContent(), '+');
    assert.deepEqual(errors, []);
    console.log('PASS advanced DOM: checkbox multi, native select/multiselect, custom combobox, own/shared image encoding, 3 implicit associations, user edits protected, image opt-in and ambiguous fields. Stubbed inference only.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
