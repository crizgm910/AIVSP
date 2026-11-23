const assert = require('node:assert/strict');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');
const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="160" height="120"><rect width="160" height="120" fill="navy"/><circle cx="80" cy="60" r="30" fill="orange"/></svg>';
const listen = server => new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
(async () => {
  let reloads = 0;
  const images = http.createServer((req, res) => {
    if (req.headers.origin) reloads++;
    if (req.url.includes('allow')) res.setHeader('Access-Control-Allow-Origin', '*');
    res.writeHead(200, { 'Content-Type': 'image/svg+xml', 'Cache-Control': 'no-store' }); res.end(svg);
  });
  await listen(images);
  const imageOrigin = `http://127.0.0.1:${images.address().port}`;
  const pages = http.createServer((req, res) => {
    res.writeHead(200, { 'Content-Type': 'text/html' });
    const cross = req.url.includes('cross');
    const src = req.url.includes('cdn-') ? 'https://hive.forms.usercontent.microsoft/images/test/one' : cross
      ? imageOrigin + (req.url.includes('allow') || req.url.includes('cancel') ? '/allow.svg' : '/blocked.svg')
      : 'data:image/svg+xml;base64,' + Buffer.from(svg).toString('base64');
    res.end(`<section data-automation-id="questionItem"><h2>Observa la imagen y describe el color del círculo.</h2><img width="160" height="120" src="${src}"><textarea></textarea></section>`);
  });
  await listen(pages);
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    async function run(kind, expected) {
      const page = await browser.newPage();
      try {
        if (kind.startsWith('cdn-')) await page.route('https://hive.forms.usercontent.microsoft/images/test/one', route => route.fulfill({
          contentType: 'image/svg+xml', headers: { 'access-control-allow-origin': 'https://not-allowed.invalid' }, body: svg
        }));
        await page.goto(`http://127.0.0.1:${pages.address().port}/${kind}`);
        await page.locator('img').evaluate(img => img.decode());
        await page.evaluate(kind => {
          globalThis.calls = 0; globalThis.canvasCalls = 0;
          globalThis.brokerCalls = 0;
          const fixture = document.createElement('canvas'); fixture.width = 100; fixture.height = 80;
          fixture.getContext('2d').fillRect(0, 0, 100, 80);
          const brokerData = fixture.toDataURL('image/jpeg');
          const original = HTMLCanvasElement.prototype.toDataURL;
          if (kind === 'large' || kind === 'reduce') HTMLCanvasElement.prototype.toDataURL = function(...args) {
            canvasCalls++;
            if (kind === 'large' || canvasCalls < 3) return 'data:image/jpeg;base64,' + 'A'.repeat(700004);
            return original.apply(this, args);
          };
          if (kind === 'canvas') HTMLCanvasElement.prototype.getContext = () => null;
          if (kind === 'encoding') HTMLCanvasElement.prototype.toDataURL = () => 'data:,';
          if (kind === 'read-blocked') HTMLCanvasElement.prototype.toDataURL = () => { throw new DOMException('Blocked test canvas', 'SecurityError'); };
          if (kind === 'cross-timeout') Image.prototype.decode = () => new Promise(() => {});
          if (kind === 'cross-cancel') {
            const decode = Image.prototype.decode;
            Image.prototype.decode = async function() { await new Promise(r => setTimeout(r, 250)); return decode.call(this); };
          }
          globalThis.chrome = { runtime: {
            onMessage: { addListener(fn) { globalThis.arm = fn; }, removeListener() {} },
            async sendMessage(message) {
              if (message.type === 'AIVSP_FETCH_FORM_IMAGE') {
                globalThis.brokerCalls++; globalThis.brokerPath = message.imagePath;
                if (kind === 'cdn-denied') return { ok: false, error: 'IMAGE_HOST_PERMISSION' };
                if (kind === 'cdn-invalid') return { ok: true, dataUrl: 'not an image' };
                return { ok: true, dataUrl: brokerData };
              }
              if (message.type !== 'AIVSP_ASSIST_REQUEST') return {};
              globalThis.calls++; globalThis.payload = message.payload;
              return { ok: false, error: 'assistant_busy' };
            }
          } };
        }, kind);
        await page.addScriptTag({ path: path.join(__dirname, '../chrome-extension/content-script.js') });
        await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', allowBrowserImages: true, diagnosticMode: true }));
        const before = reloads;
        await page.locator('h2').click();
        if (kind === 'cross-cancel') {
          await page.evaluate(() => arm({ type: 'AIVSP_BEGIN_SELECTION', diagnosticMode: true }));
          await page.waitForTimeout(500);
          assert.equal(await page.evaluate(() => calls), 0);
          assert.equal(await page.locator('#aivsp-dom-status').textContent(), 'AIVSP · clic en pregunta');
          console.log('PASS image cancellation: no stale request or status'); return;
        }
        await page.waitForFunction(() => document.getElementById('aivsp-dom-status')?.className.includes('error'));
        if (expected) {
          assert.match(await page.locator('#aivsp-dom-status').textContent(), new RegExp(`\\[${expected}\\]`), kind);
          assert.equal(await page.evaluate(() => calls), 0, 'Never send text without the image');
        } else {
          assert.equal(await page.evaluate(() => calls), 1, kind);
          const payload = await page.evaluate(() => globalThis.payload);
          assert.equal(payload.images[0].mimeType, 'image/jpeg');
          assert.ok(payload.images[0].data.length <= 700000);
        }
        if (kind === 'large' || kind === 'reduce') {
          assert.equal(reloads, before, 'Size failures must not retry over the network');
          assert.equal(await page.evaluate(() => canvasCalls), kind === 'large' ? 4 : 3);
        }
        if (kind.startsWith('cdn-')) {
          assert.equal(await page.evaluate(() => brokerCalls), 1);
          assert.equal(await page.evaluate(() => brokerPath), '/images/test/one');
        } else assert.equal(await page.evaluate(() => brokerCalls), 0, 'Never ask broker to access unrelated domains');
        console.log(`PASS image ${kind}: ${expected || 'encoded safely'}`);
      } finally { await page.close(); }
    }
    await run('same');
    await run('cross-allow');
    await run('cross-blocked', 'IMAGE_CORS_LOAD');
    await run('large', 'IMAGE_TOO_LARGE');
    await run('reduce');
    await run('canvas', 'IMAGE_CANVAS');
    await run('encoding', 'IMAGE_ENCODING');
    await run('read-blocked', 'IMAGE_CORS_READ');
    await run('cross-timeout', 'IMAGE_LOAD_TIMEOUT');
    await run('cross-cancel');
    await run('cdn-denied', 'IMAGE_HOST_PERMISSION');
    await run('cdn-allowed');
    await run('cdn-invalid', 'IMAGE_FORMAT');
  } finally { await browser.close(); await Promise.all([new Promise(r => pages.close(r)), new Promise(r => images.close(r))]); }
})().catch(error => { console.error(error); process.exitCode = 1; });
