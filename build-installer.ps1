param(
    [Parameter(Mandatory = $true)][string]$ReleaseFolder,   # the folder holding PortForge.exe, Engine\, NativeTest\ ...
    [Parameter(Mandatory = $true)][string]$Output           # where PortForge-Setup.exe goes
)
# Builds the PortForge installer: a single Setup.exe that carries the app and its Engine, with the logo as its icon.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
$work = Join-Path ([IO.Path]::GetTempPath()) ('portforge-installer-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # the payload: the program files, without the source
    $staging = Join-Path $work 'payload'
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Get-ChildItem -LiteralPath $ReleaseFolder -Force | Where-Object { $_.Name -ne 'Source' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $staging -Recurse -Force }
    if (-not (Test-Path -LiteralPath (Join-Path $staging 'PortForge.exe'))) { throw 'PortForge.exe is not in the release folder.' }
    $zip = Join-Path $work 'payload.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $icon = Join-Path $root 'assets\portforge.ico'
    $logo = Join-Path $root 'assets\logo.png'
    $uninstall = Join-Path $work 'Uninstall.exe'
    & $compiler /nologo /optimize+ /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/win32icon:$icon" "/out:$uninstall" (Join-Path $root 'installer\Uninstall.cs')
    if ($LASTEXITCODE -ne 0) { throw 'The uninstaller did not compile.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Output) | Out-Null
    & $compiler /nologo /optimize+ /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll "/win32icon:$icon" "/win32manifest:$(Join-Path $root 'installer\setup.manifest')" "/resource:$zip,payload.zip" "/resource:$uninstall,uninstall.exe" "/resource:$logo,logo.png" "/out:$Output" (Join-Path $root 'installer\Setup.cs')
    if ($LASTEXITCODE -ne 0) { throw 'The installer did not compile.' }
    '{0:N1} MB  {1}' -f ((Get-Item -LiteralPath $Output).Length / 1MB), $Output
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
