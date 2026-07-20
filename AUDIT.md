# Auditoría técnica de AIVSP

Fecha: 20 de julio de 2026.

## Resumen ejecutivo

AIVSP tiene una separación razonable entre Domain, Application, Infrastructure y
Presentation, compila sin advertencias y sus 43 pruebas actuales pasan. El prototipo
ya demuestra captura, OCR, inferencia local, overlay y autopegado.

Todavía no debe considerarse estable para uso cotidiano. Los riesgos principales no
están en la compilación, sino en el ciclo de vida de la aplicación, la falta de errores
visibles, el autopegado después de una inferencia lenta y las heurísticas que ubican la
pregunta o el campo usando principalmente coordenadas verticales.

El modo visual actual es de diagnóstico: borde verde de 8 px, relleno rojo y sombra.
Puede añadirse un perfil sutil para privacidad y baja distracción, pero ningún cambio
de software puede hacer que una pantalla sea visible únicamente desde el frente. Para
restringir ángulos de visión hace falta un filtro físico de privacidad.

## Alcance y evidencia

Se revisaron los nueve proyectos de la solución, el flujo de ejecución, contratos,
captura GDI, OCR de Windows, integración con Ollama, overlay WPF, entrada Win32,
configuración, pruebas, documentación y estado del repositorio.

Validaciones realizadas:

- `dotnet build Aivsp.slnx --configuration Debug --no-restore`: correcto, 0 errores y
  0 advertencias.
- `dotnet test ... --collect:"XPlat Code Coverage"`: 43/43 pruebas correctas.
- Cobertura observada por suite: Application 75.6 % de líneas; suite de inferencia
  53.3 % incluyendo dependencias; suite Windows 16.9 % incluyendo dependencias.
- Presentation WPF no tiene un proyecto de pruebas dedicado.
- `dotnet list ... package --vulnerable --include-transitive`: sin vulnerabilidades
  conocidas en los orígenes NuGet consultados.
- `dotnet format --verify-no-changes`: falla por numerosos problemas de formato.
- Git está inicializado en `main`, pero no existe un commit base y todos los archivos
  siguen sin seguimiento.

## Hallazgos de prioridad alta

### A-01 — No existe instancia única

Abrir el ejecutable varias veces crea procesos que compiten por el mismo hotkey. Si
`RegisterHotKey` falla, sólo se escribe un log y la aplicación aparenta estar lista.

Recomendación: mutex con nombre, activar/notificar la instancia existente y mostrar
un aviso si el hotkey no puede registrarse.

### A-02 — Los fallos son invisibles

Errores de Ollama, JSON, OCR, hotkey y pegado se registran, pero el flujo lanzado con
`Task.Run` no informa al usuario. Esto explica comportamientos percibidos como
“a veces no funciona”.

Recomendación: servicio de notificaciones tipadas, correlación por solicitud y estados
breves `Procesando`, `Completado`, `Cancelado` y `Error`.

### A-03 — El autopegado puede actuar sobre otra ventana

Entre la captura y la respuesta de Ollama pueden transcurrir varios segundos. El código
mueve el cursor y pega en coordenadas estimadas sin comprobar que la ventana activa sea
la misma que se capturó. Si el usuario cambia de aplicación, puede pegar texto en el
lugar equivocado.

Recomendación: conservar HWND/PID en `CaptureContext`, validar identidad y límites
antes de hacer clic y cancelar con notificación si el foco cambió.

### A-04 — Selección contextual frágil

`QuestionContextSelector` usa la coordenada Y y encabezados numerados. Puede mezclar
columnas, fallar en formularios sin numeración y tomar una pregunta anterior si el OCR
omite el encabezado actual.

Recomendación: usar X e Y, proximidad bidimensional, separación por bloques/tarjetas,
gaps verticales y relación geométrica entre enunciado, opciones y campo.

### A-05 — No hay línea base recuperable en Git

El repositorio no tiene commits y `outputs/` contiene artefactos ajenos al producto.
Ante una regresión no existe una versión estable a la cual comparar o volver.

Recomendación: decidir qué artefactos pertenecen al proyecto, ampliar `.gitignore`,
crear un commit base y trabajar en ramas `codex/...` o ramas de característica.

## Hallazgos de prioridad media

### A-06 — Confianza y matching no gobiernan la decisión

La salida visual crea un `MatchResult` con score y margen `1.0` aunque el modelo no
aporte confianza. `MinimumMatchScore`, `MinimumMatchMargin` y `ITextMatcher` no
participan en la ruta real.

### A-07 — El objetivo de texto abierto es heurístico

Si el placeholder no aparece en OCR, el punto de pegado se estima 45 px debajo del
enunciado. Preguntas multilínea, etiquetas, zoom o estilos distintos pueden desplazar
el campo.

### A-08 — El modo VLM es inalcanzable

Cuando se omite OCR, el detector recibe una lista vacía y devuelve `Unknown` antes de
llamar al servicio VLM. La opción está deshabilitada, lo cual evita el fallo al usuario,
pero el código incompleto permanece en producción.

### A-09 — Configuración parcialmente ficticia

Hotkey, modelo y endpoint se codifican en servicios aunque también existan propiedades
en `AssistantOptions`. `ExcludedProcesses` y `ConcurrencyPolicy` no se aplican.

### A-10 — Rendimiento y recursos OCR

Se captura y procesa la ventana completa; el recorte ocurre después del OCR. El motor
OCR se crea en cada solicitud, el buffer se copia a otro arreglo y `SoftwareBitmap` no
se dispone explícitamente. El token de cancelación no se revisa durante la captura GDI.

### A-11 — Excepciones demasiado genéricas

La inferencia envuelve cualquier excepción en `Exception`, perdiendo categorías útiles
para distinguir conexión, timeout, modelo ausente, HTTP, JSON o respuesta inválida.

### A-12 — Trabajo asíncrono difícil de cerrar

El hotkey lanza una tarea no esperada. El host puede cerrarse mientras hay una solicitud
en curso. `OnStartup`, `OnExit` y un manejador del menú usan `async void`, por lo que
algunas excepciones pueden escapar del control normal.

## Calidad, pruebas y mantenimiento

- La cobertura de Application es aceptable para un prototipo, pero Windows/Win32 es
  baja y Presentation no tiene pruebas.
- No hay pruebas end-to-end para hotkey → captura → OCR → inferencia → overlay/pegado.
- No hay pruebas de instancia única, cambio de ventana durante inferencia, clipboard
  ocupado, hotkey en conflicto, zoom del navegador o formularios en dos columnas.
- `IInferenceEngine`, `InferenceResult`, `OperationResult`, `ErrorCode` y parte del
  matching están desconectados del flujo principal.
- No hay README operativo, CI, instalador, versionado del ejecutable ni política de logs.
- El formato del código no está normalizado y no existe `.editorconfig`.

## Privacidad y datos

Aspectos positivos:

- Ollama usa por defecto `127.0.0.1`.
- La imagen capturada permanece en memoria y el buffer rentado se limpia al liberarse.
- No se encontró persistencia intencional de capturas o respuestas.

Riesgos:

- Se captura toda la ventana activa, que puede contener datos sensibles no relacionados.
- Prompt y respuesta pueden quedar en logs.
- El endpoint es configurable sin una política que advierta si deja de ser local.
- `ExcludedProcesses` no protege aplicaciones sensibles.
- El portapapeles queda reemplazado por la respuesta.

## Diseño futuro: modo sutil de privacidad

Nombre recomendado: `Subtle` o `Privacy`, no “invisible”. Debe reducir distracción y
visibilidad casual, sin intentar evadir supervisión, grabaciones o políticas externas.

Perfiles propuestos:

| Propiedad | Debug | Subtle |
|---|---:|---:|
| Borde | 8 px verde | 1 px gris/azul tenue |
| Relleno | rojo translúcido | transparente o 3–6 % |
| Sombra | 20 px | ninguna |
| Duración | 2500 ms | 600–900 ms |
| Animación | fade largo | aparición corta + fade rápido |
| Texto abierto | tarjeta grande | sin tarjeta tras pegar o aviso mínimo |

Requisitos:

1. Un único modelo de estilos (`OverlayStyleOptions`) compartido por todas las ventanas.
2. Cambio de perfil desde bandeja y persistencia en configuración.
3. Opacidad, grosor, color y duración con rangos seguros; nunca totalmente invisibles.
4. Respetar alto contraste, escalado DPI y reducción de animaciones de Windows.
5. Opción de no mostrar la tarjeta de texto cuando el pegado fue confirmado.
6. Mantener `Debug` como perfil predeterminado durante desarrollo.
7. Pruebas visuales a 100/125/150 %, fondos claros/oscuros y 50 cm/1–2 m.
8. Documentar que la privacidad angular real requiere un filtro físico para monitor.

Criterios de aceptación:

- El usuario frente a la pantalla identifica el indicador de forma consistente.
- A distancia normal de paso resulta menos llamativo que `Debug` bajo iluminación
  acordada, sin prometer invisibilidad.
- El perfil no cambia coordenadas, foco, captura ni lógica de inferencia.
- Errores y estados críticos siguen siendo visibles aunque el resultado sea sutil.
- El perfil no incorpora técnicas para ocultarse de capturas o supervisión.

## Orden recomendado de ejecución

1. Crear línea base Git y control de instancia única.
2. Implementar notificaciones y resultados de operación tipados.
3. Proteger el autopegado validando HWND/PID y foco.
4. Mejorar selector 2D y detección de campos.
5. Hacer persistente la configuración.
6. Implementar `Debug`/`Subtle` sobre la configuración estable.
7. Optimizar captura/OCR y completar pruebas end-to-end.
8. Decidir si completar o retirar VLM y abstracciones muertas.
