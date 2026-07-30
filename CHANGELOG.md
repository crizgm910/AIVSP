# Registro de cambios

Todos los cambios relevantes de AIVSP se documentarán en este archivo.

El proyecto sigue [Versionado Semántico](https://semver.org/lang/es/). Las versiones
con sufijo `alpha` o `beta` son preliminares y pueden contener defectos conocidos.

## [Sin publicar]

Sin cambios registrados después de la candidata beta.

## [0.2.0-beta.1] - 2026-07-29

### Añadido

- Proveedor opcional `GeminiCli`, seleccionable desde la bandeja sin reemplazar Ollama.
- Ejecución de Gemini CLI no interactiva, sin extensiones ni herramientas, con perfil
  de sistema aislado, salida JSON, timeout, cancelación del árbol de procesos,
  diagnóstico seguro y un reintento estructurado.
- Enrutador de inferencia para seleccionar `Ollama` o `GeminiCli` en tiempo de ejecución.
- Perfiles visuales `Visible` y `Subtle`; el segundo usa borde fino, relleno mínimo,
  sin sombras y duración configurable entre 300 y 2000 ms.
- Persistencia atómica de proveedor, perfil, modelos, endpoint, tiempos, idioma OCR y
  autopegado en `%LocalAppData%\AIVSP\settings.json`, sin guardar credenciales.
- Pruebas de Gemini CLI, enrutamiento, estilos, fuentes, tiempos, persistencia y ciclo
  de vida nativo del overlay, cierre de solicitudes y visibilidad de notificaciones;
  total actual: 136.
- `README.md` con compilación, ejecución, opciones y solución de problemas.
- Perfil y script reproducibles para publicar Windows x64 autocontenido.
- Extensión Chrome Manifest V3 opcional para extraer preguntas directamente del DOM,
  con activación explícita, permisos mínimos y OCR intacto como respaldo.
- Puente HTTP restringido a loopback, autenticado con token local de 256 bits,
  límite de solicitud, exclusión mutua y errores seguros.
- Pruebas de navegador para opción múltiple, texto abierto y cancelación si el DOM
  cambia antes de aplicar el resultado.
- Compatibilidad DOM con radios ARIA, grupos `radiogroup`, campos `role="textbox"` y
  editores `contenteditable`, validada en el navegador.
- Fuentes independientes en la bandeja: `Apps de escritorio (OCR)` y `Chrome (DOM)`.
  Desactivar escritorio detiene el flujo antes de captura/OCR sin afectar Chrome.
- Activación explícita de la tarjeta DOM mediante `Ctrl+Shift+Y` y un clic izquierdo
  capturado; el clic de selección no llega al formulario.

### Cambiado

- El mensaje y la notificación de inferencia identifican al proveedor seleccionado.
- El procesamiento estructurado de respuestas y las rutas deterministas se comparten
  entre Ollama y Gemini CLI para conservar selección, matemáticas y coordenadas.
- Gemini dispone de un timeout independiente de 75 s y una vigencia de autopegado de
  85 s; un timeout ahora se notifica como advertencia distinta de una cancelación manual.
- El timeout comienza al entrar en inferencia; captura y OCR ya no consumen ese límite
  ni producen una advertencia de IA incorrecta.
- En Windows, los ejecutables `.cmd`/`.bat` de Gemini se inician correctamente mediante
  `cmd.exe`; se rechazan metacaracteres, las variables ausentes se consultan sin error y
  el árbol de procesos se elimina ante cancelación o fallo de entrada/salida.
- La entrada, salida y error estándar de Gemini CLI usan UTF-8 explícito para impedir
  texto corrupto como `distribuciÃ³n` en respuestas con acentos.
- La inferencia real de Gemini quedó validada de extremo a extremo dentro del servicio:
  opción múltiple seleccionó `B) París` y texto abierto produjo `París`, ambas con
  confianza 1.00 y usando el runner, parser y mapeo de producción.
- Las respuestas abiertas ya respetan `OverlayDurationMilliseconds`; se eliminó el
  valor fijo de 2000 ms que ignoraba la configuración persistida.
- El inicio registra proveedor, perfil, autopegado y timeouts efectivos sin contenido
  OCR ni credenciales, y fuerza explícitamente `VLM=false`.
- Los logs operativos dejaron de guardar el prompt, la opción elegida y la respuesta
  abierta; conservan sólo metadatos técnicos como confianza, longitud y coordenadas.
- Se validó el ejecutable publicado fuera de `bin/obj`: inició bandeja, mutex, hotkey,
  configuración Gemini y `VLM=false`.
- La auditoría de dependencias directas y transitivas no detectó paquetes NuGet
  vulnerables en los orígenes consultados.
- Se corrigió la deuda de espacios detectada por `dotnet format`.
- La publicación autocontenida incluye ahora la extensión Chrome, su guía y un ZIP
  reproducible con hash SHA-256.
- El estado DOM de procesamiento usa un indicador compacto en la esquina superior
  derecha y Gemini tiene un límite específico de 30 s en Chrome, sin reducir el
  timeout de 75 s del modo de escritorio.
- La extensión `0.2.6` muestra selección, procesamiento y finalización como indicadores
  circulares de 18 px, sin sombra ni captura de clics. Ya no duplica una respuesta
  abierta en una tarjeta flotante después de autoaplicarla; las respuestas que requieren
  revisión conservan una sugerencia compacta. El controlador del script queda
  versionado para limpiar reinyecciones futuras. Si la confianza es inferior a `0.75`,
  no autoaplica el resultado.
- Gemini CLI fija `gemini-3.1-flash-lite`, deshabilita pensamiento y migra configuraciones
  antiguas con modelo vacío. Las pruebas directas resolvieron una opción legal en
  5.3 s y una pregunta abierta compleja en 4.8 s.
- El alias interno `aivsp-fast` apunta a 3.1 Flash Lite con temperatura cero y
  `thinkingBudget: 0`; evita que el razonamiento consuma casi todo el límite de salida.
- El puente real del binario publicado completó con Gemini una opción múltiple correcta
  y una abierta sin fugas en 4.4 s cada una. Con Ollama, la pregunta LFDA se resolvió
  localmente con `D` en 49 ms y la abierta 21 respondió sin datos internos en 28 s.
- La prueba publicada posterior confirmó la pregunta abierta problemática en 4.8 s con
  Gemini y 15.3 s con Ollama, ambas relevantes y sin corrupción ni instrucciones.
- Se reemplazó el atajo implícito `_execute_action` por el comando explícito
  `select-question`; el icono y `Ctrl+Shift+Y` comparten ahora la misma ruta.
- Microsoft Forms ya reconoce sus radios nativos invisibles cuando están asociados a
  una etiqueta accesible visible; antes se descartaban y la tarjeta aparecía sin
  opciones aunque el usuario hiciera clic dentro de ella.
- El contexto local se filtra ahora por relevancia antes de incluirse en el prompt; una
  respuesta abierta ajena a AIVSP ya no puede copiar el endpoint ni datos de Ollama.
- Las respuestas abiertas reservan 96 tokens, exigen una sola oración breve y reciben
  un único reintento si el modelo devuelve configuración técnica no solicitada.
- Ollama usa temperatura cero y un prompt que exige cumplir cada requisito. También se
  rechazan y reintentan respuestas meta sobre el modelo y respuestas que omiten el
  segundo elemento cuando la pregunta solicita dos.
- La instrucción de corrección de Ollama aparece antes del enunciado en los reintentos
  y sus ecos se rechazan, evitando que una instrucción interna se use como respuesta.
- Las instrucciones de Ollama se separaron en el campo `system` de `/api/generate`;
  `prompt` conserva únicamente contexto verificado y pregunta para reducir ecos.
- Se ancló la detección de placeholders al contenido completo de la línea; preguntas
  que comienzan con `Explica brevemente` ya no se confunden con el campo de entrada.
- El banco verificado incluye el artículo 29 de la LFDA y puede elegir de forma
  determinista la opción que coincide exactamente con el hecho legal almacenado.
- El host rastrea la solicitud del hotkey activa, la cancela y espera su limpieza antes
  de cerrar, sin dejar inferencias en segundo plano.
- Un modelo ausente en Ollama produce un error tipado y una instrucción útil en escritorio
  y Chrome, en lugar de confundirse con una indisponibilidad genérica del proveedor.
- Los estados rutinarios `Procesando` y `Completado` ya no abren notificaciones grandes
  de Windows; sólo errores, advertencias y cancelaciones requieren atención visible.
- El paquete beta publicado inició con mutex, hotkey, puente loopback y `VLM=false`;
  su prueba de humo real con Gemini resolvió `B) París` con confianza 1.0 en 4.076 s.

### Limitaciones conocidas

- Gemini CLI está autenticado y su ruta aislada responde en el puente publicado; queda
  probar físicamente el clic y aplicación del resultado desde Chrome.
- La cuota y disponibilidad de Gemini siguen siendo externas. Durante el diagnóstico,
  el enrutamiento automático y 2.5 Flash devolvieron errores 503 por alta demanda;
  Ollama continúa disponible como respaldo local.
- El perfil `Subtle` todavía requiere validación visual física en fondos claros/oscuros
  y distintas escalas. Es baja distracción, no un mecanismo de ocultamiento.
- VLM continúa oculto/deshabilitado y la validación multimonitor sigue aplazada.
- El paquete beta todavía no tiene firma digital ni instalador.

## [0.2.0-alpha] - 2026-07-20

Primera versión alfa documentada del MVP avanzado. Esta versión reúne el flujo local
de captura, OCR, inferencia, indicación visual y autopegado protegido que ya cuenta con
validación automatizada. No se considera todavía una versión estable para producción.

### Funciones validadas

- Aplicación local para Windows basada en .NET 8 y WPF.
- Hotkey global `Ctrl + Shift + F9` registrado con `MOD_NOREPEAT`.
- Control de instancia única mediante mutex con aviso visible y registro en logs.
- Política de última solicitud: una activación nueva cancela la ejecución anterior.
- Captura de la ventana activa mediante GDI/Win32 con liberación de recursos.
- Contexto de captura con identificadores de ventana, proceso y control enfocado.
- OCR local mediante `Windows.Media.Ocr`, con traducción de coordenadas al escritorio.
- Selección de la tarjeta numerada bajo el cursor para evitar preguntas vecinas.
- Clasificación de opción múltiple y texto abierto, incluidas opciones multilínea.
- Inferencia local con Ollama y `qwen3:4b`, salida JSON estructurada y `think: false`.
- Respuestas abiertas solicitadas en español cuando el OCR usa `es-MX`.
- Reintento único para respuestas JSON vacías, incompletas o inválidas.
- Rechazo de ecos del prompt y propiedades JSON no permitidas.
- Resolución determinista de expresiones aritméticas explícitas y fracciones del total.
- Selección contra conocimiento local verificado cuando hay una coincidencia fuerte y
  no ambigua; los casos débiles conservan el fallback a Ollama.
- Overlay por monitor, coordenadas negativas y conversión DPI `PerMonitorV2` cubiertos
  por pruebas automatizadas.
- Limpieza del overlay anterior antes de cada captura.
- Autopegado opcional con `SendInput`, reintento de portapapeles y restauración del
  contenido anterior.
- Validación de ventana, proceso, foco, antigüedad, respuesta y confianza antes de pegar.
- Notificaciones tipadas no bloqueantes para procesamiento, finalización, cancelación,
  advertencia y error, identificadas mediante `CorrelationId`.
- Logs diarios con correlación y detalle técnico separado del mensaje mostrado al usuario.
- Controles operativos básicos desde el icono de la bandeja del sistema.

### Correcciones recientes

- Se evitó que una pulsación sostenida del hotkey genere ejecuciones duplicadas.
- Se impidió que una solicitud cancelada dibuje una respuesta obsoleta.
- Se corrigió el destello que volvía a mostrar brevemente el indicador al terminar.
- La ventana transparente del overlay queda limitada al área útil de cada monitor y
  se oculta completamente cuando termina la animación o se limpia el resultado, para
  no dejar una franja residual junto a la barra de tareas.
- Los estados rutinarios `Procesando` y `Completado` conservan logs, correlación y
  tooltip, pero ya no abren un toast grande de Windows. Cancelaciones, advertencias y
  errores continúan visibles.
- Se evitó incluir el overlay anterior en la siguiente captura OCR.
- Se corrigieron las coordenadas OCR de ventanas cuyo origen no es `(0, 0)`.
- Se mejoró la detección de preguntas mixtas para priorizar estructuras A–E.
- Se eliminaron falsos emparejamientos causados por la antigua regla vertical de 80 px.
- Se corrigieron opciones con prefijos OCR duplicados, por ejemplo `C) A)`.
- Se mejoró el agrupamiento y emparejamiento de opciones que ocupan varias líneas.
- Se evitó que respuestas abiertas en español se generen en portugués mediante una
  instrucción de idioma explícita.
- Se aumentó el margen de salida de opción múltiple para evitar JSON truncado.
- Se protegió el autopegado frente a cambio de ventana, foco, timeout, respuesta vacía
  o confianza insuficiente y se añadió una notificación de cancelación.
- Se corrigió el autopegado cuando el enunciado repite el texto del placeholder: ahora
  se prioriza la pista OCR inferior correspondiente al campo de respuesta.
- Se hicieron visibles los errores de OCR, Ollama, JSON, hotkey y autopegado.

### Riesgos y limitaciones conocidas

- La corrección automatizada del overlay residual ya pasa sus pruebas de ciclo de vida;
  falta confirmar físicamente la desaparición de la franja en el equipo afectado.
- La implementación multimonitor y DPI tiene pruebas automatizadas, pero falta validarla
  físicamente con varios monitores y escalas de 100 %, 125 % y 150 %.
- El modo VLM/Moondream permanece deshabilitado porque su contrato y sus coordenadas
  todavía no producen un indicador confiable.
- La inferencia con `qwen3:4b` se ejecuta por CPU en el equipo actual y puede tardar
  varios segundos; un modelo pequeño tampoco garantiza conocimiento especializado.
- El selector contextual sigue dependiendo principalmente del eje Y y de preguntas
  numeradas; diseños de dos columnas o controles atípicos pueden fallar.
- El objetivo de texto abierto continúa siendo heurístico si OCR no detecta el campo.
- Después de cambiar la escala de Windows debe reiniciarse AIVSP; la recreación automática
  de overlays y coordenadas ante cambios de DPI en caliente aún no está implementada.
- Una segunda activación muy rápida puede ser suprimida por `MOD_NOREPEAT`; todavía debe
  mejorarse el rearme sin reintroducir solicitudes duplicadas por pulsación sostenida.
- Falta validar el rectángulo de la ventana antes del autopegado y cubrir portapapeles
  bloqueado o campo ausente con pruebas de integración.
- Modelo, endpoint, idioma y estilo visual aún no tienen configuración persistente.
- Las pruebas GDI requieren un escritorio Windows interactivo y no son portables a todos
  los entornos de integración continua.
- No existe todavía una prueba integral automatizada del recorrido completo desde el
  hotkey hasta el overlay o autopegado.
