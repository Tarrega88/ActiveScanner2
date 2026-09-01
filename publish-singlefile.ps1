<#
.SYNOPSIS
  Builds a single, self-contained ActiveScanner.exe to share with coworkers.

.DESCRIPTION
  Produces one executable that bundles the .NET runtime and all dependencies.
  Coworkers do NOT need .NET installed - they just run the .exe.

.EXAMPLE
  .\publish-singlefile.ps1
#>

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$outputDir   = Join-Path $projectRoot 'publish\single-file'

Write-Host 'Building single-file release...' -ForegroundColor Cyan

dotnet publish (Join-Path $projectRoot 'ActiveScanner.csproj') `
    -c Release `
    -p:PublishProfile=SingleFile

if ($LASTEXITCODE -ne 0) {
    Write-Error 'Publish failed.'
    exit $LASTEXITCODE
}

$exePath = Join-Path $outputDir 'ActiveScanner.exe'
if (Test-Path $exePath) {
    $sizeMb = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
    Write-Host ''
    Write-Host "Done! Share this single file ($sizeMb MB):" -ForegroundColor Green
    Write-Host "  $exePath" -ForegroundColor Yellow
} else {
    Write-Warning "Build reported success but '$exePath' was not found."
}
