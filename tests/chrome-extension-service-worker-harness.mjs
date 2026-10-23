import assert from "node:assert/strict";

let actionListener;
let commandListener;
let runtimeListener;
let activeQueryCount = 0;
const injectedTabs = [];
const sentMessages = [];
const badges = [];
let frames = [{ frameId: 0 }], blocked = [];

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
          bridgeToken: "A".repeat(64)
        };
      }
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
  message: { type: "AIVSP_BEGIN_SELECTION", frameId: 0 }
}]);
assert.ok(badges.some((badge) => badge.tabId === 17 && badge.text === "+"));

await commandListener("unrelated-command");
assert.equal(activeQueryCount, 1);
assert.equal(sentMessages.length, 1);

await actionListener({ id: 23 });
assert.deepEqual(injectedTabs, [17, 23]);
assert.deepEqual(sentMessages[1], {
  tabId: 23,
  message: { type: "AIVSP_BEGIN_SELECTION", frameId: 0 }
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
globalThis.fetch = async () => { if (error) throw error; return { ok: status === 200, async json() { return body; } }; };
const request = (p = payload) => new Promise(resolve => runtimeListener({ type: "AIVSP_ASSIST_REQUEST", payload: p }, { tab: { id: 31 } }, resolve));
assert.equal((await request()).applyAnswers, false);
for (const invalid of [{}, { ...valid, contextId: "old" }, { ...valid, mode: 1 }, { ...valid, answer: "" }, { ...valid, confidence: 2 }]) {
  body = invalid; assert.equal((await request()).error, "invalid_inference_response");
}
const multiple = { ...payload, mode: 1, options: [{ key: "A", text: "Uno" }, { key: "B", text: "Dos" }] };
body = { ...valid, mode: 1, optionKey: "Z" }; assert.equal((await request(multiple)).ok, false);
body.optionKey = "B"; assert.equal((await request(multiple)).ok, true);
status = 429; body = { error: "assistant_busy" }; assert.equal((await request()).error, "assistant_busy");
error = Object.assign(Error(), { name: "AbortError" }); assert.equal((await request()).error, "bridge_timeout");
error = Error(); assert.equal((await request()).error, "bridge_unavailable");
assert.equal((await request({ ...payload, question: "" })).error, "invalid_question");

console.log(
  "Service worker validado: comando explícito e icono comparten la selección.");
