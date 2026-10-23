# AIVSP

AIVSP es un asistente local de escritorio para Windows. Captura la ventana activa,
extrae texto mediante OCR y utiliza un proveedor de inferencia para marcar una opción
o generar una respuesta de texto. El modo VLM permanece deshabilitado.

## Estado

Versión actual: `v0.2.0-beta.2` (extensión en código `0.2.10`).

El ZIP completo beta.2 anterior incluye extensión 0.2.9. Para las matrices usa la
carpeta `chrome-extension/` actual o el ZIP específico de extensión 0.2.10.

- Compilación Release: 0 errores y 0 advertencias.
- Pruebas automatizadas: 165 aprobadas, más las regresiones JavaScript.
- Proveedor predeterminado: Ollama local con `qwen3:4b`.
- Proveedores opcionales: Antigravity CLI y Gemini CLI.
- Perfiles visuales: `Visible` y `Subtle`.
- Extensión Chrome DOM opcional para evitar OCR en páginas compatibles.
- Configuración persistente en `%LocalAppData%\AIVSP\settings.json`.

Consulta [`project_status.md`](project_status.md) para riesgos y validaciones físicas,
y [`TASKS.md`](TASKS.md) para el backlog.

## Requisitos

- Windows 10 versión 2004 o posterior.
- .NET 8 Desktop Runtime para ejecutar la compilación dependiente del framework. El
  paquete autocontenido `win-x64` ya incluye el runtime.
- .NET SDK 8 o posterior para compilar.
- Ollama y el modelo configurado para el proveedor local.
- Gemini CLI instalado y autenticado para utilizar el proveedor opcional.
- Antigravity CLI instalado y autenticado para seleccionar `AntigravityCli`.

## Compilar y probar

```powershell
dotnet build Aivsp.slnx --configuration Release
dotnet test Aivsp.slnx --configuration Release
dotnet format Aivsp.slnx --verify-no-changes --no-restore
```

## Ejecutar manualmente

```powershell
.\src\Aivsp.Presentation.Wpf\bin\Release\net8.0-windows10.0.19041.0\Aivsp.Presentation.Wpf.exe
```

AIVSP se ejecuta en la bandeja del sistema. Coloca el cursor sobre la pregunta y
presiona `Ctrl+Shift+F9`.

## Paquete listo para Windows x64

Genera una distribución autocontenida con:

```powershell
.\scripts\publish-win-x64.ps1
```

El resultado se guarda en:

```text
artifacts\publish\AIVSP-v0.2.0-beta.2-win-x64\
artifacts\publish\AIVSP-v0.2.0-beta.2-win-x64.zip
```

Para usarlo en otro equipo Windows x64:

1. Descomprime el ZIP completo.
2. Instala y prepara Ollama, Antigravity CLI o Gemini CLI, según el proveedor que usarás.
3. Ejecuta `Aivsp.Presentation.Wpf.exe`.
4. Confirma el icono de AIVSP en la bandeja.

No copies únicamente el `.exe`: la publicación contiene bibliotecas requeridas. Esta
versión beta no está firmada digitalmente, por lo que Windows puede mostrar la
procedencia del archivo al descargarlo o copiarlo desde otro equipo.

## Opciones de la bandeja

- `Apps de escritorio (OCR · Ctrl+Shift+F9)`: activa la captura y OCR para programas
  de Windows. Está habilitada por defecto.
- `Chrome (DOM · Ctrl+Shift+Y)`: activa el puente local para la extensión. Es
  independiente del modo de escritorio.
- `Inferencia: Ollama local (Qwen3)`: ruta predeterminada.
- `Inferencia: Antigravity CLI (Gemini 3.8 Flash)`: proveedor de nube con perfil
  textual limitado y respuesta JSON estructurada.
- `Inferencia: Gemini CLI (3.1 Flash Lite)`: ejecuta el CLI en modo no interactivo,
  deshabilita extensiones y herramientas, usa un perfil de sistema aislado y exige
  salida JSON.
- `Visual: Visible (diagnóstico)`: borde grueso, sombra y duración normal.
- `Visual: Discreto (baja distracción)`: borde de 1 px, relleno mínimo, sin sombra y
  duración reducida. No pretende ocultar AIVSP de grabaciones o supervisión.
- `Auto-Pegado Habilitado`: activa o desactiva el pegado protegido.
- `Modo VLM`: visible como pendiente, pero deshabilitado.

Los cambios de fuentes de entrada, proveedor, perfil y autopegado se guardan
automáticamente. No se guardan credenciales de Gemini.

## Extensión Chrome opcional

La carpeta [`chrome-extension`](chrome-extension) contiene una extensión Manifest V3
que lee únicamente la pregunta elegida explícitamente. Es más rápida y precisa
que OCR para cuestionarios HTML porque conserva el texto y las opciones exactas. OCR
continúa siendo el respaldo para aplicaciones de escritorio, PDF renderizado, canvas,
imágenes o páginas incompatibles.

El extractor admite controles HTML nativos y controles accesibles usados por
formularios modernos: `role="radiogroup"`, `role="radio"`, `role="textbox"` y
campos `contenteditable`.

1. Ejecuta AIVSP y activa `Integración Chrome (DOM)` desde la bandeja.
2. Selecciona `Copiar token para extensión Chrome`.
3. Abre `chrome://extensions`, activa `Modo de desarrollador` y elige
   `Cargar descomprimida`.
4. Selecciona la carpeta `chrome-extension`.
5. Abre las opciones de la extensión, pega el token y pulsa `Probar conexión`.
6. Pulsa el icono de la extensión o `Ctrl+Shift+Y`.
7. Cuando aparezca `AIVSP listo`, haz clic izquierdo dentro de la pregunta.

El primer clic queda reservado para seleccionar la tarjeta y no se envía al formulario.
`Esc` cancela el modo de selección.

Si Chrome no activa el atajo, abre `chrome://extensions/shortcuts` y asigna
`Ctrl+Shift+Y` al comando `Armar la selección de una pregunta`.

El modo de aplicar respuestas está desactivado inicialmente. Al activarlo, la
extensión vuelve a comprobar que la pregunta no haya cambiado antes de marcar o
rellenar el campo. Para probar `test-quiz.html` como archivo local, habilita
`Permitir acceso a URLs de archivo` en los detalles de la extensión.

La extensión usa permisos mínimos (`activeTab`, `scripting`, `storage`) y sólo puede
conectarse a `http://127.0.0.1:18791`. El puente escucha únicamente en loopback,
requiere un token aleatorio y no registra preguntas, respuestas ni credenciales.
La versión `0.2.6` no autoaplica respuestas con confianza inferior a `0.75`; las
muestra para revisión sin alterar el campo. Los estados de selección, procesamiento
y finalización se reducen a indicadores circulares de 18 px. Cuando una respuesta
abierta se autoaplica correctamente, no se crea una tarjeta flotante duplicada; las
sugerencias que sí requieren revisión usan una presentación compacta.

## Gemini CLI

### Nueva alternativa: Antigravity CLI

En la bandeja elige `Inferencia: Antigravity CLI (Gemini 3.8 Flash)`. El cambio se
guarda sin alterar el token de conexión de Chrome/Brave. Gemini CLI queda como
opción anterior y Ollama sigue siendo el predeterminado de instalaciones nuevas.

Comprueba en una terminal:

```powershell
agy --version
agy models
```

Si no está autenticado, abre `agy` y completa el inicio de sesión oficial. AIVSP
no instala el CLI ni gestiona sus credenciales. Se verificó `agy 1.4.6` con
`gemini-3.8-flash-low`; los modelos dependen de la cuenta y pueden cambiar.

Configura `AntigravityCliExecutablePath` (por defecto `agy.exe`) y
`AntigravityModelName` en el archivo de ajustes si necesitas otra ruta o modelo.
También se busca `%LocalAppData%\agy\bin\agy.exe`. No se admiten envoltorios `.cmd`
para este proveedor.

Por compatibilidad, los campos `GeminiInferenceTimeoutSeconds` (75 s),
`BrowserGeminiInferenceTimeoutSeconds` (30 s) y
`GeminiAutoPasteMaximumContextAgeSeconds` (85 s) se comparten con Antigravity.
El CLI tiene además un límite de 70 s por proceso.

La pregunta se envía a la nube y el propio CLI puede conservar historial local.
El directorio de trabajo es temporal; el perfil solo ofrece `finish` y un hook
deniega las demás herramientas. Esto no es un sandbox del sistema operativo ni
modifica tus preferencias globales del CLI.

Prueba real reproducible, con preguntas sintéticas y consumo de cuota:

```powershell
dotnet run --project tools/Aivsp.Antigravity.Smoke --configuration Release -- --live
```

No verifica pegado físico ni una tarea real de Teams. Consulta el
[modo headless oficial](https://antigravity.google/docs/cli/headless/).

### Proveedor anterior

Comprueba primero el CLI:

```powershell
gemini --version
gemini --prompt "Responde exactamente OK" --output-format json --approval-mode plan
```

AIVSP fija de forma predeterminada `gemini-3.1-flash-lite`, desactiva las extensiones
del usuario y bloquea herramientas para que el CLI sólo procese el contexto enviado.
Internamente usa el alias aislado `aivsp-fast`, con temperatura y presupuesto de
razonamiento en cero para reservar la salida a la respuesta solicitada.
La instalación local `0.53.0` respondió correctamente con esa ruta; los modelos
automático y 2.5 Flash recibieron errores 503 por alta demanda durante el diagnóstico.
Para comprobar la misma ruta fuera de AIVSP:

```powershell
gemini -e none -m gemini-3.1-flash-lite --prompt "Responde exactamente OK" --output-format json --approval-mode default --skip-trust
```

Si el propio CLI no responde, AIVSP cancela la solicitud DOM al vencer el tiempo y
recomienda usar Ollama. Si otro equipo recibe un error de elegibilidad o cuota, debe
usar una vía admitida por Gemini CLI, como una licencia compatible o una API key
configurada fuera de AIVSP:

```powershell
$env:GEMINI_API_KEY="TU_CLAVE"
gemini --prompt "Responde exactamente OK" --output-format json --approval-mode plan
```

Para persistirla, usa el administrador de credenciales/entorno de Windows o la
configuración oficial de Gemini CLI. No escribas la clave en el repositorio ni en
`settings.json`. AIVSP puede heredar estas variables desde el entorno del usuario de
Windows y nunca escribe sus valores en logs. Referencias oficiales:

- [Autenticación de Gemini CLI](https://github.com/google-gemini/gemini-cli/blob/main/README.md#-authentication-options)
- [Cambio de soporte para cuentas individuales](https://github.com/google-gemini/gemini-cli/discussions/28017)

## Logs y solución de problemas

Los logs se guardan en `%LocalAppData%\AIVSP\logs`.

- Si el hotkey no funciona, cierra otras instancias y revisa la notificación.
- Después de cambiar la escala de Windows, reinicia AIVSP.
- Si Gemini falla, ejecuta la prueba del CLI anterior fuera de AIVSP.
- Si Ollama falla, confirma que `http://127.0.0.1:11434` está disponible y que el
  modelo configurado existe.
- Si la extensión no conecta, habilita la integración desde la bandeja, vuelve a
  copiar el token y confirma que no exista otra aplicación usando el puerto `18791`.

## Límites conocidos

- La validación física con dos monitores continúa aplazada.
- Una segunda activación extremadamente rápida puede no rearmar `MOD_NOREPEAT`.
- Los perfiles visuales requieren validación física final en fondos claros y oscuros.
- Gemini CLI depende de un servicio externo; disponibilidad, cuota y errores 503 no
  están bajo control de AIVSP. Ollama continúa siendo el respaldo local.
