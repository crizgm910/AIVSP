import assert from 'node:assert/strict';
let listener, granted = false, consent = true, fetches = 0, mode = 'ok';
let requested, options;
const releases = [];
const jpeg = new Uint8Array([255, 216, 255, 0, 1, 2]);
globalThis.chrome = {
  action: { onClicked: { addListener() {} } }, commands: { onCommand: { addListener() {} } },
  runtime: { onMessage: { addListener(fn) { listener = fn; } } },
  storage: { local: { async get(defaults) { return { ...defaults, allowBrowserImages: consent }; } } },
  permissions: { async contains(value) { assert.deepEqual(value.origins, ['https://hive.forms.usercontent.microsoft/*']); return granted; } }
};
globalThis.fetch = async (url, init) => {
  fetches++; requested = url; options = init;
  if (mode === 'hold') return new Promise(resolve => releases.push(() => resolve(new Response(jpeg, { headers: { 'content-type': 'image/jpeg' } }))));
  if (mode === 'network') throw Error('Network failure with details that must not escape');
  if (mode === 'timeout') throw Object.assign(Error(), { name: 'AbortError' });
  if (mode === 'http') return new Response('', { status: 403 });
  if (mode === 'html') return new Response('<html>', { headers: { 'content-type': 'text/html' } });
  if (mode === 'signature') return new Response('not a jpeg', { headers: { 'content-type': 'image/jpeg' } });
  if (mode === 'length') return new Response(jpeg, { headers: { 'content-type': 'image/jpeg', 'content-length': String(5 * 1024 * 1024) } });
  if (mode === 'stream') return new Response(new Uint8Array(4 * 1024 * 1024 + 1), { headers: { 'content-type': 'image/jpeg' } });
  return new Response(jpeg, { headers: { 'content-type': 'image/jpeg' } });
};
await import('../chrome-extension/service-worker.js');
const sender = { url: 'https://forms.cloud.microsoft/Pages/ResponsePage.aspx', tab: { id: 7 } };
const request = (imagePath = '/images/test/one', from = sender) => new Promise(resolve => {
  assert.equal(listener({ type: 'AIVSP_FETCH_FORM_IMAGE', imagePath }, from, resolve), true);
});
assert.equal((await request()).error, 'IMAGE_HOST_PERMISSION'); assert.equal(fetches, 0);
granted = true; consent = false;
assert.equal((await request()).error, 'IMAGE_CONSENT'); assert.equal(fetches, 0);
consent = true;
for (const bad of ['https://evil.test/images/x', '//evil.test', '/images/../secret', '/images/%2e%2e/x', '/other/x', '/images/x?redirect=evil']) {
  assert.equal((await request(bad)).error, 'IMAGE_PATH');
}
for (const from of [{ url: 'https://evil.test', tab: { id: 7 } }, { url: sender.url }, { url: 'not a URL', tab: { id: 7 } }])
  assert.equal((await request('/images/test/one', from)).error, 'IMAGE_SENDER');
assert.equal(fetches, 0);
const result = await request(); assert.equal(result.ok, true);
assert.equal(result.dataUrl, 'data:image/jpeg;base64,' + Buffer.from(jpeg).toString('base64'));
assert.equal(requested, 'https://hive.forms.usercontent.microsoft/images/test/one');
assert.equal(options.credentials, 'omit'); assert.equal(options.redirect, 'error');
assert.equal(options.referrerPolicy, 'no-referrer');
assert.equal(options.headers, undefined, 'No bridge token or auth forwarded');
assert.equal(options.signal.aborted, true, 'All paths release the download');
for (const [test, expected] of [['http','IMAGE_HTTP_403'], ['html','IMAGE_FORMAT'], ['signature','IMAGE_FORMAT'],
  ['length','IMAGE_DOWNLOAD_TOO_LARGE'], ['stream','IMAGE_DOWNLOAD_TOO_LARGE'], ['network','IMAGE_DOWNLOAD_FAILED'], ['timeout','IMAGE_DOWNLOAD_TIMEOUT']]) {
  mode = test; assert.equal((await request()).error, expected, test);
}
mode = 'hold'; const pending = Array.from({ length: 4 }, () => request());
await new Promise(setImmediate);
assert.equal(releases.length, 4);
assert.equal((await request()).error, 'IMAGE_BUSY');
for (const release of releases) release();
assert.ok((await Promise.all(pending)).every(result => result.ok));
mode = 'ok'; assert.equal((await request()).ok, true, 'Concurrent slots must be released');
console.log('PASS image worker: separate permission/consent, sender/path allowlist, no credentials/redirects, MIME/signature, size/stream bounds, timeout and sanitized errors.');
