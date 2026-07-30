# AIVSP DOM Assistant

Extensión opcional de Chrome para leer directamente el texto de una pregunta visible.
Evita OCR y coordenadas cuando el cuestionario está en el navegador; el flujo de
escritorio con `Ctrl+Shift+F9` continúa disponible como respaldo.

## Instalación local

1. Ejecuta AIVSP.
2. En su icono de bandeja, activa `Integración Chrome (DOM)`.
3. Usa `Copiar token para extensión Chrome`.
4. Abre `chrome://extensions`, activa `Modo de desarrollador` y elige
   `Cargar descomprimida`.
5. Selecciona esta carpeta `chrome-extension`.
6. Abre `Detalles` de la extensión y, sólo si probarás páginas locales, activa
   `Permitir acceso a URLs de archivo`.
7. Abre las opciones de `AIVSP DOM Assistant`, pega el token y prueba la conexión.

## Uso

Pulsa el icono de la extensión o `Ctrl+Shift+Y`. Cuando aparezca el aviso compacto
`AIVSP listo`, haz clic izquierdo dentro del enunciado o la tarjeta de la pregunta.

Ese primer clic sólo selecciona la tarjeta: se bloquea antes de llegar al formulario.
`Esc` cancela el selector.

Si el atajo no queda asignado después de recargar la extensión, abre
`chrome://extensions/shortcuts` y establece `Ctrl+Shift+Y` para
`Armar la selección de una pregunta`.

Se admiten radios y campos HTML nativos, además de controles accesibles basados en
`role="radiogroup"`, `role="radio"`, `role="textbox"` y `contenteditable`.

De forma predeterminada la extensión sólo presenta la respuesta. La opción
`Aplicar respuestas automáticamente` debe activarse expresamente si se quiere marcar
un radio o rellenar un campo.

## Seguridad

- La extensión sólo obtiene acceso temporal a la pestaña activada por el usuario.
- El único host permitido es el puente local `http://127.0.0.1:18791`.
- El puente requiere un token aleatorio de 256 bits y sólo escucha en loopback.
- No se guardan credenciales de Ollama o Gemini en Chrome.
- No se registra el enunciado, las opciones ni la respuesta.
- Si el contenido de la pregunta cambia mientras se procesa, no se aplica el resultado.
- Los estados `clic en pregunta`, `procesando` y `listo` aparecen como una etiqueta
  compacta de 10 px en la esquina superior derecha y desaparecen automáticamente.
  Los errores conservan mayor contraste, pero tampoco bloquean los clics de la página.
- Con autoaplicación habilitada, una confianza inferior a `0.75` no marca ni rellena
  el formulario: se conserva la sugerencia y aparece `AIVSP · revisa`.
