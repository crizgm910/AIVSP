# Prueba aislada de visión con Antigravity ACP

Fecha: 20 de noviembre de 2026. No habilita visión en la aplicación publicada.

## Resultado real

- CLI instalado: `agy 1.5.1`; autenticación existente, sin solicitar otra clave.
- `initialize` negoció ACP v1 y anunció `promptCapabilities.image = true`.
- Modelo seleccionado explícitamente: `gemini-3.8-flash`; opción de sesión
  `thought_level = low`. Equivale a la familia y esfuerzo de la ruta textual
  `gemini-3.8-flash-low`; ACP expone esos campos por separado.
- PNG generado con nueve códigos aleatorios en cuadrícula. El código central
  no se incluyó en el prompt textual: coincidencia exacta en dos rondas.
- PNG de la cuadrícula de cuerpos celestes: respondió `Saturno`, correctamente.
- Última ronda: código aleatorio 3.662 s; planetas 2.662 s; prueba negativa 4.348 s.
- Prueba negativa: se creó un archivo sintético en el directorio temporal y se
  pidió leerlo. Respondió `ACCESO_BLOQUEADO` sin llamadas a herramientas. No se
  accedió a archivos personales y no se usó `--dangerously-skip-permissions`.
- Cero peticiones de permisos y cero herramientas observadas. El manejador del
  cliente rechaza peticiones de permisos, terminales y archivos; no se afirma que
  esta muestra sea una prueba exhaustiva de seguridad ni un sandbox del SO.
- Perfil, hook y workspace temporales por ejecución; limpieza verificada tras
  finalizar. No se modificaron credenciales, ajustes globales, app ni extensión.

La prueba utiliza dos turnos visuales en una sesión aislada, no preguntas de Teams.
No demuestra asociación de imágenes sueltas en DOM, selección de controles ni
exactitud general del modelo. Para producción se debe controlar explícitamente
el contexto de cada solicitud y evitar reutilizar imágenes de otra pregunta.

## Repetir

Sin red ni cuota (prueba del parser):

```powershell
node scripts/probe-antigravity-acp.cjs --self-test
```

Con imágenes sintéticas enviadas a la cuenta autenticada; puede consumir cuota:

```powershell
node scripts/probe-antigravity-acp.cjs --live
```

Requiere Windows, `agy.exe` en la instalación habitual, Chrome y el módulo
Playwright disponible para Node (puede resolverse mediante `NODE_PATH`). Es una
herramienta de diagnóstico, no un adaptador listo para producción. Tiene un
plazo global de 120 s y termina el árbol del proceso que ella misma inicia.

## Referencias de protocolo

- [Antigravity ACP e imágenes](https://antigravity.google/docs/ide/extensions/acp/)
- [Inicialización ACP](https://agentclientprotocol.com/protocol/v1/initialization)
- [Contenido de imagen base64 y MIME](https://agentclientprotocol.com/protocol/v1/content)

## Siguiente fase pendiente

Cliente C# ACP probado con cancelación y errores, imágenes acotadas en el puente,
asociación explícita/implícita del estímulo, contratos para múltiples opciones,
desplegables nativos/ARIA y aplicación verificada en el DOM original. Mantener la
ruta textual estable y VLM de escritorio deshabilitado durante esa integración.
