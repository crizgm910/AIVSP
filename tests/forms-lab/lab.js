"use strict";
(() => {
  const embedded = new URLSearchParams(location.search).has("embedded");
  const catalog = globalThis.FORM_LAB_CASES;
  const cases = embedded ? catalog.filter(item => ["single", "short", "matrix"].includes(item.id)) : catalog;
  const results = new Map();
  const phases = { detection: "Detección", answer: "Respuesta", application: "Aplicación" };
  const outcomes = { pending: "Sin probar", pass: "Correcto", fail: "Falló", unsupported: "No soportado" };
  function node(tag, text, attributes = {}) {
    const element = document.createElement(tag);
    if (text !== undefined) element.textContent = text;
    for (const [name, value] of Object.entries(attributes)) element.setAttribute(name, value);
    return element;
  }
  function input(type, id, label) {
    return node("input", undefined, { type, id, "aria-label": label });
  }
  function select(options, label, multiple = false) {
    const control = node("select", undefined, { "aria-label": label });
    if (multiple) { control.multiple = true; control.size = options.length; }
    else control.append(node("option", "Selecciona…", { value: "" }));
    options.forEach(text => control.append(node("option", text, { value: text })));
    return control;
  }
  function radios(item, type = item.type) {
    const group = node("div", undefined, type === "aria-radio" ? { role: "radiogroup", "aria-label": item.title } : {});
    item.options.forEach((text, i) => {
      if (type === "aria-radio") {
        const control = node("div", text, { role: "radio", "aria-checked": "false", tabindex: i ? "-1" : "0", class: "choice" });
        const activate = () => {
          group.querySelectorAll('[role="radio"]').forEach(radio => { radio.setAttribute("aria-checked", String(radio === control)); radio.tabIndex = radio === control ? 0 : -1; });
        };
        control.addEventListener("click", activate);
        control.addEventListener("keydown", event => {
          if ([" ", "Enter"].includes(event.key)) { event.preventDefault(); activate(); }
          if (["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) {
            event.preventDefault(); const next = (i + (event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 1) + item.options.length) % item.options.length;
            group.children[next].click(); group.children[next].focus();
          }
        });
        group.append(control);
      } else {
        const control = input(type, `${item.id}-${i}`, text);
        control.name = item.id; control.value = String.fromCharCode(65 + i);
        const label = node("label", undefined, { class: "choice", for: control.id });
        label.append(control, node("span", text)); group.append(label);
      }
    });
    return group;
  }
  function customSelect(item) {
    const group = node("div", undefined, { class: "custom-dropdown" });
    const popup = node("div", undefined, { id: `${item.id}-list`, role: "listbox", "aria-label": "Opciones" });
    popup.hidden = true;
    const trigger = node("button", "Selecciona la respuesta", { type: "button", role: "combobox", "aria-expanded": "false", "aria-haspopup": "listbox", "aria-controls": popup.id, "aria-label": item.title });
    function close() { popup.hidden = true; trigger.setAttribute("aria-expanded", "false"); }
    function open(index = 0) { popup.hidden = false; trigger.setAttribute("aria-expanded", "true"); popup.children[index].focus(); }
    item.options.forEach((text, index) => {
      const option = node("div", text, { role: "option", tabindex: "-1", "aria-selected": "false" });
      function choose() {
        popup.querySelectorAll('[role="option"]').forEach(candidate => candidate.setAttribute("aria-selected", String(candidate === option)));
        trigger.textContent = text; close(); trigger.focus();
      }
      option.addEventListener("click", choose);
      option.addEventListener("keydown", event => {
        if (["Enter", " "].includes(event.key)) { event.preventDefault(); choose(); }
        else if (event.key === "Escape") { event.preventDefault(); close(); trigger.focus(); }
        else if (["ArrowDown", "ArrowUp"].includes(event.key)) { event.preventDefault(); popup.children[(index + (event.key === "ArrowDown" ? 1 : -1) + item.options.length) % item.options.length].focus(); }
      });
      popup.append(option);
    });
    trigger.addEventListener("click", () => popup.hidden ? open() : close());
    trigger.addEventListener("keydown", event => {
      if (["ArrowDown", "ArrowUp"].includes(event.key)) { event.preventDefault(); open(event.key === "ArrowDown" ? 0 : item.options.length - 1); }
      else if (event.key === "Escape") close();
    });
    group.append(trigger, popup); return group;
  }
  function matrix(item) {
    const aria = item.type === "aria-matrix";
    const grid = node(aria ? "div" : "table", undefined, aria ? { role: "grid", "aria-label": item.title } : {});
    const header = node(aria ? "div" : "tr", undefined, aria ? { role: "row" } : {});
    ["Afirmación", ...item.options].forEach(text => header.append(node(aria ? "div" : "th", text, aria ? { role: "columnheader" } : { scope: "col" })));
    grid.append(header);
    item.rows.forEach((text, r) => {
      const row = node(aria ? "div" : "tr", undefined, aria ? { role: "row" } : {});
      row.append(node(aria ? "div" : "th", text, aria ? { role: "rowheader" } : { scope: "row" }));
      item.options.forEach((label, c) => {
        const cell = node(aria ? "div" : "td", undefined, aria ? { role: "gridcell" } : {});
        const control = input("radio", `${item.id}-${r}-${c}`, `${text}: ${label}`);
        control.name = `${item.id}-${r}`; control.value = label; cell.append(control); row.append(cell);
      });
      grid.append(row);
    });
    return node("div", undefined, { class: "table-wrap" }).appendChild(grid).parentElement;
  }
  function controls(item, card, heading) {
    if (item.image) card.append(node("img", undefined, { src: item.image, alt: "Cuadrícula de cuerpos celestes; posiciones y etiquetas disponibles únicamente en la imagen.", class: "spatial-image" }));
    if (item.type === "custom-select") card.append(customSelect(item));
    else if (["radio", "checkbox", "aria-radio"].includes(item.type)) card.append(radios(item));
    else if (item.type.includes("matrix")) card.append(matrix(item));
    else if (["select", "multiselect"].includes(item.type)) card.append(select(item.options, item.title, item.type === "multiselect"));
    else if (["textarea", "editable"].includes(item.type)) {
      card.append(item.type === "textarea" ? node("textarea", undefined, { placeholder: "Escribe tu respuesta", "aria-label": item.title }) : node("div", undefined, { contenteditable: "true", role: "textbox", "aria-label": item.title }));
    } else if (item.type === "two-fields" || item.type === "match") {
      ["Francia", "Italia"].forEach((country, i) => {
        const label = node("label", `${country}: `, { class: "sort-row", for: `${item.id}-${i}` });
        const control = item.type === "match" ? select(["París", "Roma", "Madrid"], country) : input("text", `${item.id}-${i}`, country);
        control.id = `${item.id}-${i}`; label.append(control); card.append(label);
      });
    } else if (item.type === "rank") {
      [30, 10, 20].forEach(number => { const row = node("label", `${number}: posición `, { class: "sort-row" }); row.append(select(["1", "2", "3"], `Posición del ${number}`)); card.append(row); });
    } else if (item.type === "image") {
      const svg = '<svg xmlns="http://www.w3.org/2000/svg" width="500" height="90"><rect width="100%" height="100%" fill="#e7eff8"/><text x="20" y="54" font-size="24">¿Cuánto es 6 × 7?</text></svg>';
      card.append(node("img", undefined, { alt: "Imagen de una pregunta sin transcripción textual", src: "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg) }), input("text", item.id, "Respuesta a la imagen"));
    } else {
      const type = ["dynamic", "disabled"].includes(item.type) ? "text" : item.type;
      const control = input(type, item.id, item.title);
      if (type === "text") control.placeholder = "Escribe tu respuesta";
      if (type === "range") { control.min = "0"; control.max = "10"; control.value = "0"; const output = node("output", "0", { for: item.id }); control.addEventListener("input", () => { output.textContent = control.value; }); card.append(output); }
      if (item.type === "disabled") control.disabled = true;
      if (item.type === "dynamic") {
        const button = node("button", "Cambiar enunciado", { type: "button" });
        button.addEventListener("click", () => { heading.textContent = "Pregunta nueva: escribe la capital de Portugal."; card.dataset.version = "2"; });
        card.append(button);
      }
      card.append(control);
    }
  }
  function updateSummary() {
    const records = [...results.values()];
    const completed = records.filter(record => Object.keys(phases).every(phase => record[phase] !== "pending")).length;
    document.getElementById("summary").textContent = `${cases.length} casos · ${completed} con las tres fases registradas. Registro manual, no validación automática de la extensión.`;
  }
  function render() {
    const container = document.getElementById("cases"); container.replaceChildren(); results.clear();
    cases.forEach(item => {
      if (item.id === "implicit-center") {
        // Deliberately no id, aria-describedby or stimulus metadata linking this
        // loose image to the three following question cards.
        const loose = node("figure", undefined, { class: "shared-stimulus" });
        loose.append(node("img", undefined, { src: "space-grid.svg", alt: "Imagen de cuerpos celestes, sin transcripción textual." }));
        container.append(loose);
      }
      if (item.id === "shared-most") {
        const stimulus = node("figure", undefined, { id: "sales-chart", class: "card shared-stimulus" });
        stimulus.append(node("h2", "Imagen compartida: VENTAS"), node("img", undefined, {
          src: "shared-chart.svg", alt: "Gráfica de ventas de tres días; los datos están solo en la imagen, sin transcripción en las preguntas."
        }), node("figcaption", "Las tres preguntas siguientes utilizan esta misma imagen. Deben conservar esa asociación aunque la imagen salga de la vista al desplazarse.", { id: "sales-chart-caption" }));
        container.append(stimulus);
      }
      results.set(item.id, { id: item.id, detection: "pending", answer: "pending", application: "pending" });
      const wrapper = node("section", undefined, { class: "case", "data-case-id": item.id });
      wrapper.append(node("p", `${item.id} · ${item.kind} · ${item.support}`, { class: "meta" }));
      const card = node("article", undefined, { class: "card question-card", "data-automation-id": "questionItem" });
      if (item.stimulus) { card.dataset.stimulusId = item.stimulus; card.setAttribute("aria-describedby", `${item.stimulus}-caption`); }
      const heading = node("h2", item.title, { class: "question" }); card.append(heading); controls(item, card, heading); wrapper.append(card);
      const tracking = node("div", undefined, { class: "tracking", "aria-label": `Registro manual de ${item.id}` });
      Object.entries(phases).forEach(([phase, text]) => {
        const label = node("label", text); const control = node("select", undefined, { "aria-label": `${text} de ${item.id}` });
        Object.entries(outcomes).forEach(([value, title]) => control.append(node("option", title, { value })));
        control.addEventListener("change", () => { results.get(item.id)[phase] = control.value; updateSummary(); }); label.append(control); tracking.append(label);
      });
      wrapper.append(tracking); container.append(wrapper);
    });
    updateSummary();
  }
  render();
  document.getElementById("key-list")?.replaceChildren(...catalog.map(item => node("p", `${item.id}: ${item.expected}`, { class: "key-item" })));
  document.getElementById("show-key")?.addEventListener("click", event => {
    const key = document.getElementById("answer-key"); key.hidden = !key.hidden;
    event.currentTarget.setAttribute("aria-expanded", String(!key.hidden)); event.currentTarget.textContent = key.hidden ? "Mostrar guía de resultados" : "Ocultar guía de resultados";
  });
  document.getElementById("filter")?.addEventListener("change", event => {
    cases.forEach(item => { document.querySelector(`[data-case-id="${item.id}"]`).hidden = event.target.value === "base" ? item.support !== "Base existente" : event.target.value === "pending" ? item.support === "Base existente" : false; });
  });
  document.getElementById("reset")?.addEventListener("click", () => {
    if (!confirm("¿Borrar los valores y registros de prueba de esta página? Los informes ya descargados no se borran.")) return;
    document.getElementById("filter").value = "all"; render();
    document.getElementById("answer-key").hidden = true;
    document.getElementById("show-key").setAttribute("aria-expanded", "false");
    document.getElementById("show-key").textContent = "Mostrar guía de resultados";
  });
  document.getElementById("export").addEventListener("click", () => {
    const report = { schemaVersion: 1, generatedAt: new Date().toISOString(), view: embedded ? "iframe" : "principal", synthetic: true, records: [...results.values()] };
    const url = URL.createObjectURL(new Blob([JSON.stringify(report, null, 2)], { type: "application/json" }));
    const link = node("a", "", { href: url, download: `aivsp-pruebas-${Date.now()}.json` }); link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
  const crossFrame = document.getElementById("cross-frame");
  if (crossFrame) {
    if (location.protocol === "http:" && location.hostname === "127.0.0.1" && location.port === "8765") crossFrame.src = "http://127.0.0.1:8766/cases.html?embedded=1";
    else crossFrame.replaceWith(node("p", "Para probar origen distinto, inicia el servidor y abre http://127.0.0.1:8765. Abrir el archivo directamente solo permite la prueba local básica."));
  }
})();
