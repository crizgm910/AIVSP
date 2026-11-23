import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
const controls = new Map();
for (const id of ["bridge-token", "apply-answers", "matrix-test-mode", "allow-browser-images", "diagnostic-mode", "status", "save", "test", "enable-microsoft-frames", "enable-forms-images", "refresh-diagnostics", "matrix-diagnostics", 'image-domain', 'authorize-image-domain', 'revoke-image-domain', 'image-domains', 'auto-image-authorization']) {
  controls.set(id, { value: "", checked: false, addEventListener(event, listener) { this[event] = listener; } });
}
let saved = { bridgeToken: "A".repeat(64) };
let granted = true, requestedOrigins, removedOrigins;
const permissions = new Set();
const context = vm.createContext({ URL, document: { getElementById: id => controls.get(id) },
  chrome: { permissions: { async request(value) { requestedOrigins = [...value.origins]; if (granted) value.origins.forEach(origin => permissions.add(origin)); return granted; },
    async contains(value) { return value.origins.every(origin => permissions.has(origin) || permissions.has('https://*/*')); },
    async remove(value) { removedOrigins = [...value.origins]; value.origins.forEach(origin => permissions.delete(origin)); return true; } },
    runtime: { getManifest: () => JSON.parse(readFileSync(new URL('../chrome-extension/manifest.json', import.meta.url), 'utf8')) },
    storage: { local: { async get(defaults) { return { ...defaults, ...saved }; }, async set(value) { saved = { ...saved, ...value }; } } } } });
vm.runInContext(readFileSync(new URL('../chrome-extension/image-policy.js', import.meta.url), 'utf8'), context);
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
assert.equal(requestedOrigins.includes('https://*/*'), false);
for (const bad of ['http://images.example.com/a.png', 'https://127.0.0.1/a.png', 'https://localhost/a.png', 'https://x.local/a.png', 'https://user:pass@images.example.com/a.png']) {
  requestedOrigins = null; controls.get('image-domain').value = bad;
  await controls.get('authorize-image-domain').click(); assert.equal(requestedOrigins, null, bad);
}
controls.get('image-domain').value = 'https://images.example.com/a.jpg?secret=must-not-save';
granted = false; await controls.get('authorize-image-domain').click();
assert.equal(saved.imageOrigins?.length || 0, 0);
granted = true; await controls.get('authorize-image-domain').click();
assert.deepEqual(requestedOrigins, ['https://images.example.com/*']);
assert.equal(saved.imageOrigins[0], 'https://images.example.com');
assert.equal(controls.get('image-domain').value, 'https://images.example.com');
assert.equal(JSON.stringify(saved).includes('must-not-save'), false);
await controls.get('revoke-image-domain').click();
assert.equal(saved.imageOrigins.length, 0); assert.deepEqual(removedOrigins, ['https://images.example.com/*']);
controls.get('image-domain').value = 'https://forms.cloud.microsoft/images/a.jpg';
await controls.get('authorize-image-domain').click(); removedOrigins = null;
await controls.get('revoke-image-domain').click();
assert.equal(saved.imageOrigins.length, 0); assert.equal(removedOrigins, null, 'Do not revoke frame access');
assert.equal(saved.bridgeToken, 'A'.repeat(64));
const auto = controls.get('auto-image-authorization');
assert.equal(auto.checked, false);
granted = false; auto.checked = true; await auto.change();
assert.deepEqual(requestedOrigins, ['https://*/*']); assert.equal(saved.autoImageAuthorization, false); assert.equal(auto.checked, false);
granted = true; auto.checked = true; await auto.change();
assert.equal(saved.autoImageAuthorization, true); assert.equal(auto.checked, true);
await vm.runInContext('initialize()', context); assert.equal(auto.checked, true);
auto.checked = false; await auto.change();
assert.equal(saved.autoImageAuthorization, false); assert.deepEqual(removedOrigins, ['https://*/*']);
assert.equal(permissions.has('https://*/*'), false);
saved.autoImageAuthorization = true; await vm.runInContext('initialize()', context);
assert.equal(auto.checked, false); assert.equal(saved.autoImageAuthorization, false);
console.log("Opciones: prueba de matrices apagada por defecto, persistencia, desactivación y token conservado aprobados.");
