import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
const controls = new Map();
for (const id of ["bridge-token", "apply-answers", "matrix-test-mode", "allow-browser-images", "diagnostic-mode", "status", "save", "test", "enable-microsoft-frames", "enable-forms-images", "refresh-diagnostics", "matrix-diagnostics"]) {
  controls.set(id, { value: "", checked: false, addEventListener(event, listener) { if (event === 'click') this.click = listener; } });
}
let saved = { bridgeToken: "A".repeat(64) };
let granted = true, requestedOrigins;
const context = vm.createContext({ document: { getElementById: id => controls.get(id) },
  chrome: { permissions: { async request(value) { requestedOrigins = [...value.origins]; return granted; } },
    runtime: { getManifest: () => JSON.parse(readFileSync(new URL('../chrome-extension/manifest.json', import.meta.url), 'utf8')) },
    storage: { local: { async get(defaults) { return { ...defaults, ...saved }; }, async set(value) { saved = { ...saved, ...value }; } } } } });
vm.runInContext(readFileSync(new URL("../chrome-extension/options.js", import.meta.url), "utf8"), context);
await vm.runInContext("initialize()", context);
assert.equal(controls.get("matrix-test-mode").checked, false);
assert.equal(controls.get("allow-browser-images").checked, false);
assert.equal(controls.get("diagnostic-mode").checked, false);
controls.get("diagnostic-mode").checked = true;
await vm.runInContext("save()", context);
assert.equal(saved.diagnosticMode, true);
assert.equal(saved.matrixTestMode, false, "Diagnostics must not enable fixed matrix answers");
controls.get("diagnostic-mode").checked = false;
await vm.runInContext("initialize()", context);
assert.equal(controls.get("diagnostic-mode").checked, true);
controls.get("diagnostic-mode").checked = false;
await vm.runInContext("save()", context);
assert.equal(saved.diagnosticMode, false);
controls.get("allow-browser-images").checked = true;
controls.get("matrix-test-mode").checked = true;
await vm.runInContext("save()", context);
assert.equal(saved.matrixTestMode, true);
assert.equal(saved.allowBrowserImages, true);
assert.equal(saved.bridgeToken, "A".repeat(64));
controls.get("matrix-test-mode").checked = false;
await vm.runInContext("initialize()", context);
assert.equal(controls.get("matrix-test-mode").checked, true);
controls.get("matrix-test-mode").checked = false;
await vm.runInContext("save()", context);
assert.equal(saved.matrixTestMode, false);
saved.matrixDiagnostics = [{ at: "test-date", stage: "matrix_missing", tables: 0, rows: 0, radios: 4 }];
await vm.runInContext("refreshDiagnostics()", context);
assert.match(controls.get("matrix-diagnostics").textContent, /matrix_missing.*radios=4/);
await controls.get('enable-forms-images').click();
assert.deepEqual(requestedOrigins, ['https://hive.forms.usercontent.microsoft/*']);
assert.match(controls.get('status').textContent, /autorizado/);
granted = false; await controls.get('enable-forms-images').click();
assert.match(controls.get('status').textContent, /no concedido/);
granted = true; await controls.get('enable-microsoft-frames').click();
assert.equal(requestedOrigins.includes('https://hive.forms.usercontent.microsoft/*'), false);
assert.equal(saved.bridgeToken, 'A'.repeat(64));
console.log("Opciones: prueba de matrices apagada por defecto, persistencia, desactivación y token conservado aprobados.");
