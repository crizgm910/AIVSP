"use strict";

const BRIDGE_URL = "http://127.0.0.1:18791";
const tokenInput = document.getElementById("bridge-token");
const applyAnswersInput = document.getElementById("apply-answers");
const statusOutput = document.getElementById("status");

initialize();

document.getElementById("save").addEventListener("click", save);
document.getElementById("test").addEventListener("click", testConnection);

async function initialize() {
  const settings = await chrome.storage.local.get({
    bridgeToken: "",
    applyAnswers: false
  });
  tokenInput.value = settings.bridgeToken;
  applyAnswersInput.checked = settings.applyAnswers === true;
}

async function save() {
  const bridgeToken = tokenInput.value.trim();
  if (!/^[A-Fa-f0-9]{64}$/.test(bridgeToken)) {
    showStatus("El token debe contener exactamente 64 caracteres hexadecimales.", false);
    return;
  }

  await chrome.storage.local.set({
    bridgeToken,
    applyAnswers: applyAnswersInput.checked
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
