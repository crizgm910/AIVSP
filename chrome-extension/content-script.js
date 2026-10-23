(() => {
"use strict";

const AIVSP_CONTENT_SCRIPT_VERSION = "0.2.9";
const previousAivspController = globalThis.__aivspDomAssistantController;
if (previousAivspController?.version !== AIVSP_CONTENT_SCRIPT_VERSION) {
  previousAivspController?.dispose?.();
  globalThis.__aivspDomAssistantInstalled = false;
}

if (!globalThis.__aivspDomAssistantInstalled) {
  globalThis.__aivspDomAssistantInstalled = true;

  const STATE = {
    cleanupSelection: null,
    activeContext: null,
    generation: 0,
    statusElement: null
  };

  const onRuntimeMessage = (message) => {
    if (message?.type === "AIVSP_BEGIN_SELECTION") {
      beginSelection();
    } else if (message?.type === "AIVSP_CANCEL_SELECTION" && STATE.cleanupSelection) {
      cancelSelection();
      STATE.statusElement?.remove();
    }
  };
  chrome.runtime.onMessage.addListener(onRuntimeMessage);

  function beginSelection(
    message = "AIVSP · clic en pregunta") {
    STATE.generation++;
    STATE.activeContext = null;
    cancelSelection();
    clearPreviousResult();
    showStatus(message, "selection", 0);
    document.documentElement.classList.add("aivsp-selection-active");

    const onClick = async (event) => {
      const generation = STATE.generation;
      event.preventDefault();
      event.stopImmediatePropagation();
      const target = event.target instanceof Element ? event.target : null;
      cancelSelection();
      chrome.runtime.sendMessage({ type: "AIVSP_SELECTION_FINISHED" }).catch(() => {});

      try {
        await processTarget(target);
      } catch (error) {
        if (generation !== STATE.generation) return;
        showStatus(
          error instanceof Error ? error.message : "No se pudo leer esta pregunta.",
          "error",
          4500);
      }
    };
    const onKeyDown = (event) => {
      if (event.key === "Escape") {
        cancelSelection();
        chrome.runtime.sendMessage({ type: "AIVSP_SELECTION_FINISHED" }).catch(() => {});
        showStatus("Selección cancelada.", "info", 1800);
      }
    };

    document.addEventListener("click", onClick, true);
    document.addEventListener("keydown", onKeyDown, true);
    STATE.cleanupSelection = () => {
      document.removeEventListener("click", onClick, true);
      document.removeEventListener("keydown", onKeyDown, true);
      document.documentElement.classList.remove("aivsp-selection-active");
      STATE.cleanupSelection = null;
    };
  }

  async function processTarget(target) {
    const generation = STATE.generation;
    const context = extractContext(target);
    STATE.activeContext = context;
    showStatus("AIVSP · procesando", "processing", 0);
    const response = await chrome.runtime.sendMessage({
      type: "AIVSP_ASSIST_REQUEST",
      payload: context.payload
    });
    if (generation === STATE.generation) applyResponse(response, context);
  }

  function cancelSelection() {
    STATE.cleanupSelection?.();
  }

  function extractContext(target) {
    if (!target) {
      throw new Error("No se encontró el elemento seleccionado.");
    }

    const root = findBestRoot(target);
    if (!root) {
      throw new Error("No se reconoció un contenedor de pregunta.");
    }

    const radios = usableRadios(root).slice(0, 12);
    const input = findAnswerInput(root, target);
    const options = radios.length >= 2
      ? radios.map((radio, index) => extractOption(root, radio, index))
      : [];
    const mode = options.length >= 2 ? 1 : input ? 2 : 0;
    if (mode === 0) {
      throw new Error("La selección no contiene opciones ni un campo de respuesta.");
    }

    const question = extractQuestion(root, options, input);
    if (!question) {
      throw new Error("No se encontró el enunciado de la pregunta.");
    }

    const fingerprint = createFingerprint(root, question, options);
    const contextId = `dom-${fingerprint}`;
    return {
      root,
      input,
      optionElements: new Map(options.map((option, index) => [
        option.key.toUpperCase(),
        radios[index]
      ])),
      fingerprint,
      payload: {
        contextId,
        mode,
        question,
        options: options.map(({ key, text }) => ({ key, text })),
        inputPlaceholder:
          input?.getAttribute("placeholder") ||
          input?.getAttribute("aria-label") ||
          null
      }
    };
  }

  function findBestRoot(target) {
    const candidates = [];
    let element = target;
    for (let depth = 0;
      element && element !== document.body && depth < 10;
      depth++, element = element.parentElement) {
      if (!isVisible(element)) {
        continue;
      }
      const textLength = normalizedText(element.innerText).length;
      if (textLength < 10 || textLength > 12000) {
        continue;
      }
      const radioCount = usableRadios(element).length;
      const inputCount = visibleElements(
        element.querySelectorAll(
          'textarea, input:not([type="radio"]):not([type="checkbox"]):not([type="hidden"]):not([type="submit"]):not([type="button"]), [contenteditable="true"], [role="textbox"]')).length;
      let score = 0;
      if (radioCount >= 2 && radioCount <= 12) {
        score += 200 - (radioCount * 2);
      }
      if (inputCount >= 1 && inputCount <= 3) {
        score += 120 - (inputCount * 5);
      }
      if (element.matches(
        "fieldset, article, section, form, [role='group'], [role='radiogroup'], .card, .question-card")) {
        score += 30;
      }
      if (element.querySelector(
        ".question, [data-question], legend, [role='heading'], h1, h2, h3, h4")) {
        score += 50;
      }
      score -= depth;
      score -= Math.min(textLength / 1000, 10);
      if (score > 0) {
        candidates.push({ element, score, textLength });
      }
    }

    candidates.sort((left, right) =>
      right.score - left.score || left.textLength - right.textLength);
    return candidates[0]?.element || null;
  }

  function extractOption(root, radio, index) {
    const label = Array.from(radio.labels || [])
      .find((candidate) => root.contains(candidate));
    const text = normalizedText(
      label?.innerText ||
      radio.getAttribute("aria-label") ||
      radio.innerText ||
      radio.getAttribute("data-value") ||
      radio.value ||
      "");
    const rawKey = normalizedText(
      radio.value ||
      radio.getAttribute("data-value") ||
      "");
    const prefix = text.match(/^\s*([A-Za-z0-9]{1,8})\s*[\)\].:\-]/)?.[1];
    const key = /^[A-Za-z0-9]{1,8}$/.test(rawKey)
      ? rawKey
      : prefix || String.fromCharCode(65 + index);
    if (!text) {
      throw new Error("Una opción visible no tiene texto.");
    }
    return { key, text };
  }

  function findAnswerInput(root, target) {
    const selector =
      'textarea, input:not([type="radio"]):not([type="checkbox"]):not([type="hidden"]):not([type="submit"]):not([type="button"]), [contenteditable="true"], [role="textbox"]';
    if (target.matches?.(selector) && isVisible(target)) {
      return target;
    }
    return visibleElements(root.querySelectorAll(selector))[0] || null;
  }

  function extractQuestion(root, options, input) {
    const selectors = [
      ".question",
      "[data-question]",
      "legend",
      "[role='heading']",
      "h1",
      "h2",
      "h3",
      "h4",
      "p"
    ];
    for (const selector of selectors) {
      const candidates = visibleElements(root.querySelectorAll(selector));
      const candidate = candidates.find((element) => {
        const text = normalizedText(element.innerText);
        return text.length >= 8 && text.length <= 8000;
      });
      if (candidate) {
        return normalizedText(candidate.innerText);
      }
    }

    const excluded = new Set(options.map((option) => normalizedText(option.text)));
    const inputHint =
      input?.getAttribute("placeholder") ||
      input?.getAttribute("aria-label");
    if (inputHint) {
      excluded.add(normalizedText(inputHint));
    }
    const lines = String(root.innerText || "")
      .split(/\r?\n/)
      .map(normalizedText)
      .filter((line) => line && !excluded.has(line));
    return normalizedText(lines.slice(0, 12).join(" "));
  }

  function applyResponse(response, context) {
    if (!response?.ok) {
      showStatus(errorMessage(response?.error), "error", 5000);
      return;
    }
    if (!context.root.isConnected ||
        createFingerprint(
          context.root,
          context.payload.question,
          context.payload.options) !== context.fingerprint) {
      showStatus(
        "La pregunta cambió antes de aplicar la respuesta. Inténtalo de nuevo.",
        "error",
        5000);
      return;
    }

    const result = response.result;
    const subtle = result.overlayProfile === 1;
    const mayApply = response.applyAnswers === true &&
      Number.isFinite(result.confidence) &&
      result.confidence >= 0.75;
    if (result.mode === 1) {
      const radio = context.optionElements.get(
        String(result.optionKey || "").toUpperCase());
      if (!radio?.isConnected) {
        showStatus("La opción devuelta ya no está disponible.", "error", 5000);
        return;
      }
      const target = radio.labels?.[0] || radio;
      target.classList.add(
        subtle ? "aivsp-result-subtle" : "aivsp-result-visible");
      target.dataset.aivspResult = "true";
      if (mayApply) {
        if (radio instanceof HTMLInputElement) {
          radio.checked = true;
          radio.dispatchEvent(new Event("input", { bubbles: true }));
          radio.dispatchEvent(new Event("change", { bubbles: true }));
        } else {
          radio.click();
        }
      }
      showStatus(
        response.applyAnswers === true && !mayApply
          ? "AIVSP · revisa"
          : "AIVSP · listo",
        response.applyAnswers === true && !mayApply ? "warning" : "success",
        subtle ? 900 : 1400);
      return;
    }

    if (!context.input?.isConnected) {
      showStatus("El campo de respuesta ya no está disponible.", "error", 5000);
      return;
    }
    if (mayApply) {
      setNativeValue(context.input, result.answer);
      context.input.dispatchEvent(new Event("input", { bubbles: true }));
      context.input.dispatchEvent(new Event("change", { bubbles: true }));
    }
    if (!mayApply) {
      showSuggestion(context.input, result.answer, subtle);
    } else {
      document.getElementById("aivsp-dom-suggestion")?.remove();
    }
    showStatus(
      response.applyAnswers === true && !mayApply
        ? "AIVSP · revisa"
        : "AIVSP · listo",
      response.applyAnswers === true && !mayApply ? "warning" : "success",
      subtle ? 900 : 1400);
  }

  function setNativeValue(input, value) {
    if (!(input instanceof HTMLInputElement) &&
        !(input instanceof HTMLTextAreaElement)) {
      input.textContent = value;
      return;
    }
    const prototype = input instanceof HTMLTextAreaElement
      ? HTMLTextAreaElement.prototype
      : HTMLInputElement.prototype;
    const descriptor = Object.getOwnPropertyDescriptor(prototype, "value");
    if (descriptor?.set) {
      descriptor.set.call(input, value);
    } else {
      input.value = value;
    }
  }

  function showSuggestion(input, answer, subtle) {
    document.getElementById("aivsp-dom-suggestion")?.remove();
    const suggestion = document.createElement("div");
    suggestion.id = "aivsp-dom-suggestion";
    suggestion.className = subtle
      ? "aivsp-suggestion-subtle"
      : "aivsp-suggestion-visible";
    suggestion.textContent = answer;
    suggestion.title = answer;
    suggestion.setAttribute("role", "status");
    suggestion.setAttribute("aria-label", answer);
    const rect = input.getBoundingClientRect();
    suggestion.style.left = `${Math.max(8, Math.min(rect.left, innerWidth - 440))}px`;
    suggestion.style.top = `${Math.max(8, Math.min(rect.bottom + 8, innerHeight - 120))}px`;
    document.documentElement.appendChild(suggestion);
    setTimeout(() => suggestion.remove(), subtle ? 1800 : 4500);
  }

  function clearPreviousResult() {
    document.querySelectorAll("[data-aivsp-result='true']").forEach((element) => {
      element.classList.remove("aivsp-result-visible", "aivsp-result-subtle");
      delete element.dataset.aivspResult;
    });
    document.getElementById("aivsp-dom-suggestion")?.remove();
  }

  function showStatus(message, kind, duration) {
    ensureStyles();
    STATE.statusElement?.remove();
    const element = document.createElement("div");
    element.id = "aivsp-dom-status";
    element.className = `aivsp-status-${kind}`;
    element.textContent = statusGlyph(kind);
    element.title = message;
    element.setAttribute("aria-label", message);
    element.setAttribute("role", kind === "error" ? "alert" : "status");
    element.setAttribute("aria-live", kind === "error" ? "assertive" : "polite");
    document.documentElement.appendChild(element);
    STATE.statusElement = element;
    if (duration > 0) {
      setTimeout(() => {
        element.remove();
        if (STATE.statusElement === element) {
          STATE.statusElement = null;
        }
      }, duration);
    }
  }

  function statusGlyph(kind) {
    const glyphs = {
      error: "!",
      info: "·",
      processing: "…",
      selection: "+",
      success: "✓",
      warning: "!"
    };
    return glyphs[kind] || "·";
  }

  function ensureStyles() {
    if (document.getElementById("aivsp-dom-styles")) {
      return;
    }
    const style = document.createElement("style");
    style.id = "aivsp-dom-styles";
    style.textContent = `
      html.aivsp-selection-active, html.aivsp-selection-active * {
        cursor: crosshair !important;
      }
      #aivsp-dom-status {
        position: fixed !important;
        z-index: 2147483647 !important;
        left: auto !important;
        right: 8px !important;
        top: 8px !important;
        transform: none !important;
        box-sizing: border-box !important;
        width: 18px !important;
        height: 18px !important;
        max-width: 18px !important;
        padding: 0 !important;
        border-radius: 50% !important;
        border: 1px solid rgba(56,189,248,.5) !important;
        background: rgba(15,23,42,.42) !important;
        color: rgba(248,250,252,.92) !important;
        font: 600 10px/16px system-ui, sans-serif !important;
        text-align: center !important;
        white-space: nowrap !important;
        overflow: hidden !important;
        box-shadow: none !important;
        opacity: .58 !important;
        pointer-events: none !important;
      }
      #aivsp-dom-status.aivsp-status-error {
        border-color: rgba(239,68,68,.72) !important;
        background: rgba(69,10,10,.72) !important;
        opacity: .72 !important;
      }
      #aivsp-dom-status.aivsp-status-success {
        border-color: rgba(34,197,94,.62) !important;
      }
      #aivsp-dom-status.aivsp-status-selection {
        border-color: rgba(56,189,248,.62) !important;
      }
      .aivsp-result-visible {
        outline: 3px solid #22c55e !important;
        outline-offset: 3px !important;
        background: rgba(34,197,94,.16) !important;
      }
      .aivsp-result-subtle {
        outline: 1px solid rgba(34,197,94,.72) !important;
        outline-offset: 2px !important;
        background: rgba(34,197,94,.06) !important;
      }
      #aivsp-dom-suggestion {
        position: fixed !important;
        z-index: 2147483646 !important;
        box-sizing: border-box !important;
        overflow: hidden !important;
        color: #f8fafc !important;
        box-shadow: none !important;
        pointer-events: none !important;
      }
      .aivsp-suggestion-visible {
        max-width: min(260px, calc(100vw - 16px)) !important;
        max-height: 66px !important;
        padding: 5px 7px !important;
        border: 1px solid rgba(56,189,248,.54) !important;
        border-radius: 5px !important;
        background: rgba(15,23,42,.72) !important;
        font: 500 10px/1.35 system-ui, sans-serif !important;
        opacity: .78 !important;
      }
      .aivsp-suggestion-subtle {
        max-width: min(180px, calc(100vw - 16px)) !important;
        max-height: 20px !important;
        padding: 2px 5px !important;
        border: 1px solid rgba(56,189,248,.38) !important;
        border-radius: 3px !important;
        background: rgba(15,23,42,.42) !important;
        font: 500 9px/14px system-ui, sans-serif !important;
        white-space: nowrap !important;
        text-overflow: ellipsis !important;
        opacity: .58 !important;
      }
    `;
    document.documentElement.appendChild(style);
  }

  function visibleElements(elements) {
    return Array.from(elements).filter(isVisible);
  }

  function usableRadios(root) {
    return Array.from(
      root.querySelectorAll('input[type="radio"], [role="radio"]'))
      .filter(isUsableRadio);
  }

  function isUsableRadio(radio) {
    if (isVisible(radio)) {
      return true;
    }
    if (!(radio instanceof HTMLInputElement) ||
        radio.type !== "radio" ||
        radio.disabled) {
      return false;
    }

    const label = Array.from(radio.labels || []).find(isVisible);
    if (label) {
      return true;
    }

    const labelledBy = String(radio.getAttribute("aria-labelledby") || "")
      .split(/\s+/)
      .filter(Boolean);
    return labelledBy.some((id) => {
      const element = document.getElementById(id);
      return element && isVisible(element) && normalizedText(element.innerText);
    });
  }

  function isVisible(element) {
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return style.display !== "none" &&
      style.visibility !== "hidden" &&
      Number(style.opacity || 1) > 0 &&
      rect.width > 0 &&
      rect.height > 0;
  }

  function normalizedText(value) {
    return String(value || "").replace(/\s+/g, " ").trim();
  }

  function createFingerprint(root, question, options) {
    const source = [
      location.origin,
      location.pathname,
      normalizedText(root.innerText),
      question,
      ...options.map((option) => `${option.key}:${option.text}`)
    ].join("|");
    let hash = 2166136261;
    for (let index = 0; index < source.length; index++) {
      hash ^= source.charCodeAt(index);
      hash = Math.imul(hash, 16777619);
    }
    return (hash >>> 0).toString(16).padStart(8, "0");
  }

  function errorMessage(code) {
    const messages = {
      token_missing: "Configura el token local de AIVSP.",
      bridge_unavailable: "AIVSP no está disponible en el puente local.",
      bridge_timeout: "AIVSP agotó el tiempo de espera.",
      inference_timeout:
        "La IA no respondió a tiempo. Prueba Ollama o vuelve a intentarlo.",
      browser_integration_disabled: "Habilita Integración Chrome desde la bandeja de AIVSP.",
      assistant_busy: "AIVSP ya está procesando otra pregunta.",
      provider_unavailable: "El proveedor de IA no está disponible.",
      model_unavailable:
        "El modelo configurado no está instalado. Descárgalo en Ollama.",
      invalid_inference_response: "La IA devolvió una respuesta no válida.",
      invalid_request: "La pregunta extraída no es válida."
    };
    return messages[code] || "No se pudo completar la solicitud de AIVSP.";
  }

  globalThis.__aivspDomAssistantController = {
    version: AIVSP_CONTENT_SCRIPT_VERSION,
    dispose() {
      STATE.generation++;
      STATE.activeContext = null;
      cancelSelection();
      chrome.runtime.onMessage.removeListener?.(onRuntimeMessage);
      clearPreviousResult();
      STATE.statusElement?.remove();
      STATE.statusElement = null;
      document.getElementById("aivsp-dom-styles")?.remove();
      globalThis.__aivspDomAssistantInstalled = false;
    }
  };
}
})();
