param([switch]$Test)
# Legacy 0.1 emulator packaging build. The default build.ps1 now builds the native frontend.
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$buildRoot = Join-Path $projectRoot 'build'
$releaseRoot = Join-Path $buildRoot 'PortForge'
. (Join-Path $projectRoot 'tools\build-common.ps1')
$compiler = Get-PortForgeCSharpCompiler
New-Item -ItemType Directory -Force -Path $releaseRoot,(Join-Path $releaseRoot 'Demo\Runtime'),(Join-Path $releaseRoot 'Demo\Game') | Out-Null

# Draw an original icon using Windows drawing APIs; no external assets or downloads.
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(17,21,30))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(105,226,183)),4
$points = [System.Drawing.Point[]]@([System.Drawing.Point]::new(11,20),[System.Drawing.Point]::new(32,8),[System.Drawing.Point]::new(53,20),[System.Drawing.Point]::new(53,44),[System.Drawing.Point]::new(32,56),[System.Drawing.Point]::new(11,44))
$graphics.DrawPolygon($pen,$points)
$graphics.DrawLines($pen,[System.Drawing.Point[]]@([System.Drawing.Point]::new(26,44),[System.Drawing.Point]::new(26,23),[System.Drawing.Point]::new(40,23),[System.Drawing.Point]::new(40,33),[System.Drawing.Point]::new(26,33)))
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$iconPath = Join-Path $buildRoot 'PortForge.ico'
$iconFile = [System.IO.File]::Create($iconPath)
try { $icon.Save($iconFile) } finally { $iconFile.Dispose(); $icon.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }

$shared = @('/nologo','/codepage:65001','/optimize+','/target:winexe','/platform:anycpu',('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')),('/win32icon:' + $iconPath),'/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Core.dll','/r:System.Xml.dll')
$launcherPath = Join-Path $buildRoot 'PortForge.Launcher.exe'
& $compiler @shared (('/out:' + $launcherPath)) (Join-Path $projectRoot 'src\Core.cs') (Join-Path $projectRoot 'src\Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
& $compiler @shared (('/out:' + (Join-Path $releaseRoot 'PortForge.exe'))) (('/resource:' + $launcherPath + ',PortForge.Launcher.exe')) (('/resource:' + $iconPath + ',PortForge.ico')) (Join-Path $projectRoot 'src\Core.cs') (Join-Path $projectRoot 'src\App.cs')
if ($LASTEXITCODE -ne 0) { throw 'Desktop app compilation failed.' }
& $compiler @shared (('/out:' + (Join-Path $releaseRoot 'Demo\Runtime\OrbitDemo.exe'))) (Join-Path $projectRoot 'src\Demo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Demo compilation failed.' }
[System.IO.File]::WriteAllText((Join-Path $releaseRoot 'Demo\Game\scene.txt'),'First flight')
Copy-Item -LiteralPath (Join-Path $projectRoot 'PACKAGER-0.1.md') -Destination (Join-Path $releaseRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'ROADMAP.md') -Destination (Join-Path $releaseRoot 'ROADMAP.md')
if ($Test) {
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /r:System.Core.dll /r:System.Xml.dll (('/out:' + (Join-Path $buildRoot 'SmokeRuntime.exe'))) (Join-Path $projectRoot 'tests\SmokeRuntime.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Smoke runtime compilation failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /r:System.Core.dll /r:System.Xml.dll (('/out:' + (Join-Path $buildRoot 'PortForge.Tests.exe'))) (Join-Path $projectRoot 'src\Core.cs') (Join-Path $projectRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $buildRoot 'PortForge.Tests.exe') $launcherPath (Join-Path $buildRoot 'SmokeRuntime.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Output ('Built: ' + (Join-Path $releaseRoot 'PortForge.exe'))
