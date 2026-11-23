[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $root
try {
    & (Join-Path $PSScriptRoot 'validate-chrome-extension.ps1')
    # Requires Playwright and the local fixture server (serve-forms-lab.mjs).
    # Does not attach to the user's browser or submit any form.
    foreach ($test in @(
        'browser-teams-safety.cjs',
        'browser-form-choices.cjs',
        'browser-images.cjs',
        'browser-dropdowns.cjs',
        'browser-advanced.cjs',
        'forms-lab-browser.cjs'
    )) {
        & node (Join-Path $root "tests/$test")
        if ($LASTEXITCODE -ne 0) { throw "Falló la regresión $test (código $LASTEXITCODE)." }
    }
    Write-Host 'Todas las regresiones de navegador aprobadas. Teams real sigue pendiente.'
}
finally { Pop-Location }
