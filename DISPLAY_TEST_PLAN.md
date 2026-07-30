# Plan de pruebas de pantalla, DPI y zoom

Versión objetivo: `v0.2.0-beta.1`
Estado general: un monitor a 100 %, 125 % y 150 % aprobado; el zoom 90 %, 100 % y
125 % está validado parcialmente con una pregunta de opción múltiple, y las pruebas
de dos monitores están aplazadas por falta de hardware.

## Objetivo

Comprobar que captura, OCR, indicador y autopegado conservan coordenadas correctas con
distintas escalas de Windows, disposiciones de monitores y niveles de zoom del navegador.
Estas pruebas no habilitan ni evalúan VLM; se usa exclusivamente OCR + Ollama textual.

## Preparación común

1. Compilar con `dotnet build --configuration Release` y aprobar `dotnet test`.
2. Iniciar Ollama y comprobar que el modelo textual configurado está disponible.
3. Ejecutar AIVSP Release y abrir `test-quiz.html` en el navegador.
4. Mantener el perfil visual de diagnóstico para poder evaluar posición y dimensiones.
5. En cada escenario probar una pregunta de opción múltiple y una pregunta abierta.
6. Ejecutar cinco consultas consecutivas, incluida una segunda consulta inmediata.
7. Conservar captura o video, escala, resolución, zoom, disposición y `CorrelationId`.

## Criterios generales de aprobación

- El indicador cubre exclusivamente la opción elegida, con una desviación visual máxima
  de 5 píxeles físicos por lado.
- La respuesta abierta se pega en el campo enfocado de la ventana capturada.
- No se pega si cambia ventana o foco durante la consulta.
- El overlay anterior desaparece antes de la captura siguiente y no deja paneles oscuros.
- Ningún overlay queda fuera del área útil ni encima de la barra de tareas al finalizar.
- Una segunda consulta cancela o reemplaza la anterior sin mostrar resultados obsoletos.
- No se mezclan preguntas vecinas y no aparece una respuesta duplicada.
- No hay errores no informados; cualquier cancelación conserva su `CorrelationId`.

## Matriz obligatoria

| ID | Configuración | Estado | Comprobación específica |
|---|---|---|---|
| DSP-01 | 1 monitor, Windows 100 % | Aprobada el 2026-07-20 | Cinco ejecuciones correctas tras reiniciar AIVSP: tres posiciones de Q14 y autopegado en Q9/Q10. |
| DSP-02 | 1 monitor, Windows 125 % | Aprobada el 2026-07-20 | Cinco ejecuciones correctas: tres posiciones de Q14 y autopegado en Q9/Q10. |
| DSP-03 | 1 monitor, Windows 150 % | Aprobada el 2026-07-20 | Cinco ejecuciones correctas a 144 DPI: tres posiciones de Q14 y autopegado en Q9/Q10. |
| DSP-04 | 2 monitores con escalas diferentes | Pendiente por falta de hardware | Ejecutar en cada monitor y mover el navegador entre ambos. |
| DSP-05 | Monitor secundario a la izquierda | Pendiente por falta de hardware | Confirmar origen X negativo y overlay en la pantalla correcta. |
| DSP-06 | Coordenadas negativas | Automatizada; falta prueba física | Probar esquinas y zona próxima al límite entre pantallas. |
| DSP-07 | Ventana entre dos pantallas | Pendiente por falta de hardware | Probar con pregunta y respuesta a cada lado del límite. |
| DSP-08 | Navegador con zoom 90 % | Validación parcial cerrada el 2026-07-20 | Q14, Q13 multilínea y Q9 abierta fueron correctas. Borde inferior y activación inmediata se aplazan por decisión del usuario. |
| DSP-09 | Navegador con zoom 100 % | Validación parcial cerrada el 2026-07-20 | Q14, Q13, Q9 y el caso matemático próximo al borde inferior fueron correctos. Activación inmediata aplazada. |
| DSP-10 | Navegador con zoom 125 % | Validación parcial cerrada el 2026-07-20 | Q14, Q13 y tres consultas abiertas fueron correctas; un intento abierto previo agotó 30 s. Borde inferior y activación inmediata se aplazan. |

## Procedimiento multimonitor

Para `DSP-04` a `DSP-07`:

> Estado: aplazado hasta disponer físicamente de dos monitores. Esto no bloquea las
> pruebas `DSP-01`, `DSP-02`, `DSP-03`, `DSP-08`, `DSP-09` y `DSP-10`.

1. Registrar resolución, escala y posición de cada pantalla en Configuración de Windows.
2. Colocar primero el navegador completamente en la pantalla principal y ejecutar casos
   de opción múltiple y texto abierto.
3. Repetir completamente en la pantalla secundaria.
4. Colocar la secundaria a la izquierda de la principal y repetir cerca de sus cuatro
   esquinas para ejercer coordenadas X negativas.
5. Extender la ventana entre ambas pantallas con aproximadamente 50 % en cada una.
6. Probar una pregunta situada en cada mitad y otra próxima al límite físico.
7. Repetir con escalas diferentes, cerrando y abriendo AIVSP después de cambiar escalas
   para eliminar resultados ambiguos causados por caché de Windows.

## Procedimiento de zoom del navegador

Para cada zoom de `90 %`, `100 %` y `125 %`:

1. Confirmar el valor desde el menú del navegador.
2. Recargar `test-quiz.html` y situar el cursor sobre la pregunta activa.
3. Probar una opción corta, una opción multilínea y una pregunta abierta.
4. Probar una pregunta próxima al borde inferior sin permitir que el indicador invada
   la barra de tareas.
5. Ejecutar dos consultas consecutivas y verificar que no reaparezca la primera.

## Evidencia por ejecución

| Campo | Valor |
|---|---|
| ID de prueba | |
| Fecha y versión | |
| Monitor y resolución | |
| Escala de Windows | |
| Posición del monitor | |
| Zoom del navegador | |
| Tipo de pregunta | |
| Resultado | Aprobada / Fallida / Bloqueada |
| CorrelationId | |
| Captura o video | |
| Observaciones | |

## Resultados registrados

### DSP-02 — Un monitor a 125 %

Entorno: Windows a 120 DPI (125 %), monitor principal `1536×864`, AIVSP Release
`v0.2.0-beta.1`, OCR + Ollama textual con `qwen3:4b` ejecutado al 100 % por CPU.

| Caso | CorrelationId | Resultado | Tiempo y evidencia |
|---|---|---|---|
| Opción múltiple: `12 + 8` | `9ff94224-ae0b-418f-9af9-6418641a475c` | Fallida | OCR aisló 12 líneas y detectó `MultipleChoice`, pero no activó el cálculo determinista. Ollama agotó aproximadamente 30 s y la ejecución fue cancelada. |
| Texto abierto sobre recursos GDI | `cbc70277-19a4-481f-acd6-a0de507f91bf` | Fallida | OCR aisló 9 líneas y detectó `OpenText`. Ollama respondió en 17.9 s; flujo total 18.3 s. La respuesta se generó, pero el autopegado fue cancelado con `ContextExpired`. |
| Q14, opción múltiple: `12 + 8` | `e698d474-d00c-4aa6-82b5-3e30fc748891` | Aprobada | Seleccionó `C) 20` en 186 ms. No hubo solicitud HTTP a Ollama: se usó la ruta matemática determinista. Validación física confirmada por el usuario. |
| Q9, texto abierto sobre recursos GDI | `e2385584-3257-4590-80ef-1ba653710ae6` | Aprobada | Generó una respuesta en español, seleccionó el campo correcto y completó el autopegado en 5.926 s. Validación física confirmada por el usuario. |

Ronda final de cinco ejecuciones consecutivas:

| Posición/caso | CorrelationId | Resultado | Tiempo |
|---|---|---|---|
| Q14, zona superior | `15679f7f-cf00-4c03-bc58-fe2126d0b863` | `C) 20` | 257 ms |
| Q14, zona central | `d1e27398-190c-4785-9db3-bac48efaaa91` | `C) 20` | 189 ms |
| Q14, zona inferior | `52d6fbc0-65ec-4981-91cd-f775b9ecfefe` | `C) 20` | 189 ms |
| Q9, texto abierto | `c22614e8-6b11-4729-81f3-0c63b1d489f1` | Autopegado correcto | 6.397 s |
| Q10, texto abierto | `3abb3169-de86-41c2-a9e0-8ff7715f44d8` | Autopegado correcto | 8.090 s |

Conclusión: `DSP-02` queda aprobado. Las regresiones aritmética simple y de autopegado
en Q9 quedaron corregidas y validadas físicamente. Las cinco ejecuciones consecutivas
terminaron sin cancelaciones, respuestas obsoletas ni duplicadas; Q14 funcionó en las
zonas superior, central e inferior y Q9/Q10 completaron el autopegado.

### DSP-01 — Un monitor a 100 %

El primer intento se realizó después de cambiar Windows de 125 % a 100 % sin reiniciar
AIVSP. OCR e inferencia devolvían resultados correctos, pero el indicador y el clic del
autopegado usaban las coordenadas conservadas por el proceso anterior. Tras reiniciar
AIVSP, Windows confirmó 96 DPI y las pruebas visuales fueron correctas.

| Caso | CorrelationId | Resultado | Tiempo |
|---|---|---|---|
| Q14, opción múltiple | `f77347af-a59f-474a-8632-dc500f2dbb7f` | `C) 20`, indicador correcto | 595 ms |
| Q10, texto abierto | `2a59eb4e-66e7-40da-b2c0-cf244e1590a2` | Autopegado correcto | 10.960 s |
| Q9, texto abierto | `e28d169d-fb0f-4199-84af-8e595d530096` | Autopegado correcto | 8.658 s |

Ronda final de cinco ejecuciones consecutivas:

| Posición/caso | CorrelationId | Resultado | Tiempo |
|---|---|---|---|
| Q14, primera posición | `25da98fc-2d19-4128-af7c-203b578feaa9` | `C) 20` | 146 ms |
| Q14, segunda posición | `055820bd-b4fd-4be9-80f6-9e167ed85c57` | `C) 20` | 144 ms |
| Q14, tercera posición | `ebe17066-f686-4f18-b0b5-e17b642ea1e5` | `C) 20` | 135 ms |
| Q10, texto abierto | `688e6153-f672-4072-8a76-af50697cb0dc` | Autopegado correcto | 8.545 s |
| Q9, texto abierto | `4973547b-b6b3-44fe-af6e-f4853c640ed0` | Autopegado correcto | 6.033 s |

Conclusión: `DSP-01` queda aprobado. Captura, indicador y autopegado funcionan a 100 %
después de iniciar AIVSP con esa escala activa. Las cinco ejecuciones terminaron sin
cancelaciones ni respuestas duplicadas. Cada cambio de escala debe ir seguido de cerrar
y abrir AIVSP; el cambio de DPI en caliente permanece como limitación conocida.

### DSP-03 — Un monitor a 150 %

Entorno confirmado directamente en las ventanas de Chrome y AIVSP: 144 DPI (150 %).
AIVSP fue reiniciado después del cambio de escala antes de la ronda definitiva.

| Posición/caso | CorrelationId | Resultado | Tiempo |
|---|---|---|---|
| Q14, primera posición | `64528413-1032-476b-875c-412deb7ad18f` | `C) 20` | 370 ms |
| Q14, segunda posición | `77e19ab7-0e98-4b91-bcc3-106748dabb60` | `C) 20` | 120 ms |
| Q14, tercera posición | `3bb964e1-4ba8-4f70-b30a-542ab873ea74` | `C) 20` | 116 ms |
| Q10, texto abierto | `1d0286ed-54b3-43b6-bcc0-b0884cec2104` | Autopegado correcto | 8.786 s |
| Q9, texto abierto | `c92f59bc-610f-4df0-a8d6-2dca1ecf2f13` | Autopegado correcto | 6.113 s |

Conclusión: `DSP-03` queda aprobado sin cancelaciones ni respuestas duplicadas.

### DSP-08, DSP-09 y DSP-10 — Zoom del navegador

Entorno: Windows a 125 %, Chrome y AIVSP confirmados a 120 DPI. Las correlaciones se
asignan a 90 %, 100 % y 125 % según el orden de las tres pruebas físicas confirmado
por el usuario; el log de AIVSP no registra directamente el zoom del navegador.

| Zoom | CorrelationId | Resultado | Tiempo |
|---|---|---|---|
| 90 % | `4ba8385e-a4a7-4b26-b345-1506fa884ae9` | Q14: `C) 20`, correcta | 381 ms |
| 100 % | `f8b5e52e-78f8-47ca-adca-10bc6d22e46e` | Q14: `C) 20`, correcta; OCR incluyó el artefacto de radio `o C) 20` | 121 ms |
| 125 % | `09a22e04-92ad-429d-a987-90f197c2d53e` | Q14: `C) 20`, correcta | 127 ms |

Conclusión parcial: en esta pregunta de opción múltiple el zoom del navegador no
afectó la selección ni produjo cancelaciones o errores. Esto todavía no aprueba la
matriz completa: faltan una opción multilínea, una pregunta abierta, el borde inferior
y dos consultas consecutivas en cada nivel de zoom.

Ronda complementaria confirmada por el usuario: tres pruebas a 125 % y dos a 90 %.

| Zoom | Caso | CorrelationId | Resultado | Tiempo |
|---|---|---|---|---|
| 125 % | Texto abierto | `6126cbae-a0be-40cf-b90b-7e8f10f55581` | Timeout; ejecución cancelada | 30 s |
| 125 % | Texto abierto | `523560e2-1b6a-44f5-ae0b-399102adc901` | Autopegado correcto | 10.212 s |
| 125 % | Q9, texto abierto | `60532829-c7d5-4952-befa-82cead974533` | Autopegado correcto | 8.597 s |
| 125 % | Texto abierto | `72f38399-dd5d-4bb6-b734-34c46d8795b1` | Autopegado correcto | 9.882 s |
| 90 % | Q14, opción múltiple | `c6aec60a-1b24-48b3-a9fa-c53860215106` | `C) 20`, correcta | 159 ms |
| 90 % | Q13, opción multilínea | `b0956dcf-fae8-44a9-a0d8-73d210765381` | `B`, correcta | 12.859 s |

El timeout se conserva como intento fallido adicional y no se cuenta entre las tres
pruebas completadas a 125 %. La ronda amplía la cobertura, pero no cierra los tres
escenarios: 100 % sólo tiene Q14 y aún faltan casos de la matriz en 90 % y 125 %.

Ronda de casos cruzados, asignada según el orden de pruebas solicitado y ejecutado:

| Zoom | Caso | CorrelationId | Resultado | Tiempo |
|---|---|---|---|---|
| 100 % | Q13, opción multilínea | `42c9bee8-274e-4373-a694-39ae0847c850` | `B`, correcta | 5.977 s |
| 125 % | Q13, opción multilínea | `0b1c35c3-2245-4cb4-a09a-f4f273fd5904` | `B`, correcta | 5.233 s |
| 90 % | Q9, texto abierto | `c77fbb75-6762-4c72-903c-de993848eebb` | Autopegado correcto | 5.951 s |
| 100 % | Q9, texto abierto | `8d203591-6790-4154-a7fb-678974918c72` | Autopegado correcto | 5.707 s |
| 100 % | Q11 cerca del borde inferior | `33c11efb-eca5-420b-87ac-18a8be301f6a` | `B) 6 manzanas`, correcta | 153 ms |

Las cinco ejecuciones terminaron sin errores ni cancelaciones. Los tres zooms ya
cubren opción corta, opción multilínea y texto abierto. Para cerrar DSP-08 a DSP-10
faltan la comprobación del borde inferior en 90 % y 125 %, además de la activación
consecutiva inmediata, que conserva una incidencia conocida en el rearme del hotkey.

Decisión de cierre: el usuario aplaza esas tres comprobaciones. DSP-08, DSP-09 y
DSP-10 quedan cerradas como validación parcial, no como aprobación completa. La
incidencia del rearme del hotkey permanece en el backlog y no se considera resuelta.

### Incidencia separada — Activación rápida

Al intentar iniciar Q10 y activar Q9 inmediatamente después, el log recibió un solo
`WM_HOTKEY`; no se creó una segunda correlación. La ejecución registrada completó Q10
correctamente (`78f364e3-96f5-4dc6-9b8e-7145c2c27672`, 8.421 s), pero Q9 nunca llegó
a captura. La causa está en el rearme de la combinación con `MOD_NOREPEAT`, no en OCR,
Ollama, `LatestRequestWins` ni autopegado. Debe conservarse la protección contra una
pulsación sostenida al diseñar la corrección.

Las pruebas `DSP-08`, `DSP-09` y `DSP-10` requieren interacción manual porque el entorno
de automatización no admite controlar páginas locales `file://`. No se intentará eludir
esa restricción; sus resultados se incorporarán con captura o video proporcionado por
el usuario.

## Cobertura automatizada existente

- Escala 100 %, 125 % y 150 % en `OverlayCoordinateMapperTests`.
- Conversión con escalas DPI distintas por eje.
- Origen virtual negativo y límites físicos con coordenadas negativas.
- Rechazo de escalas DPI inválidas.

La cobertura matemática de coordenadas no valida composición real de Windows, cambios
de DPI en caliente, límites entre monitores, zoom del navegador ni apariencia visual.

## Gemini CLI — Validación real

El 29 de julio de 2026 se verificó la autenticación del CLI y se ejecutaron dos
consultas mediante `GeminiCliInferenceService`, `GeminiCliProcessRunner`, el parser y
el mapeo de producción. No se usaron respuestas simuladas.

| Caso | Resultado | Confianza |
|---|---|---:|
| Opción múltiple: capital de Francia | `B) París` | 1.00 |
| Texto abierto: capital de Francia | `París` | 1.00 |

Tiempo combinado: 30.8 s. Ambas consultas finalizaron dentro del timeout de 75 s.

Pendiente de interacción física:

- [x] `GEM-UI-01`: captura + OCR + Gemini + indicador sobre opción múltiple.
- [x] `GEM-UI-02`: captura + OCR + Gemini + autopegado en pregunta abierta.
- [x] `GEM-UI-03`: ejecutar varias consultas consecutivas con Gemini.
- [x] `SUB-01`: perfil `Subtle` activado y usado físicamente.
- [ ] `SUB-02`: perfil `Subtle` en fondo oscuro.

AIVSP registra `CorrelationId`, proveedor, duración y resultado de autopegado; esos
datos permiten documentar los casos sin incluir el texto de la captura en este plan.

### Ronda física Gemini — 29 de julio de 2026

| CorrelationId | Modo | Resultado | Tiempo |
|---|---|---|---:|
| `2221672f-110e-46ec-9b2a-e86a46822524` | Opción múltiple compleja | Falló el mapeo de candidato después de inferencia | 39.2 s |
| `7b66f3fd-cf19-418f-a128-d82784890a8a` | Abierta | Autopegado correcto, confianza 1.00 | 10.143 s |
| `a96a00d5-f3bc-464f-aa26-8b32fa53a592` | Captura | OCR no aisló líneas | 17 ms |
| `9fde1f49-2f83-407c-bf81-2ee39539fc99` | Abierta + `Subtle` | Autopegado correcto, confianza 0.98 | 10.532 s |
| `64457ab8-1cb4-4e7d-8e67-2c267ab6dde6` | Abierta | Autopegado correcto, confianza 1.00 | 9.598 s |
| `93a96b4b-8fe7-44d1-9db7-b0cf70da43ad` | Abierta | Autopegado correcto, confianza 0.95 | 10.359 s |
| `d8c27216-fec4-49d6-bde6-3c4842c5cfdf` | Opción múltiple | Indicador correcto, confianza 1.00 | 157 ms |
| `57024339-a169-4ec7-8ba9-0f1f473c8272` | Opción múltiple | Indicador correcto, confianza 1.00 | 113 ms |
| `6dbb2667-9228-43bf-9920-1ce353631a88` | Opción múltiple compleja | Indicador correcto, confianza 0.95 | 18.072 s |

Conclusión: Gemini, autopegado y `Subtle` funcionan físicamente. La ronda conserva
dos incidencias atribuibles a la ruta visual: una captura sin texto aislado y una
respuesta que no pudo asociarse al candidato OCR. Una integración DOM opcional para
Chrome puede eliminar esos dos puntos sin retirar el fallback OCR.
