"use strict";

const BRIDGE_URL = "http://127.0.0.1:18791";
const tokenInput = document.getElementById("bridge-token");
const applyAnswersInput = document.getElementById("apply-answers");
const matrixTestModeInput = document.getElementById("matrix-test-mode");
const allowBrowserImagesInput = document.getElementById("allow-browser-images");
const diagnosticModeInput = document.getElementById("diagnostic-mode");
const statusOutput = document.getElementById("status");

initialize();

document.getElementById("save").addEventListener("click", save);
document.getElementById("test").addEventListener("click", testConnection);
document.getElementById("refresh-diagnostics").addEventListener("click", refreshDiagnostics);
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
      origins: chrome.runtime.getManifest().optional_host_permissions.filter(origin => origin !== 'https://hive.forms.usercontent.microsoft/*')
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
    diagnosticMode: false
  });
  tokenInput.value = settings.bridgeToken;
  applyAnswersInput.checked = settings.applyAnswers === true;
  matrixTestModeInput.checked = settings.matrixTestMode === true;
  allowBrowserImagesInput.checked = settings.allowBrowserImages === true;
  diagnosticModeInput.checked = settings.diagnosticMode === true;
  await refreshDiagnostics();
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
