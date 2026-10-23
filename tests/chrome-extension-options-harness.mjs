import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
const controls = new Map();
for (const id of ["bridge-token", "apply-answers", "matrix-test-mode", "status", "save", "test", "enable-microsoft-frames", "refresh-diagnostics", "matrix-diagnostics"]) {
  controls.set(id, { value: "", checked: false, addEventListener() {} });
}
let saved = { bridgeToken: "A".repeat(64) };
const context = vm.createContext({ document: { getElementById: id => controls.get(id) },
  chrome: { storage: { local: { async get(defaults) { return { ...defaults, ...saved }; }, async set(value) { saved = { ...saved, ...value }; } } } } });
vm.runInContext(readFileSync(new URL("../chrome-extension/options.js", import.meta.url), "utf8"), context);
await vm.runInContext("initialize()", context);
assert.equal(controls.get("matrix-test-mode").checked, false);
controls.get("matrix-test-mode").checked = true;
await vm.runInContext("save()", context);
assert.equal(saved.matrixTestMode, true);
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
console.log("Opciones: prueba de matrices apagada por defecto, persistencia, desactivación y token conservado aprobados.");
