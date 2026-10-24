param(
    [Parameter(Mandatory = $true)][string]$Version,  # e.g. 0.40
    [string]$NativeFolder = '',
    [switch]$SkipBuild
)
# Stages PortForge-<version>-Windows-and-Source.zip, PortForge-<version>-source.zip,
# PortForge-Setup-<version>.exe and SHA256 sums under build\release-<version>.
# Source never includes .git, .cache, build, or other VCS/build junk (critical for in-app updates).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $NativeFolder) { $NativeFolder = Join-Path $root 'build\PortForgeNative' }
$ver = $Version.TrimStart('v', 'V')
$out = Join-Path $root ("build\release-" + $ver)
$app = Join-Path $out 'PortForge'

if (-not $SkipBuild) {
    & (Join-Path $root 'build-native.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'build-native.ps1 failed.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $NativeFolder 'PortForge.exe'))) {
    throw "PortForge.exe not found in $NativeFolder. Build first or pass -NativeFolder."
}

if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item -Path (Join-Path $NativeFolder '*') -Destination $app -Recurse -Force

# Directory names never copied into Source/ (any depth). Robocopy /XD matches folder names only.
$excludeDirNames = @(
    '.git', '.svn', '.hg', '.cache', '.claude',
    'build', 'obj', 'bin', 'node_modules',
    'package', 'package-candidate', 'package-*'
)

function Copy-SourceTree([string]$dest) {
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    $top = @(
        'assets', 'engine', 'installer', 'src', 'tests', 'tools',
        '.gitattributes', '.gitignore', 'app.config', 'app.manifest',
        'BENCHMARKS.md', 'build-installer.ps1', 'build-native.ps1', 'build-packager.ps1',
        'build-release.ps1', 'build.ps1', 'CLOUD-SAVES.md', 'NATIVE.md', 'PACKAGER-0.1.md',
        'README.md', 'ROADMAP.md', 'USERGUIDE.md'
    )
    $top += Get-ChildItem -LiteralPath $root -Filter 'VALIDATION-*.md' -File | ForEach-Object { $_.Name }
    foreach ($name in ($top | Select-Object -Unique)) {
        $src = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $src)) { continue }
        $item = Get-Item -LiteralPath $src -Force
        if ($item.PSIsContainer) {
            $target = Join-Path $dest $name
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            # /E copy subdirs including empty; /NFL /NDL /NJH /NJS quiet; /XD skips named folders at any depth
            $args = @($src, $target, '/E', '/XD') + $excludeDirNames + @('/NFL', '/NDL', '/NJH', '/NJS', '/NC', '/NS')
            & robocopy @args | Out-Null
            $code = $LASTEXITCODE
            if ($code -ge 8) { throw "robocopy failed copying $name (exit $code)." }
            # Robocopy exit 0-7 are success; clear so callers see 0
            $global:LASTEXITCODE = 0
        } else {
            Copy-Item -LiteralPath $src -Destination (Join-Path $dest $name) -Force
        }
    }
    # Drop any .git that slipped through (junctions / alternate streams / rename)
    Get-ChildItem -LiteralPath $dest -Directory -Force -Recurse -Filter '.git' -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    Get-ChildItem -LiteralPath $dest -Force -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @('.gitmodules', '.git') -and -not $_.PSIsContainer } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
}

$srcStage = Join-Path $app 'Source'
Copy-SourceTree $srcStage

# Guard: packaged Source must not contain .git (breaks older in-app extractors on empty objects\info\)
$gitHits = @(Get-ChildItem -LiteralPath $srcStage -Force -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq '.git' -or $_.FullName -match '[\\/]\.git([\\/]|$)' })
if ($gitHits.Count -gt 0) {
    throw ("Source still contains .git after packaging: " + ($gitHits | Select-Object -First 5 | ForEach-Object FullName) -join '; ')
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$winZip = Join-Path $out ("PortForge-$ver-Windows-and-Source.zip")
if (Test-Path -LiteralPath $winZip) { Remove-Item -LiteralPath $winZip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($app, $winZip, [IO.Compression.CompressionLevel]::Optimal, $false)

# Second guard on the zip itself (catches CreateFromDirectory oddities)
$zipCheck = [IO.Compression.ZipFile]::OpenRead($winZip)
try {
    $bad = @($zipCheck.Entries | Where-Object { $_.FullName -match '(^|[\\/])\.git([\\/]|$)' })
    if ($bad.Count -gt 0) {
        throw ("ZIP contains .git entries: " + ($bad | Select-Object -First 5 | ForEach-Object FullName) -join '; ')
    }
} finally { $zipCheck.Dispose() }

$srcOnly = Join-Path $out 'source-only'
Copy-SourceTree $srcOnly
$srcZip = Join-Path $out ("PortForge-$ver-source.zip")
if (Test-Path -LiteralPath $srcZip) { Remove-Item -LiteralPath $srcZip -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($srcOnly, $srcZip, [IO.Compression.CompressionLevel]::Optimal, $false)

# Setup embeds app files only (build-installer already excludes Source/)
$setup = Join-Path $out ("PortForge-Setup-$ver.exe")
& (Join-Path $root 'build-installer.ps1') -ReleaseFolder $app -Output $setup
if ($LASTEXITCODE -ne 0) { throw 'build-installer.ps1 failed.' }

$hashes = @()
foreach ($f in @("PortForge-$ver-Windows-and-Source.zip", "PortForge-$ver-source.zip", "PortForge-Setup-$ver.exe")) {
    $path = Join-Path $out $f
    $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
    $hashes += "$h  $f"
}
$hashes -join "`n" | Set-Content -Encoding ascii (Join-Path $out ("PortForge-$ver-SHA256.txt"))
Get-ChildItem -LiteralPath $out -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
Get-Content (Join-Path $out ("PortForge-$ver-SHA256.txt"))
Write-Output "Release folder: $out"
