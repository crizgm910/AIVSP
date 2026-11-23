import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
globalThis.importScripts = name => vm.runInThisContext(readFileSync(new URL('../chrome-extension/' + name, import.meta.url), 'utf8'));
let listener, command, permissionRemoved, storageChanged;
let now = 1000000, fetches = 0, broad = true, exact = false;
const oldNow = Date.now; Date.now = () => now;
const state = { autoImageAuthorization: true, allowBrowserImages: true, imageOrigins: [] };
const sent = [], opened = [], writes = [];
const frames = [
  { frameId: 0, url: 'https://forms.cloud.microsoft/Pages/ResponsePage.aspx', documentId: 'doc-0' },
  { frameId: 4, url: 'https://quiz.example.org/form', documentId: 'doc-4' }
];
globalThis.chrome = {
  action: { onClicked: { addListener() {} }, async setBadgeBackgroundColor() {}, async setBadgeText() {} },
  commands: { onCommand: { addListener(fn) { command = fn; } } },
  runtime: { getURL: path => 'chrome-extension://test/' + path, onMessage: { addListener(fn) { listener = fn; } } },
  scripting: { async executeScript() {} }, webNavigation: { async getAllFrames() { return frames; } },
  tabs: { async query() { return [{ id: 9 }]; }, async sendMessage(tab, message, target) { sent.push({ tab, message, target }); },
    async create(value) { opened.push(value); } },
  storage: { local: { async get(defaults) { return { ...defaults, ...state }; }, async set(value) { writes.push(value); Object.assign(state, value); } },
    onChanged: { addListener(fn) { storageChanged = fn; } } },
  permissions: { async contains(value) { return value.origins[0] === 'https://*/*' ? broad : exact || broad; },
    onRemoved: { addListener(fn) { permissionRemoved = fn; } },
    request() { throw Error('Authorizing by hotkey must not request broad/exact Chrome permission silently'); } }
};
globalThis.fetch = async () => { fetches++; return new Response(new Uint8Array([255,216,255,0]), {
  headers: { 'content-type': 'image/jpeg', 'cache-control': 'no-store' }
}); };
const sender = { tab: { id: 9 }, ...frames[0] };
const image = 'https://cdn.example.net/a.jpg?signature=do-not-store';
const send = (message, from = sender) => new Promise(resolve => assert.equal(listener(message, from, resolve), true));
const download = (url = image, from = sender) => send({ type: 'AIVSP_FETCH_IMAGE', imageUrl: url }, from);
async function arm() { await command('authorize-image'); return sent.filter(value => value.message.type === 'AIVSP_BEGIN_IMAGE_AUTHORIZATION').at(-1).message; }
try {
  await import('../chrome-extension/service-worker.js');
  assert.equal((await download()).error, 'IMAGE_SHORTCUT_REQUIRED'); assert.equal(fetches, 0);
  state.imageOrigins = ['https://cdn.example.net'];
  assert.equal((await download()).error, 'IMAGE_SHORTCUT_REQUIRED', 'Manual domain registry does not approve every image in automatic mode');
  state.imageOrigins = [];
  assert.equal((await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: image, ticket: 'invented' })).error, 'IMAGE_AUTH_STALE');
  const first = await arm(); assert.equal(first.automatic, true);
  assert.equal((await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: image, ticket: first.ticket })).ok, true);
  assert.equal(fetches, 0, 'Shortcut authorizes only; no download or AI');
  assert.equal(opened.length, 0, 'Automatic mode has no popup per image');
  assert.equal(writes.length, 0, 'Do not persist image URL or signed query');
  assert.equal((await download()).ok, true); assert.equal(fetches, 1);
  assert.equal((await download('https://cdn.example.net/other.jpg')).error, 'IMAGE_SHORTCUT_REQUIRED');
  assert.equal((await download(image, { tab: sender.tab, ...frames[1] })).error, 'IMAGE_SHORTCUT_REQUIRED');
  assert.equal((await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: image, ticket: first.ticket })).error, 'IMAGE_AUTH_STALE', 'Ticket is single use');
  const stale = await arm(); now += 30001;
  assert.equal((await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: image, ticket: stale.ticket })).error, 'IMAGE_AUTH_STALE');
  const moved = await arm();
  assert.equal((await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: image, ticket: moved.ticket }, { ...sender, documentId: 'new-doc' })).error, 'IMAGE_AUTH_STALE');
  const unsafe = await arm();
  assert.equal((await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: 'https://127.0.0.1/a.jpg', ticket: unsafe.ticket })).error, 'IMAGE_URL');
  permissionRemoved({}); assert.equal((await download()).error, 'IMAGE_SHORTCUT_REQUIRED');
  const expired = await arm(); await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: image, ticket: expired.ticket });
  now += 20 * 60 * 1000 + 1; assert.equal((await download()).error, 'IMAGE_SHORTCUT_REQUIRED');
  const turnedOff = await arm(); state.autoImageAuthorization = false;
  assert.equal((await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: image, ticket: turnedOff.ticket })).error, 'IMAGE_AUTO_PERMISSION');
  const manual = await arm(); assert.equal(manual.automatic, false);
  const pending = await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: image, ticket: manual.ticket });
  assert.equal(pending.pending, true); assert.equal(opened.length, 1);
  assert.ok(opened[0].url.startsWith('chrome-extension://test/options.html#image-origin='));
  assert.equal(decodeURIComponent(opened[0].url.split('=')[1]), 'https://cdn.example.net');
  assert.equal(opened[0].url.includes('do-not-store'), false);
  broad = false; assert.equal((await download()).error, 'IMAGE_HOST_PERMISSION');
  state.imageOrigins = ['https://cdn.example.net']; exact = true; assert.equal((await download()).ok, true);
  state.autoImageAuthorization = true; storageChanged({ autoImageAuthorization: {} }, 'local');
  assert.equal((await download()).error, 'IMAGE_AUTO_PERMISSION');
  broad = true; permissionRemoved({});
  for (let i = 0; i < 33; i++) {
    const ticket = await arm();
    assert.equal((await send({ type: 'AIVSP_AUTHORIZE_IMAGE', imageUrl: `https://cdn.example.net/selected-${i}.jpg`, ticket: ticket.ticket })).ok, true);
  }
  assert.equal((await download('https://cdn.example.net/selected-0.jpg')).error, 'IMAGE_SHORTCUT_REQUIRED', 'Bound selected-image grants');
  assert.equal((await download('https://cdn.example.net/selected-32.jpg')).ok, true);
  console.log('PASS shortcut authorization: broad grant alone never approves images, one-use tickets, exact image/frame scope, TTL, no inference/download on authorization, visible manual flow, revocation and no URL persistence.');
} finally { Date.now = oldNow; }
