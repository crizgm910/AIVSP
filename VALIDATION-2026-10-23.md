# Validación de recuperación y Antigravity — 2026-10-23

Base recuperada de GitHub: `a5b263e`. Recuperación de extensión: `d7e372a`.
Rama de trabajo: `codex/recover-extension-antigravity`.
Versión de aplicación: `0.2.0-beta.2`; extensión: `0.2.9`.

## Plan aplicado y resultados

1. Reconstruir la extensión y probar reinyección, marcos, Escape, cancelación,
   texto abierto y descarte de respuestas o errores tardíos: aprobado.
2. Integrar Antigravity como proveedor opcional, sin eliminar Ollama/Gemini,
   sin cambiar el algoritmo de OCR/overlay ni el token de conexión: implementado.
3. Validar configuración nueva y anterior, salida JSON, confianza, errores,
   denegación de herramientas y cancelación: 165 pruebas .NET aprobadas
   (Domain 6, Application 61, Inference 69, Windows 29).
4. `dotnet build --configuration Release --no-restore`: 0 advertencias, 0 errores.
5. `dotnet format Aivsp.slnx --verify-no-changes --no-restore`: aprobado.
6. `scripts/validate-chrome-extension.ps1`: ambos harnesses y manifest aprobados.
7. `scripts/publish-win-x64.ps1`: publicación autocontenida y ZIP generados.

## Pruebas reales del proveedor

CLI instalado: `agy 1.4.6`; modelo confirmado por `agy models`:
`gemini-3.8-flash-low`. Cuenta ya autenticada; no se cambiaron credenciales ni
ajustes globales. Preguntas sintéticas, no datos personales.

Última ronda usando el servicio C# y runner de producción:

| Caso | Resultado | Tiempo aproximado |
| --- | --- | --- |
| 12 + 8 (abierta) | 20 | 7.85 s |
| Logotipo escalable y fotografías (abierta) | Vectorial y raster, con justificación | 9.71 s |
| UX/UI y jerarquía/proximidad (abierta) | Respuesta en español relacionada | 8.39 s |
| Clean Architecture (opción múltiple) | C: dependencias hacia dentro | 6.68 s |
| Cancelación durante ejecución | `OperationCanceledException`, proceso detenido | Plazo de 500 ms |

En una ronda adicional respondió sobre colores de una fintech en 7.97 s.
Estas muestras no demuestran exactitud general ni ausencia de alucinaciones.

Prueba negativa independiente: con el agente general, se solicitó leer un archivo
de prueba. El hook devolvió `deny`; el CLI informó que la lectura fue bloqueada
antes de ejecutarse y terminó con `BLOQUEADO`. El perfil de AIVSP limita además
las herramientas del modelo a `finish`. No se usó `--dangerously-skip-permissions`.

La versión instalada incluye metadatos extras en `structured_output`; se extraen
solamente `answer` y `confidence`. Nunca se acepta la prosa de `response` como
fallback. Las salidas fallidas, vacías, duplicadas o de confianza inválida se rechazan.

Repetir con consumo de cuota:

```powershell
dotnet run --project tools/Aivsp.Antigravity.Smoke --configuration Release -- --live
```

## Límites pendientes

- Confirmar físicamente autopegado y marcado en Chrome/Brave con Antigravity.
- Confirmar extracción y aplicación dentro de una tarea real de Teams. La cruz
  de selección confirmada anteriormente no prueba el flujo completo.
- Dos monitores y DPI mixto siguen pendientes.
- La disponibilidad, cuotas, nombres de modelos e historial de preguntas dependen
  del CLI y del proveedor. La restricción de herramientas no sustituye un sandbox.
- No se hizo un nuevo ensayo físico de Ollama/Gemini CLI; sí pasaron sus regresiones.

## Artefacto

`artifacts/publish/AIVSP-v0.2.0-beta.2-win-x64.zip`

SHA256: `2AEE94F09236960264A24536E6F1E3DE27D3D86E9BA8957C9336342151B5BF91`

Los binarios, logs, capturas, preguntas de prueba generadas y credenciales no se
incluyen en el commit. Este informe y `project_status.md` se mantienen en el código.
