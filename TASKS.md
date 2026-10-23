# Lista de tareas de AIVSP

Estados: `Pendiente`, `En progreso`, `Bloqueada`, `Terminada`.

## Plan: modo de prueba de matrices — 2026-10-23

- [x] Añadir opción desactivada por defecto y aviso de primera opción fija, sin IA.
- [x] Transmitirla al armar selección y limitarla a una fila de matriz por activación.
- [x] Validar destino vigente/habilitado antes del clic; no enviar el formulario.
- [x] Probar modo apagado/encendido, opciones y regresiones normales; sin nuevos permisos.
- [x] Documentar cómo activarlo/desactivarlo y preparar extensión 0.2.11.
- [ ] Confirmar físicamente el clic en la rúbrica real de Teams.

## Plan: preguntas matriz/Likert en extensión — 2026-10-23

- [x] Reconocer tablas HTML y matrices ARIA, asociando fila y columnas.
- [x] Procesar una sola fila por activación; pedir fila si el clic es ambiguo.
- [x] No inventar valoraciones de rúbricas subjetivas sin evidencia del trabajo.
- [x] Probar extracción, encabezados, aislamiento de filas y regresiones existentes.
- [x] Añadir formulario local de prueba; documentar límites y validar Release/tests.
- [ ] Confirmar reconocimiento con la representación real de Forms/Teams.

La captura de Teams muestra una escala de valoración, no una pregunta con una
respuesta objetivamente correcta. La compatibilidad con el DOM real de Teams
seguirá pendiente hasta ejecutar una prueba allí.

## Plan de recuperación y migración — 2026-10-23

1. [x] Recuperar `main` en `C:\proyectos\AIVSP-recuperado`, sin sobrescribir ajustes.
2. [x] Reconstruir extensión 0.2.9: reinyección, marcos, cancelación y respuestas tardías.
3. [x] Guardar la recuperación en un commit separado (`d7e372a`).
4. [x] Verificar Antigravity real, su esquema JSON y límites de herramientas.
5. [x] Integrar proveedor opcional sin cambiar OCR, overlay ni proveedores anteriores.
6. [x] Probar configuración antigua, token conservado, estados fallidos, herramientas
   denegadas, preguntas abiertas, opción múltiple y cancelación; 165 pruebas .NET.
7. [x] Documentar configuración, pruebas reales y riesgos externos.
8. [ ] Validar físicamente autopegado con Antigravity en Chrome/Brave.
9. [ ] Validar extracción y aplicación en una tarea real de Teams.

Los registros posteriores de beta.1 se conservan como historial; el estado de la
recuperación y beta.2 es el de esta sección y `project_status.md`.

## Orden de trabajo

| ID | Prioridad | Estado | Tarea |
|---|---|---|---|
| AIVSP-001 | Alta | En progreso | Soporte completo para multimonitor y DPI |
| AIVSP-002 | Alta | En progreso | Notificaciones visibles para errores de Ollama |
| AIVSP-003 | Alta | En progreso | Configuración persistente de modelo, endpoint, idioma y overlay |
| AIVSP-004 | Media | En progreso | Mejorar la detección de preguntas mixtas |
| AIVSP-005 | Media | Pendiente | Completar el contrato y las coordenadas del modo VLM |
| AIVSP-006 | Media | Pendiente | Integrar o retirar `ITextMatcher` e `IInferenceEngine` |
| AIVSP-007 | Alta | En progreso | Instancia única y ciclo de vida confiable |
| AIVSP-008 | Alta | En progreso | Autopegado seguro ligado a la ventana capturada |
| AIVSP-009 | Media | En progreso | Perfil visual `Subtle` para privacidad y baja distracción |
| AIVSP-010 | Media | Pendiente | Mejorar contexto 2D y detección de campos |
| AIVSP-011 | Media | Pendiente | Rendimiento, cobertura y mantenimiento del repositorio |
| AIVSP-012 | Alta | Pendiente | Modos de conocimiento y reducción de alucinaciones |
| AIVSP-013 | Alta | En progreso | Proveedor opcional Gemini CLI |
| AIVSP-014 | Media | En progreso | Empaquetado y entrega reproducible para Windows x64 |

## AIVSP-001 — Soporte completo para multimonitor y DPI

Objetivo: mostrar el indicador en la posición correcta sin depender de una pantalla
primaria de `1920x1080` ni de una escala fija.

Plan manual detallado: [`DISPLAY_TEST_PLAN.md`](DISPLAY_TEST_PLAN.md).

Tareas:

- [x] Crear una ventana de overlay ajustada a los límites físicos de cada monitor.
- [x] Traducir coordenadas absolutas de pantalla a coordenadas locales del overlay.
- [x] Manejar monitores situados a la izquierda o arriba, con coordenadas negativas.
- [x] Convertir píxeles físicos a unidades WPF usando el DPI de cada ventana/monitor.
- [x] Limitar cada ventana transparente al área útil para no cubrir la barra de tareas.
- [x] Ocultar la ventana nativa completa cuando no hay indicador ni sugerencia activos.
- [x] Probar el ocultamiento inmediato y posterior a la animación en un hilo STA real.
- [x] Añadir pruebas para orígenes distintos de `(0, 0)` y escalas diferentes.
- [x] Probar manualmente con un monitor a 100 %, 125 % y 150 %.
- [ ] Probar dos monitores con escalas diferentes (aplazado por falta de hardware).
- [ ] Probar el monitor secundario a la izquierda y coordenadas negativas (aplazado
  por falta de hardware; las coordenadas negativas conservan cobertura automatizada).
- [ ] Probar una ventana distribuida entre dos pantallas (aplazado por falta de hardware).
- [ ] Probar el navegador con zoom de 90 %, 100 % y 125 % (validación parcial cerrada:
  opción corta, Q13 y Q9 aprobadas en los tres; borde inferior aprobado a 100 %.
  Borde inferior a 90/125 % y activación inmediata aplazados por decisión del usuario).

Avance de implementación (15 de julio de 2026):

- Se eliminó el tamaño fijo de `1920x1080`.
- El servicio crea y enruta una ventana transparente por monitor.
- Los overlays se reconstruyen si Windows cambia la configuración de pantallas.
- La aplicación declara `PerMonitorV2` mediante `ApplicationHighDpiMode`.
- Pasan 136 pruebas. Las escalas 100 %, 125 % y 150 % están validadas físicamente en
  un monitor. Q14 también pasó con zoom 90 %, 100 % y 125 %; la matriz completa de
  zoom y la validación física multimonitor siguen pendientes.

Criterios de aceptación:

- El indicador coincide con la respuesta en la pantalla primaria y secundaria.
- Funciona con ventanas no maximizadas y monitores con coordenadas negativas.
- No hay regresión en una sola pantalla a 100 % de escala.

## AIVSP-002 — Notificaciones visibles para errores de Ollama

Objetivo: informar al usuario de fallos recuperables sin depender de logs internos.

Tareas:

- [x] Definir un contrato de notificaciones independiente de la bandeja.
- [x] Implementar estados tipados y códigos seguros con `CorrelationId`.
- [x] Diferenciar indisponibilidad de Ollama y JSON inválido.
- [x] Mostrar mensajes breves mediante la bandeja sin bloquear el flujo.
- [x] Mantener progreso y finalización en tooltip/log sin abrir toasts grandes; reservar
  los avisos visibles para cancelaciones, advertencias y errores.
- [ ] Evitar notificaciones duplicadas durante pulsaciones repetidas.
- [x] Mantener el detalle técnico completo en logs sin exponerlo al usuario.
- [x] Cubrir procesando, completado, cancelado, advertencia, OCR, JSON y autopegado.
- [x] Diferenciar timeout de una cancelación manual mediante una advertencia tipada.
- [x] Diferenciar modelo ausente con una acción recomendada específica.

Criterios de aceptación:

- Un fallo de Ollama produce una notificación comprensible.
- La aplicación continúa activa después del error.
- No se muestra una respuesta o indicador obsoleto después del fallo.

## AIVSP-003 — Configuración persistente

Objetivo: eliminar valores operativos codificados directamente en `App.xaml.cs`.

Tareas:

- [ ] Cargar valores predeterminados desde `appsettings.json`.
- [x] Persistir modelo, endpoint, idioma OCR y timeout.
- [x] Persistir duración y perfil del overlay.
- [x] Crear los estilos `Visible` y `Subtle` sin duplicar lógica.
- [x] Validar endpoint, tiempos y valores numéricos antes de guardarlos.
- [x] Persistir cambios entre ejecuciones.
- [x] Añadir acceso a proveedor y perfil desde la bandeja.
- [ ] Añadir una interfaz para editar modelo, endpoint, idioma y timeout.

Criterios de aceptación:

- Reiniciar la aplicación conserva la configuración elegida.
- Una configuración inválida muestra un mensaje y conserva el último valor válido.
- El estilo se puede cambiar sin recompilar.

## AIVSP-004 — Detección de preguntas mixtas

Objetivo: evitar que palabras como “explica” oculten una estructura real de opción múltiple.

Tareas:

- [x] Detectar primero estructuras fuertes de opciones A–E.
- [x] Separar indicios fuertes y débiles de texto abierto.
- [ ] Cubrir formatos con paréntesis, puntos, guiones, números y viñetas.
- [ ] Añadir casos en español e inglés.
- [x] Añadir pruebas para preguntas con instrucciones mixtas.
- [ ] Registrar la razón de clasificación para diagnóstico.

Criterios de aceptación:

- Dos o más opciones claras tienen prioridad sobre palabras ambiguas de texto abierto.
- Los casos existentes continúan pasando.
- Cada regla nueva tiene una prueba de regresión.

## AIVSP-005 — Contrato y coordenadas del modo VLM

Objetivo: reactivar Moondream únicamente cuando pueda producir un overlay confiable.

Tareas:

- [ ] Definir un contrato con `mode`, `x`, `y`, `width`, `height` y confianza.
- [ ] Documentar si las coordenadas son relativas a la captura o absolutas de pantalla.
- [ ] Evitar que el flujo VLM dependa de resultados OCR vacíos.
- [ ] Validar y limitar el rectángulo a la imagen capturada.
- [ ] Traducir el rectángulo al escritorio virtual.
- [ ] Añadir pruebas de respuestas válidas, incompletas y fuera de rango.
- [ ] Reactivar la opción de bandeja sólo después de superar las pruebas.

Criterios de aceptación:

- VLM puede marcar una respuesta sin crear un `OcrResult` ficticio ambiguo.
- Coordenadas inválidas no dibujan fuera de pantalla.
- El modo permanece deshabilitado mientras falle cualquier prueba esencial.

## AIVSP-006 — Limpiar abstracciones de inferencia y matching

Objetivo: eliminar arquitectura muerta y dejar una sola ruta clara para seleccionar opciones.

Tareas:

- [ ] Documentar dónde debería intervenir `ITextMatcher` en el flujo actual.
- [ ] Integrar `ITextMatcher` como fallback común o eliminarlo con sus registros.
- [ ] Comparar `IInferenceEngine` con `IInferenceService` y conservar un solo contrato.
- [ ] Retirar implementaciones, registros y pruebas que queden obsoletos.
- [ ] Mantener la separación entre Application e Infrastructure.
- [ ] Verificar que no queden tipos públicos sin consumidores.

Criterios de aceptación:

- No existen servicios registrados que nunca se resuelven.
- Hay una sola abstracción responsable de la inferencia usada por la aplicación.
- La selección difusa conserva pruebas para coincidencia, margen y ambigüedad.

## AIVSP-007 — Instancia única y ciclo de vida confiable

Objetivo: impedir procesos duplicados y hacer inequívoco el estado operativo.

Tareas:

- [x] Crear un mutex con nombre por sesión de usuario.
- [x] Si ya existe una instancia, avisar mediante `MessageBox` y salir sin iniciar el host.
- [x] Mostrar error visible cuando `RegisterHotKey` falle.
- [x] Registrar adquisición, rechazo y liberación de instancia, además del registro y
  liberación del hotkey, mediante `ILogger`.
- [x] Esperar o cancelar ordenadamente la solicitud activa al cerrar.
- [ ] Rearmar el hotkey después de detectar la liberación real de `F9`, permitiendo una
  segunda pulsación rápida sin retirar la protección contra repetición sostenida.
- [ ] Añadir una prueba de regresión para dos activaciones distintas y otra para una
  pulsación mantenida que sólo debe producir una ejecución.
- [x] Cubrir adquisición exclusiva, rechazo, liberación, reinicio e idempotencia del mutex.
- [ ] Automatizar el conflicto real de hotkey y el cierre durante inferencia.

Criterios de aceptación:

- Nunca hay dos instancias compitiendo por el hotkey.
- Dos pulsaciones físicas separadas generan dos solicitudes aunque sean rápidas; mantener
  las teclas presionadas genera solamente una.
- La bandeja diferencia listo, procesando, pausado y error sin mostrar un toast grande
  para cada ejecución correcta.

## AIVSP-008 — Autopegado seguro ligado a la ventana capturada

Objetivo: no hacer clic ni pegar si cambió la aplicación objetivo.

Tareas:

- [x] Añadir HWND, PID y control enfocado al contexto de captura.
- [x] Verificar ventana, proceso y foco justo antes de pegar.
- [ ] Verificar también el rectángulo de la ventana antes de pegar.
- [x] Cancelar por cambio de contexto, timeout, respuesta vacía o confianza baja y notificar.
- [ ] Detectar el control editable con geometría, no sólo con placeholder OCR.
- [x] Restaurar el contenido anterior del portapapeles después de usar `Ctrl+V`.
- [x] Probar cambio de ventana, foco, timeout, respuesta vacía, confianza baja y éxito.
- [ ] Probar campo ausente y portapapeles bloqueado.

Criterios de aceptación:

- Nunca se pega en una ventana distinta de la capturada.
- Un objetivo ambiguo no genera clic y explica el problema al usuario.

## AIVSP-009 — Perfil visual `Subtle`

Objetivo: ofrecer un indicador de baja distracción y menor visibilidad casual para el
usuario situado frente a la pantalla, sin prometer privacidad angular por software.

Tareas:

- [x] Crear opciones de estilo compartidas por los overlays.
- [x] Mantener `Visible` con el estilo llamativo actual.
- [x] Añadir `Subtle`: borde de 1 px, sin sombra, relleno máximo de 6 % y 300–2000 ms.
- [x] Ocultar la tarjeta de texto después de un pegado confirmado.
- [x] Cambiar perfil desde la bandeja y persistir la selección.
- [ ] Respetar alto contraste, DPI y reducción de animaciones.
- [ ] Probar fondos claros/oscuros, 100/125/150 % y distancias acordadas.
- [ ] Documentar el uso de un filtro físico para privacidad de ángulo real.

Criterios de aceptación:

- `Subtle` sigue siendo identificable por el usuario directo y es menos llamativo que
  `Debug` a distancia, bajo condiciones de prueba documentadas.
- El estilo no altera coordenadas, foco ni lógica de respuesta.
- Errores críticos permanecen visibles.
- No se añaden mecanismos destinados a evadir grabaciones o supervisión.

## AIVSP-010 — Contexto 2D y detección de campos

Objetivo: funcionar en dos columnas, preguntas sin número y controles con estilos variados.

Tareas:

- [ ] Usar cursor X/Y y distancia bidimensional.
- [ ] Segmentar por gaps, alineación y bloques visuales.
- [ ] Asociar continuaciones multilínea a enunciados y opciones.
- [ ] Detectar cajas editables aunque el placeholder no aparezca en OCR.
- [ ] Añadir casos de dos columnas, zoom, scroll y preguntas sin numeración.

## AIVSP-011 — Rendimiento, cobertura y repositorio

Objetivo: reducir latencia y convertir el prototipo en una base mantenible.

Tareas:

- [x] Crear el primer commit limpio y excluir artefactos ajenos/temporales.
- [x] Añadir README, `.editorconfig`, CI y versionado.
- [x] Corregir `dotnet format --verify-no-changes`.
- [ ] Recortar la captura antes del OCR cuando sea seguro.
- [ ] Reutilizar el motor OCR por idioma y disponer recursos WinRT.
- [ ] Añadir pruebas WPF/Win32 e integración end-to-end.
- [ ] Establecer umbrales de cobertura por capa.

## AIVSP-012 — Modos de conocimiento y reducción de alucinaciones

Objetivo: hacer explícito de dónde puede obtener información la inferencia y evitar
que el conocimiento general sin fuentes sea la ruta principal.

Orden de implementación futuro:

- [ ] Definir `Strict`: responder únicamente con el contexto visible capturado.
- [ ] Definir el comportamiento cuando el contexto visible sea insuficiente, sin inventar.
- [ ] Añadir pruebas de respuestas soportadas, insuficientes y ambiguas.
- [ ] Crear después un banco local pequeño, curado, versionado y verificable.
- [ ] Definir `Assisted`: contexto visible más banco local, indicando qué fuente ganó.
- [ ] Mantener `General` como fallback opcional con advertencia de menor confiabilidad.
- [x] Filtrar el contexto técnico local por relevancia antes de construir el prompt.
- [x] Rechazar y reintentar respuestas abiertas que filtren configuración de AIVSP u
  Ollama cuando la pregunta no la solicita.
- [x] Rechazar respuestas meta sobre el modelo y respuestas incompletas cuando la
  pregunta exige dos elementos.
- [x] Usar temperatura cero y requisitos explícitos para reducir variación en Ollama.
- [x] Evitar que Chrome autoaplique resultados con confianza inferior a `0.75`.
- [x] Añadir el artículo 29 de la LFDA al banco verificado y resolver una coincidencia
  exacta y única antes de consultar al modelo.
- [x] Cubrir con regresiones la fuga de contexto, el reintento, la coincidencia legal
  exacta y el presupuesto de salida para texto abierto.
- [ ] Permitir seleccionar un modelo más grande desde configuración, sin cambiar el
  predeterminado ni prometer menor latencia en equipos que ejecutan por CPU.
- [ ] Aplazar RAG local hasta que OCR, inferencia textual y configuración estén estables.
- [ ] Mantener VLM oculto y deshabilitado durante todo este trabajo.

Criterios de aceptación:

- El modo activo y sus fuentes permitidas son inequívocos y quedan registrados en logs.
- `Strict` no completa información ausente con conocimiento general del modelo.
- Cada modo tiene pruebas y una notificación segura cuando no puede responder.
- La incorporación del banco local o de otro modelo no altera captura, OCR ni autopegado.

## AIVSP-013 — Proveedor opcional Gemini CLI

Objetivo: permitir elegir Gemini CLI sin reemplazar la ruta estable de Ollama ni
conceder permisos de edición al proceso externo.

Tareas:

- [x] Añadir selección `Ollama`/`GeminiCli` desde la bandeja.
- [x] Ejecutar Gemini de forma no interactiva, sin extensiones ni herramientas y con
  salida JSON.
- [x] Validar en Windows la ejecución real de un `.cmd`, su entrada estándar y salida.
- [x] Aislar argumentos, validar el nombre del modelo y enviar el contenido por entrada
  estándar para reducir riesgos de inyección de comandos.
- [x] Implementar cancelación y finalización del árbol de procesos.
- [x] Aplicar timeout y vigencia de autopegado independientes para la mayor latencia
  esperada de Gemini, sin debilitar las validaciones de contexto.
- [x] Reutilizar matemáticas deterministas, banco verificado, parseo y coordenadas.
- [x] Informar proveedor no disponible sin mostrar datos sensibles.
- [x] Persistir proveedor, ejecutable y modelo sin guardar credenciales.
- [x] Cubrir opción múltiple, texto abierto, reintento, error, cancelación y enrutamiento.
- [x] Autenticar Gemini CLI mediante una cuenta o proyecto compatible.
- [x] Validar el servicio real con respuesta múltiple y abierta usando el runner,
  parser y mapeo de producción.
- [x] Fijar `gemini-3.1-flash-lite`, aislar el prompt de sistema y reducir pensamiento
  y salida para evitar extensiones, contexto ajeno y latencia innecesaria.
- [x] Migrar configuraciones antiguas con modelo vacío al modelo estable.
- [ ] Ejecutar pruebas físicas DOM de respuesta múltiple y abierta con el binario
  publicado y Gemini real.

Estado actual: Gemini CLI `0.53.0` respondió con `gemini-3.1-flash-lite` y aislamiento
completo. La prueba directa resolvió la opción legal correcta en 5.3 s y una pregunta
abierta compleja en 4.8 s; el puente del ejecutable publicado completó una opción
múltiple correcta y una abierta sin fugas en 4.4 s cada una. Los modelos automático y
2.5 Flash devolvieron 503 por alta demanda, por lo que AIVSP conserva Ollama como
respaldo. Después de fijar UTF-8 y el alias sin pensamiento, la pregunta problemática
pasó de nuevo en 4.8 s con acentos correctos. AIVSP no crea ni almacena credenciales.

## AIVSP-014 — Empaquetado Windows x64

Objetivo: distribuir una compilación ejecutable fuera del árbol `bin/obj` y sin
requerir que el equipo de destino tenga instalado el SDK de .NET.

Tareas:

- [x] Crear un perfil Release autocontenido para `win-x64`.
- [x] Añadir un script reproducible con validación de ruta y ejecutable resultante.
- [x] Generar y comprimir el paquete en `artifacts/publish`.
- [x] Verificar versión, tamaño y presencia del ejecutable.
- [x] Iniciar el binario publicado y comprobar bandeja, mutex, hotkey, configuración
  persistente y `VLM=false`.
- [ ] Evaluar firma digital e instalador después de estabilizar la versión beta.

Criterio beta cumplido: el ZIP autocontenido se puede descomprimir y ejecutar en
Windows x64. La firma y el instalador se mantienen como endurecimiento de distribución,
no como requisito para las pruebas locales de esta versión.

## AIVSP-015 — Extensión Chrome DOM opcional

Objetivo: obtener texto y controles exactos del navegador sin depender de OCR, sin
reemplazar el flujo de escritorio ni ampliar permisos innecesariamente.

Tareas:

- [x] Crear extensión Manifest V3 con `activeTab`, `scripting` y `storage`.
- [x] Limitar la comunicación a `http://127.0.0.1:18791`.
- [x] Crear un puente loopback autenticado con token aleatorio y desactivado por defecto.
- [x] Extraer pregunta, opciones y campo abierto mediante `Ctrl+Shift+Y` y clic izquierdo.
- [x] Reemplazar `_execute_action` por el comando explícito `select-question`.
- [x] Interceptar el clic de selección antes de que alcance el formulario.
- [x] Reconocer radios nativos visualmente ocultos con etiqueta accesible visible,
  como los controles personalizados de Microsoft Forms.
- [x] Admitir radios HTML/ARIA y campos nativos, `role="textbox"` o `contenteditable`.
- [x] Reutilizar el proveedor, timeout y perfil visual configurados en AIVSP.
- [x] Cancelar la aplicación si el contenedor o su contenido cambian.
- [x] Mantener desactivada por defecto la aplicación automática de respuestas.
- [x] Añadir validación de manifiesto, permisos y sintaxis JavaScript.
- [x] Probar en navegador opción múltiple, texto abierto y cambio de contexto.
- [x] Incluir la extensión y su documentación en el paquete autocontenido.
- [x] Separar en la bandeja `Apps de escritorio (OCR)` y `Chrome (DOM)` como fuentes
  independientes y persistentes.
- [x] Instalarla físicamente en Chrome.
- [ ] Ejecutar la ronda física final con selección por clic izquierdo sobre
  `test-quiz.html` y Microsoft Forms.

Criterio beta: la ruta DOM es opcional, local y explícita; no registra contenido,
no guarda credenciales de proveedores y OCR sigue disponible como fallback.

## Regla de cierre

Una tarea sólo pasa a `Terminada` cuando cumple sus criterios de aceptación, tiene
pruebas automatizadas proporcionales al cambio y la solución compila sin advertencias.
