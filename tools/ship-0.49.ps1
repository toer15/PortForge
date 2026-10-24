#Requires -Version 5.1
<#
.SYNOPSIS
  Build Engine 1.31 + PortForge 0.49 and publish the GitHub release (Windows).

.DESCRIPTION
  Run from a PortForge checkout on Windows with MSYS2 CLANG64 (engine build) and
  .NET Framework csc (app build). Typical machine layout: D:\portforge.

  Steps:
    1) Ensure engine/src exists (clone ScarletNX @ BASE, or restore from 0.47 Source)
    2) Upgrade 1.30 src → 1.31 via tools/upgrade-engine-src-1.31.py
    3) build-engine.sh + package-engine.sh in MSYS2 CLANG64
    4) build.ps1 -Test (includes FgCadenceTests)
    5) build-release.ps1 -Version 0.49 -SkipBuild
    6) gh release create 0.49 with the four artifacts

.NOTES
  Does not merge any PR. Safe to re-run; skips release create if tag 0.49 exists
  unless -ForceRelease is passed.
#>
param(
    [string]$RepoRoot = '',
    [string]$MsysBash = 'C:\msys64\clang64\bin\bash.exe',
    [switch]$SkipEngineBuild,
    [switch]$SkipTests,
    [switch]$SkipRelease,
    [switch]$ForceRelease
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
Set-Location -LiteralPath $RepoRoot

function Invoke-Native([string]$File, [string[]]$ArgList) {
    & $File @ArgList
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit $LASTEXITCODE" }
}

Write-Host "=== PortForge 0.49 / Engine 1.31 ship ==="
Write-Host "Repo: $RepoRoot"

# Sync branch if this is a git checkout (git writes progress to stderr; don't treat as failure)
if (Test-Path -LiteralPath (Join-Path $RepoRoot '.git')) {
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    git fetch origin cursor/fsr3-limit-aware-display-fps-1ecc 2>$null | Out-Null
    git checkout cursor/fsr3-limit-aware-display-fps-1ecc 2>$null | Out-Null
    git pull origin cursor/fsr3-limit-aware-display-fps-1ecc 2>$null | Out-Null
    $ErrorActionPreference = $prevEap
}

$engineSrc = Join-Path $RepoRoot 'engine\src'
$verFile = Join-Path $RepoRoot 'engine\VERSION'
if (-not (Test-Path -LiteralPath $engineSrc)) {
    throw @"
engine\src is missing. On this PC either:
  - Keep your existing ScarletNX tree under engine\src, or
  - Extract Source\engine\src from PortForge-0.47-Windows-and-Source.zip into engine\,
then re-run this script (it upgrades 1.30 → 1.31).
"@
}

# Upgrade in-tree sources to 1.31 (no-op if already upgraded)
$py = Get-Command python -ErrorAction SilentlyContinue
if (-not $py) { $py = Get-Command python3 -ErrorAction SilentlyContinue }
$pyExe = if ($py) { $py.Source } else { $null }
if (-not $pyExe) {
    foreach ($cand in @(
        'C:\msys64\clang64\bin\python.exe',
        'C:\msys64\usr\bin\python.exe',
        'C:\msys64\mingw64\bin\python.exe'
    )) {
        if (Test-Path -LiteralPath $cand) { $pyExe = $cand; break }
    }
}
if (-not $pyExe) { throw 'python/python3 required for tools/upgrade-engine-src-1.31.py (or MSYS2 python)' }
Invoke-Native $pyExe @((Join-Path $RepoRoot 'tools\upgrade-engine-src-1.31.py'), $engineSrc)
$ver = (Get-Content -LiteralPath $verFile -Raw).Trim()
if ($ver -ne '1.31') { throw "engine/VERSION is '$ver', expected 1.31" }

if (-not $SkipEngineBuild) {
    if (-not (Test-Path -LiteralPath $MsysBash)) {
        throw "MSYS2 CLANG64 bash not found at $MsysBash. Install MSYS2 or pass -MsysBash."
    }
    # Convert Windows path to MSYS (/d/portforge/...)
    $drive = $RepoRoot.Substring(0, 1).ToLowerInvariant()
    $unixRoot = '/' + $drive + ($RepoRoot.Substring(2) -replace '\\', '/')
    $buildSh = @"
set -euo pipefail
cd '$unixRoot/engine'
# Prefer existing configured tree; otherwise configure+build
./build-engine.sh
./package-engine.sh
test -f package/scarletnx.exe
grep -q 'version=1.31' package/ENGINE.txt
echo ENGINE_PACKAGE_OK
"@
    $tmpSh = Join-Path $env:TEMP 'portforge-ship-0.49-engine.sh'
    Set-Content -LiteralPath $tmpSh -Value $buildSh -Encoding ascii
    Write-Host "Building Engine in MSYS2..."
    Invoke-Native $MsysBash @('-lc', ("bash '" + ($tmpSh -replace '\\', '/') + "'"))
}

$enginePkg = Join-Path $RepoRoot 'engine\package\ENGINE.txt'
if (-not (Test-Path -LiteralPath $enginePkg)) {
    throw 'engine\package\ENGINE.txt missing — Engine build/package did not produce a package.'
}
$engineTxt = Get-Content -LiteralPath $enginePkg -Raw
if ($engineTxt -notmatch 'version=1\.31') {
    throw "engine\package\ENGINE.txt does not say version=1.31:`n$engineTxt"
}

if (-not $SkipTests) {
    Write-Host "Running build.ps1 -Test..."
    Invoke-Native 'powershell.exe' @('-NoProfile', '-File', (Join-Path $RepoRoot 'build.ps1'), '-Test')
} else {
    Write-Host "Building app (tests skipped)..."
    Invoke-Native 'powershell.exe' @('-NoProfile', '-File', (Join-Path $RepoRoot 'build-native.ps1'))
}

Write-Host "Staging release zip/setup..."
Invoke-Native 'powershell.exe' @('-NoProfile', '-File', (Join-Path $RepoRoot 'build-release.ps1'), '-Version', '0.49', '-SkipBuild')

$rel = Join-Path $RepoRoot 'build\release-0.49'
$assets = @(
    'PortForge-0.49-Windows-and-Source.zip',
    'PortForge-0.49-source.zip',
    'PortForge-Setup-0.49.exe',
    'PortForge-0.49-SHA256.txt'
) | ForEach-Object { Join-Path $rel $_ }
foreach ($a in $assets) {
    if (-not (Test-Path -LiteralPath $a)) { throw "Missing release asset: $a" }
}

if ($SkipRelease) {
    Write-Host "SkipRelease: artifacts ready in $rel"
    Get-ChildItem -LiteralPath $rel -File | Format-Table Name, Length
    exit 0
}

$existing = gh release view 0.49 --repo toer15/PortForge 2>$null
if ($LASTEXITCODE -eq 0 -and -not $ForceRelease) {
    Write-Host "Release 0.49 already exists. Pass -ForceRelease to delete/recreate, or upload manually from $rel"
    exit 0
}

$notes = @"
## Summary
- **Engine 1.31** limit-aware multi-mid FSR 3: Display FPS rises toward Limit (30 FPS + FSR 3 + Limit 120 → ~120) without speeding guest physics. Classic 2× floor kept.
- Esc: ``Engine 1.31 | Display FPS: N (4x game 30) | Limit: 120`` (no ``(~60)`` glued onto Limit).
- Play / Doctor still install ``PortForge\Engine\`` into games (``EnsureForPlay``).

## Verify
1. Install 0.49; ``Engine\ENGINE.txt`` says ``version=1.31``.
2. Play a game — Esc must show **Engine 1.31**.
3. BOTW-class 30 FPS: FSR 3 On, Limit 120 → Display FPS leaves 60 toward ~120; characters do not fall through the floor.
4. 60 FPS title + FSR 3 → ~120.

See VALIDATION-0.49.md.
"@

if ($ForceRelease -and $existing) {
    gh release delete 0.49 --repo toer15/PortForge --yes
}

Write-Host "Creating GitHub release 0.49..."
gh release create 0.49 `
    --repo toer15/PortForge `
    --title 'PortForge 0.49 / Engine 1.31' `
    --notes $notes `
    --target cursor/fsr3-limit-aware-display-fps-1ecc `
    @assets

Write-Host "SHIP_OK https://github.com/toer15/PortForge/releases/tag/0.49"
