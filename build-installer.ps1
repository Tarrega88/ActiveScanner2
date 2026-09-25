<#
.SYNOPSIS
  Builds a single Setup.exe installer for ActiveScanner using Inno Setup.

.DESCRIPTION
  1. Publishes a single-file, self-contained ActiveScanner.exe.
  2. Compiles installer\ActiveScanner.iss with the Inno Setup compiler (ISCC.exe).
  If Inno Setup is not installed, the script offers to install it via winget.

  The resulting Setup.exe is per-user (no admin required) and creates
  Start Menu + optional Desktop shortcuts, plus an Add/Remove Programs entry.

.EXAMPLE
  .\build-installer.ps1
#>

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$issFile     = Join-Path $projectRoot 'installer\ActiveScanner.iss'
$outputDir   = Join-Path $projectRoot 'installer\output'

# --- Read version from the .csproj so the installer filename matches the app ---
$csprojPath = Join-Path $projectRoot 'ActiveScanner.csproj'
[xml]$csproj = Get-Content $csprojPath
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = '1.1.2' }
Write-Host "App version: $version" -ForegroundColor Cyan

# --- Locate the Inno Setup compiler (ISCC.exe) ---
function Find-ISCC {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$iscc = Find-ISCC
if (-not $iscc) {
    Write-Warning 'Inno Setup compiler (ISCC.exe) was not found.'
    $answer = Read-Host 'Install Inno Setup now via winget? (Y/N)'
    if ($answer -match '^(y|yes)$') {
        winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
        $iscc = Find-ISCC
    }
    if (-not $iscc) {
        Write-Error 'Inno Setup is required. Install it from https://jrsoftware.org/isdl.php and re-run.'
        exit 1
    }
}
Write-Host "Using compiler: $iscc" -ForegroundColor DarkGray

# --- 1. Publish the single-file exe ---
Write-Host 'Publishing single-file release...' -ForegroundColor Cyan
dotnet publish $csprojPath -c Release -p:PublishProfile=SingleFile
if ($LASTEXITCODE -ne 0) { Write-Error 'Publish failed.'; exit $LASTEXITCODE }

# --- 2. Compile the installer ---
Write-Host 'Compiling installer...' -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$version" $issFile
if ($LASTEXITCODE -ne 0) { Write-Error 'Installer compilation failed.'; exit $LASTEXITCODE }

$setupExe = Join-Path $outputDir "ActiveScanner-Setup-$version.exe"
if (Test-Path $setupExe) {
    $sizeMb = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
    Write-Host ''
    Write-Host "Done! Share this installer ($sizeMb MB):" -ForegroundColor Green
    Write-Host "  $setupExe" -ForegroundColor Yellow
} else {
    Write-Warning "Compilation reported success but '$setupExe' was not found."
}
