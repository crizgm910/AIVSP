# AIVSP DOM Assistant

## Endurecimiento SPA y vistas ocultas (0.3.8)

La huella del contexto incluye query y fragmento de URL: una respuesta pendiente
no se aplica si cambia de asignación/ruta aunque la página conserve el mismo DOM.
Las imágenes ocultas por CSS se excluyen; las imágenes visibles en el documento
pero fuera de pantalla siguen siendo elegibles. Ambos fallos se reprodujeron con
el código anterior y las regresiones ahora pasan. No cambia app/inferencia/permisos.

Pruebas sintéticas adicionales: edición del usuario, campo deshabilitado,
contenedor sustituido, consulta válida, activaciones consecutivas y opciones largas.
`scripts/test-browser-regressions.ps1` ejecuta todas las suites, deteniéndose ante
cualquier fallo. Requiere Playwright y laboratorio local activo; no usa tu sesión.

### Comprobación física pendiente de Teams

En un formulario de prueba autorizado, con PRUEBA de matrices desactivada:

1. Opción única, varias respuestas, abierta y desplegable: comprobar campo final.
2. Imagen propia y una imagen suelta seguida de tres preguntas: comprobar cada asociación.
3. Dos consultas seguidas: no aplicar la primera sobre la segunda pregunta.
4. Cambiar de tarea/página, editar o deshabilitar el destino mientras espera:
   cancelar, no sobrescribir. Comprobar tanto pestaña directa como marco de Teams.
5. Navegar con Siguiente/Atrás y verificar persistencia manual, sin enviar el formulario.

Los tests locales no validan todos los DOM ni políticas de una institución.
Si falla, conservar versión, paso y aviso diagnóstico; no compartir token ni datos personales.

## Detección de opciones y estímulo separado (0.3.7)

Lee nombres de opción mediante `aria-labelledby` dentro de la pregunta, admite
checkboxes nativos ocultos con etiqueta visible y evita contar dos veces un
control nativo dentro de un radio/checkbox ARIA. No cambia inferencia ni permisos.
Reproducido antes de la corrección: opciones con nombres accesibles se enviaban
como valores internos 0/1/2; pruebas nuevas verifican los nombres y la aplicación.

`node tests/browser-form-choices.cjs` comprueba también imagen previa fuera de
pantalla, sin vínculos DOM de ayuda, compartida por tres preguntas consecutivas
(radio, abierta, casillas), con wrappers anidados. Verifica que una imagen nueva
reemplaza el estímulo anterior y que una imagen propia de otra pregunta no se toma
como compartida. DOM sintético e inferencia simulada: no valida el Forms/Teams real.
Mantener la prueba física pendiente; si vuelve a fallar, obtener el aviso exacto.

## Confirmación asíncrona (0.3.6)

Después de un solo clic en una opción personalizada, observa durante un máximo
de un segundo si Forms actualiza la etiqueta o aria-selected. Admite que el
framework sustituya el control dentro de la misma pregunta. No repite el clic.
No confunde la lista de opciones visible con el valor seleccionado. Si no hay
evidencia, conserva la advertencia; una nueva activación cancela la observación.
No confirma guardado remoto ni envío del formulario, solo cambio local del campo.

Regresión reproducida: valor actualizado 180 ms después del clic provocaba aviso
prematuro con el código anterior. Ahora pasa, junto con control sustituido, ausencia
de cambio y activación nueva durante la espera. Misma app alpha.2 y mismo token.

## Enunciados y rechazos de IA (0.3.5)

Se ignoran títulos genéricos (“Pregunta”, “28. Pregunta *”) si existe un enunciado
descriptivo posterior. Reproducido en Chrome: el código anterior enviaba solo el
título; la regresión ahora comprueba el enunciado. No confirma por sí sola que sea
la única causa del intento real, cuyos logs anteriores no guardaban esa longitud.

Con la app v0.3.0-alpha.2 se distinguen abstención por falta de contexto, ausencia
de clave, clave desconocida, duplicados y varias claves en respuesta única. La
validación no se relaja ni inventa una selección. Logs: razón estática, longitudes
y conteos, nunca el texto de pregunta/respuesta ni imágenes.

## Imágenes alojadas por Microsoft Forms (0.3.4)

Cuando CORS impide leer una imagen de `hive.forms.usercontent.microsoft`, la
extensión puede descargar sus bytes mediante el service worker con permiso del
usuario. Opciones → **Autorizar imágenes de Microsoft Forms** → conceder acceso.
El botón de Forms/Teams no solicita este permiso; es independiente y opcional.
**Adjuntar imágenes** debe seguir activado y guardado. No cambia token ni app.

El broker solo acepta emisores HTTPS de Forms/Teams, rutas `/images/` del dominio
exacto y JPEG/PNG/WebP con firma válida; máximo 4 MiB y diez segundos por descarga.
No envía cookies, credenciales, token local ni referrer, no sigue redirecciones,
no registra URLs/píxeles ni permite URLs arbitrarias. Máximo cuatro descargas
simultáneas. Los bytes se convierten a JPEG bajo el límite normal del puente.
Esto es acceso por permiso de extensión, no desactivar CORS del navegador:
[documentación oficial de Chrome](https://developer.chrome.com/docs/extensions/develop/concepts/network-requests).

La imagen proporcionada devolvió HTTP 200, JPEG de 37723 bytes sin autenticación y
sin cabecera CORS el 20 de noviembre. No se guarda su dirección en el repositorio.
La descarga y el flujo se probaron por separado con mocks y Chrome aislado;
la concesión real del permiso y el Forms del usuario siguen pendientes.

La asociación se busca en el DOM, incluida imagen previa fuera de la pantalla;
no requiere capturar la pantalla. Si Forms desmonta la imagen del DOM o hay
varias candidatas, no se puede garantizar la asociación y se cancela.

## Diagnóstico de imágenes (0.3.3)

No amplía permisos ni usa capturas completas. Los errores incluyen códigos:

- `IMAGE_CORS_LOAD`: el canvas original bloqueó lectura de píxeles y falló la
  recarga anónima autorizada. Puede ser CORS, autenticación o carga del recurso;
  no permite concluir cuál de estas causas provocó la recarga fallida.
- `IMAGE_CORS_READ`: incluso tras la recarga se bloquea leer los píxeles.
- `IMAGE_LOAD_TIMEOUT`: recarga superior a cinco segundos.
- `IMAGE_TOO_LARGE`: supera 700000 caracteres base64 incluso después de reducir.
- `IMAGE_CANVAS`, `IMAGE_ENCODING`, `IMAGE_NOT_READY`: lector no disponible,
  conversión JPEG inválida o imagen sin dimensiones decodificadas.

Se intenta reducir primero calidad/tamaño, siempre con lado máximo 1280 px.
Solo se reintenta por red después de un `SecurityError` del canvas; errores de
tamaño/conversión conservan su causa. Nunca se envía texto sin su imagen.
Prueba con dos orígenes HTTP reales: `node tests/browser-images.cjs` (Playwright).
El formulario real sigue pendiente: comunicar el código con diagnóstico activado.

## Desplegables personalizados (0.3.2)

Corrección candidata para el error real “La selección no contiene opciones ni un
campo de respuesta”. Reconoce combobox, disparadores con aria-haspopup=listbox y
listbox; espera hasta dos segundos por las opciones y admite aria-controls,
aria-owns, aria-labelledby o un único portal nuevo tras abrir el menú.
Prioriza el contenedor questionItem de Forms frente a una tarjeta visual interna.
Rechaza varios menús/desplegables ambiguos y no usa listas vecinas ya abiertas.

`tests/browser-dropdowns.cjs` verifica estos patrones con DOM sintético, extracción
y selección. No es una copia del DOM real de Microsoft Forms ni prueba definitiva
en Teams. Para comprobarlo: recarga extensión y Forms, mantén diagnóstico activado,
selecciona el enunciado y comunica el mensaje si vuelve a fallar. No cambies el token.

## Diagnóstico opcional (0.3.1)

En Opciones → Diagnóstico de prueba, activa **Mostrar mensajes de diagnóstico en el
formulario**, guarda y recarga Forms. Al seleccionar una pregunta se mostrará el
motivo del fallo en texto, en lugar de solo `!`. El error permanece hasta la siguiente
activación o recarga. No activa el clic fijo de matrices,
no cambia el proveedor y no registra preguntas, imágenes ni respuestas.

Está apagado por defecto. Desactívalo al terminar para recuperar los indicadores
discretos. Esta revisión añade observabilidad; el fallo específico del desplegable
real de Forms sigue pendiente del mensaje de diagnóstico. No requiere cambiar el
ejecutable v0.3.0-alpha.1 ni el token. Recarga la extensión y el formulario.

## Imágenes y opciones avanzadas (0.3.0)

Requiere app `v0.3.0-alpha.1` para imágenes y respuestas múltiples, con proveedor
Antigravity CLI/ACP (validado con agy 1.5.1). Activa **Adjuntar imágenes** en Opciones;
por defecto no se permite enviar imágenes. No borres ni compartas el token.

Admite inicialmente casillas, select/multiselect HTML, combobox ARIA de opción única,
imagen de la pregunta y estímulo visual anterior sin campos de respuesta. Una misma
imagen previa puede acompañar varias consultas sucesivas. Si hay otra pregunta con
controles entre ambas, no toma su imagen como estímulo compartido.

Los controles deben tener semántica accesible y las imágenes deben poder leerse
desde canvas (mismo origen o CORS permitido). No se descarga desde el servidor ni
se captura toda la pantalla para eludir esta restricción. Máximo dos imágenes,
1280 px por lado y 700000 caracteres base64 por imagen. No se envía el formulario.

La selección es por proximidad DOM, no una garantía de relación semántica. El modo
matriz subjetiva continúa requiriendo evidencia; su modo PRUEBA no consulta IA.
Teams real, imágenes protegidas y desplegables personalizados multiselección no
se declaran validados. Ver `tests/BROWSER-VISUAL-INTEGRATION.md` en el repositorio.

## Diagnóstico de Teams (0.2.12)

Durante el modo de prueba los avisos muestran texto legible (el modo normal
mantiene sus indicadores). En Opciones → Diagnóstico de prueba → Actualizar
diagnóstico aparecen las últimas 24 etapas, con fecha y conteos de tablas, filas y
radios. No contiene preguntas, respuestas, direcciones de página ni tokens.

`selection_started`: selector activado en un documento. `target_clicked`: recibió
el clic. `extraction_failed`: no pudo construir el contexto (ver aviso visible).
`matrix_missing`: detectó controles por la ruta normal, no una matriz semántica.
`matrix_recognized`: matriz interpretada. `test_click_confirmed`: el radio indica
selección; `test_click_not_confirmed`: se llamó al clic, pero no se confirmó cambio.
Los documentos se registran individualmente, por lo que una activación puede
producir varias entradas en páginas con iframes. La lista es local, no se envía a AIVSP.

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

Versión actual: 0.2.12. Para Forms incrustado, abre las opciones, pulsa
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
