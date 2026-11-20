# Laboratorio de formularios AIVSP

35 casos sintéticos para comprobar **detección**, **respuesta** y **aplicación**
por separado. No es un Microsoft Forms nativo ni reproduce exactamente Teams.
No hay envío de formulario ni subida de archivos a un servidor.

## Abrir

Desde la raíz del repositorio, con Node.js instalado:

```powershell
node scripts/serve-forms-lab.mjs
```

Abre `http://127.0.0.1:8765` en Chrome con la extensión y AIVSP ejecutándose.
El servidor solo escucha en loopback y sirve los archivos de esta fixture.
Usa también el puerto 8766 para el iframe de origen distinto. Ctrl+C lo detiene.
Abrir `index.html` directamente permite revisar el formulario, pero no reproduce
la prueba de dos orígenes y puede necesitar acceso a archivos en la extensión.

## Cobertura

- Opción única, verdadero/falso, texto corto y largo, opciones multilínea y acentos.
- Matemáticas con enunciado inequívoco.
- Varias respuestas: casillas y lista con selección múltiple.
- Menú desplegable de respuesta única, números, fechas, horas y escala deslizante.
- Matrices HTML y ARIA, rúbrica subjetiva y radios personalizados.
- Texto editable, ordenación, emparejamiento y varios campos de una pregunta.
- Pregunta individual en imagen, carga de archivo, contexto insuficiente y campos bloqueados.
- Una imagen de gráfica, fuera de las tarjetas, compartida por tres preguntas:
  desplegable, abierta y casillas múltiples. Cada tarjeta identifica su estímulo
  con `data-stimulus-id` y una descripción accesible. El dato no se transcribe en
  los enunciados: el extractor textual actual no puede resolverlo por sí solo.
- Enunciado que cambia durante la espera y dos iframes de diferente origen.
- Cuadrícula visual 3×3 de cuerpos celestes y pregunta espacial con desplegable
  personalizado ARIA (`combobox`/`listbox`/`option`), no solo un `<select>` nativo.
- Otra imagen suelta seguida de tres tarjetas, sin imágenes copiadas dentro,
  sin `data-stimulus-id`, sin `aria-describedby` y sin identificador que las vincule
  al estímulo. Se requiere asociar el contexto visual previo incluso cuando la
  imagen queda fuera de pantalla. Contrasta con la gráfica que sí tiene vínculos DOM.

Los rótulos de soporte describen el estado del código, **no certifican que todos
los casos funcionen**. Los no implementados están incluidos precisamente para
descubrir fallos. VLM sigue deshabilitado: no se añadió soporte de imágenes aquí.

## Prueba manual

1. Desactiva PRUEBA de matrices para comprobar respuestas reales. Actívala solo
   para probar un clic fijo de rúbrica y no registres ese clic como acierto de IA.
2. Mantén oculta la guía de resultados al consultar AIVSP.
3. Activa Ctrl+Shift+Y y pulsa el enunciado; en matrices, pulsa una fila concreta.
4. Verifica que corresponde a la pregunta correcta y que no cambió otro campo.
5. Registra cada fase como Correcto, Falló o No soportado; por defecto está Sin probar.
6. Exporta el JSON y compártelo para priorizar arreglos. El informe no contiene
   respuestas, archivos elegidos, preguntas ni tokens. No se conserva al recargar.

Cada marco mantiene su propio registro y botón de exportación. El iframe de otro
puerto puede quedar bloqueado por los permisos actuales: es una prueba de acceso,
no un error de razonamiento. No se amplían los permisos de la extensión.

## Validación automatizada realizada el 20 de noviembre de 2026

Con el servidor ejecutándose y Playwright disponible en Node:

```powershell
node tests/forms-lab-browser.cjs
```

Se usó Chrome headless en un perfil aislado, sin consultar IA: 35 tarjetas,
controles nativos y ARIA, guía separada, filtros, exportación manual, imagen
compartida por tres tarjetas, dos orígenes de iframe y layout de 390 px.
Se ejecutó también el content script real con un puente simulado, sin probar
permisos de la extensión cargada ni inferencia real.

**Fallo existente reproducido, no corregido en esta tarea:** en los marcos que
mezclan una única matriz con preguntas normales, `extractMatrix` puede encontrar
la matriz vecina al ascender por el contenedor. La consulta normal termina con
“Matriz reconocida: haz clic en la fila…” en lugar de extraer su propio enunciado.
La prueba registra esa limitación como conocida, no como extracción correcta.
Las matrices sí extrajeron su propia fila en los tres documentos.

Las regresiones existentes de la extensión también pasaron. No se modificaron
captura, OCR, inferencia, autopegado ni el código de extracción de la extensión.
