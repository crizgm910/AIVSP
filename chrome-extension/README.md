# AIVSP DOM Assistant

## Matrices y escalas (0.2.10)

### Prueba opcional de marcado (0.2.11)

En las opciones de la extensión activa **PRUEBA de matrices: marcar siempre la
primera opción de la fila (sin IA)** y pulsa Guardar. Está apagada por defecto.
Después activa el selector y pulsa una fila de la matriz: hará clic en su primera
opción, por ejemplo Excelente, únicamente para comprobar la integración DOM.
No es una respuesta evaluada y no utiliza IA ni envía el formulario. Es independiente
de la opción de aplicar respuestas de IA. Las preguntas normales conservan su flujo.
Desmarca la prueba y guarda al terminar; el ajuste permanece activo hasta que lo
desactives. Cada activación del selector permite un clic, no procesa toda la matriz.

Reconoce tablas HTML y matrices con roles ARIA `table`/`grid`, filas, celdas y
encabezados. Consulta una fila por activación: pulsa su texto o uno de sus radios.
Si solo hay una fila, puedes pulsar el enunciado; con varias, debes elegir fila.
No se combinan respuestas de filas distintas. Se exigen encabezados completos y
sin celdas combinadas. Los diseños sin semántica de tabla todavía necesitan
validación/adaptación con el DOM real.

En escalas de valoración como Excelente/Bueno/Regular/Deficiente, reconoce la
matriz pero muestra una advertencia y no solicita ni selecciona una nota inventada.
Es necesario revisar el trabajo para evaluarlo; la captura sola no basta.

Fixture local: `tests/fixtures/matrix-questions.html`. Para páginas `file://`, el
usuario debe autorizar acceso a archivos en los detalles de la extensión; también
puede servir la fixture desde un servidor local. No es necesario cambiar permisos
para probar directamente un formulario de Teams permitido.

Prueba real de Teams: pendiente. Recarga esta misma extensión en
`chrome://extensions` y después recarga el formulario. Conserva el atajo existente.

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

Versión actual: 0.2.11. Para Forms incrustado, abre las opciones, pulsa
`Habilitar Forms y Teams`, acepta los permisos y actualiza la tarea. La activación se
realiza por marco; una selección nueva descarta respuestas anteriores. Recargar el
script ya no provoca redeclaración de constantes. Actualiza la página tras actualizar.

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
- El puente es `http://127.0.0.1:18791`; Forms y Teams tienen permisos opcionales
  separados para documentos incrustados.
- El puente requiere un token aleatorio de 256 bits y sólo escucha en loopback.
- No se guardan credenciales de Ollama o Gemini en Chrome.
- No se registra el enunciado, las opciones ni la respuesta.
- Si el contenido de la pregunta cambia mientras se procesa, no se aplica el resultado.
- Los estados `clic en pregunta`, `procesando` y `listo` aparecen como una etiqueta
  compacta de 10 px en la esquina superior derecha y desaparecen automáticamente.
  Los errores conservan mayor contraste, pero tampoco bloquean los clics de la página.
- Con autoaplicación habilitada, una confianza inferior a `0.75` no marca ni rellena
  el formulario: se conserva la sugerencia y aparece `AIVSP · revisa`.
