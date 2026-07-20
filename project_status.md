# Estado del proyecto: AIVSP

Última revisión técnica: 20 de julio de 2026.

La auditoría completa está en [`AUDIT.md`](AUDIT.md). El backlog y su orden de
ejecución se mantienen en [`TASKS.md`](TASKS.md).

## Objetivo

AIVSP es un asistente local de escritorio para Windows. Captura la ventana activa,
extrae texto, consulta un modelo servido por Ollama y muestra una indicación visual
o pega una sugerencia de texto. El código está separado en Domain, Application,
Infrastructure y Presentation (WPF).

## Tecnologías verificadas en el código

- C# y .NET 8.
- WPF para el overlay y el icono de bandeja.
- `Windows.Media.Ocr` para OCR. Actualmente no se usa Tesseract.
- Captura de la ventana activa con GDI/Win32 (`BitBlt`).
- Ollama mediante `POST /api/generate` y respuestas JSON.
- xUnit y Moq para pruebas.

## Flujo actual

1. El usuario presiona `Ctrl + Shift + F9` con el cursor cerca de la pregunta.
2. Se elimina cualquier overlay anterior y se captura la ventana activa.
3. `Windows.Media.Ocr` procesa la captura completa.
4. Después del OCR se aísla la tarjeta numerada bajo el cursor, desde su enunciado
   hasta antes de la siguiente pregunta. Esto evita mezclar respuestas vecinas.
5. `QuestionTypeDetector` clasifica el contenido como opción múltiple o texto abierto.
6. Ollama devuelve JSON estructurado.
7. Se muestra un indicador sobre la respuesta o se pega/muestra texto sugerido.

## Estado validado

- ✅ Hotkey global registrado con `MOD_NOREPEAT`.
- ✅ Política efectiva de última solicitud: una nueva activación cancela la anterior.
- ✅ Captura de ventana activa y liberación de recursos GDI.
- ✅ OCR local con coordenadas trasladadas al origen de la ventana capturada.
- ✅ Inferencia OCR + `llama3.2:3b` y parseo de JSON.
- ✅ Overlay sin el destello final que volvía a mostrar brevemente la respuesta.
- ✅ Limpieza del overlay antes de cada captura para evitar que entre al siguiente OCR.
- ✅ Auto-pegado opcional y controles básicos desde la bandeja.
- ✅ 43 pruebas aprobadas; compilación con 0 errores y 0 advertencias.
- ✅ Arquitectura de un overlay por monitor con conciencia DPI `PerMonitorV2`.
- ⚠️ El modo VLM/Moondream está deshabilitado en la bandeja hasta completar su flujo.

## Correcciones de esta revisión

- Se evitó que una sola pulsación sostenida genere varias ejecuciones del hotkey.
- Se implementó cancelación de la ejecución anterior (`LatestRequestWins`).
- Se corrigió la animación del overlay: mantiene opacidad cero antes de colapsarse y
  reinicia su estado mientras ya está oculto.
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
- El prompt de opción múltiple se redujo a unas 100 unidades de contexto y 10 de salida
  en la prueba local; Ollama permanece cargado durante 10 minutos.

## Errores y riesgos pendientes

### Prioridad alta

1. **VLM incompleto.** Al omitir OCR, la detección queda en `Unknown`; además, la
   respuesta VLM no proporciona un rectángulo completo que pueda convertirse de forma
   confiable en `VisualIndicatorOutput`.
2. **Validación física multimonitor pendiente.** La implementación ya crea una ventana
   por monitor, admite coordenadas negativas y aplica el DPI de cada ventana. Este
   equipo sólo dispone de una pantalla, por lo que todavía falta comprobar físicamente
   combinaciones de 100 %, 125 % y 150 % antes de cerrar AIVSP-001.
3. **Errores invisibles para el usuario.** Si Ollama no está iniciado, falta el modelo
   o falla el JSON, el detalle queda principalmente en logs; la aplicación debería
   mostrar una notificación breve y accionable en la bandeja.
4. **Inferencia limitada por CPU.** En este equipo `llama3.2:3b` se ejecuta al 100 %
   en CPU porque sólo está disponible Intel UHD integrada. La medición caliente del
   prompt compacto es de aproximadamente 3.4 segundos, antes de sumar captura y OCR.

### Prioridad media

1. `ExcludedProcesses` está declarado pero no se aplica antes de procesar una captura.
2. `ConcurrencyPolicy` sigue siendo texto de configuración; el comportamiento actual
   está implementado directamente como “última solicitud gana”.
3. `ITextMatcher` y `IInferenceEngine` están registrados/definidos pero el flujo actual
   no los usa; conviene integrarlos o retirarlos para reducir arquitectura muerta.
4. El detector ya prioriza opciones A–E, pero todavía depende de formatos limitados y
   el selector contextual usa principalmente la coordenada Y.
5. Modelo, endpoint e idioma están definidos en código y no en configuración persistente.
6. Las pruebas GDI dependen del escritorio interactivo y no son adecuadas para todos
   los agentes de CI sin una categoría o condición específica de Windows interactivo.

## Siguientes pasos recomendados

1. Crear pruebas de integración para pulsaciones repetidas y cancelación durante Ollama.
2. Validar el overlay en hardware con dos o más monitores y escalas DPI mixtas.
3. Añadir una ventana de configuración para modelo, endpoint, idioma, duración y estilo.
4. Separar estilos de overlay, por ejemplo `Debug` (marcado evidente) y `Subtle`
   (borde fino o sombreado), sin duplicar la lógica de presentación.
5. Corregir el contrato VLM para devolver modo, punto y rectángulo; después reactivar
   Moondream con pruebas específicas.
6. Añadir notificaciones de estado para Ollama no disponible, timeout y respuesta inválida.
7. Añadir control de instancia única y validar HWND/PID antes del autopegado.
8. Implementar perfiles de overlay `Debug` y `Subtle`; la privacidad angular real
   requerirá un filtro físico de pantalla.
