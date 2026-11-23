"use strict";
importScripts('image-policy.js');

const BRIDGE_URL = "http://127.0.0.1:18791";
const REQUEST_TIMEOUT_MS = 40000;
let diagnosticWrites = Promise.resolve();
const FORM_IMAGE_ORIGIN = 'https://hive.forms.usercontent.microsoft';
let activeImageLoads = 0;
const imageCache = new Map();
const imageInFlight = new Map();
const IMAGE_CACHE_TTL_MS = 30000;
const IMAGE_CACHE_MAX_CHARS = 8 * 1024 * 1024;
let imageCacheRevision = 0;
const imageAuthorizationTickets = new Map();
const selectedImageGrants = new Map();
const imageContextKey = (sender, target) => sender.tab.id + '|' + JSON.stringify([sender.documentId || sender.frameId || 0, sender.url, target.url]);

function clearImageCache() {
  for (const value of imageCache.values()) clearTimeout(value.timer);
  imageCache.clear(); selectedImageGrants.clear(); imageCacheRevision++;
}

function cacheImage(key, dataUrl, ttl) {
  if (imageCache.has(key)) clearTimeout(imageCache.get(key).timer);
  const expires = Date.now() + ttl;
  const timer = setTimeout(() => {
    if (imageCache.get(key)?.expires === expires) imageCache.delete(key);
  }, ttl);
  imageCache.set(key, { dataUrl, expires, timer });
  let size = [...imageCache.values()].reduce((total, value) => total + value.dataUrl.length, 0);
  while (imageCache.size > 4 || size > IMAGE_CACHE_MAX_CHARS) {
    const oldest = imageCache.keys().next().value;
    size -= imageCache.get(oldest).dataUrl.length; clearTimeout(imageCache.get(oldest).timer); imageCache.delete(oldest);
  }
}
chrome.permissions?.onRemoved?.addListener(clearImageCache);
chrome.storage.onChanged?.addListener((changes, area) => {
  if (area === 'local' && (changes.allowBrowserImages || changes.imageOrigins || changes.autoImageAuthorization)) clearImageCache();
});
chrome.tabs?.onRemoved?.addListener(tabId => {
  imageCacheRevision++;
  imageAuthorizationTickets.delete(tabId);
  for (const key of selectedImageGrants.keys()) if (key.startsWith(tabId + '|')) selectedImageGrants.delete(key);
  for (const key of imageCache.keys()) if (key.startsWith(tabId + '|')) {
    clearTimeout(imageCache.get(key).timer); imageCache.delete(key);
  }
});

async function fetchFormImage(imagePath, sender) {
  let origin;
  try { origin = new URL(sender.url).origin; } catch { return { ok: false, error: 'IMAGE_SENDER' }; }
  if (!Number.isInteger(sender.tab?.id) || !['https://forms.cloud.microsoft', 'https://forms.office.com',
    'https://teams.microsoft.com', 'https://teams.cloud.microsoft'].includes(origin)) return { ok: false, error: 'IMAGE_SENDER' };
  if (typeof imagePath !== 'string' || imagePath.length > 2048 || !/^\/images\/[A-Za-z0-9_/-]+$/.test(imagePath))
    return { ok: false, error: 'IMAGE_PATH' };
  return fetchAuthorizedImage(FORM_IMAGE_ORIGIN + imagePath, sender, false);
}

async function fetchAuthorizedImage(imageUrl, sender, useCache = true) {
  if (!Number.isInteger(sender.tab?.id) || !globalThis.AivspImagePolicy.parse(sender.url))
    return { ok: false, error: 'IMAGE_SENDER' };
  const target = globalThis.AivspImagePolicy.parse(imageUrl);
  if (!target) return { ok: false, error: 'IMAGE_URL' };
  if (target.origin === FORM_IMAGE_ORIGIN && !/^\/images\/[A-Za-z0-9_/-]+$/.test(new URL(target.url).pathname))
    return { ok: false, error: 'IMAGE_PATH' };
  const accessError = await imageAccessError(target, sender);
  if (accessError) return { ok: false, error: accessError };
  const key = imageContextKey(sender, target);
  for (const [oldKey, value] of imageCache) if (value.expires <= Date.now()) { clearTimeout(value.timer); imageCache.delete(oldKey); }
  if (useCache && imageCache.has(key)) {
    const value = imageCache.get(key); imageCache.delete(key); imageCache.set(key, value);
    return { ok: true, dataUrl: value.dataUrl };
  }
  const existing = useCache && imageInFlight.get(key);
  const load = existing || downloadImage(target, useCache ? key : null);
  if (useCache && !existing) imageInFlight.set(key, load);
  try {
    const result = await load;
    const revoked = await imageAccessError(target, sender);
    if (revoked) { clearImageCache(); return { ok: false, error: revoked }; }
    return result;
  } finally { if (useCache && !existing) imageInFlight.delete(key); }
}

async function imageAccessError(target, sender) {
  const settings = await chrome.storage.local.get({ allowBrowserImages: false, imageOrigins: [], autoImageAuthorization: false });
  if (settings.allowBrowserImages !== true) return 'IMAGE_CONSENT';
  if (settings.autoImageAuthorization === true) {
    if (!await chrome.permissions.contains({ origins: ['https://*/*'] })) return 'IMAGE_AUTO_PERMISSION';
    const key = imageContextKey(sender, target);
    if ((selectedImageGrants.get(key) || 0) <= Date.now()) { selectedImageGrants.delete(key); return 'IMAGE_SHORTCUT_REQUIRED'; }
    return null;
  }
  if ((target.origin !== FORM_IMAGE_ORIGIN && (!Array.isArray(settings.imageOrigins) || !settings.imageOrigins.includes(target.origin))) ||
      !await chrome.permissions.contains({ origins: [target.pattern] })) return 'IMAGE_HOST_PERMISSION';
  return null;
}

async function downloadImage(target, cacheKey) {
  if (activeImageLoads >= 4) return { ok: false, error: 'IMAGE_BUSY' };
  activeImageLoads++;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 10000);
  const cacheRevision = imageCacheRevision;
  try {
    const response = await fetch(target.url, {
      credentials: 'omit', redirect: 'error', cache: 'no-store', referrerPolicy: 'no-referrer', signal: controller.signal
    });
    if (!response.ok) return { ok: false, error: 'IMAGE_HTTP_' + response.status };
    const mime = (response.headers.get('content-type') || '').split(';')[0].trim().toLowerCase();
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(mime)) return { ok: false, error: 'IMAGE_FORMAT' };
    const maximum = 4 * 1024 * 1024;
    if (Number(response.headers.get('content-length')) > maximum) return { ok: false, error: 'IMAGE_DOWNLOAD_TOO_LARGE' };
    if (!response.body) return { ok: false, error: 'IMAGE_EMPTY' };
    const reader = response.body.getReader();
    const chunks = []; let length = 0;
    try {
      while (true) {
        const { value, done } = await reader.read(); if (done) break;
        length += value.length;
        if (length > maximum) { await reader.cancel(); return { ok: false, error: 'IMAGE_DOWNLOAD_TOO_LARGE' }; }
        chunks.push(value);
      }
    } finally { reader.releaseLock(); }
    const bytes = new Uint8Array(length); let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
    const signature = mime === 'image/jpeg' ? bytes[0] === 255 && bytes[1] === 216 && bytes[2] === 255 :
      mime === 'image/png' ? [137,80,78,71,13,10,26,10].every((value, i) => bytes[i] === value) :
      length >= 12 && String.fromCharCode(...bytes.slice(0, 4)) === 'RIFF' && String.fromCharCode(...bytes.slice(8, 12)) === 'WEBP';
    if (!signature) return { ok: false, error: 'IMAGE_FORMAT' };
    let binary = '';
    for (let i = 0; i < bytes.length; i += 8192) binary += String.fromCharCode(...bytes.subarray(i, i + 8192));
    const dataUrl = `data:${mime};base64,${btoa(binary)}`;
    const directives = response.headers.get('cache-control') || '';
    const maxAge = Number(directives.match(/(?:^|,)\s*max-age\s*=\s*"?(\d+)/i)?.[1] || 0);
    const age = Math.max(0, Number(response.headers.get('age')) || 0);
    const ttl = Math.min(IMAGE_CACHE_TTL_MS, (maxAge - age) * 1000);
    if (cacheKey && cacheRevision === imageCacheRevision && ttl > 0 &&
        !/(?:no-store|no-cache)/i.test(directives) && response.headers.get('vary') !== '*') cacheImage(cacheKey, dataUrl, ttl);
    return { ok: true, dataUrl };
  } catch (error) { return { ok: false, error: error?.name === 'AbortError' ? 'IMAGE_DOWNLOAD_TIMEOUT' : 'IMAGE_DOWNLOAD_FAILED' }; }
  finally { controller.abort(); clearTimeout(timer); activeImageLoads--; }
}

chrome.action.onClicked.addListener(async (tab) => {
  await armSelection(tab);
});

chrome.commands.onCommand.addListener(async (command) => {
  if (!['select-question', 'authorize-image'].includes(command)) {
    return;
  }

  const tabs = await chrome.tabs.query({
    active: true,
    currentWindow: true
  });
  if (command === 'authorize-image') await armImageAuthorization(tabs[0]);
  else await armSelection(tabs[0]);
});

async function armImageAuthorization(tab) {
  if (!Number.isInteger(tab?.id)) return;
  const settings = await chrome.storage.local.get({ autoImageAuthorization: false });
  const ticket = { id: crypto.randomUUID(), expires: Date.now() + 30000, frames: new Map(), automatic: settings.autoImageAuthorization === true };
  for (const [id, old] of imageAuthorizationTickets) if (old.expires <= Date.now()) imageAuthorizationTickets.delete(id);
  if (imageAuthorizationTickets.size >= 32) imageAuthorizationTickets.delete(imageAuthorizationTickets.keys().next().value);
  imageAuthorizationTickets.set(tab.id, ticket);
  try {
    const knownFrames = await chrome.webNavigation.getAllFrames({ tabId: tab.id });
    const frames = knownFrames?.length ? knownFrames : [{ frameId: 0 }];
    const results = await Promise.allSettled(frames.slice(0, 64).map(async frame => {
      await chrome.scripting.executeScript({ target: { tabId: tab.id, frameIds: [frame.frameId] }, files: ['content-script.js'] });
      ticket.frames.set(frame.frameId, { url: frame.url, documentId: frame.documentId });
      await chrome.tabs.sendMessage(tab.id, { type: 'AIVSP_BEGIN_IMAGE_AUTHORIZATION', ticket: ticket.id, automatic: ticket.automatic }, { frameId: frame.frameId });
    }));
    if (!results.some(result => result.status === 'fulfilled')) { imageAuthorizationTickets.delete(tab.id); await setBadge(tab.id, '!', '#b91c1c'); }
  } catch { imageAuthorizationTickets.delete(tab.id); await setBadge(tab.id, '!', '#b91c1c'); }
}

async function authorizeSelectedImage(message, sender) {
  const ticket = imageAuthorizationTickets.get(sender.tab?.id);
  const frame = ticket?.frames.get(sender.frameId || 0);
  if (!ticket || ticket.id !== message.ticket || ticket.expires <= Date.now() || !frame ||
      (frame.url && frame.url !== sender.url) || (frame.documentId && frame.documentId !== sender.documentId))
    return { ok: false, error: 'IMAGE_AUTH_STALE' };
  imageAuthorizationTickets.delete(sender.tab.id); // One image click per shortcut.
  const target = globalThis.AivspImagePolicy.parse(message.imageUrl);
  if (!target || !globalThis.AivspImagePolicy.parse(sender.url)) return { ok: false, error: 'IMAGE_URL' };
  const settings = await chrome.storage.local.get({ autoImageAuthorization: false });
  if (ticket.automatic) {
    if (settings.autoImageAuthorization !== true || !await chrome.permissions.contains({ origins: ['https://*/*'] }))
      return { ok: false, error: 'IMAGE_AUTO_PERMISSION' };
    for (const [key, expiry] of selectedImageGrants) if (expiry <= Date.now()) selectedImageGrants.delete(key);
    if (selectedImageGrants.size >= 32) selectedImageGrants.delete(selectedImageGrants.keys().next().value);
    selectedImageGrants.set(imageContextKey(sender, target), Date.now() + 20 * 60 * 1000);
    return { ok: true };
  }
  await chrome.tabs.create({ url: chrome.runtime.getURL('options.html') + '#image-origin=' + encodeURIComponent(target.origin) });
  return { ok: true, pending: true };
}

async function armSelection(tab) {
  if (!Number.isInteger(tab?.id)) {
    return;
  }

  const settings = await chrome.storage.local.get({
    bridgeToken: "",
    applyAnswers: false,
    matrixTestMode: false,
    allowBrowserImages: false,
    diagnosticMode: false
  });
  if (!isValidToken(settings.bridgeToken)) {
    await chrome.runtime.openOptionsPage();
    await setBadge(tab.id, "!", "#b91c1c");
    return;
  }

  try {
    const frames = await chrome.webNavigation.getAllFrames({ tabId: tab.id });
    const results = await Promise.allSettled((frames?.length ? frames : [{ frameId: 0 }]).map(async (frame) => {
      await chrome.scripting.executeScript({
        target: { tabId: tab.id, frameIds: [frame.frameId] },
        files: ["content-script.js"]
      });
      await chrome.tabs.sendMessage(tab.id, {
        type: "AIVSP_BEGIN_SELECTION",
        matrixTestMode: settings.matrixTestMode === true,
        allowBrowserImages: settings.allowBrowserImages === true,
        diagnosticMode: settings.diagnosticMode === true
      }, { frameId: frame.frameId });
    }));
    if (!results.some((result) => result.status === "fulfilled")) {
      throw new Error("No accessible document");
    }
    await setBadge(tab.id, "+", "#0369a1");
    setTimeout(() => {
      chrome.action.setBadgeText({ tabId: tab.id, text: "" }).catch(() => {});
    }, 1500);
  } catch {
    await setBadge(tab.id, "X", "#b91c1c");
  }
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === 'AIVSP_AUTHORIZE_IMAGE') {
    authorizeSelectedImage(message, sender).then(sendResponse).catch(() => sendResponse({ ok: false, error: 'IMAGE_AUTH_FAILED' }));
    return true;
  }
  if (message?.type === 'AIVSP_FETCH_IMAGE') {
    fetchAuthorizedImage(message.imageUrl, sender).then(sendResponse).catch(() => sendResponse({ ok: false, error: 'IMAGE_DOWNLOAD_FAILED' }));
    return true;
  }
  if (message?.type === 'AIVSP_FETCH_FORM_IMAGE') {
    fetchFormImage(message.imagePath, sender).then(sendResponse).catch(() => sendResponse({ ok: false, error: 'IMAGE_DOWNLOAD_FAILED' }));
    return true;
  }
  if (message?.type === "AIVSP_TEST_DIAGNOSTIC" && sender.tab?.id) {
    diagnosticWrites = diagnosticWrites.catch(() => {}).then(async () => {
      const allowed = ["selection_started", "target_clicked", "extraction_failed", "matrix_missing",
        "matrix_recognized", "test_click_confirmed", "test_click_not_confirmed"];
      if (!allowed.includes(message.payload?.stage)) return;
      const bounded = value => Number.isInteger(value) && value >= 0 ? Math.min(value, 999) : 0;
      const stored = await chrome.storage.local.get({ matrixDiagnostics: [] });
      const events = Array.isArray(stored.matrixDiagnostics) ? stored.matrixDiagnostics.slice(-23) : [];
      events.push({ at: new Date().toISOString(), stage: message.payload.stage,
        tables: bounded(message.payload.tables), rows: bounded(message.payload.rows), radios: bounded(message.payload.radios) });
      await chrome.storage.local.set({ matrixDiagnostics: events });
    });
    diagnosticWrites.then(() => sendResponse({ ok: true }), () => sendResponse({ ok: false }));
    return true;
  }
  if (message?.type === "AIVSP_SELECTION_FINISHED" && sender.tab?.id) {
    chrome.tabs.sendMessage(sender.tab.id, { type: "AIVSP_CANCEL_SELECTION" }).catch(() => {});
    return false;
  }
  if (message?.type !== "AIVSP_ASSIST_REQUEST" || !sender.tab?.id) {
    return false;
  }

  assist(message.payload, sender.tab.id)
    .then(sendResponse)
    .catch(() => sendResponse({
      ok: false,
      error: "bridge_request_failed"
    }));
  return true;
});

async function assist(payload, tabId) {
  const settings = await chrome.storage.local.get({
    bridgeToken: "",
    applyAnswers: false,
    allowBrowserImages: false
  });
  if (!isValidToken(settings.bridgeToken)) {
    await chrome.runtime.openOptionsPage();
    return { ok: false, error: "token_missing" };
  }

  const validationError = validatePayload(payload);
  if (validationError) {
    return { ok: false, error: validationError };
  }
  if (payload.images?.length && settings.allowBrowserImages !== true) return { ok: false, error: "images_not_enabled" };

  await setBadge(tabId, "…", "#0369a1");
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

  try {
    if (payload.multipleAnswers || payload.images?.length) {
      const statusResponse = await fetch(`${BRIDGE_URL}/v1/status`, { headers: { "Authorization": `Bearer ${settings.bridgeToken}` }, cache: "no-store", signal: controller.signal });
      const status = await statusResponse.json();
      if (!statusResponse.ok || status.browserProtocolVersion !== 2) return { ok: false, error: "app_upgrade_required" };
      if (status.provider !== "AntigravityCli") return { ok: false, error: "acp_provider_required" };
    }
    const response = await fetch(`${BRIDGE_URL}/v1/assist`, {
      method: "POST",
      headers: {
        "Authorization": `Bearer ${settings.bridgeToken}`,
        "Content-Type": "application/json"
      },
      body: JSON.stringify(payload),
      signal: controller.signal,
      cache: "no-store"
    });
    const body = await response.json().catch(() => ({}));
    if (!response.ok) {
      await setBadge(tabId, "!", "#b91c1c");
      return {
        ok: false,
        error: body.error || `http_${response.status}`,
        reason: typeof body.reason === 'string' && /^[a-z_]{1,48}$/.test(body.reason) ? body.reason : null,
        correlationId: body.correlationId || null
      };
    }

    if (!isValidResult(body, payload)) {
      await setBadge(tabId, "!", "#b91c1c");
      return { ok: false, error: "invalid_inference_response" };
    }
    await setBadge(tabId, "✓", "#15803d");
    return {
      ok: true,
      result: body,
      applyAnswers: settings.applyAnswers === true
    };
  } catch (error) {
    await setBadge(tabId, "!", "#b91c1c");
    return {
      ok: false,
      error: error?.name === "AbortError"
        ? "bridge_timeout"
        : "bridge_unavailable"
    };
  } finally {
    clearTimeout(timeout);
    setTimeout(() => {
      chrome.action.setBadgeText({ tabId, text: "" }).catch(() => {});
    }, 3000);
  }
}

function isValidResult(result, payload) {
  if (payload.mode === 1 && payload.multipleAnswers) {
    if (!Array.isArray(result?.optionKeys) || !result.optionKeys.length || result.optionKeys.length > payload.options.length ||
        new Set(result.optionKeys.map(key => String(key).toUpperCase())).size !== result.optionKeys.length ||
        result.optionKeys.some(key => typeof key !== "string" || !payload.options.some(option => option.key.toUpperCase() === key.toUpperCase()))) return false;
  }
  return result && typeof result === "object" &&
    result.contextId === payload.contextId && result.mode === payload.mode &&
    typeof result.answer === "string" && result.answer.trim().length > 0 &&
    Number.isFinite(result.confidence) && result.confidence >= 0 && result.confidence <= 1 &&
    (payload.mode !== 1 || payload.options.some((option) =>
      option.key.toUpperCase() === String(result.optionKey || "").toUpperCase()));
}

function validatePayload(payload) {
  if (!payload || typeof payload !== "object") {
    return "invalid_payload";
  }
  if (payload.multipleAnswers !== undefined && typeof payload.multipleAnswers !== "boolean") return "invalid_mode";
  if (payload.multipleAnswers && payload.mode !== 1) return "invalid_mode";
  if (payload.images !== undefined && (!Array.isArray(payload.images) || payload.images.length > 2 || payload.images.some(image =>
    !image || !["image/png", "image/jpeg"].includes(image.mimeType) || typeof image.data !== "string" || !image.data.length || image.data.length > 700000 || !/^[A-Za-z0-9+/]+={0,2}$/.test(image.data)))) return "invalid_image";
  if (typeof payload.contextId !== "string" ||
      payload.contextId.length < 1 ||
      payload.contextId.length > 128) {
    return "invalid_context";
  }
  if (payload.mode !== 1 && payload.mode !== 2) {
    return "invalid_mode";
  }
  if (typeof payload.question !== "string" ||
      payload.question.trim().length < 1 ||
      payload.question.length > 8000) {
    return "invalid_question";
  }
  if (!Array.isArray(payload.options) || payload.options.length > 12) {
    return "invalid_options";
  }
  if (payload.mode === 1 && payload.options.length < 2) {
    return "invalid_options";
  }
  if (payload.mode === 2 && payload.options.length !== 0) {
    return "invalid_options";
  }
  const keys = new Set();
  for (const option of payload.options) {
    if (!option ||
        typeof option.key !== "string" ||
        !/^[A-Za-z0-9]{1,8}$/.test(option.key) ||
        typeof option.text !== "string" ||
        option.text.trim().length < 1 ||
        option.text.length > 2000) {
      return "invalid_option";
    }
    const normalizedKey = option.key.toUpperCase();
    if (keys.has(normalizedKey)) {
      return "duplicate_option";
    }
    keys.add(normalizedKey);
  }
  return null;
}

function isValidToken(value) {
  return typeof value === "string" && /^[A-Fa-f0-9]{64}$/.test(value);
}

async function setBadge(tabId, text, color) {
  await chrome.action.setBadgeBackgroundColor({ tabId, color });
  await chrome.action.setBadgeText({ tabId, text });
}
