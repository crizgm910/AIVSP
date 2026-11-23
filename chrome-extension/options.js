"use strict";

const BRIDGE_URL = "http://127.0.0.1:18791";
const tokenInput = document.getElementById("bridge-token");
const applyAnswersInput = document.getElementById("apply-answers");
const matrixTestModeInput = document.getElementById("matrix-test-mode");
const allowBrowserImagesInput = document.getElementById("allow-browser-images");
const diagnosticModeInput = document.getElementById("diagnostic-mode");
const statusOutput = document.getElementById("status");
const autoImageInput = document.getElementById('auto-image-authorization');

initialize();

document.getElementById("save").addEventListener("click", save);
document.getElementById("test").addEventListener("click", testConnection);
document.getElementById("refresh-diagnostics").addEventListener("click", refreshDiagnostics);
autoImageInput.addEventListener('change', async () => {
  const enable = autoImageInput.checked;
  autoImageInput.disabled = true;
  try {
    if (enable) {
      const granted = await chrome.permissions.request({ origins: ['https://*/*'] });
      await chrome.storage.local.set({ autoImageAuthorization: granted === true });
      autoImageInput.checked = granted === true;
      showStatus(granted ? 'Automático activado. Ctrl+Shift+9 y clic autoriza solo la imagen elegida; no consulta IA.'
        : 'Permiso amplio no concedido. Se mantiene el modo manual.', granted);
    } else {
      await chrome.storage.local.set({ autoImageAuthorization: false });
      const removed = await chrome.permissions.remove({ origins: ['https://*/*'] });
      showStatus(removed ? 'Automático apagado y permiso amplio retirado. Usa el modo manual.'
        : 'Automático apagado. Revisa en Chrome si permanece el permiso amplio.', removed);
    }
  } catch {
    autoImageInput.checked = false;
    showStatus('No se pudo completar el cambio de permiso. Revisa los permisos de la extensión en Chrome.', false);
  } finally { autoImageInput.disabled = false; }
});
const imageDomainInput = document.getElementById('image-domain');
async function refreshImageDomains() {
  const settings = await chrome.storage.local.get({ imageOrigins: [] });
  const origins = Array.isArray(settings.imageOrigins) ? settings.imageOrigins.filter(value => globalThis.AivspImagePolicy.parse(value)?.origin === value) : [];
  document.getElementById('image-domains').textContent = origins.length ? origins.join('\n') : 'Sin otros dominios autorizados.';
}
document.getElementById('authorize-image-domain').addEventListener('click', async () => {
  const target = globalThis.AivspImagePolicy.parse(imageDomainInput.value.trim());
  if (!target) { showStatus('Usa un dominio HTTPS público, sin credenciales, IP literal ni puerto especial.', false); return; }
  try {
    // This call must stay directly inside the user's click gesture.
    const granted = await chrome.permissions.request({ origins: [target.pattern] });
    if (!granted) { showStatus('Permiso no concedido. No se habilitó la descarga.', false); return; }
    const settings = await chrome.storage.local.get({ imageOrigins: [] });
    const origins = Array.isArray(settings.imageOrigins) ? settings.imageOrigins : [];
    await chrome.storage.local.set({ imageOrigins: [...new Set([...origins, target.origin])] });
    imageDomainInput.value = target.origin;
    await refreshImageDomains();
    showStatus('Dominio de imágenes autorizado. Mantén Adjuntar imágenes activado y guardado.', true);
  } catch { showStatus('No se pudo autorizar el dominio. Recarga la extensión y vuelve a intentarlo.', false); }
});
document.getElementById('revoke-image-domain').addEventListener('click', async () => {
  const target = globalThis.AivspImagePolicy.parse(imageDomainInput.value.trim());
  if (!target) { showStatus('Introduce el dominio HTTPS que quieres retirar.', false); return; }
  try {
    const settings = await chrome.storage.local.get({ imageOrigins: [] });
    await chrome.storage.local.set({ imageOrigins: (Array.isArray(settings.imageOrigins) ? settings.imageOrigins : []).filter(value => value !== target.origin) });
    // Do not remove Forms/Teams frame permissions when revoking image access.
    if (!globalThis.AivspImagePolicy.frameOrigins.includes(target.origin)) await chrome.permissions.remove({ origins: [target.pattern] });
    await refreshImageDomains(); showStatus('Autorización de imagen retirada para este dominio.', true);
  } catch { showStatus('No se pudo retirar completamente el permiso. Revisa los permisos en Chrome.', false); }
});
document.getElementById("enable-forms-images").addEventListener("click", async () => {
  try {
    const granted = await chrome.permissions.request({ origins: ['https://hive.forms.usercontent.microsoft/*'] });
    showStatus(granted ? "Servidor de imágenes Forms autorizado. Recarga el formulario y vuelve a seleccionar la pregunta."
      : "Permiso de imágenes no concedido. No se descargarán desde la extensión.", granted);
  } catch { showStatus("No se pudo solicitar permiso de imágenes. Recarga la extensión.", false); }
});
document.getElementById("enable-microsoft-frames").addEventListener("click", async () => {
  try {
    const granted = await chrome.permissions.request({
      origins: globalThis.AivspImagePolicy.frameOrigins.map(origin => origin + '/*')
    });
    showStatus(granted ? "Forms y Teams habilitados. Actualiza la tarea."
      : "Acceso no concedido. Abre Forms en una pestaña independiente.", granted);
  } catch {
    showStatus("No se pudo conceder acceso. Recarga la extensión.", false);
  }
});

async function initialize() {
  const settings = await chrome.storage.local.get({
    bridgeToken: "",
    applyAnswers: false,
    matrixTestMode: false,
    allowBrowserImages: false,
    diagnosticMode: false,
    autoImageAuthorization: false
  });
  tokenInput.value = settings.bridgeToken;
  applyAnswersInput.checked = settings.applyAnswers === true;
  matrixTestModeInput.checked = settings.matrixTestMode === true;
  allowBrowserImagesInput.checked = settings.allowBrowserImages === true;
  diagnosticModeInput.checked = settings.diagnosticMode === true;
  autoImageInput.checked = settings.autoImageAuthorization === true && await chrome.permissions.contains({ origins: ['https://*/*'] });
  if (settings.autoImageAuthorization === true && !autoImageInput.checked) await chrome.storage.local.set({ autoImageAuthorization: false });
  await refreshDiagnostics();
  await refreshImageDomains();
  if (typeof location !== 'undefined') {
    const target = globalThis.AivspImagePolicy.parse(new URLSearchParams(location.hash.slice(1)).get('image-origin'));
    if (target) {
      imageDomainInput.value = target.origin;
      imageDomainInput.scrollIntoView({ block: 'center' });
      showStatus('Imagen elegida: pulsa Autorizar este dominio para confirmar su permiso. Este paso no consulta IA.', true);
    }
  }
}

async function refreshDiagnostics() {
  const settings = await chrome.storage.local.get({ matrixDiagnostics: [] });
  const events = Array.isArray(settings.matrixDiagnostics) ? settings.matrixDiagnostics.slice(-24) : [];
  document.getElementById("matrix-diagnostics").textContent = events.length
    ? events.map(event => `${event.at} | ${event.stage} | tablas=${event.tables} filas=${event.rows} radios=${event.radios}`).join("\n")
    : "Sin intentos registrados. Activa la prueba, guarda y vuelve a seleccionar la matriz.";
}

async function save() {
  const bridgeToken = tokenInput.value.trim();
  if (!/^[A-Fa-f0-9]{64}$/.test(bridgeToken)) {
    showStatus("El token debe contener exactamente 64 caracteres hexadecimales.", false);
    return;
  }

  await chrome.storage.local.set({
    bridgeToken,
    applyAnswers: applyAnswersInput.checked,
    matrixTestMode: matrixTestModeInput.checked,
    allowBrowserImages: allowBrowserImagesInput.checked,
    diagnosticMode: diagnosticModeInput.checked
  });
  showStatus("Configuración guardada localmente.", true);
}

async function testConnection() {
  const bridgeToken = tokenInput.value.trim();
  if (!/^[A-Fa-f0-9]{64}$/.test(bridgeToken)) {
    showStatus("Guarda primero un token válido.", false);
    return;
  }

  try {
    const response = await fetch(`${BRIDGE_URL}/v1/status`, {
      headers: {
        "Authorization": `Bearer ${bridgeToken}`
      },
      cache: "no-store"
    });
    const body = await response.json().catch(() => ({}));
    if (!response.ok) {
      showStatus(`AIVSP rechazó la conexión (${response.status}).`, false);
      return;
    }
    showStatus(
      `Conectado. Proveedor: ${body.provider}. Perfil: ${body.overlay}. ` +
      `Integración: ${body.enabled ? "habilitada" : "deshabilitada"}.`,
      body.enabled === true);
  } catch {
    showStatus("No se encontró AIVSP en el puente local.", false);
  }
}

function showStatus(message, success) {
  statusOutput.textContent = message;
  statusOutput.className = success ? "success" : "error";
}
