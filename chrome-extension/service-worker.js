"use strict";

const BRIDGE_URL = "http://127.0.0.1:18791";
const REQUEST_TIMEOUT_MS = 40000;
let diagnosticWrites = Promise.resolve();

chrome.action.onClicked.addListener(async (tab) => {
  await armSelection(tab);
});

chrome.commands.onCommand.addListener(async (command) => {
  if (command !== "select-question") {
    return;
  }

  const tabs = await chrome.tabs.query({
    active: true,
    currentWindow: true
  });
  await armSelection(tabs[0]);
});

async function armSelection(tab) {
  if (!Number.isInteger(tab?.id)) {
    return;
  }

  const settings = await chrome.storage.local.get({
    bridgeToken: "",
    applyAnswers: false,
    matrixTestMode: false
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
        matrixTestMode: settings.matrixTestMode === true
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
    applyAnswers: false
  });
  if (!isValidToken(settings.bridgeToken)) {
    await chrome.runtime.openOptionsPage();
    return { ok: false, error: "token_missing" };
  }

  const validationError = validatePayload(payload);
  if (validationError) {
    return { ok: false, error: validationError };
  }

  await setBadge(tabId, "…", "#0369a1");
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

  try {
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
