[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts'))
$projectPath = Join-Path $repositoryRoot `
    'src\Aivsp.Presentation.Wpf\Aivsp.Presentation.Wpf.csproj'
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "No se encontró Version en $projectPath"
}
$version = $versionNode.InnerText.Trim()
$publishDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot "publish\AIVSP-v$version-win-x64"))
$extensionPath = Join-Path $repositoryRoot 'chrome-extension'
$extensionValidationScript = Join-Path $PSScriptRoot 'validate-chrome-extension.ps1'
$zipPath = "$publishDirectory.zip"

if (-not $publishDirectory.StartsWith(
        $artifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "La carpeta de publicación calculada está fuera de artifacts: $publishDirectory"
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

& $extensionValidationScript

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishProfile=WinX64SelfContained

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish terminó con código $LASTEXITCODE."
}

$executable = Join-Path $publishDirectory 'Aivsp.Presentation.Wpf.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "La publicación no generó el ejecutable esperado: $executable"
}

Copy-Item -LiteralPath $extensionPath `
    -Destination (Join-Path $publishDirectory 'chrome-extension') `
    -Recurse
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') `
    -Destination (Join-Path $publishDirectory 'README.md')

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishDirectory '*') `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
Write-Host "AIVSP publicado correctamente en:"
Write-Host $publishDirectory
Write-Host $zipPath
Write-Host "SHA256: $($hash.Hash)"
