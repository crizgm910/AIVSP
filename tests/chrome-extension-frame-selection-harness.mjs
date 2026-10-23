import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
const source = readFileSync(new URL("../chrome-extension/content-script.js", import.meta.url), "utf8");
const elements = new Map(), events = new Map(), classes = new Set(), listeners = new Set(), pending = [];
class Element {
  isConnected = true; parentElement = null; innerText = "";
  getBoundingClientRect() { return { width: 500, height: 80 }; }
  getAttribute() { return null; }
}
class Textarea extends Element {
  _value = ""; changes = [];
  get value() { return this._value; } set value(v) { this._value = v; }
  dispatchEvent(e) { this.changes.push(e.type); }
}
const body = {};
const context = vm.createContext({
  document: { body, documentElement: { classList: { add: n => classes.add(n), remove: n => classes.delete(n) }, appendChild: e => elements.set(e.id, e) },
    createElement() { return { setAttribute() {}, remove() { elements.delete(this.id); } }; },
    getElementById: id => elements.get(id), querySelectorAll: () => [],
    addEventListener: (n, f) => events.set(n, f), removeEventListener: n => events.delete(n) },
  chrome: { runtime: { onMessage: { addListener: f => listeners.add(f), removeListener: f => listeners.delete(f) },
    async sendMessage(m) { if (m.type === "AIVSP_ASSIST_REQUEST") return new Promise((resolve, reject) => pending.push({ resolve, reject })); } } },
  Element, HTMLInputElement: class extends Element {}, HTMLTextAreaElement: Textarea, Event,
  getComputedStyle: () => ({ display: "block", visibility: "visible", opacity: 1 }),
  location: { origin: "https://forms.office.com", pathname: "/response" }, setTimeout() {}
});
const inject = (s = source) => vm.runInContext(s, context);
const send = (type, extra = {}) => { for (const f of listeners) f({ type, ...extra }); };
inject(); const initial = context.__aivspDomAssistantController;
for (let i = 0; i < 5; i++) { inject(); send("AIVSP_BEGIN_SELECTION"); assert.equal(listeners.size, 1); assert.equal(context.__aivspDomAssistantController, initial); assert.ok(classes.has("aivsp-selection-active")); }
inject('const AIVSP_CONTENT_SCRIPT_VERSION = "0.2.8"; const previousAivspController = null;'); inject();
inject(source.replace('VERSION = "0.2.12"', 'VERSION = "test-upgrade"'));
assert.equal(listeners.size, 1); assert.notEqual(context.__aivspDomAssistantController, initial);
send("AIVSP_BEGIN_SELECTION"); events.get("keydown")({ key: "Escape" }); assert.equal(events.has("click"), false);
send("AIVSP_BEGIN_SELECTION"); send("AIVSP_CANCEL_SELECTION"); assert.equal(classes.size, 0);
function question(text) {
  const heading = new Element(); heading.innerText = text;
  const input = new Textarea(), root = new Element(); root.innerText = text; root.parentElement = body;
  root.matches = s => s.startsWith("fieldset"); root.querySelector = () => heading;
  root.querySelectorAll = s => s.startsWith("textarea") ? [input] : s === ".question" ? [heading] : [];
  return { root, input };
}
const first = question("Explica qué es una prueba automatizada."), second = question("Explica qué es una regresión de software.");
const click = q => events.get("click")({ target: q.root, preventDefault() {}, stopImmediatePropagation() {} });
const response = answer => ({ ok: true, applyAnswers: true, result: { mode: 2, answer, confidence: 0.95, overlayProfile: 1 } });
send("AIVSP_BEGIN_SELECTION"); const old = click(first);
send("AIVSP_BEGIN_SELECTION"); const current = click(second);
pending[1].resolve(response("Actual")); await current; pending[0].resolve(response("Obsoleta")); await old;
assert.equal(first.input.value, ""); assert.equal(second.input.value, "Actual"); assert.deepEqual(second.input.changes, ["input", "change"]);
send("AIVSP_BEGIN_SELECTION"); const rejected = click(first); send("AIVSP_BEGIN_SELECTION"); pending[2].reject(Error()); await rejected;
assert.equal(elements.get("aivsp-dom-status").className, "aivsp-status-selection");
const closing = click(first); context.__aivspDomAssistantController.dispose(); pending[3].resolve(response("Tardía")); await closing;
assert.equal(first.input.value, ""); assert.equal(elements.has("aivsp-dom-status"), false);
inject(); assert.equal(listeners.size, 1);
send("AIVSP_BEGIN_SELECTION", { matrixTestMode: true });
assert.match(elements.get("aivsp-dom-status").textContent, /PRUEBA: haz clic/);
const openWithTestMode = click(first);
pending[4].resolve(response("Respuesta normal")); await openWithTestMode;
assert.equal(first.input.value, "Respuesta normal", "Matrix test mode must not replace normal open answers");
console.log("Selector: reinyección, actualización, Escape, texto abierto y respuestas tardías aprobados.");
