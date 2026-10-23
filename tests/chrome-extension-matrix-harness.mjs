import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

class Element {
  constructor(tag, text = "", attributes = {}) { this.tag = tag; this.innerText = text; this.attributes = attributes; this.children = []; this.isConnected = true; }
  append(...children) { for (const child of children) { child.parentElement = this; this.children.push(child); } return this; }
  getAttribute(name) { return this.attributes[name] ?? null; }
  setAttribute(name, value) { this.attributes[name] = value; }
  click() { this.clickCount = (this.clickCount || 0) + 1; if (!this.ignoreClick) this.checked = true; }
  getBoundingClientRect() { return { width: 100, height: 30 }; }
  matches(selector) { return selector.split(",").some(part => {
    part = part.trim();
    if (part === ".question") return this.attributes.class === "question";
    if (part.startsWith("[")) { const match = part.match(/^\[([^=\]]+)(?:=["']?([^"'\]]+)["']?)?\]$/); return !!match && (match[2] ? this.getAttribute(match[1]) === match[2] : this.getAttribute(match[1]) !== null); }
    const input = part.match(/^input\[type="radio"\]$/);
    return input ? this.tag === "input" && this.getAttribute("type") === "radio" : this.tag === part;
  }); }
  closest(selector) { for (let element = this; element; element = element.parentElement) if (element.matches(selector)) return element; return null; }
  querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
  querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
  contains(element) { return element === this || this.children.some(child => child.contains(element)); }
}
const body = new Element("body");
const elements = new Map(), requests = [], diagnostics = [];
const context = vm.createContext({ Element, HTMLInputElement: class extends Element {},
  document: { body, getElementById: id => elements.get(id),
    createElement() { return { setAttribute() {}, remove() { elements.delete(this.id); } }; },
    documentElement: { appendChild(element) { elements.set(element.id, element); } } },
  location: { origin: "https://forms.office.com", pathname: "/response" }, setTimeout() {},
  chrome: { runtime: { onMessage: { addListener() {} }, async sendMessage(message) {
    if (message.type === "AIVSP_TEST_DIAGNOSTIC") diagnostics.push(message.payload);
    else requests.push(message);
    return { ok: false };
  } } },
  getComputedStyle: () => ({ display: "block", visibility: "visible", opacity: 1 }) });
const source = readFileSync(new URL("../chrome-extension/content-script.js", import.meta.url), "utf8");
// Expose the real extractor only inside the test VM; production has no test API.
vm.runInContext(source.replace("  function findBestRoot(target) {", "  globalThis.testMatrix = extractMatrix; globalThis.testProcess = processTarget;\n  function findBestRoot(target) {"), context);

function fixture(aria = false, headers = ["Verdadero", "Falso"], labels = ["2 + 2 = 4", "3 + 3 = 7"]) {
  const element = (tag, text, role) => new Element(aria ? "div" : tag, text, role && aria ? { role } : {});
  const card = new Element("section"), title = new Element("h3", "Evalúa cada afirmación.", { class: "question" });
  const table = element("table", "", "grid"), heading = element("tr", "", "row");
  heading.append(element("th", "Afirmación", "columnheader"), ...headers.map(text => element("th", text, "columnheader")));
  const rows = labels.map(label => {
    const row = element("tr", label, "row");
    row.append(element("th", label, "rowheader"), ...headers.map(() => element("td", "", "gridcell").append(new Element("input", "", { type: "radio" }))));
    return row;
  });
  table.append(heading, ...rows); card.append(title, table); body.append(card);
  return { card, table, rows, heading };
}
for (const aria of [false, true]) {
  const f = fixture(aria);
  const result = context.testMatrix(f.rows[1].children[2].children[0]);
  assert.equal(result.radios.length, 2);
  assert.equal(result.radios[0], f.rows[1].children[1].children[0]);
  assert.equal(result.options[0].text, "Verdadero");
  assert.equal(result.options[1].text, "Falso");
  assert.match(result.question, /Evalúa cada afirmación.*3 \+ 3 = 7/);
  assert.equal(result.requiresEvidence, false);
  assert.throws(() => context.testMatrix(f.table), /haz clic en la fila/);
  assert.throws(() => context.testMatrix(f.heading), /haz clic en la fila/);
}
const rating = fixture(false, ["Excelente", "Bueno", "Regular", "Deficiente"], ["Instrucción 1"]);
assert.equal(context.testMatrix(rating.table).requiresEvidence, true);
assert.equal(context.testMatrix(rating.card).options.length, 4);
await context.testProcess(rating.table);
assert.equal(requests.length, 0, "A rubric without evidence must not be sent for an invented grade");
assert.match(elements.get("aivsp-dom-status").title, /requiere revisar el trabajo/);
await context.testProcess(rating.table, true);
assert.equal(requests.length, 0, "Test mode must not query AI");
assert.equal(rating.rows[0].children[1].children[0].clickCount, 1);
assert.equal(rating.rows[0].children[2].children[0].clickCount, undefined);
assert.match(elements.get("aivsp-dom-status").title, /PRUEBA sin IA/);
assert.equal(diagnostics.at(-1).stage, "test_click_confirmed");
const ignored = fixture(); ignored.rows[0].children[1].children[0].ignoreClick = true;
await context.testProcess(ignored.rows[0], true);
assert.equal(diagnostics.at(-1).stage, "test_click_not_confirmed");
const testRows = fixture();
await context.testProcess(testRows.rows[1], true);
assert.equal(testRows.rows[1].children[1].children[0].clickCount, 1);
assert.equal(testRows.rows[0].children[1].children[0].clickCount, undefined);
const stale = fixture(); stale.card.isConnected = false;
await assert.rejects(() => context.testProcess(stale.rows[0], true), /Prueba cancelada/);
const objective = fixture();
await context.testProcess(objective.rows[1]);
assert.equal(requests.length, 1);
assert.equal(requests[0].payload.options.length, 2);
assert.match(requests[0].payload.question, /3 \+ 3 = 7/);
const missing = fixture(); missing.heading.children.pop();
assert.throws(() => context.testMatrix(missing.rows[0]), /asociar/);
const duplicate = fixture(false, ["Igual", "Igual"]);
assert.throws(() => context.testMatrix(duplicate.rows[0]), /etiquetas/);
const span = fixture(); span.heading.children[1].attributes.colspan = "2";
assert.throws(() => context.testMatrix(span.rows[0]), /asociar/);
const noLabel = fixture(false, ["Sí", "No"], [""]);
assert.throws(() => context.testMatrix(noLabel.table), /etiquetas/);
const disabled = fixture(); disabled.rows[0].children[1].children[0].disabled = true;
assert.throws(() => context.testMatrix(disabled.rows[0]), /deshabilitadas/);
assert.equal(context.testMatrix(new Element("section")), null);
console.log("Matrices: HTML/ARIA, selección de fila, encabezados, ambigüedad y valoración sin evidencia aprobados.");
