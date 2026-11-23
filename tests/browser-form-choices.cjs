const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="240" height="160"><rect width="240" height="160" fill="white"/><path d="M120 5L200 80L120 155L40 80Z" fill="gold"/><text x="65" y="85">Condition</text></svg>';
const image = 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    for (const kind of ['labelled-radio', 'hidden-checkbox', 'nested-radio', 'long-radio']) {
      const page = await browser.newPage();
      try {
        const names = kind === 'long-radio' ? [20, 35, 50].map(n => 'Texto largo con acentos y símbolos; '.repeat(n).slice(0, 1800).trim()) : ['Una decisión', 'Un ciclo', 'Un archivo'];
        const choices = names.map((text, i) => {
          if (kind === 'hidden-checkbox') return `<label><input type="checkbox" style="position:absolute;opacity:0" value="${i}">${text}</label>`;
          if (kind === 'nested-radio') return `<div role="radio" aria-checked="false" tabindex="0"><input type="radio" name="nested" value="${i}">${text}</div>`;
          return `<input type="radio" name="q" value="${i}" aria-labelledby="name-${i}"><span id="name-${i}">${text}</span>`;
        }).join('');
        await page.setContent(`<div><div><img src="${image}"></div><div style="height:1000px"></div>
          <section data-automation-id="questionItem"><h2>Pregunta</h2><p>Observando el diagrama anterior, selecciona las respuestas.</p>${choices}</section></div>`);
        await page.locator('img').evaluate(img => img.decode());
        await page.evaluate(() => {
          for (const radio of document.querySelectorAll('[role="radio"]')) radio.onclick = () => {
            for (const candidate of document.querySelectorAll('[role="radio"]')) candidate.setAttribute('aria-checked', String(candidate === radio));
          };
          globalThis.chrome = { runtime: {
            onMessage: { addListener(fn) { globalThis.arm = fn; }, removeListener() {} },
            async sendMessage(message) { if (message.type !== 'AIVSP_ASSIST_REQUEST') return {};
              globalThis.payload = message.payload; return new Promise(resolve => { globalThis.reply = resolve; }); }
          } };
        });
        await page.addScriptTag({ path: path.join(__dirname, '../chrome-extension/content-script.js') });
        await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', allowBrowserImages: true, diagnosticMode: true }));
        await page.locator('h2').click();
        await page.waitForFunction(() => globalThis.payload || document.getElementById('aivsp-dom-status')?.className.includes('error'));
        const payload = await page.evaluate(() => globalThis.payload);
        assert.ok(payload, `${kind}: options must be detected`);
        assert.deepEqual(payload.options.map(option => option.text), names, kind);
        assert.equal(payload.images.length, 1, 'Associate preceding offscreen stimulus');
        assert.equal(payload.multipleAnswers, kind === 'hidden-checkbox');
        const keys = kind === 'hidden-checkbox' ? ['A', 'C'] : ['B'];
        await page.evaluate(keys => reply({ ok: true, applyAnswers: true, result: { contextId: payload.contextId,
          mode: 1, optionKey: keys[0], optionKeys: keys, answer: 'Prueba', confidence: 0.95, overlayProfile: 1 } }), keys);
        await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('success'));
        if (kind === 'nested-radio') assert.equal(await page.locator('[role="radio"][aria-checked="true"]').count(), 1);
        else assert.equal(await page.locator('input:checked').count(), keys.length);
        console.log(`PASS form choices ${kind}: detection, names, shared image and application`);
      } finally { await page.close(); }
    }
    const page = await browser.newPage();
    try {
      const options = (type, prefix) => ['Primera', 'Segunda', 'Tercera'].map((text, i) =>
        `<label><input type="${type}" name="${prefix}" value="${i}" ${type === 'checkbox' ? 'style="opacity:0;position:absolute"' : ''}>${text}</label>`).join('');
      const image2 = 'data:image/svg+xml;base64,' + Buffer.from(svg.replace('gold', 'cyan')).toString('base64');
      const question = (id, controls) => `<div><section id="${id}" data-automation-id="questionItem"><h2>Pregunta</h2><p>Observa el diagrama anterior y responde esta pregunta ${id}.</p>${controls}</section></div>`;
      await page.setContent(`<main><div><img id="shared" src="${image}"></div><div style="height:1300px"></div>
        ${question('q1', options('radio', 'r1'))}<div style="display:none"><img src="${image2}"></div>${question('q2', '<textarea></textarea>')}${question('q3', options('checkbox', 'c3'))}
        <div><img src="${image2}"></div>${question('q4', options('radio', 'r4'))}
        <section data-automation-id="questionItem"><p>Otra pregunta con imagen propia</p><img src="${image}">${options('radio', 'owned')}</section>
        ${question('q5', options('radio', 'r5'))}</main>`);
      await page.locator('img').evaluateAll(images => Promise.all(images.map(img => img.decode())));
      await page.evaluate(() => {
        globalThis.chrome = { runtime: {
          onMessage: { addListener(fn) { globalThis.arm = fn; }, removeListener() {} },
          async sendMessage(message) { if (message.type !== 'AIVSP_ASSIST_REQUEST') return {};
            globalThis.payload = message.payload; return new Promise(resolve => { globalThis.reply = resolve; }); }
        } };
      });
      await page.addScriptTag({ path: path.join(__dirname, '../chrome-extension/content-script.js') });
      let firstImage;
      for (const id of ['q1', 'q2', 'q3', 'q4']) {
        await page.evaluate(() => { globalThis.payload = null; arm({ type: 'AIVSP_BEGIN_SELECTION', allowBrowserImages: true, diagnosticMode: true }); });
        await page.locator(`#${id} h2`).click();
        await page.waitForFunction(() => globalThis.payload || document.getElementById('aivsp-dom-status')?.className.includes('error'));
        const payload = await page.evaluate(() => globalThis.payload);
        assert.ok(payload, id); assert.equal(payload.images.length, 1, id);
        if (id === 'q1') {
          firstImage = payload.images[0].data;
          assert.ok(await page.locator('#shared').evaluate(img => img.getBoundingClientRect().bottom < 0), 'Shared image must really be outside viewport');
        } else if (id === 'q4') assert.ok(payload.images[0].data !== firstImage, 'New stimulus replaces old image');
        else assert.ok(payload.images[0].data === firstImage, 'Three questions must share identical image bytes, ignoring CSS-hidden images');
        const keys = id === 'q2' ? [] : id === 'q3' ? ['A', 'C'] : ['B'];
        await page.evaluate(keys => reply({ ok: true, applyAnswers: true, result: { contextId: payload.contextId, mode: payload.mode,
          optionKey: keys[0] || null, optionKeys: keys, answer: 'Respuesta sintética', confidence: 0.95, overlayProfile: 1 } }), keys);
        await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('success'));
        if (id === 'q2') assert.equal(await page.locator('#q2 textarea').inputValue(), 'Respuesta sintética');
        else assert.equal(await page.locator(`#${id} input:checked`).count(), keys.length);
      }
      await page.evaluate(() => { globalThis.payload = null; arm({ type: 'AIVSP_BEGIN_SELECTION', allowBrowserImages: true, diagnosticMode: true }); });
      await page.locator('#q5 h2').click();
      await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('error'));
      assert.equal(await page.evaluate(() => globalThis.payload), null, 'Never steal an image owned by another question');
      console.log('PASS separate stimulus: offscreen image shared by radio/open/checkbox questions, nested wrappers, new stimulus boundary, owned-image rejection. Stubbed inference only.');
    } finally { await page.close(); }
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
