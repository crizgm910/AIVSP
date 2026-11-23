const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    await page.goto('http://127.0.0.1:8765', { waitUntil: 'networkidle' });
    assert.equal(await page.locator('.case').count(), 35);
    const visual = page.locator('[data-case-id="image-dropdown"] article');
    assert.equal(await visual.locator('img').evaluate(image => image.complete && image.naturalWidth > 0), true);
    assert.equal(await visual.locator('select').count(), 0);
    await visual.getByRole('combobox').click();
    await visual.getByRole('option', { name: 'C) Saturno', exact: true }).click();
    assert.equal(await visual.getByRole('combobox').textContent(), 'C) Saturno');
    assert.equal(await visual.getByRole('combobox').getAttribute('aria-expanded'), 'false');
    await visual.getByRole('combobox').press('ArrowDown');
    await page.keyboard.press('ArrowDown'); await page.keyboard.press('Enter');
    assert.equal(await visual.getByRole('combobox').textContent(), 'B) Urano');
    await visual.getByRole('combobox').click(); await page.keyboard.press('Escape');
    assert.equal(await visual.getByRole('combobox').getAttribute('aria-expanded'), 'false');
    for (const id of ['implicit-center', 'implicit-below', 'implicit-row']) {
      const card = page.locator(`[data-case-id="${id}"] article`);
      assert.equal(await card.getAttribute('data-stimulus-id'), null);
      assert.equal(await card.getAttribute('aria-describedby'), null);
      assert.equal(await card.locator('img').count(), 0);
    }
    const loose = page.locator('[data-case-id="implicit-center"]').locator('xpath=preceding-sibling::*[1]');
    assert.equal(await loose.locator('img').evaluate(image => image.complete && image.naturalWidth > 0), true);
    assert.equal(await loose.getAttribute('id'), null);
    await fs.mkdir(path.join(__dirname, '../outputs'), { recursive: true });
    await visual.screenshot({ path: path.join(__dirname, '../outputs/forms-lab-spatial.png') });
    assert.equal(await page.locator('[data-stimulus-id="sales-chart"]').count(), 3);
    assert.equal(await page.locator('#sales-chart img').count(), 1);
    assert.equal(await page.locator('#sales-chart img').evaluate(image => image.complete && image.naturalWidth > 0), true);
    await page.locator('#check-0').check(); await page.locator('#check-1').check(); await page.locator('#check-3').check();
    assert.equal(await page.locator('[data-case-id="check"] article input:checked').count(), 3);
    await page.locator('[data-case-id="dropdown"] article select').selectOption('12');
    await page.locator('[data-case-id="multi-select"] article select').selectOption(['Azul', 'Blanco', 'Rojo']);
    assert.equal(await page.locator('[data-case-id="multi-select"] article option:checked').count(), 3);
    await page.locator('[data-case-id="shared-most"] article select').selectOption('Martes');
    await page.locator('#shared-total').fill('10');
    await page.locator('#shared-multi-0').check(); await page.locator('#shared-multi-1').check();
    assert.equal(await page.locator('[data-case-id="shared-multi"] article input:checked').count(), 2);
    assert.equal(await page.locator('#answer-key').isVisible(), false);
    assert.equal(await page.locator('.card .key-item').count(), 0);
    await page.locator('[data-case-id="single"] label').filter({ hasText: 'París' }).click();
    assert.equal(await page.locator('#single-1').isChecked(), true);
    await page.locator('#short').fill('Roma');
    await page.locator('[data-case-id="aria-radio"] [role="radio"]').nth(1).click();
    assert.equal(await page.locator('[data-case-id="aria-radio"] [aria-checked="true"]').textContent(), '3');
    await page.locator('#matrix-1-1').check();
    assert.equal(await page.locator('#matrix-1-1').isChecked(), true);
    assert.equal(await page.locator('#matrix-0-1').isChecked(), false);
    await page.locator('#dynamic').fill('Respuesta antigua');
    await page.locator('[data-case-id="dynamic"] button').click();
    assert.match(await page.locator('[data-case-id="dynamic"] h2').textContent(), /Portugal/);
    assert.equal(await page.locator('#disabled').isDisabled(), true);
    for (const phase of ['Detección','Respuesta','Aplicación']) await page.getByLabel(`${phase} de short`, { exact: true }).selectOption('pass');
    assert.match(await page.locator('#summary').textContent(), /1 con las tres fases/);
    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: 'Exportar informe manual', exact: true }).click()]);
    const report = JSON.parse(await fs.readFile(await download.path(), 'utf8'));
    assert.equal(report.records.length, 35); assert.equal(report.synthetic, true);
    assert.deepEqual(Object.keys(report.records[0]).sort(), ['answer','application','detection','id']);
    assert.equal(report.records.find(record => record.id === 'short').application, 'pass');
    page.once('dialog', dialog => dialog.accept());
    await page.locator('#reset').click();
    assert.equal(await page.locator('#short').inputValue(), '');
    assert.match(await page.locator('#summary').textContent(), /0 con las tres fases/);
    await page.locator('#filter').selectOption('base');
    assert.equal(await page.locator('.case:visible').count(), 7);
    await page.locator('#filter').selectOption('all');
    await page.locator('#show-key').click(); assert.equal(await page.locator('.key-item').count(), 35);
    await page.locator('#show-key').click();
    const frames = page.frames().filter(frame => frame.url().includes('cases.html'));
    assert.equal(frames.length, 2);
    assert.notEqual(new URL(frames[0].url()).origin, new URL(frames[1].url()).origin);
    for (const frame of frames) assert.equal(await frame.locator('.case').count(), 3);

    // Real content script, stubbed bridge: validates DOM extraction, not provider or extension permissions.
    const source = await fs.readFile(path.join(__dirname, '../chrome-extension/content-script.js'), 'utf8');
    for (const frame of [page.mainFrame(), ...frames]) {
      await frame.evaluate(() => {
        globalThis.chrome = { runtime: { onMessage: { addListener(fn) { globalThis.arm = fn; }, removeListener() {} },
          async sendMessage(message) { if (message.type === 'AIVSP_ASSIST_REQUEST') globalThis.extracted = message.payload; return { ok: false }; } } };
      });
      await frame.addScriptTag({ content: source });
      for (const [id, mode, count] of [['single', 1, 3], ['short', 2, 0], ['matrix', 1, 2]]) {
        await frame.evaluate(() => { globalThis.extracted = null; globalThis.arm({ type: 'AIVSP_BEGIN_SELECTION' }); });
        await frame.locator(id === 'matrix' ? '#matrix-0-0' : `[data-case-id="${id}"] h2`).click();
        const extracted = await frame.evaluate(() => globalThis.extracted);
        assert.ok(extracted, `${id} must extract in ${frame.url()}: ${await frame.locator('#aivsp-dom-status').getAttribute('aria-label')}`);
        assert.equal(extracted.mode, mode); assert.equal(extracted.options.length, count);
      }
    }
    await page.evaluate(() => { globalThis.extracted = null; globalThis.arm({ type: 'AIVSP_BEGIN_SELECTION' }); });
    await page.locator('[data-case-id="check"] h2').click();
    assert.equal(await page.evaluate(() => globalThis.extracted.multipleAnswers), true);
    assert.equal(await page.evaluate(() => globalThis.extracted.options.length), 4);
    await page.locator('#aivsp-dom-status').evaluate(element => element.remove());
    await page.locator('#single-0').scrollIntoViewIfNeeded();
    await fs.mkdir(path.join(__dirname, '../outputs'), { recursive: true });
    await page.screenshot({ path: path.join(__dirname, '../outputs/forms-lab-desktop.png') });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('http://127.0.0.1:8765', { waitUntil: 'networkidle' });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.screenshot({ path: path.join(__dirname, '../outputs/forms-lab-mobile.png') });
    assert.deepEqual(errors, []);
    console.log('PASS laboratorio: 35 casos; extractor de normales y matriz en tres documentos sin confundir la matriz vecina, y casillas múltiples. No valida IA ni permisos reales.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
