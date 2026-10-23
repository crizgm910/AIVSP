[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$extension = Join-Path $root 'chrome-extension'
$manifestPath = Join-Path $extension 'manifest.json'
$requiredFiles = @(
    'manifest.json',
    'service-worker.js',
    'content-script.js',
    'options.html',
    'options.js',
    'options.css'
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $extension $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Falta el archivo requerido de la extensión: $file"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.manifest_version -ne 3) {
    throw 'La extensión debe usar Manifest V3.'
}

$expectedPermissions = @('activeTab', 'scripting', 'storage', 'webNavigation')
$actualPermissions = @($manifest.permissions | Sort-Object)
$sortedExpectedPermissions = @($expectedPermissions | Sort-Object)
if (Compare-Object $actualPermissions $sortedExpectedPermissions) {
    throw "Permisos inesperados: $($actualPermissions -join ', ')"
}

$hostPermissions = @($manifest.host_permissions)
if ($hostPermissions.Count -ne 1 -or
    $hostPermissions[0] -ne 'http://127.0.0.1:18791/*') {
    throw "El único host permitido debe ser el puente local de AIVSP."
}

$selectionCommand = $manifest.commands.'select-question'
if (-not $selectionCommand -or
    $selectionCommand.suggested_key.default -ne 'Ctrl+Shift+Y') {
    throw 'El comando select-question debe usar Ctrl+Shift+Y.'
}
if ($manifest.commands.'_execute_action') {
    throw 'El atajo ambiguo _execute_action no debe estar presente.'
}
if ($manifest.version -ne '0.2.10') {
    throw 'La versión esperada de la extensión es 0.2.10.'
}
$expectedOptionalHosts = @('https://forms.cloud.microsoft/*', 'https://forms.office.com/*',
    'https://teams.microsoft.com/*', 'https://teams.cloud.microsoft/*')
if (Compare-Object @($manifest.optional_host_permissions | Sort-Object) @($expectedOptionalHosts | Sort-Object)) {
    throw 'Los permisos opcionales deben limitarse a Forms y Teams.'
}

$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) {
    throw 'Node.js es necesario para validar la sintaxis de la extensión.'
}

Get-ChildItem -LiteralPath $extension -Filter '*.js' | ForEach-Object {
    & $node.Source --check $_.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "JavaScript inválido: $($_.Name)"
    }
}

$serviceWorkerHarness = Join-Path $root 'tests\chrome-extension-service-worker-harness.mjs'
& $node.Source $serviceWorkerHarness
if ($LASTEXITCODE -ne 0) {
    throw 'Falló el banco de pruebas del service worker de Chrome.'
}

$source = Get-ChildItem -LiteralPath $extension -File |
    Get-Content -Raw
& $node.Source (Join-Path $root 'tests\chrome-extension-frame-selection-harness.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las regresiones de selección.' }
& $node.Source (Join-Path $root 'tests\chrome-extension-matrix-harness.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las regresiones de matrices.' }
$forbiddenPatterns = @(
    '<all_urls>',
    'externally_connectable',
    'eval(',
    'new Function',
    'http://localhost'
)
foreach ($pattern in $forbiddenPatterns) {
    if ($source -match [regex]::Escape($pattern)) {
        throw "Se encontró un patrón no permitido en la extensión: $pattern"
    }
}

$contentScript = Get-Content -LiteralPath (Join-Path $extension 'content-script.js') -Raw
$requiredSubtlePatterns = @(
    'right: 8px !important',
    'width: 18px !important',
    'height: 18px !important',
    'font: 600 10px/16px system-ui, sans-serif !important',
    'border-radius: 50% !important',
    'box-shadow: none !important',
    'pointer-events: none !important',
    'element.setAttribute("aria-live"',
    'element.setAttribute("aria-label", message)',
    'element.textContent = statusGlyph(kind)',
    '"AIVSP · listo"',
    'result.confidence >= 0.75',
    '"AIVSP · revisa"',
    'if (!mayApply)',
    'AIVSP_CONTENT_SCRIPT_VERSION = "0.2.10"',
    '__aivspDomAssistantController'
)
foreach ($pattern in $requiredSubtlePatterns) {
    if (-not $contentScript.Contains($pattern, [System.StringComparison]::Ordinal)) {
        throw "Falta la regla discreta requerida en el aviso DOM: $pattern"
    }
}

Write-Host 'Extensión Chrome validada: Manifest V3, permisos mínimos y JavaScript válido.'
