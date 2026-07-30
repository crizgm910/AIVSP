# Auditoría técnica de AIVSP

Fecha de corte: 29 de julio de 2026.
Versión evaluada: `v0.2.0-beta.1`.

## Resumen ejecutivo

AIVSP cumple las condiciones técnicas para producir una candidata beta: compila en
Release sin advertencias, mantiene 136 pruebas automatizadas, valida la extensión
Chrome, publica un paquete autocontenido `win-x64` y no presenta vulnerabilidades
NuGet conocidas en los orígenes consultados. No se considera listo para producción.

Los flujos principales de OCR, Ollama textual, Gemini CLI, overlay, autopegado,
instancia única, notificaciones y Chrome DOM tienen protección y regresiones. El VLM
permanece forzado a `false`. La validación física multimonitor, la firma digital y el
instalador son limitaciones declaradas de esta beta, no capacidades implícitas.

## Evidencia revisada

- `dotnet format Aivsp.slnx --verify-no-changes`.
- `dotnet build Aivsp.slnx --configuration Release`: 0 errores y 0 advertencias.
- `dotnet test Aivsp.slnx --configuration Release`: 136 pruebas.
- `scripts/validate-chrome-extension.ps1`: Manifest V3, permisos y JavaScript válidos.
- `dotnet list Aivsp.slnx package --vulnerable --include-transitive`: sin paquetes
  vulnerables conocidos en `nuget.org` al momento de la revisión.
- Publicación autocontenida mediante `scripts/publish-win-x64.ps1`.
- Pruebas físicas documentadas en `DISPLAY_TEST_PLAN.md` y `project_status.md`.
- Revisión de `.gitignore`, archivos versionados y referencias a credenciales.

## Controles aprobados

### Ciclo de vida

- Mutex con nombre impide dos instancias activas.
- El fallo de `RegisterHotKey` es visible y queda registrado.
- `MOD_NOREPEAT` evita repeticiones por mantener las teclas presionadas.
- Una nueva solicitud cancela la anterior.
- Al cerrar, la solicitud activa se cancela y el host espera su limpieza.

### Autopegado

- El contexto conserva HWND, PID y control enfocado.
- Antes de pegar se comprueban ventana, proceso, foco, antigüedad, contenido y confianza.
- El portapapeles anterior se restaura después de `Ctrl+V`.
- Los motivos de éxito o cancelación quedan en logs y las cancelaciones son visibles.

### Inferencia y datos

- Ollama y Gemini usan contratos JSON, timeout, cancelación y errores tipados.
- La ausencia del modelo Ollama tiene una acción recomendada específica.
- Gemini se ejecuta sin extensiones ni herramientas, con argumentos validados y UTF-8.
- No se persisten credenciales ni se registran preguntas o respuestas completas.
- El puente Chrome escucha sólo en loopback y exige un token aleatorio.
- Las rutas deterministas y el banco local verificado reducen errores conocidos.

### Presentación

- El overlay usa el área útil del monitor, DPI por monitor y coordenadas negativas.
- La ventana nativa se oculta al finalizar y no deja una franja sobre la barra de tareas.
- Procesando y completado no abren toasts grandes; errores, advertencias y cancelaciones sí.
- Los perfiles `Visible` y `Subtle` no alteran foco, captura ni coordenadas.

## Limitaciones beta aceptadas

1. La validación física con dos monitores y escalas distintas está aplazada por falta
   de hardware; existen pruebas automatizadas para orígenes y coordenadas negativas.
2. VLM/Moondream está oculto y deshabilitado porque su contrato de coordenadas no está
   completo. No forma parte de esta beta.
3. Un modelo pequeño sin fuentes puede equivocarse en conocimiento especializado.
   `Strict`, un banco curado más amplio y RAG siguen en el backlog.
4. Gemini depende de cuota, conectividad y disponibilidad externas; Ollama sigue como
   ruta local predeterminada.
5. La selección OCR en dos columnas, controles sin placeholder y preguntas sin
   numeración todavía depende de heurísticas.
6. Una segunda pulsación extremadamente cercana a la liberación de F9 puede no generar
   otro `WM_HOTKEY` por el comportamiento de `MOD_NOREPEAT`.
7. El paquete no está firmado y no incluye instalador.
8. Un cambio de escala de Windows en caliente requiere reiniciar AIVSP.

## Deuda no bloqueante

- Aplicar `ExcludedProcesses` antes de capturar.
- Integrar o retirar `ITextMatcher` e `IInferenceEngine`.
- Añadir detección geométrica más fuerte para campos y disposición en dos columnas.
- Definir umbrales de cobertura automatizados por capa.
- Automatizar pruebas físicas del conflicto de hotkey y multimonitor.
- Añadir firma e instalador antes de una versión estable.

## Criterio de salida

La etiqueta beta sólo se considera validada cuando:

1. formato, Release, 136 pruebas y extensión pasan desde un árbol limpio;
2. el paquete `v0.2.0-beta.1-win-x64` se genera, inicia y conserva mutex, hotkey,
   configuración y `VLM=false`;
3. no hay secretos ni artefactos generados en Git;
4. la rama publicada pasa el flujo de CI;
5. `BETA_READINESS.md` registra evidencia y limitaciones sin ocultar pendientes.
