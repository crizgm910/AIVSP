# Lista de tareas de AIVSP

Estados: `Pendiente`, `En progreso`, `Bloqueada`, `Terminada`.

## Orden de trabajo

| ID | Prioridad | Estado | Tarea |
|---|---|---|---|
| AIVSP-001 | Alta | En progreso | Soporte completo para multimonitor y DPI |
| AIVSP-002 | Alta | Pendiente | Notificaciones visibles para errores de Ollama |
| AIVSP-003 | Alta | Pendiente | Configuración persistente de modelo, endpoint, idioma y overlay |
| AIVSP-004 | Media | En progreso | Mejorar la detección de preguntas mixtas |
| AIVSP-005 | Media | Pendiente | Completar el contrato y las coordenadas del modo VLM |
| AIVSP-006 | Media | Pendiente | Integrar o retirar `ITextMatcher` e `IInferenceEngine` |
| AIVSP-007 | Alta | Pendiente | Instancia única y ciclo de vida confiable |
| AIVSP-008 | Alta | Pendiente | Autopegado seguro ligado a la ventana capturada |
| AIVSP-009 | Media | Pendiente | Perfil visual `Subtle` para privacidad y baja distracción |
| AIVSP-010 | Media | Pendiente | Mejorar contexto 2D y detección de campos |
| AIVSP-011 | Media | Pendiente | Rendimiento, cobertura y mantenimiento del repositorio |

## AIVSP-001 — Soporte completo para multimonitor y DPI

Objetivo: mostrar el indicador en la posición correcta sin depender de una pantalla
primaria de `1920x1080` ni de una escala fija.

Tareas:

- [x] Crear una ventana de overlay ajustada a los límites físicos de cada monitor.
- [x] Traducir coordenadas absolutas de pantalla a coordenadas locales del overlay.
- [x] Manejar monitores situados a la izquierda o arriba, con coordenadas negativas.
- [x] Convertir píxeles físicos a unidades WPF usando el DPI de cada ventana/monitor.
- [x] Añadir pruebas para orígenes distintos de `(0, 0)` y escalas diferentes.
- [ ] Probar manualmente con 100 %, 125 % y 150 % de escala cuando haya hardware disponible.

Avance de implementación (15 de julio de 2026):

- Se eliminó el tamaño fijo de `1920x1080`.
- El servicio crea y enruta una ventana transparente por monitor.
- Los overlays se reconstruyen si Windows cambia la configuración de pantallas.
- La aplicación declara `PerMonitorV2` mediante `ApplicationHighDpiMode`.
- Pasan 43 pruebas y la aplicación inicia correctamente en la pantalla disponible
  (`1536x864`, origen `(0, 0)`). La validación física multimonitor sigue pendiente.

Criterios de aceptación:

- El indicador coincide con la respuesta en la pantalla primaria y secundaria.
- Funciona con ventanas no maximizadas y monitores con coordenadas negativas.
- No hay regresión en una sola pantalla a 100 % de escala.

## AIVSP-002 — Notificaciones visibles para errores de Ollama

Objetivo: informar al usuario de fallos recuperables sin depender de logs internos.

Tareas:

- [ ] Definir un servicio de notificaciones independiente de la bandeja.
- [ ] Diferenciar conexión rechazada, timeout, modelo ausente y JSON inválido.
- [ ] Mostrar mensajes breves con una acción recomendada.
- [ ] Evitar notificaciones duplicadas durante pulsaciones repetidas.
- [ ] Mantener el detalle técnico completo en logs.
- [ ] Añadir pruebas de cada categoría de error.

Criterios de aceptación:

- Un fallo de Ollama produce una notificación comprensible.
- La aplicación continúa activa después del error.
- No se muestra una respuesta o indicador obsoleto después del fallo.

## AIVSP-003 — Configuración persistente

Objetivo: eliminar valores operativos codificados directamente en `App.xaml.cs`.

Tareas:

- [ ] Cargar valores predeterminados desde `appsettings.json`.
- [ ] Permitir configurar modelo, endpoint, idioma OCR y timeout.
- [ ] Permitir configurar duración y estilo del overlay.
- [ ] Crear los estilos `Debug` y `Subtle` sin duplicar lógica.
- [ ] Validar endpoint, tiempos y valores numéricos antes de guardarlos.
- [ ] Persistir cambios entre ejecuciones.
- [ ] Añadir acceso a configuración desde la bandeja.

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

- [ ] Crear un mutex con nombre por usuario.
- [ ] Si ya existe una instancia, avisar o activar su icono de bandeja y salir.
- [ ] Mostrar error visible cuando `RegisterHotKey` falle.
- [ ] Esperar o cancelar ordenadamente la solicitud activa al cerrar.
- [ ] Cubrir segunda instancia, hotkey ocupado, cierre durante inferencia y reinicio.

Criterios de aceptación:

- Nunca hay dos instancias compitiendo por el hotkey.
- La bandeja diferencia claramente listo, procesando, pausado y error.

## AIVSP-008 — Autopegado seguro ligado a la ventana capturada

Objetivo: no hacer clic ni pegar si cambió la aplicación objetivo.

Tareas:

- [ ] Añadir HWND y PID al contexto de captura.
- [ ] Verificar ventana, proceso y rectángulo justo antes de pegar.
- [ ] Cancelar si cambió el foco y mostrar una notificación.
- [ ] Detectar el control editable con geometría, no sólo con placeholder OCR.
- [ ] Restaurar opcionalmente el contenido anterior del portapapeles.
- [ ] Probar cambio de ventana, campo ausente, clipboard bloqueado y cancelación.

Criterios de aceptación:

- Nunca se pega en una ventana distinta de la capturada.
- Un objetivo ambiguo no genera clic y explica el problema al usuario.

## AIVSP-009 — Perfil visual `Subtle`

Objetivo: ofrecer un indicador de baja distracción y menor visibilidad casual para el
usuario situado frente a la pantalla, sin prometer privacidad angular por software.

Tareas:

- [ ] Crear `OverlayStyleOptions` compartido por los overlays.
- [ ] Mantener `Debug` con el estilo llamativo actual.
- [ ] Añadir `Subtle`: borde de 1 px, sin sombra, relleno máximo de 6 % y 600–900 ms.
- [ ] Permitir ocultar la tarjeta de texto después de un pegado confirmado.
- [ ] Cambiar perfil desde la bandeja y persistir la selección.
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

- [ ] Crear el primer commit limpio y excluir artefactos ajenos/temporales.
- [ ] Añadir README, `.editorconfig`, CI y versionado.
- [ ] Corregir `dotnet format --verify-no-changes`.
- [ ] Recortar la captura antes del OCR cuando sea seguro.
- [ ] Reutilizar el motor OCR por idioma y disponer recursos WinRT.
- [ ] Añadir pruebas WPF/Win32 e integración end-to-end.
- [ ] Establecer umbrales de cobertura por capa.

## Regla de cierre

Una tarea sólo pasa a `Terminada` cuando cumple sus criterios de aceptación, tiene
pruebas automatizadas proporcionales al cambio y la solución compila sin advertencias.
