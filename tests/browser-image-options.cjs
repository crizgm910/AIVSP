const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { chromium } = require('playwright');
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1100, height: 900 } });
    const errors = []; page.on('pageerror', error => errors.push(error.message));
    const files = new Set(['options.html', 'options.css', 'options.js', 'image-policy.js']);
    await page.route('https://extension.example.com/**', route => {
      const name = new URL(route.request().url()).pathname.slice(1);
      if (!files.has(name)) return route.abort();
      return route.fulfill({ path: path.join(__dirname, '../chrome-extension', name) });
    });
    await page.addInitScript(() => {
      globalThis.saved = { bridgeToken: 'A'.repeat(64) }; globalThis.requests = []; globalThis.granted = true; globalThis.permissionOrigins = new Set();
      globalThis.chrome = {
        storage: { local: { async get(defaults) { return { ...defaults, ...saved }; }, async set(value) { saved = { ...saved, ...value }; } } },
        permissions: {
          async request(value) { requests.push({ ...value, gesture: navigator.userActivation.isActive }); if (granted) value.origins.forEach(origin => permissionOrigins.add(origin)); return granted; },
          async contains(value) { return value.origins.every(origin => permissionOrigins.has(origin) || permissionOrigins.has('https://*/*')); },
          async remove(value) { value.origins.forEach(origin => permissionOrigins.delete(origin)); return true; }
        }
      };
    });
    await page.goto('https://extension.example.com/options.html');
    await page.waitForFunction(() => document.getElementById('bridge-token').value.length === 64);
    assert.equal(await page.locator('#allow-browser-images').isChecked(), false);
    assert.equal(await page.locator('#auto-image-authorization').isChecked(), false);
    await page.locator('#image-domain').fill('https://images.example.com/a.png?token=do-not-store');
    await page.locator('#authorize-image-domain').click();
    await page.waitForFunction(() => saved.imageOrigins?.length === 1);
    const request = await page.evaluate(() => requests.at(-1));
    assert.deepEqual(request.origins, ['https://images.example.com/*']); assert.equal(request.gesture, true);
    assert.deepEqual(await page.evaluate(() => saved.imageOrigins), ['https://images.example.com']);
    assert.equal(await page.locator('#image-domain').inputValue(), 'https://images.example.com');
    assert.equal(await page.evaluate(() => JSON.stringify(saved).includes('do-not-store')), false);
    await page.locator('#revoke-image-domain').click();
    await page.waitForFunction(() => saved.imageOrigins.length === 0);
    await page.locator('#image-domain').fill('https://127.0.0.1/a.jpg');
    const before = await page.evaluate(() => requests.length);
    await page.locator('#authorize-image-domain').click();
    assert.equal(await page.evaluate(() => requests.length), before);
    await page.evaluate(() => { globalThis.granted = false; });
    await page.locator('#image-domain').fill('https://denied.example.com/a.jpg');
    await page.locator('#authorize-image-domain').click();
    await page.waitForFunction(() => document.getElementById('status').textContent.includes('no concedido'));
    assert.deepEqual(await page.evaluate(() => saved.imageOrigins), []);
    await page.locator('#enable-microsoft-frames').click();
    assert.equal((await page.evaluate(() => requests.at(-1).origins)).includes('https://*/*'), false);
    await page.evaluate(() => { globalThis.granted = true; });
    await page.locator('#auto-image-authorization').check();
    await page.waitForFunction(() => saved.autoImageAuthorization === true);
    assert.deepEqual(await page.evaluate(() => requests.at(-1).origins), ['https://*/*']);
    assert.equal(await page.evaluate(() => requests.at(-1).gesture), true);
    await page.locator('#auto-image-authorization').uncheck();
    await page.waitForFunction(() => saved.autoImageAuthorization === false && !permissionOrigins.has('https://*/*'));
    await page.goto('https://extension.example.com/options.html#image-origin=https%3A%2F%2Fcdn.example.net');
    await page.reload(); // Worker opens a new Options document, not just a hash change.
    await page.waitForFunction(() => document.getElementById('image-domain').value === 'https://cdn.example.net');
    assert.match(await page.locator('#status').textContent(), /pulsa Autorizar/);
    assert.equal(await page.evaluate(() => requests.length), 0, 'Manual prefill cannot grant permission on page load');
    assert.equal(await page.locator('#bridge-token').inputValue(), 'A'.repeat(64));
    assert.deepEqual(errors, []);
    await fs.mkdir(path.join(__dirname, '../outputs'), { recursive: true });
    await page.locator('#image-domain').scrollIntoViewIfNeeded();
    await page.screenshot({ path: path.join(__dirname, '../outputs/image-domain-options.png') });
    console.log('PASS rendered options: real HTML/script loading, user gesture, exact host request, no URL persistence, revoke, invalid/denied host and separate frame permissions. Mock Chrome API.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
