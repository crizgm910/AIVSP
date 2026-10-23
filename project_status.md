# Estado del proyecto: AIVSP

**Versión actual: `v0.2.0-beta.1`.**

Última revisión técnica: 23 de octubre de 2026.

Recuperación: el proyecto eliminado se recuperó desde `main` de GitHub en
`C:\proyectos\AIVSP-recuperado`. Las correcciones locales de extensión hasta 0.2.9
se reconstruyeron y sus regresiones JavaScript pasan. El usuario había confirmado
la cruz/selección dentro de Teams; la detección y aplicación en una tarea real siguen
pendientes. La migración a Antigravity se realiza después de este punto recuperable.

Esta es la primera candidata beta del MVP avanzado: el flujo principal, sus protecciones
y su empaquetado cuentan con validación automatizada y pruebas físicas en un monitor.
Las limitaciones indicadas en este documento impiden considerarla estable para producción.
El historial de la versión está en
[`CHANGELOG.md`](CHANGELOG.md).

La auditoría completa está en [`AUDIT.md`](AUDIT.md). El backlog y su orden de
ejecución se mantienen en [`TASKS.md`](TASKS.md).

## Objetivo

AIVSP es un asistente de escritorio para Windows. Captura la ventana activa, extrae
texto, consulta el proveedor seleccionado —Ollama local o Gemini CLI— y muestra una
indicación visual o pega una sugerencia de texto. En Chrome puede usar opcionalmente
el DOM de la pregunta elegida con `Ctrl+Shift+Y` y clic izquierdo para evitar OCR,
conservando OCR como respaldo. El
código está separado en Domain, Application, Infrastructure y Presentation (WPF).

## Tecnologías verificadas en el código

- C# y .NET 8.
- WPF para el overlay y el icono de bandeja.
- `Windows.Media.Ocr` para OCR. Actualmente no se usa Tesseract.
- Captura de la ventana activa con GDI/Win32 (`BitBlt`).
- Ollama mediante `POST /api/generate` y respuestas JSON.
- Gemini CLI opcional mediante un proceso no interactivo y respuesta JSON.
- xUnit y Moq para pruebas.

## Flujo actual

1. El usuario presiona `Ctrl + Shift + F9` con el cursor cerca de la pregunta.
2. Se elimina cualquier overlay anterior y se captura la ventana activa.
3. `Windows.Media.Ocr` procesa la captura completa.
4. Después del OCR se aísla la tarjeta numerada bajo el cursor, desde su enunciado
   hasta antes de la siguiente pregunta. Esto evita mezclar respuestas vecinas.
5. `QuestionTypeDetector` clasifica el contenido como opción múltiple o texto abierto.
6. El proveedor seleccionado —Ollama local o Gemini CLI— devuelve JSON estructurado.
7. Se muestra un indicador sobre la respuesta o se pega/muestra texto sugerido.

## Estado validado

- ✅ Hotkey global registrado con `MOD_NOREPEAT`.
- ✅ Política efectiva de última solicitud: una nueva activación cancela la anterior.
- ✅ Captura de ventana activa y liberación de recursos GDI.
- ✅ OCR local con coordenadas trasladadas al origen de la ventana capturada.
- ✅ Inferencia OCR + `qwen3:4b`, `think: false` y parseo de JSON.
- ✅ Overlay sin el destello final que volvía a mostrar brevemente la respuesta.
- ✅ Limpieza del overlay antes de cada captura para evitar que entre al siguiente OCR.
- ✅ Auto-pegado opcional y controles básicos desde la bandeja.
- ✅ 136 pruebas aprobadas; compilación Release con 0 errores y 0 advertencias.
- ✅ Ollama local permanece como proveedor predeterminado.
- ✅ Gemini CLI está integrado como proveedor opcional con salida JSON, cancelación,
  timeout, reintento, selección desde bandeja y pruebas automatizadas.
- ✅ Perfiles `Visible` y `Subtle` seleccionables; `Subtle` usa borde fino, relleno
  mínimo, sin sombra y duración reducida.
- ✅ Proveedor, perfil, modelos, endpoint, idioma, tiempos y autopegado se conservan en
  `%LocalAppData%\AIVSP\settings.json`; no se persisten credenciales.
- ✅ `dotnet format --verify-no-changes`, Release y las 136 pruebas están aprobados.
- ✅ Auditoría de paquetes NuGet directos y transitivos sin vulnerabilidades conocidas
  en los orígenes consultados el 29 de julio de 2026.
- ✅ Publicación autocontenida `win-x64` reproducible y validada fuera de `bin/obj`:
  ZIP beta de aproximadamente 90 MB, versión de producto `0.2.0-beta.1` y versión de
  archivo `0.2.0.1`.
- ✅ GitHub Actions valida formato, extensión, Release, pruebas no interactivas,
  publicación autocontenida y carga del ZIP en cada PR y al integrar en `main`.
- ✅ Q14 (`12 + 8`) validada físicamente: `C) 20` en 186 ms sin solicitud HTTP a Ollama.
- ✅ Q9 validada físicamente: respuesta abierta en español y autopegado correcto en 5.926 s.
- ✅ DSP-02 aprobado físicamente a 125 %: cinco ejecuciones consecutivas, tres posiciones
  de pantalla y autopegado en Q9/Q10 sin cancelaciones ni respuestas duplicadas.
- ✅ DSP-01 aprobado físicamente a 100 % después de reiniciar AIVSP a 96 DPI: cinco
  ejecuciones correctas, tres posiciones de Q14 y autopegado en Q9/Q10.
- ✅ DSP-03 aprobado físicamente a 150 % después de reiniciar AIVSP a 144 DPI: cinco
  ejecuciones correctas, tres posiciones de Q14 y autopegado en Q9/Q10.
- 🟡 Zoom 90 %, 100 % y 125 % con validación parcial cerrada: opción corta, Q13
  multilínea y Q9 abierta pasaron en los tres niveles; el borde inferior pasó a 100 %.
  El borde inferior a 90/125 % y la activación inmediata quedaron aplazados por decisión
  del usuario. DSP-08 a DSP-10 no se registran como aprobación completa.
- ✅ Arquitectura de un overlay por monitor con conciencia DPI `PerMonitorV2`.
- ✅ Extensión Chrome DOM opcional con Manifest V3, activación explícita, permisos
  mínimos, puente local autenticado y OCR intacto como fallback.
- ✅ Flujo DOM probado en navegador para opción múltiple, texto abierto y cancelación
  segura cuando cambia la pregunta durante la inferencia.
- ✅ Controles ARIA validados: `radiogroup`/`radio` para opción múltiple y
  `role="textbox"`/`contenteditable` para respuestas abiertas.
- ✅ `Ctrl+Shift+Y` arma un selector explícito y el siguiente clic izquierdo elige la
  pregunta sin activar accidentalmente un control del formulario.
- ✅ Los radios personalizados de Microsoft Forms se extraen mediante su input nativo
  y etiqueta accesible, aunque el input esté visualmente oculto.
- ✅ Fuentes independientes y persistentes: `Apps de escritorio (OCR)` habilitada por
  defecto y `Chrome (DOM)` opcional. Desactivar escritorio evita captura, OCR y overlay.
- ⚠️ El modo VLM/Moondream está deshabilitado en la bandeja hasta completar su flujo.

## Correcciones de esta revisión

- Se evitó que una sola pulsación sostenida genere varias ejecuciones del hotkey.
- Se implementó cancelación de la ejecución anterior (`LatestRequestWins`).
- Se corrigió la animación del overlay: mantiene opacidad cero antes de colapsarse y
  reinicia su estado mientras ya está oculto.
- La ventana nativa del overlay se limita al área útil del monitor y se oculta por
  completo al limpiar o al concluir la última animación; dos pruebas STA cubren ambos
  recorridos para evitar la franja residual junto a la barra de tareas.
- Se limpia el indicador anterior antes de capturar la pantalla.
- Se empezó a respetar `InferenceTimeoutSeconds` como límite de la operación.
- Se corrigieron las coordenadas OCR para ventanas que no empiezan en `(0, 0)`.
- Se alineó el proyecto de pruebas de inferencia con .NET 8.
- Se corrigió una prueba de texto abierto que usaba datos incompatibles con el flujo.
- Se eliminaron clases y pruebas vacías generadas por las plantillas.
- Se agregó `.gitignore` para excluir `bin`, `obj` y resultados de pruebas.
- Se eliminó la regla de 80 px que confundía opciones cercanas con el enunciado.
- La respuesta textual del modelo tiene prioridad sobre índices numéricos contradictorios.
- Las estructuras con dos o más opciones A–E tienen prioridad sobre palabras ambiguas
  como “escribe” o “explica”.
- En selección múltiple Ollama devuelve sólo una letra, evitando JSON truncado cuando
  las opciones ocupan varias líneas.
- Las respuestas abiertas con OCR `es-MX` exigen español y excluyen portugués.
- El autopegado usa `SendInput`, reintenta el portapapeles y espera a que el campo
  reciba el foco antes de enviar `Ctrl+V`.
- Las preguntas abiertas enfocan el campo detectado antes de pegar la respuesta.
- El prompt de opción múltiple se mantiene compacto y reserva 48 tokens de salida;
  Ollama permanece cargado durante 10 minutos.
- Se corrigió el doble prefijo generado por OCR en controles de radio, por ejemplo
  `C) A)`, para seleccionar la letra real de la opción y conservar sus coordenadas.
- Las respuestas JSON vacías, truncadas o inválidas reciben un único reintento con
  instrucciones estructurales más estrictas.
- Qwen3 se ejecuta con `think: false` para que la respuesta útil no quede atrapada en
  el canal de razonamiento; opción múltiple reserva 48 tokens para no truncar JSON.
- Los prompts preservan literalmente prefijos, nombres y restricciones técnicas.
- El contexto técnico local de AIVSP se configura por separado y se incluye solo como
  apoyo para preguntas relacionadas con el propio proyecto.
- El parser rechaza propiedades desconocidas como `prompt` y respuestas que repiten
  las instrucciones, evitando mostrar o pegar un eco del prompt.
- Las fracciones del total con preguntas de cantidad restante y las expresiones
  aritméticas explícitas se resuelven y verifican de forma determinista antes de
  consultar al modelo; si no existe una coincidencia única se conserva el fallback.
- La búsqueda de expresiones matemáticas explícitas cubre todo el contexto OCR aislado,
  incluso cuando el enunciado se divide en varias líneas; múltiples expresiones
  ambiguas continúan hacia Ollama.
- El evento `Procesando` se emite después de captura, OCR y detección para que no
  contamine la imagen reconocida. `Procesando` y `Completado` actualizan logs y tooltip
  sin abrir un toast grande; sólo cancelaciones, advertencias y errores lo muestran.
- El contexto válido de autopegado es de 35 s, por encima del timeout de inferencia de
  30 s, manteniendo las comprobaciones de ventana, PID, foco, respuesta y confianza.
- Gemini CLI usa límites independientes: 75 s para inferencia y 85 s para vigencia del
  contexto de autopegado. Un timeout genera una advertencia tipada y no se confunde con
  una cancelación solicitada por el usuario o por una ejecución posterior.
- La integración DOM limita Gemini a 30 s y muestra una recomendación útil al vencer;
  el modo de escritorio conserva su límite de 75 s.
- El límite de inferencia empieza después de captura, OCR y detección; una etapa previa
  lenta ya no consume ese tiempo ni se etiqueta erróneamente como timeout del proveedor.
- El runner de Gemini inicia scripts `.cmd` reales mediante `cmd.exe`, rechaza
  metacaracteres del shell, tolera variables de entorno ausentes y elimina el proceso
  completo si se cancela o falla su entrada/salida.
- Gemini CLI `0.53.0` se ejecuta con extensiones y herramientas deshabilitadas, un
  prompt de sistema aislado, pensamiento deshabilitado y el modelo estable
  `gemini-3.1-flash-lite`. En pruebas directas respondió la opción legal correcta en
  5.3 s y una pregunta abierta compleja en 4.8 s.
- El alias interno `aivsp-fast` evita heredar `thinkingLevel`: usa
  `thinkingBudget: 0`, temperatura cero y conserva los 256 tokens para la respuesta.
  La prueba directa registró 0 tokens de pensamiento y 56 de respuesta completa.
- La entrada, salida y error estándar del proceso Gemini se fuerzan a UTF-8; los
  acentos y la `ñ` ya no dependen de la página de códigos heredada de `cmd.exe`.
- El puente real del ejecutable publicado resolvió con Gemini una opción múltiple
  correcta en 4.4 s y una abierta compleja en 4.4 s, sin filtrar configuración local.
- El paquete beta volvió a validar el puente real: `B) París`, confianza `1.0`, en
  4.076 s con Gemini CLI.
- Tras corregir placeholders, UTF-8 y presupuesto de pensamiento, la pregunta abierta
  problemática volvió a pasar con Gemini en 4.8 s, con acentos correctos y contenido
  temático completo.
- El mismo puente con Ollama resolvió localmente la pregunta LFDA con la opción `D` en
  49 ms y contestó la pregunta abierta 21 sin fugas en 28 s. Esta diferencia confirma
  que el modelo local sigue siendo la ruta más variable en preguntas abiertas largas.
- Con el enunciado preservado y las reglas en `system`, Ollama respondió esa pregunta
  abierta en 15.3 s con dos retos concretos y sin ecos de instrucciones.
- Ollama usa temperatura cero y un prompt que enumera los requisitos explícitos. Las
  respuestas meta sobre “el modelo de lenguaje” y las respuestas que omiten el segundo
  elemento solicitado se rechazan y reintentan antes de mostrarse o pegarse.
- Las reglas internas de Ollama se envían en el campo `system` de `/api/generate`; el
  campo `prompt` contiene sólo el contexto verificado y la pregunta. Esto reduce ecos
  de instrucciones sin aumentar el número de llamadas.
- Los placeholders abiertos se reconocen únicamente cuando ocupan la línea completa.
  Una pregunta que empieza con “Explica brevemente” ya no se elimina antes de inferir.
- La extensión Chrome `0.2.6` reduce selección, procesamiento y finalización a
  indicadores circulares de 18 px en la esquina superior derecha, sin sombra ni
  captura de clics. Las respuestas abiertas autoaplicadas ya no generan una tarjeta
  flotante duplicada; sólo las sugerencias que requieren revisión conservan una
  presentación compacta. El script queda versionado para limpiar reinyecciones
  futuras. El validador confirma estructura, permisos y sintaxis; la ronda física
  final sigue pendiente tras recargar la extensión y actualizar la página una vez.
  Además, no autoaplica resultados por debajo de `0.75`.
- Las sugerencias abiertas respetan ahora la misma duración visible configurada que las
  opciones múltiples; el perfil `Subtle` continúa aplicando su duración independiente.
- El arranque registra configuración operativa segura y fuerza `VLM=false`; no incluye
  texto OCR, respuestas ni credenciales.
- Los eventos de ejecución ya no registran el prompt ni el contenido de las respuestas;
  únicamente conservan metadatos necesarios para diagnóstico.
- Si el enunciado y el placeholder repiten una pista como “Explica brevemente”, el
  objetivo de autopegado prioriza la coincidencia OCR inferior dentro de la tarjeta.
- Las opciones múltiples pueden resolverse contra hechos locales verificados cuando
  una opción supera 0.75 de cobertura y aventaja a la siguiente por al menos 0.15;
  las coincidencias débiles o empatadas continúan hacia Ollama.
- El contexto técnico local se segmenta y sólo entra al prompt cuando comparte
  suficientes términos significativos con la pregunta; respuestas ajenas que devuelvan
  datos de AIVSP, Ollama o endpoints se rechazan y reciben un único reintento.
- Las respuestas abiertas reservan 96 tokens y piden una sola oración breve para evitar
  JSON truncado sin aumentar innecesariamente la latencia.
- El banco local verificado incluye el plazo del artículo 29 de la LFDA; una coincidencia
  textual exacta y única con una opción se resuelve localmente antes de llamar al modelo.

## Errores y riesgos pendientes

### Prioridad alta

1. **Overlay físico confirmado.** La ventana queda recortada al área útil, se oculta al
   terminar y ya no deja una franja junto a la barra de tareas. El usuario confirmó el
   resultado en el equipo afectado y dos regresiones STA cubren el ciclo de vida nativo.
2. **VLM incompleto.** Al omitir OCR, la detección queda en `Unknown`; además, la
   respuesta VLM no proporciona un rectángulo completo que pueda convertirse de forma
   confiable en `VisualIndicatorOutput`.
3. **Validación física multimonitor pendiente.** La implementación ya crea una ventana
   por monitor, admite coordenadas negativas y aplica el DPI de cada ventana. Este
   equipo sólo dispone de una pantalla, por lo que todavía falta comprobar físicamente
   combinaciones de 100 %, 125 % y 150 % antes de cerrar AIVSP-001.
4. **Notificaciones tipadas implementadas.** OCR, proveedor de IA, modelo ausente,
   JSON, hotkey, autopegado y timeout muestran avisos breves con `CorrelationId`.
   Procesando y completado permanecen en logs/tooltip y no abren un toast grande.
5. **Inferencia limitada por CPU y conocimiento local.** En este equipo `qwen3:4b`
   se ejecuta por CPU porque sólo está disponible Intel UHD integrada. Las consultas
   directas de regresión tardaron aproximadamente 5–8 segundos. La regresión matemática
   simple quedó corregida y Q14 se resolvió en 186 ms sin HTTP a Ollama. Q9 también fue
   validada físicamente con autopegado correcto en 5.926 s. La ronda final de DSP-02
   completó cinco ejecuciones sin errores. Ningún modelo pequeño garantiza datos
   especializados; la opción múltiple ya pasó parcialmente en zoom 90 %, 100 % y
   125 %, pero falta completar esos escenarios con texto multilínea y abierto.
6. **Gemini CLI depende de disponibilidad externa.** La versión `0.53.0` y
   `gemini-3.1-flash-lite` ya respondieron con el perfil aislado; durante el mismo
   diagnóstico, el enrutamiento automático y 2.5 Flash devolvieron errores 503 por
   alta demanda. El puente del binario publicado ya pasó una opción múltiple y una
   abierta; queda validar el clic y aplicación desde Chrome. Ollama permanece como
   respaldo local cuando Gemini no tenga cuota o disponibilidad.
7. **Perfil `Subtle` pendiente de validación física.** La configuración y reglas están
   automatizadas, pero aún deben comprobarse visualmente fondos claros/oscuros y escalas.

### Prioridad media

1. `ExcludedProcesses` está declarado pero no se aplica antes de procesar una captura.
2. `ConcurrencyPolicy` sigue siendo texto de configuración; el comportamiento actual
   está implementado directamente como “última solicitud gana”.
3. `ITextMatcher` y `IInferenceEngine` están registrados/definidos pero el flujo actual
   no los usa; conviene integrarlos o retirarlos para reducir arquitectura muerta.
4. El detector ya prioriza opciones A–E, pero todavía depende de formatos limitados y
   el selector contextual usa principalmente la coordenada Y.
5. Modelo, endpoint, idioma y tiempos ya se persisten, pero todavía no existe una
   interfaz para editarlos; por ahora deben ajustarse en el archivo de configuración.
6. Las pruebas GDI dependen del escritorio interactivo y no son adecuadas para todos
   los agentes de CI sin una categoría o condición específica de Windows interactivo.
7. Los cambios de escala de Windows en caliente conservan coordenadas anteriores en las
   ventanas ya creadas. Hasta implementar recreación automática ante `WM_DPICHANGED`,
   AIVSP debe cerrarse y abrirse después de cambiar entre 100 %, 125 % o 150 %.
8. Una segunda activación extremadamente rápida puede no generar `WM_HOTKEY` porque
   `MOD_NOREPEAT` aún considera presionada la combinación anterior. Debe mejorarse el
   rearme sin reintroducir ejecuciones duplicadas al mantener las teclas presionadas.
9. La distribución beta es autocontenida, pero no tiene firma digital ni instalador;
   Windows puede advertir sobre su procedencia al moverla entre equipos.

## Siguientes pasos recomendados

1. Estabilizar y probar lo existente antes de introducir otra función grande.
2. Diseñar un modo `StrictVisibleContext` que responda sólo con el contenido visible y
   sea la opción predeterminada para reducir alucinaciones.
3. Añadir después un banco local pequeño, curado y verificable, como complemento opcional.
4. Mover modelo, endpoint, idioma, timeout y modo de conocimiento a configuración.
5. Permitir un modelo local más grande sólo como opción, ya que este equipo trabaja por
   CPU y el aumento de tamaño elevará considerablemente la latencia.
6. Mantener el conocimiento general sin fuentes como fallback explícito y advertido.
7. Aplazar RAG local hasta estabilizar OCR, inferencia textual y configuración.
8. Mantener VLM oculto y deshabilitado durante esta etapa.

## Decisión sobre fuentes y conocimiento

Orden acordado: contexto visible estricto, banco local verificado, selección opcional
de modelo y RAG local posterior. El modelo sin fuentes no será la ruta principal. Se
proponen tres modos futuros: `Strict` (sólo pantalla), `Assisted` (pantalla más banco
local) y `General` (conocimiento del modelo con advertencia de menor confiabilidad).
