import assert from "node:assert/strict";

let actionListener;
let commandListener;
let runtimeListener;
let activeQueryCount = 0;
const injectedTabs = [];
const sentMessages = [];
const badges = [];
let frames = [{ frameId: 0 }], blocked = [];
let matrixTestMode = false;
let diagnosticMode = false;
let matrixDiagnostics = [];
let allowBrowserImages = false, protocol = 2, provider = "AntigravityCli", posts = 0;

globalThis.setTimeout = (callback) => {
  callback();
  return 0;
};

globalThis.chrome = {
  action: {
    onClicked: {
      addListener(listener) {
        actionListener = listener;
      }
    },
    async setBadgeBackgroundColor() {},
    async setBadgeText(value) {
      badges.push(value);
    }
  },
  commands: {
    onCommand: {
      addListener(listener) {
        commandListener = listener;
      }
    }
  },
  runtime: {
    onMessage: {
      addListener(listener) {
        runtimeListener = listener;
      }
    },
    async openOptionsPage() {}
  },
  scripting: {
    async executeScript({ target }) {
      if (blocked.includes(target.frameIds[0])) throw Error("permission denied");
      injectedTabs.push(target.tabId);
    }
  },
  webNavigation: { async getAllFrames() { return frames; } },
  storage: {
    local: {
      async get(defaults) {
        return {
          ...defaults,
          bridgeToken: "A".repeat(64), matrixTestMode, matrixDiagnostics, allowBrowserImages, diagnosticMode
        };
      },
      async set(value) { if (value.matrixDiagnostics) matrixDiagnostics = value.matrixDiagnostics; }
    }
  },
  tabs: {
    async query(query) {
      assert.deepEqual(query, { active: true, currentWindow: true });
      activeQueryCount++;
      return [{ id: 17 }];
    },
    async sendMessage(tabId, message, options) {
      if (options) message = { ...message, frameId: options.frameId };
      sentMessages.push({ tabId, message });
    }
  }
};

await import("../chrome-extension/service-worker.js");

assert.equal(typeof actionListener, "function");
assert.equal(typeof commandListener, "function");
assert.equal(typeof runtimeListener, "function");

await commandListener("select-question");
assert.equal(activeQueryCount, 1);
assert.deepEqual(injectedTabs, [17]);
assert.deepEqual(sentMessages, [{
  tabId: 17,
  message: { type: "AIVSP_BEGIN_SELECTION", matrixTestMode: false, allowBrowserImages: false, diagnosticMode: false, frameId: 0 }
}]);
assert.ok(badges.some((badge) => badge.tabId === 17 && badge.text === "+"));

await commandListener("unrelated-command");
assert.equal(activeQueryCount, 1);
assert.equal(sentMessages.length, 1);

await actionListener({ id: 23 });
assert.deepEqual(injectedTabs, [17, 23]);
assert.deepEqual(sentMessages[1], {
  tabId: 23,
  message: { type: "AIVSP_BEGIN_SELECTION", matrixTestMode: false, allowBrowserImages: false, diagnosticMode: false, frameId: 0 }
});

frames = [{ frameId: 0 }, { frameId: 4 }, { frameId: 9 }]; blocked = [4];
await actionListener({ id: 31 });
assert.deepEqual(sentMessages.slice(-2).map(x => x.message.frameId), [0, 9]);
blocked = [0, 4, 9]; await actionListener({ id: 32 });
assert.ok(badges.some(x => x.tabId === 32 && x.text === "X"));
runtimeListener({ type: "AIVSP_SELECTION_FINISHED" }, { tab: { id: 31 } }, () => {});
assert.equal(sentMessages.at(-1).message.type, "AIVSP_CANCEL_SELECTION");
const payload = { contextId: "test", mode: 2, question: "Explica qué es una prueba.", options: [] };
const valid = { contextId: "test", mode: 2, answer: "Comprueba un comportamiento.", confidence: 0.95 };
let body = valid, status = 200, error;
globalThis.fetch = async url => {
  if (error) throw error;
  if (url.endsWith('/v1/status')) return { ok: true, async json() { return { browserProtocolVersion: protocol, provider }; } };
  posts++; return { ok: status === 200, async json() { return body; } };
};
const request = (p = payload) => new Promise(resolve => runtimeListener({ type: "AIVSP_ASSIST_REQUEST", payload: p }, { tab: { id: 31 } }, resolve));
assert.equal((await request()).applyAnswers, false);
for (const invalid of [{}, { ...valid, contextId: "old" }, { ...valid, mode: 1 }, { ...valid, answer: "" }, { ...valid, confidence: 2 }]) {
  body = invalid; assert.equal((await request()).error, "invalid_inference_response");
}
const multiple = { ...payload, mode: 1, options: [{ key: "A", text: "Uno" }, { key: "B", text: "Dos" }] };
body = { ...valid, mode: 1, optionKey: "Z" }; assert.equal((await request(multiple)).ok, false);
body.optionKey = "B"; assert.equal((await request(multiple)).ok, true);
status = 429; body = { error: "assistant_busy" }; assert.equal((await request()).error, "assistant_busy");
status = 502; body = { error: 'invalid_inference_response', reason: 'insufficient_context' };
assert.equal((await request()).reason, 'insufficient_context');
body.reason = 'sensitive question text'; assert.equal((await request()).reason, null);
error = Object.assign(Error(), { name: "AbortError" }); assert.equal((await request()).error, "bridge_timeout");
error = Error(); assert.equal((await request()).error, "bridge_unavailable");
assert.equal((await request({ ...payload, question: "" })).error, "invalid_question");

frames = [{ frameId: 0 }]; blocked = []; matrixTestMode = true;
await commandListener("select-question");
assert.equal(sentMessages.at(-1).message.matrixTestMode, true);
matrixTestMode = "true";
await commandListener("select-question");
assert.equal(sentMessages.at(-1).message.matrixTestMode, false);
diagnosticMode = true; matrixTestMode = false;
await commandListener("select-question");
assert.equal(sentMessages.at(-1).message.diagnosticMode, true);
assert.equal(sentMessages.at(-1).message.matrixTestMode, false);
diagnosticMode = "true";
await commandListener("select-question");
assert.equal(sentMessages.at(-1).message.diagnosticMode, false);
diagnosticMode = false;

for (let i = 0; i < 30; i++) {
  await new Promise(resolve => runtimeListener({ type: "AIVSP_TEST_DIAGNOSTIC",
    payload: { stage: "matrix_recognized", tables: 1, rows: 2, radios: 4, question: "must not persist", token: "secret" }
  }, { tab: { id: 31 } }, resolve));
}
assert.equal(matrixDiagnostics.length, 24);
assert.deepEqual(Object.keys(matrixDiagnostics[0]).sort(), ["at", "radios", "rows", "stage", "tables"]);

error = undefined; status = 200;
const advanced = { ...multiple, multipleAnswers: true };
body = { ...valid, mode: 1, optionKey: "A", optionKeys: ["A", "B"] };
assert.equal((await request(advanced)).ok, true);
body.optionKeys = ["A", "A"]; assert.equal((await request(advanced)).ok, false);
body.optionKeys = ["Z"]; assert.equal((await request(advanced)).ok, false);
body.optionKeys = ["A", "B"];
protocol = 1; const before = posts;
assert.equal((await request(advanced)).error, "app_upgrade_required"); assert.equal(posts, before);
protocol = 2; provider = "Ollama";
assert.equal((await request(advanced)).error, "acp_provider_required");
provider = "AntigravityCli";
const visual = { ...multiple, images: [{ mimeType: "image/png", data: "iVBORw0KGgo=" }] };
assert.equal((await request(visual)).error, "images_not_enabled");
allowBrowserImages = true; body.optionKeys = ["A"];
assert.equal((await request(visual)).ok, true);
assert.equal((await request({ ...visual, images: [{ mimeType: "text/html", data: "YWJj" }] })).error, "invalid_image");

console.log(
  "Service worker validado: comando explícito e icono comparten la selección.");
