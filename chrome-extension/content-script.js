(() => {
"use strict";

const AIVSP_CONTENT_SCRIPT_VERSION = "0.3.8";
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
    statusElement: null,
    testDiagnostics: false
  };

  const onRuntimeMessage = (message) => {
    if (message?.type === "AIVSP_BEGIN_SELECTION") {
      beginSelection(undefined, message.matrixTestMode === true, message.allowBrowserImages === true, message.diagnosticMode === true);
    } else if (message?.type === "AIVSP_CANCEL_SELECTION" && STATE.cleanupSelection) {
      cancelSelection();
      STATE.statusElement?.remove();
    }
  };
  chrome.runtime.onMessage.addListener(onRuntimeMessage);

  function beginSelection(
    message = "AIVSP · clic en pregunta", matrixTestMode = false, allowBrowserImages = false, diagnosticMode = false) {
    STATE.generation++;
    STATE.activeContext = null;
    STATE.testDiagnostics = matrixTestMode === true || diagnosticMode === true;
    cancelSelection();
    clearPreviousResult();
    showStatus(matrixTestMode ? "PRUEBA: haz clic en una fila de matriz; se marcará su primera opción sin IA." : message, "selection", 0);
    if (matrixTestMode) reportTest("selection_started");
    document.documentElement.classList.add("aivsp-selection-active");

    const onClick = async (event) => {
      const generation = STATE.generation;
      event.preventDefault();
      event.stopImmediatePropagation();
      const target = event.target instanceof Element ? event.target : null;
      cancelSelection();
      chrome.runtime.sendMessage({ type: "AIVSP_SELECTION_FINISHED" }).catch(() => {});

      try {
        await processTarget(target, matrixTestMode, allowBrowserImages);
      } catch (error) {
        if (generation !== STATE.generation) return;
        if (matrixTestMode) reportTest("extraction_failed", target);
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

  async function processTarget(target, matrixTestMode = false, allowBrowserImages = false) {
    const generation = STATE.generation;
    if (matrixTestMode) reportTest("target_clicked", target);
    const candidateRoot = findQuestionRoot(target) || (target && findBestRoot(target));
    const dropdown = dropdownTrigger(candidateRoot) ? await prepareDropdown(candidateRoot, generation) : null;
    if (generation !== STATE.generation) return;
    const context = extractContext(target, dropdown);
    STATE.activeContext = context;
    if (matrixTestMode && !context.isMatrix) {
      reportTest("matrix_missing", target);
    }
    if (matrixTestMode === true && context.isMatrix) {
      reportTest("matrix_recognized", target);
      const radio = context.optionElements.get("A");
      if (!context.root.isConnected || !radio?.isConnected || !context.root.contains(radio) ||
          radio.disabled || radio.getAttribute("aria-disabled") === "true" ||
          createFingerprint(context.root, context.payload.question, context.payload.options) !== context.fingerprint) {
        throw new Error("Prueba cancelada: la fila ya no está disponible.");
      }
      radio.click();
      // A dispatched click alone is not evidence that a controlled Forms radio changed.
      const selected = radio.checked === true || radio.getAttribute("aria-checked") === "true";
      reportTest(selected ? "test_click_confirmed" : "test_click_not_confirmed", target);
      showStatus(selected
        ? `PRUEBA sin IA: primera opción marcada (${context.payload.options[0].text}). No es una respuesta evaluada.`
        : "PRUEBA: matriz reconocida, pero el clic no confirmó la selección. Abre Opciones → Diagnóstico de prueba.",
        selected ? "warning" : "error", 10000);
      return;
    }
    if (context.requiresEvidence) {
      showStatus("Matriz reconocida. Esta valoración requiere revisar el trabajo; no se seleccionará una calificación automáticamente.", "warning", 6500);
      return;
    }
    const images = findQuestionImages(context.root);
    const needsImage = /imagen|diagrama|gráfic[ao]|cuadrícula|figura/i.test(context.payload.question);
    if (needsImage && !images.length) throw new Error("La pregunta necesita una imagen, pero no se encontró un estímulo inequívoco. No se enviará solo texto.");
    if (images.length) {
      if (!allowBrowserImages) throw new Error("Activa Adjuntar imágenes en las opciones de la extensión para resolver esta pregunta visual.");
      context.imageSources = images.map(element => ({ element, source: element.currentSrc || element.src }));
      context.payload.images = await Promise.all(images.map(encodeImage));
      if (generation !== STATE.generation) return;
    }
    showStatus("AIVSP · procesando", "processing", 0);
    const response = await chrome.runtime.sendMessage({
      type: "AIVSP_ASSIST_REQUEST",
      payload: context.payload
    });
    if (generation === STATE.generation) await applyResponse(response, context);
  }

  function cancelSelection() {
    STATE.cleanupSelection?.();
  }

  function reportTest(stage, target) {
    // Only structural counts: no page URL, question, labels, answers, or token.
    let root = target;
    for (let depth = 0; root?.parentElement && root.parentElement !== document.body && depth < 4; depth++) root = root.parentElement;
    const count = selector => Math.min(root?.querySelectorAll?.(selector).length || 0, 999);
    chrome.runtime.sendMessage({ type: "AIVSP_TEST_DIAGNOSTIC", payload: {
      stage, tables: count('table, [role="table"], [role="grid"]'),
      rows: count('tr, [role="row"]'), radios: count('input[type="radio"], [role="radio"]')
    } }).catch(() => {});
  }

  function dropdownTrigger(root) {
    if (!root) return null;
    return root.querySelector('[role="combobox"]') ||
      root.querySelector('[aria-haspopup="listbox"]') ||
      root.querySelector('[role="listbox"]') ||
      root.querySelector('[data-automation-id="dropdown"]');
  }

  function linkedPopup(combo) {
    const ids = `${combo.getAttribute("aria-controls") || ""} ${combo.getAttribute("aria-owns") || ""}`.trim().split(/\s+/);
    const linked = ids.map(id => document.getElementById(id)).filter(Boolean);
    const labelled = combo.id ? Array.from(document.querySelectorAll('[role="listbox"][aria-labelledby]'))
      .filter(node => (node.getAttribute("aria-labelledby") || "").split(/\s+/).includes(combo.id)) : [];
    const candidates = [...new Set([...linked, ...labelled])].filter(isVisible);
    if (candidates.length > 1) throw new Error("Hay varios menús asociados al desplegable. No se elegirán opciones ambiguas.");
    return candidates[0] || null;
  }

  async function prepareDropdown(root, generation) {
    const combo = dropdownTrigger(root);
    if (!combo || root.querySelector('select')) return null;
    const triggers = visibleElements(root.querySelectorAll('[role="combobox"], [aria-haspopup="listbox"], [role="listbox"], [data-automation-id="dropdown"]'));
    if (triggers.filter(node => !triggers.some(other => other !== node && other.contains(node))).length > 1)
      throw new Error("Hay varios desplegables en este contenedor. Selecciona una pregunta con un solo campo.");
    if (combo.disabled || combo.getAttribute("aria-disabled") === "true") throw new Error("El desplegable está deshabilitado.");
    const optionsIn = node => node ? visibleElements(node.querySelectorAll('[role="option"]')) : [];
    let popup = linkedPopup(combo);
    if (optionsIn(popup || combo).length >= 2) return { combo, popup: popup || combo };
    const before = new Set(visibleElements(document.querySelectorAll('[role="listbox"]')));
    if (combo.getAttribute("aria-expanded") !== "true") combo.click();
    // Forms can mount an asynchronously rendered popup outside the question card.
    for (let attempt = 0; attempt < 40; attempt++) {
      if (generation !== STATE.generation) return null;
      if (!root.isConnected || !combo.isConnected) throw new Error("La pregunta cambió al abrir el desplegable.");
      popup = linkedPopup(combo);
      if (optionsIn(popup || combo).length >= 2) return { combo, popup: popup || combo };
      const local = optionsIn(root);
      if (local.length >= 2 && !popup) return { combo, popup: root };
      const appeared = visibleElements(document.querySelectorAll('[role="listbox"]'))
        .filter(node => !before.has(node) && optionsIn(node).length >= 2);
      if (appeared.length > 1) throw new Error("Aparecieron varios menús. No se pueden asociar opciones con seguridad.");
      if (!popup && appeared.length === 1) return { combo, popup: appeared[0] };
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    throw new Error("Se detectó el desplegable, pero no se pudieron leer sus opciones. Abre el menú y vuelve a seleccionar la pregunta.");
  }

  function extractContext(target, dropdown = null) {
    if (!target) {
      throw new Error("No se encontró el elemento seleccionado.");
    }

    const matrix = extractMatrix(target);
    const root = matrix?.root || findQuestionRoot(target) || findBestRoot(target);
    if (!root) {
      throw new Error("No se reconoció un contenedor de pregunta.");
    }

    const select = root.querySelector('select');
    const combo = dropdown?.combo || dropdownTrigger(root);
    const popup = dropdown?.popup || (combo && linkedPopup(combo));
    const checkboxes = distinctChoices(Array.from(root.querySelectorAll('input[type="checkbox"], [role="checkbox"]')).filter(isUsableChoice));
    const selectionKind = select ? "select" : combo ? "combobox" : checkboxes.length >= 2 ? "checkbox" : "radio";
    const radios = matrix?.radios || (select ? Array.from(select.options).filter(option => option.value && !option.disabled) : combo ?
      visibleElements((popup || root).querySelectorAll('[role="option"]')) : checkboxes.length >= 2 ? checkboxes : usableRadios(root));
    if (radios.length > 12) throw new Error("La pregunta contiene demasiadas opciones.");
    const input = matrix || radios.length >= 2 ? null : findAnswerInput(root, target);
    const options = matrix?.options || (radios.length >= 2
      ? radios.map((radio, index) => ({ key: String.fromCharCode(65 + index), text: select || combo ? normalizedText(radio.textContent || radio.innerText) : extractOption(root, radio, index).text }))
      : []);
    const mode = options.length >= 2 ? 1 : input ? 2 : 0;
    if (mode === 0) {
      throw new Error("La selección no contiene opciones ni un campo de respuesta.");
    }

    const question = matrix?.question || extractQuestion(root, options, input);
    if (!question) {
      throw new Error("No se encontró el enunciado de la pregunta.");
    }

    const fingerprint = createFingerprint(root, question, options);
    const contextId = `dom-${fingerprint}`;
    return {
      root,
      input,
      requiresEvidence: matrix?.requiresEvidence === true,
      isMatrix: !!matrix,
      selectionKind, select, combo,
      popup,
      selectionSnapshot: radios.map(control => ({ control, checked: control.checked === true || control.selected === true || control.getAttribute("aria-checked") === "true" || control.getAttribute("aria-selected") === "true", text: normalizedText(control.textContent || control.innerText) })),
      originalInputValue: input ? (input.value ?? input.textContent) : null,
      optionElements: new Map(options.map((option, index) => [
        option.key.toUpperCase(),
        radios[index]
      ])),
      fingerprint,
      payload: {
        contextId,
        mode,
        question,
        multipleAnswers: selectionKind === "checkbox" || select?.multiple === true || popup?.getAttribute("aria-multiselectable") === "true",
        options: options.map(({ key, text }) => ({ key, text })),
        inputPlaceholder:
          input?.getAttribute("placeholder") ||
          input?.getAttribute("aria-label") ||
          null
      }
    };
  }

  function findQuestionRoot(target) {
    return target?.closest?.('[data-automation-id="questionItem"]') ||
      target?.closest?.('[data-question], .question-card, .card, fieldset') || null;
  }

  function findQuestionImages(root) {
    const eligible = element => element.isConnected && isVisible(element) && element.naturalWidth >= 80 && element.naturalHeight >= 50;
    const own = Array.from(root.querySelectorAll('img')).filter(eligible);
    if (own.length > 2) throw new Error("Hay varias imágenes posibles. Selecciona una pregunta con contexto inequívoco.");
    if (own.length) return own;
    // Only preceding stimulus-only blocks in this question list, never arbitrary page logos.
    let anchor = root.closest?.('.case') || root;
    for (let level = 0; anchor && level < 2; level++, anchor = anchor.parentElement) {
      let previous = anchor.previousElementSibling;
      for (let count = 0; previous && count < 12; count++, previous = previous.previousElementSibling) {
        const found = Array.from(previous.querySelectorAll('img')).filter(eligible);
        if (!found.length) continue;
        const controls = previous.querySelectorAll('input, textarea, select, [role="combobox"], [role="radio"], [role="checkbox"], [contenteditable="true"]');
        if (controls.length) return []; // An image owned by another question is not shared by default.
        if (found.length > 2) throw new Error("La imagen anterior es ambigua; no se seleccionará automáticamente.");
        return found;
      }
      if (anchor.parentElement === document.body) break;
    }
    return [];
  }

  async function encodeImage(element) {
    const fail = (code, message) => new Error(`${message} [${code}] No se enviará la consulta sin la imagen.`);
    const draw = source => {
      if (!source.naturalWidth || !source.naturalHeight)
        throw fail("IMAGE_NOT_READY", "La imagen no terminó de cargar o no puede decodificarse.");
      const scale = Math.min(1, 1280 / Math.max(source.naturalWidth, source.naturalHeight));
      const canvas = document.createElement('canvas');
      for (const [reduction, quality] of [[1, 0.88], [1, 0.72], [0.75, 0.72], [0.5, 0.72]]) {
        canvas.width = Math.max(1, Math.round(source.naturalWidth * scale * reduction));
        canvas.height = Math.max(1, Math.round(source.naturalHeight * scale * reduction));
        const context = canvas.getContext('2d');
        if (!context) throw fail("IMAGE_CANVAS", "El navegador no pudo crear el lector de imágenes.");
        context.drawImage(source, 0, 0, canvas.width, canvas.height);
        const encoded = canvas.toDataURL('image/jpeg', quality);
        if (!encoded.startsWith('data:image/jpeg;base64,')) throw fail("IMAGE_ENCODING", "El navegador no pudo convertir la imagen a JPEG.");
        const data = encoded.split(',')[1];
        if (data && data.length <= 700000) return { mimeType: 'image/jpeg', data };
      }
      throw fail("IMAGE_TOO_LARGE", "La imagen supera el límite incluso después de reducirla.");
    };
    try { return draw(element); }
    catch (error) {
      // Only a tainted canvas warrants an authorized CORS reload. Size/encoding
      // failures must retain their real cause and never trigger a network retry.
      if (error?.name !== 'SecurityError') throw error;
      const image = new Image(); image.crossOrigin = 'anonymous';
      image.src = element.currentSrc || element.src;
      let timer;
      try {
        await Promise.race([image.decode(), new Promise((_, reject) => {
          timer = setTimeout(() => reject(fail("IMAGE_LOAD_TIMEOUT", "La recarga autorizada de la imagen tardó más de 5 segundos.")), 5000);
        })]);
      } catch (loadError) {
        image.removeAttribute('src');
        if (loadError?.message?.includes('[IMAGE_LOAD_TIMEOUT]')) throw loadError;
        let url;
        try { url = new URL(element.currentSrc || element.src); } catch { /* Unsupported source keeps the CORS diagnostic. */ }
        if (url?.origin === 'https://hive.forms.usercontent.microsoft') {
          const result = await chrome.runtime.sendMessage({ type: 'AIVSP_FETCH_FORM_IMAGE', imagePath: url.pathname });
          if (!result?.ok) {
            const code = /^IMAGE_[A-Z0-9_]+$/.test(result?.error || '') ? result.error : 'IMAGE_DOWNLOAD_FAILED';
            throw fail(code, code === 'IMAGE_HOST_PERMISSION'
              ? 'En Opciones, pulsa Autorizar imágenes de Microsoft Forms y concede acceso al servidor.'
              : 'La descarga autorizada de la imagen Forms no pudo completarse.');
          }
          if (typeof result.dataUrl !== 'string' || result.dataUrl.length > 5600000 || !/^data:image\/(jpeg|png|webp);base64,[A-Za-z0-9+/]+=*$/.test(result.dataUrl))
            throw fail('IMAGE_FORMAT', 'La extensión no recibió una imagen válida.');
          const downloaded = new Image(); downloaded.src = result.dataUrl;
          try { await downloaded.decode(); return draw(downloaded); }
          catch (decodeError) {
            if (decodeError?.message?.includes('[IMAGE_')) throw decodeError;
            throw fail('IMAGE_DECODE', 'La imagen descargada no pudo decodificarse.');
          } finally { downloaded.removeAttribute('src'); }
        }
        throw fail("IMAGE_CORS_LOAD", "El navegador bloqueó la lectura de píxeles (CORS) y la recarga con permiso del servidor falló. Puede deberse a CORS, autenticación o carga del recurso.");
      } finally { clearTimeout(timer); }
      try { return draw(image); }
      catch (retryError) {
        if (retryError?.name === 'SecurityError') throw fail("IMAGE_CORS_READ", "El servidor no autorizó leer los píxeles de esta imagen (CORS).");
        throw retryError;
      } finally { image.removeAttribute('src'); }
    }
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
      const dropdownCount = visibleElements(element.querySelectorAll('select, [role="combobox"], [role="listbox"], [aria-haspopup="listbox"]')).length;
      const inputCount = visibleElements(
        element.querySelectorAll(
          'textarea, input:not([type="radio"]):not([type="checkbox"]):not([type="hidden"]):not([type="submit"]):not([type="button"]), [contenteditable="true"], [role="textbox"]')).length;
      let score = 0;
      if (dropdownCount === 1) score += 125;
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

  // Standard HTML/ARIA matrices only. Never flatten radios from different rows.
  function extractMatrix(target) {
    const tableSelector = 'table, [role="table"], [role="grid"]';
    let table = target.closest?.(tableSelector);
    if (!table) {
      for (let parent = target, depth = 0;
        parent && parent !== document.body && depth < 5;
        parent = parent.parentElement, depth++) {
        const tables = visibleElements(parent.querySelectorAll(tableSelector));
        if (tables.length === 1) { table = tables[0]; break; }
        if (tables.length > 1) break;
        if (parent === findQuestionRoot(target)) break;
      }
    }
    if (!table) return null;
    const rowSelector = 'tr, [role="row"]';
    const cellSelector = 'th, td, [role="cell"], [role="gridcell"], [role="rowheader"], [role="columnheader"]';
    const rows = visibleElements(table.querySelectorAll(rowSelector))
      .filter(row => row.closest(tableSelector) === table);
    const answerRows = rows.filter(row => usableRadios(row).length >= 2);
    if (!answerRows.length) return null;
    const clickedRow = target.closest?.(rowSelector);
    const row = answerRows.includes(clickedRow) ? clickedRow : answerRows.length === 1 ? answerRows[0] : null;
    if (!row) throw new Error("Matriz reconocida: haz clic en la fila que quieres consultar.");
    const radios = usableRadios(row);
    if (radios.some(radio => radio.disabled || radio.getAttribute("aria-disabled") === "true")) {
      throw new Error("La fila de matriz contiene opciones deshabilitadas.");
    }
    if (radios.length > 12) throw new Error("La fila de matriz tiene demasiadas opciones.");
    const cells = Array.from(row.querySelectorAll(cellSelector));
    const headerRow = rows.find(candidate => !usableRadios(candidate).length &&
      candidate.querySelectorAll(cellSelector).length >= radios.length);
    const headerCells = headerRow ? Array.from(headerRow.querySelectorAll(cellSelector)) : [];
    if (cells.length !== radios.length + 1 ||
        headerCells.length !== radios.length + 1 ||
        [...cells, ...headerCells].some(cell => Number(cell.getAttribute("colspan") || cell.getAttribute("aria-colspan") || 1) !== 1 ||
          Number(cell.getAttribute("rowspan") || cell.getAttribute("aria-rowspan") || 1) !== 1)) {
      throw new Error("Matriz reconocida, pero no se pudieron asociar sus filas y columnas con seguridad.");
    }
    const headers = headerCells.slice(-radios.length).map(cell => normalizedText(cell.innerText));
    const rowLabel = normalizedText(cells[0].innerText);
    if (!rowLabel || headers.some(text => !text) || new Set(headers).size !== headers.length ||
        cells.slice(1).some((cell, index) => {
          const contained = usableRadios(cell);
          return contained.length !== 1 || contained[0] !== radios[index];
        })) {
      throw new Error("Matriz reconocida, pero faltan etiquetas inequívocas de fila o columna.");
    }
    const options = headers.map((text, index) => ({ key: String.fromCharCode(65 + index), text }));
    let root = table;
    let title = normalizedText(table.getAttribute("aria-label") || table.querySelector("caption")?.innerText);
    for (let parent = table.parentElement, depth = 0;
      parent && parent !== document.body && depth < 4;
      parent = parent.parentElement, depth++) {
      const heading = Array.from(parent.querySelectorAll('.question, [data-question], legend, [role="heading"], h1, h2, h3, h4'))
        .find(element => !table.contains(element) && isVisible(element));
      if (heading) { root = parent; title = normalizedText(heading.innerText); break; }
    }
    return {
      root, radios, options,
      question: `${title || "Matriz de opciones"}. Fila: ${rowLabel}`,
      requiresEvidence: headers.some(text => /^(excelente|bueno|regular|deficiente|muy bueno|muy malo|de acuerdo|en desacuerdo|satisfecho|insatisfecho)$/i.test(text))
    };
  }

  function extractOption(root, radio, index) {
    const label = Array.from(radio.labels || [])
      .find((candidate) => root.contains(candidate));
    const linkedText = String(radio.getAttribute('aria-labelledby') || '').split(/\s+/)
      .map(id => document.getElementById(id)).filter(element => element && root.contains(element))
      .map(element => normalizedText(element.innerText || element.textContent)).filter(Boolean).join(' ');
    const text = normalizedText(
      label?.innerText ||
      linkedText ||
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
      'textarea, input[type="text"], input:not([type]), [contenteditable="true"], [role="textbox"]';
    if (target.matches?.(selector) && isVisible(target)) {
      return target;
    }
    const inputs = visibleElements(root.querySelectorAll(selector)).filter(element => !element.disabled && element.getAttribute("aria-disabled") !== "true");
    if (inputs.length > 1) throw new Error("Hay varios campos; selecciona directamente el campo de esta pregunta.");
    return inputs[0] || null;
  }

  function extractQuestion(root, options, input) {
    const generic = text => /^(?:(?:pregunta|question)\s*)?\d*[.)\-:\s]*(?:pregunta|question)?\s*[*: .]*$/i.test(text) ||
      /^(selecciona (la|una) respuesta|selecciona una opción|select an option)$/i.test(text);
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
        return text.length >= 8 && text.length <= 8000 && !generic(text);
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
      .filter((line) => line && !generic(line) && !excluded.has(line));
    return normalizedText(lines.slice(0, 12).join(" "));
  }

  async function applyResponse(response, context) {
    const generation = STATE.generation;
    if (!response?.ok) {
      showStatus(errorMessage(response?.error, response?.reason), "error", 5000);
      return;
    }
    if (!context.root.isConnected ||
        context.selectionSnapshot?.some(snapshot => !snapshot.control.isConnected ||
          !(context.select ? context.select.contains(snapshot.control) : context.popup ? context.popup.contains(snapshot.control) : context.root.contains(snapshot.control)) ||
          snapshot.checked !== (snapshot.control.checked === true || snapshot.control.selected === true || snapshot.control.getAttribute("aria-checked") === "true" || snapshot.control.getAttribute("aria-selected") === "true") ||
          snapshot.text !== normalizedText(snapshot.control.textContent || snapshot.control.innerText)) ||
        context.imageSources?.some(image => !image.element.isConnected || (image.element.currentSrc || image.element.src) !== image.source) ||
        (context.input && (context.input.disabled || context.input.getAttribute("aria-disabled") === "true" || (context.input.value ?? context.input.textContent) !== context.originalInputValue)) ||
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
      const keys = Array.isArray(result.optionKeys) ? result.optionKeys.map(key => String(key).toUpperCase()) : [String(result.optionKey || "").toUpperCase()];
      if (!keys.length || new Set(keys).size !== keys.length || (!context.payload.multipleAnswers && keys.length !== 1) || keys.some(key => !context.optionElements.has(key))) {
        showStatus("Las opciones devueltas no corresponden a la pregunta.", "error", 5000); return;
      }
      const selected = keys.map(key => context.optionElements.get(key));
      const selectedText = selected.map(option => normalizedText(option.innerText || option.textContent));
      const controls = [...context.optionElements.values()];
      if (controls.some(control => !control?.isConnected || control.disabled || control.getAttribute("aria-disabled") === "true") ||
          (context.select && !context.root.contains(context.select)) || (context.combo && !context.root.contains(context.combo))) {
        showStatus("La opción devuelta ya no está disponible.", "error", 5000);
        return;
      }
      const radio = selected[0];
      const target = context.select || context.combo || radio.labels?.[0] || radio;
      target.classList.add(
        subtle ? "aivsp-result-subtle" : "aivsp-result-visible");
      target.dataset.aivspResult = "true";
      if (mayApply) {
        if (context.select) {
          for (const option of Array.from(context.select.options)) option.selected = selected.includes(option);
          context.select.dispatchEvent(new Event("input", { bubbles: true }));
          context.select.dispatchEvent(new Event("change", { bubbles: true }));
        } else if (context.payload.multipleAnswers) {
          for (const control of controls) {
            const checked = control.checked === true || control.getAttribute("aria-checked") === "true" || control.getAttribute("aria-selected") === "true";
            if (checked !== selected.includes(control)) control.click();
          }
        } else radio.click();
        let confirmed = context.select ? selected.every(option => option.selected) : context.combo ? false :
          controls.every(control => (control.checked === true || control.getAttribute("aria-checked") === "true") === selected.includes(control));
        if (context.combo) {
          // React/Forms can update the selected caption after the click returns.
          // Only observe here: never retry the click or reuse a newer generation.
          for (let attempt = 0; attempt <= 20; attempt++) {
            if (generation !== STATE.generation) return;
            if (!context.root.isConnected) break;
            const live = context.combo.isConnected ? context.combo : dropdownTrigger(context.root);
            const marked = selected.every(option => option.isConnected && option.getAttribute('aria-selected') === 'true');
            const visibleOptions = live ? visibleElements(live.querySelectorAll('[role="option"]')) : [];
            const caption = live ? normalizedText(live.innerText || live.value || '') : '';
            confirmed = !!live && context.root.contains(live) && (marked ||
              (selectedText.length === 1 && selectedText[0] && visibleOptions.length === 0 &&
                live.getAttribute('aria-expanded') !== 'true' && caption === selectedText[0]));
            if (confirmed || attempt === 20) break;
            await new Promise(resolve => setTimeout(resolve, 50));
          }
        }
        if (!confirmed) { showStatus("Se intentó aplicar, pero el formulario no confirmó el cambio. Revisa la respuesta.", "warning", 5000); return; }
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
    if (STATE.testDiagnostics) {
      element.textContent = message;
      element.setAttribute("data-aivsp-diagnostic", "true");
    }
    element.title = message;
    element.setAttribute("aria-label", message);
    element.setAttribute("role", kind === "error" ? "alert" : "status");
    element.setAttribute("aria-live", kind === "error" ? "assertive" : "polite");
    document.documentElement.appendChild(element);
    STATE.statusElement = element;
    if (duration > 0 && !(STATE.testDiagnostics && kind === "error")) {
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
    style.textContent += `
      #aivsp-dom-status[data-aivsp-diagnostic="true"] {
        width: auto !important; max-width: min(640px, 85vw) !important;
        height: auto !important; padding: 10px 14px !important;
        font: 600 14px/1.4 system-ui, sans-serif !important;
        border-radius: 8px !important; white-space: normal !important;
      }
    `;
    document.documentElement.appendChild(style);
  }

  function visibleElements(elements) {
    return Array.from(elements).filter(isVisible);
  }

  function usableRadios(root) {
    return distinctChoices(Array.from(
      root.querySelectorAll('input[type="radio"], [role="radio"]'))
      .filter(isUsableChoice));
  }

  function distinctChoices(controls) {
    return controls.filter(control => !controls.some(other => other !== control && other.contains?.(control)));
  }

  function isUsableChoice(radio) {
    if (isVisible(radio)) {
      return true;
    }
    if (!(radio instanceof HTMLInputElement) ||
        !['radio', 'checkbox'].includes(radio.type) ||
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
      location.search,
      location.hash,
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

  function errorMessage(code, reason) {
    const reasons = {
      insufficient_context: "La IA se abstuvo por falta de contexto. Revisa que se leyó el enunciado y la imagen.",
      no_option_selected: "La IA devolvió una respuesta sin seleccionar ninguna clave válida.",
      unknown_option_key: "La IA devolvió una clave que no pertenece a estas opciones.",
      duplicate_option_keys: "La IA repitió claves de opción; no se aplicó la respuesta.",
      too_many_option_keys: "La IA eligió varias opciones en una pregunta de respuesta única."
    };
    if (code === 'invalid_inference_response' && reasons[reason]) return reasons[reason];
    const messages = {
      images_not_enabled: "Activa Adjuntar imágenes en las opciones de la extensión.",
      app_upgrade_required: "Esta pregunta necesita la app visual nueva. Cierra el ejecutable anterior e inicia el paquete 0.3.",
      acp_provider_required: "Selecciona Antigravity CLI en la bandeja para imágenes o varias respuestas.",
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
