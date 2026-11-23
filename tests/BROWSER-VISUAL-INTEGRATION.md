# Integración visual DOM — 20 de noviembre de 2026

Versión app `v0.3.0-alpha.1`, extensión `0.3.0`. No cambia OCR ni VLM de escritorio.

## Implementado

- Imágenes propias o del bloque anterior de estímulo sin campos; se conserva la
  asociación al consultar tres preguntas consecutivas. No se copia una captura completa.
- Casillas múltiples, select/multiselect HTML y combobox ARIA de opción única.
- Consentimiento apagado por defecto; máximo dos imágenes JPEG/PNG, 700000
  caracteres base64 cada una, canvas reducido a 1280 px. Rechazo si CORS impide leer.
- Puente protocolo 2 y verificación de proveedor antes de enviar contexto avanzado.
- ACP en proceso temporal con perfil sin herramientas; modelo y esfuerzo explícitos,
  sin fallback, sin terminal ni lectura/escritura de archivos del cliente.
- Rechazo de claves inválidas, JSON incorrecto, selección ambigua, respuesta tardía
  y contexto editado. Aplicación automática requiere confianza mínima 0.75.

## Pruebas

1. `dotnet test Aivsp.slnx --configuration Release`: 185 pruebas aprobadas.
2. `scripts/validate-chrome-extension.ps1`: cuatro harnesses y sintaxis aprobados;
   incluyen consentimiento, protocolo antiguo, proveedor incorrecto y claves inválidas.
3. Con `node scripts/serve-forms-lab.mjs` activo y Playwright disponible:
   `node tests/forms-lab-browser.cjs` y `node tests/browser-advanced.cjs`.
   Chrome aislado, DOM real, puente simulado; extracción normal/matriz en tres
   documentos, aplicación de controles, tres asociaciones implícitas y protección
   de campos editados. No equivale a cargar la extensión en Teams.
4. `dotnet run --project tools/Aivsp.BrowserVisual.Smoke --configuration Release -- --live outputs/acp-browser-fixture.jpg`
   (imagen sintética producida por browser-advanced). Usa cuenta autenticada y envía
   solo contenido de prueba. Cliente C# y BrowserDomAssistantService reales:
   Saturno centro 4625 ms; Mercurio debajo 4842 ms; Sol/Venus/Júpiter fila superior
   4836 ms; primos 2/3/5 5321 ms; cancelación aprobada. Tiempos no garantizados.

La prueba negativa de capacidades del prototipo ACP se documenta por separado en
`ANTIGRAVITY-ACP-PROBE.md`. El perfil/hook y rechazo de RPC son defensa en profundidad,
no un sandbox de sistema operativo. No hay acceso remoto a archivos en el protocolo.

## Comprobación manual pendiente

Cerrar app antigua, abrir paquete nuevo, recargar extensión y formulario, seleccionar
Antigravity CLI, guardar token local y habilitar Adjuntar imágenes/aplicar respuestas.
PRUEBA de matrices debe estar apagada. Activar Ctrl+Shift+Y y hacer clic en cada
pregunta. Verificar tanto respuesta como campo/selección final, sin enviar formulario.

Probar después en Teams web: origen y marcos autorizados, pregunta abierta, única,
casillas, desplegable e imagen compartida. La app de escritorio Teams no carga una
extensión de Chrome. Un iframe o imagen sin permiso se rechaza, no se elude.

Pendientes: asociación semántica ambigua (se usa proximidad DOM), imágenes CORS
protegidas, custom multiselect, rerenderizado durante clics múltiples y exactitud
general del modelo. Ordenación, emparejamiento, archivos y varios campos ambiguos
no están implementados. No se garantiza compatibilidad con cualquier formulario.
