const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    async function setup(kind, delay = 350) {
      const page = await browser.newPage();
      const heading = kind === 'heading' ? '<h2 role="heading">28. Pregunta *</h2><p>Selecciona el número par de estas opciones.</p>' : '<h2>Selecciona el número par de estas opciones.</h2>';
      await page.setContent(`<section data-automation-id="questionItem"><div class="card">${heading}</div><button id="trigger">Selecciona la respuesta</button></section>`);
      await page.evaluate(({ kind, delay }) => {
        const combo = document.getElementById('trigger');
        if (kind.startsWith('listbox')) combo.setAttribute('role', 'listbox');
        else if (kind === 'portal' || kind === 'ambiguous') combo.setAttribute('aria-haspopup', 'listbox');
        else combo.setAttribute('role', 'combobox');
        if (kind === 'owns' || kind === 'open') combo.setAttribute('aria-owns', 'answer-menu');
        function menu(id = 'answer-menu') {
          const popup = document.createElement('div'); popup.id = id; popup.setAttribute('role', 'listbox');
          for (const text of ['3', '4', '5']) {
            const option = document.createElement('div'); option.setAttribute('role', 'option'); option.textContent = text;
            option.onclick = () => {
              combo.dataset.clicks = String(Number(combo.dataset.clicks || 0) + 1);
              const update = () => {
                const live = kind === 'replace' ? combo.cloneNode(false) : combo;
                live.textContent = text; live.setAttribute('aria-expanded', 'false');
                if (live !== combo) combo.replaceWith(live);
                popup.remove();
              };
              if (kind === 'delayed' || kind === 'cancel-confirm') setTimeout(update, 180);
              else if (!kind.includes('unchanged')) update();
            };
            popup.appendChild(option);
          }
          (kind.startsWith('listbox') ? combo : document.body).appendChild(popup);
        }
        // Unrelated menu must never supply this question's options.
        const unrelated = document.createElement('div'); unrelated.setAttribute('role', 'listbox');
        unrelated.innerHTML = '<div role="option">Vecino A</div><div role="option">Vecino B</div>';
        document.body.appendChild(unrelated);
        combo.onclick = event => {
          if (event.target !== combo) return;
          combo.setAttribute('aria-expanded', 'true');
          setTimeout(() => { menu(); if (kind === 'ambiguous') menu('second-menu'); }, delay);
        };
        if (kind === 'open') { combo.setAttribute('aria-expanded', 'true'); menu(); }
        globalThis.chrome = { runtime: {
          onMessage: { addListener(fn) { globalThis.arm = fn; }, removeListener() {} },
          async sendMessage(message) {
            if (message.type !== 'AIVSP_ASSIST_REQUEST') return {};
            globalThis.request = message.payload;
            return new Promise(resolve => { globalThis.reply = resolve; });
          }
        } };
      }, { kind, delay });
      await page.addScriptTag({ path: path.join(__dirname, '../chrome-extension/content-script.js') });
      await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', diagnosticMode: true }));
      await page.locator('h2').click();
      return page;
    }
    for (const kind of ['owns', 'listbox', 'portal', 'open', 'heading', 'delayed', 'replace']) {
      const page = await setup(kind);
      try {
        await page.waitForFunction(() => globalThis.request);
        const request = await page.evaluate(() => globalThis.request);
        assert.deepEqual(request.options.map(option => option.text), ['3', '4', '5'], kind);
        assert.match(request.question, /número par/);
        await page.evaluate(() => reply({ ok: true, applyAnswers: true, result: {
          contextId: request.contextId, mode: 1, answer: '4', optionKey: 'B', optionKeys: ['B'], confidence: 0.95, overlayProfile: 1
        } }));
        await page.waitForFunction(() => document.getElementById('trigger').textContent === '4');
        await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('success'));
        assert.equal(await page.locator('#aivsp-dom-status').textContent(), 'AIVSP · listo');
        assert.equal(await page.locator('#trigger').getAttribute('data-clicks'), '1');
      } finally { await page.close(); }
    }
    for (const kind of ['unchanged', 'listbox-unchanged', 'cancel-confirm']) {
      const page = await setup(kind);
      try {
        await page.waitForFunction(() => globalThis.request);
        await page.evaluate(() => reply({ ok: true, applyAnswers: true, result: {
          contextId: request.contextId, mode: 1, answer: '4', optionKey: 'B', optionKeys: ['B'], confidence: 0.95, overlayProfile: 1
        } }));
        await page.waitForFunction(() => document.getElementById('trigger').dataset.clicks === '1');
        if (kind === 'cancel-confirm') {
          await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', diagnosticMode: true }));
          await page.waitForTimeout(1100);
          assert.equal(await page.locator('#aivsp-dom-status').textContent(), 'AIVSP · clic en pregunta');
        } else {
          await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('warning'));
          assert.match(await page.locator('#aivsp-dom-status').textContent(), /no confirmó/);
        }
        assert.equal(await page.locator('#trigger').getAttribute('data-clicks'), '1');
      } finally { await page.close(); }
    }
    const ambiguous = await setup('ambiguous');
    await ambiguous.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('error'));
    assert.match(await ambiguous.locator('#aivsp-dom-status').textContent(), /varios menús/);
    assert.equal(await ambiguous.evaluate(() => globalThis.request), undefined);
    await ambiguous.close();
    const cancelled = await setup('owns', 600);
    await cancelled.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', diagnosticMode: true }));
    await cancelled.waitForTimeout(800);
    assert.equal(await cancelled.evaluate(() => globalThis.request), undefined);
    assert.equal(await cancelled.locator('#aivsp-dom-status').textContent(), 'AIVSP · clic en pregunta');
    await cancelled.close();
    console.log('PASS dropdowns: delayed options, aria-owns, role=listbox, external portal, already open, nested card, unrelated menu, ambiguity and cancellation. Synthetic DOM; not real Forms.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
