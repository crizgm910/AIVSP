import assert from "node:assert/strict";

let actionListener;
let commandListener;
let runtimeListener;
let activeQueryCount = 0;
const injectedTabs = [];
const sentMessages = [];
const badges = [];

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
      injectedTabs.push(target.tabId);
    }
  },
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
    async sendMessage(tabId, message) {
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
  message: { type: "AIVSP_BEGIN_SELECTION" }
}]);
assert.ok(badges.some((badge) => badge.tabId === 17 && badge.text === "+"));

await commandListener("unrelated-command");
assert.equal(activeQueryCount, 1);
assert.equal(sentMessages.length, 1);

await actionListener({ id: 23 });
assert.deepEqual(injectedTabs, [17, 23]);
assert.deepEqual(sentMessages[1], {
  tabId: 23,
  message: { type: "AIVSP_BEGIN_SELECTION" }
});

console.log(
  "Service worker validado: comando explícito e icono comparten la selección.");
