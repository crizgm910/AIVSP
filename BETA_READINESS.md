# Preparación de AIVSP v0.2.0-beta.1

Fecha de evaluación: 29 de julio de 2026.

## Puertas de salida

| Puerta | Estado | Evidencia |
|---|---|---|
| Formato | Aprobada | `dotnet format Aivsp.slnx --verify-no-changes` |
| Compilación Release | Aprobada | 0 errores y 0 advertencias |
| Pruebas automatizadas | Aprobada | 136/136 |
| Extensión Chrome | Aprobada | Validador de Manifest V3, permisos y sintaxis |
| Dependencias | Aprobada | Sin vulnerabilidades NuGet conocidas |
| Repositorio | Aprobada | `.gitignore`, sin artefactos rastreados ni credenciales incluidas |
| Publicación `win-x64` | Aprobada | ZIP beta iniciado; mutex, hotkey, configuración y VLM verificados |
| CI de la rama | Aprobada | GitHub Actions validó y publicó el artefacto del PR |

## Flujos incluidos en la beta

- OCR de aplicaciones de escritorio con `Ctrl+Shift+F9`.
- Ollama textual como proveedor local predeterminado.
- Gemini CLI opcional y aislado.
- Opción múltiple, respuesta abierta y matemáticas deterministas cubiertas.
- Autopegado protegido por HWND, PID, foco, timeout, respuesta y confianza.
- Instancia única, hotkey visible ante conflicto y cierre ordenado.
- Notificaciones tipadas sin toasts rutinarios grandes.
- Overlay `Visible` y `Subtle`.
- Extensión Chrome DOM opcional con selección explícita.

## Exclusiones y limitaciones conocidas

- VLM/Moondream no forma parte de la beta y permanece forzado a `false`.
- La prueba física con dos monitores está aplazada por falta de hardware.
- El paquete no tiene firma digital ni instalador.
- Gemini depende de un servicio externo.
- Los modelos pueden equivocarse en preguntas especializadas; AIVSP es una ayuda y el
  resultado debe revisarse.
- Dos columnas, campos sin indicios OCR y pulsaciones de hotkey extremadamente rápidas
  siguen en el backlog.

## Criterio de decisión

La candidata puede llamarse beta cuando todas las puertas de salida estén aprobadas.
Las exclusiones anteriores deben continuar documentadas y no se presentarán como
funciones disponibles.

## Evidencia del paquete local

- Ejecutable: `AIVSP-v0.2.0-beta.1-win-x64\Aivsp.Presentation.Wpf.exe`.
- Versión de producto: `0.2.0-beta.1`.
- Versión de archivo: `0.2.0.1`.
- ZIP autocontenido: aproximadamente 90 MB.
- La huella SHA-256 definitiva se calcula sobre el paquete generado desde el commit
  publicado, para que el ejecutable incluya la revisión correcta.
- Inicio verificado: mutex adquirido, bandeja creada, hotkey registrado, puente loopback
  activo, configuración cargada y `VLM=false`.
- Smoke test real del binario: Gemini CLI resolvió `B) París` con confianza `1.0` en
  4.076 s mediante `/v1/assist`.
- GitHub Actions: las ejecuciones de push y PR del commit `54dd26b` aprobaron formato,
  Release, pruebas no interactivas, extensión, publicación y carga del artefacto.
