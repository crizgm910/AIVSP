import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
globalThis.importScripts = name => vm.runInThisContext(readFileSync(new URL('../chrome-extension/' + name, import.meta.url), 'utf8'));
importScripts('image-policy.js');
for (const url of ['http://cdn.example.com/a', 'file:///tmp/a.png', 'data:image/png;base64,abc',
  'https://127.0.0.1/a', 'https://127.1/a', 'https://0x7f000001/a', 'https://2130706433/a',
  'https://[::1]/a', 'https://192.168.1.1/a', 'https://localhost/a', 'https://server.local/a',
  'https://corp.internal/a', 'https://x.lan/a', 'https://127.0.0.1.nip.io/a',
  'https://user:pass@cdn.example.com/a', 'https://cdn.example.com:8443/a', 'https://singlelabel/a',
  'https://cdn.example.com./a', '//cdn.example.com/a', '*://*/*', 'https://*.example.com/a']) {
  assert.equal(AivspImagePolicy.parse(url), null, url);
}
assert.equal(AivspImagePolicy.parse('https://CDN.example.com:443/a.jpg?signature=abc#ignored').url,
  'https://cdn.example.com/a.jpg?signature=abc');

let listener, consent = true, origins = [], permissionRemoved, storageChanged, tabRemoved;
const grants = new Set();
const originalTimeout = globalThis.setTimeout, originalClear = globalThis.clearTimeout, originalNow = Date.now;
let now = 1000000, timerId = 0, calls = 0, mode = 'ok', lastOptions, lastUrl;
const timers = new Map(), releases = [];
globalThis.setTimeout = (fn, delay) => { const id = ++timerId; timers.set(id, { fn, delay }); return id; };
globalThis.clearTimeout = id => timers.delete(id);
Date.now = () => now;
globalThis.chrome = {
  action: { onClicked: { addListener() {} } }, commands: { onCommand: { addListener() {} } },
  tabs: { onRemoved: { addListener(fn) { tabRemoved = fn; } } },
  runtime: { onMessage: { addListener(fn) { listener = fn; } } },
  storage: { local: { async get(defaults) { return { ...defaults, allowBrowserImages: consent, imageOrigins: origins }; } },
    onChanged: { addListener(fn) { storageChanged = fn; } } },
  permissions: { async contains(value) { assert.notEqual(value.origins[0], 'https://*/*'); return grants.has(value.origins[0]); },
    onRemoved: { addListener(fn) { permissionRemoved = fn; } } }
};
function response() {
  const bytes = new Uint8Array(mode === 'large-cache' ? 2 * 1024 * 1024 : 6); bytes.set([255,216,255]);
  const headers = { 'content-type': 'image/jpeg' };
  if (mode !== 'no-header') headers['cache-control'] = mode === 'no-store' ? 'no-store, max-age=60' :
    mode === 'no-cache' ? 'no-cache, max-age=60' : mode === 'zero-age' ? 'max-age=0' : mode === 'short' ? 'max-age=1' : 'public, max-age=60';
  if (mode === 'aged') headers.age = '61';
  if (mode === 'vary') headers.vary = '*';
  return new Response(bytes, { headers });
}
globalThis.fetch = async (url, options) => {
  calls++; lastOptions = options; lastUrl = url;
  if (mode === 'hold') return new Promise(resolve => releases.push(() => resolve(response())));
  if (mode === 'redirect') throw new TypeError('Redirect to a secret URL must never escape');
  return response();
};
try {
  await import('../chrome-extension/service-worker.js');
  const sender = { url: 'https://quiz.example.org/form?question=1', documentId: 'doc-1', frameId: 0, tab: { id: 19 } };
  const image = 'https://cdn.example.com/image.jpg?signature=private-query';
  const request = (imageUrl = image, from = sender) => new Promise(resolve => {
    assert.equal(listener({ type: 'AIVSP_FETCH_IMAGE', imageUrl }, from, resolve), true);
  });
  const clear = () => permissionRemoved({ origins: [] });
  consent = false; assert.equal((await request()).error, 'IMAGE_CONSENT'); consent = true;
  assert.equal((await request()).error, 'IMAGE_HOST_PERMISSION');
  grants.add('https://cdn.example.com/*');
  assert.equal((await request()).error, 'IMAGE_HOST_PERMISSION', 'Chrome grant alone is insufficient');
  origins = ['https://cdn.example.com']; grants.clear();
  assert.equal((await request()).error, 'IMAGE_HOST_PERMISSION', 'Registry alone is insufficient');
  for (const invalid of ['http://cdn.example.com/a', 'https://10.0.0.1/a', 'https://localhost/a', 'https://x.internal/a'])
    assert.equal((await request(invalid)).error, 'IMAGE_URL');
  assert.equal((await request(image, { ...sender, url: 'http://127.0.0.1/' })).error, 'IMAGE_SENDER');
  assert.equal(calls, 0);
  grants.add('https://cdn.example.com/*');
  assert.equal((await request()).ok, true); assert.equal(calls, 1);
  assert.equal(lastUrl, image, 'Signed query must survive download but not be persisted');
  assert.equal(lastOptions.credentials, 'omit'); assert.equal(lastOptions.redirect, 'error');
  assert.equal(lastOptions.headers, undefined); assert.equal(lastOptions.referrerPolicy, 'no-referrer');
  assert.equal((await request()).ok, true); assert.equal(calls, 1, 'Memory hit avoids network');
  origins = []; storageChanged({ imageOrigins: {} }, 'local');
  assert.equal((await request()).error, 'IMAGE_HOST_PERMISSION'); assert.equal(calls, 1);
  origins = ['https://cdn.example.com']; await request(); assert.equal(calls, 2);
  grants.clear(); assert.equal((await request()).error, 'IMAGE_HOST_PERMISSION'); assert.equal(calls, 2);
  grants.add('https://cdn.example.com/*'); clear(); await request(); assert.equal(calls, 3);
  tabRemoved(19); await request(); assert.equal(calls, 4, 'Closing tab clears memory');
  await request(image, { ...sender, documentId: 'doc-2' }); assert.equal(calls, 5, 'Do not share between documents');
  await request(image, { ...sender, tab: { id: 20 } }); assert.equal(calls, 6, 'Do not share between tabs');
  await request(image, { ...sender, url: 'https://quiz.example.org/form?question=2' }); assert.equal(calls, 7);
  now += 30001; await request(); assert.equal(calls, 8, 'TTL must expire');
  for (const test of ['no-header', 'no-store', 'no-cache', 'zero-age', 'aged', 'vary']) {
    clear(); mode = test; const before = calls; await request(); await request(); assert.equal(calls, before + 2, test);
  }
  clear(); mode = 'short'; const beforeShort = calls; await request(); now += 1001; await request();
  assert.equal(calls, beforeShort + 2, 'Honor freshness shorter than 30 seconds');
  clear(); mode = 'ok'; const beforeBound = calls;
  for (let i = 0; i < 5; i++) await request(`https://cdn.example.com/${i}.jpg`);
  await request('https://cdn.example.com/0.jpg'); assert.equal(calls, beforeBound + 6, 'Bound entry count');
  clear(); mode = 'large-cache'; const beforeMemory = calls;
  for (let i = 0; i < 3; i++) await request(`https://cdn.example.com/big-${i}.jpg`);
  await request('https://cdn.example.com/big-0.jpg'); assert.equal(calls, beforeMemory + 4, 'Bound total cached characters');
  clear(); mode = 'hold'; const beforeHold = calls;
  const first = request(), second = request(); await new Promise(setImmediate);
  assert.equal(calls, beforeHold + 1, 'Coalesce identical concurrent downloads');
  releases.shift()(); assert.ok((await Promise.all([first, second])).every(value => value.ok));
  clear(); const closing = request(); await new Promise(setImmediate);
  tabRemoved(19); releases.shift()(); await closing;
  mode = 'ok'; const afterClose = calls; await request(); assert.equal(calls, afterClose + 1, 'Do not repopulate cache after tab closes');
  clear(); mode = 'hold'; const pending = request(); await new Promise(setImmediate);
  origins = []; storageChanged({ imageOrigins: {} }, 'local'); releases.shift()();
  assert.equal((await pending).error, 'IMAGE_HOST_PERMISSION', 'Revocation while downloading must deny bytes');
  origins = ['https://cdn.example.com']; mode = 'redirect'; clear();
  const failed = await request(); assert.equal(failed.error, 'IMAGE_DOWNLOAD_FAILED'); assert.equal(JSON.stringify(failed).includes('secret'), false);
  mode = 'ok'; await request(); const beforeConsent = calls;
  consent = false; storageChanged({ allowBrowserImages: {} }, 'local');
  assert.equal((await request()).error, 'IMAGE_CONSENT'); assert.equal(calls, beforeConsent);
  console.log('PASS image domains: URL policy, exact permission+registry, revocation, no credentials/redirects, signed URLs, cache isolation/TTL/freshness/bounds and coalescing.');
} finally {
  permissionRemoved?.({ origins: [] });
  globalThis.setTimeout = originalTimeout; globalThis.clearTimeout = originalClear; Date.now = originalNow;
}
