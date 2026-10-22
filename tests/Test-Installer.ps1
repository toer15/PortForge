# Installs the PortForge Setup into a temporary folder (with its shortcuts and Windows entry redirected), checks every part, and uninstalls it.
param([Parameter(Mandatory = $true)][string]$Setup)
$ErrorActionPreference = 'Stop'
$t = Join-Path ([IO.Path]::GetTempPath()) ('pf-setup-test-' + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $t | Out-Null
$dir = "$t\App Folder"; $sm = "$t\Start Menu"; $dk = "$t\Desktop"; $reg = 'Software\PortForgeSetupTest'
$checks = 0; function Check($ok, $name) { if (-not $ok) { throw "FAIL: $name" }; $script:checks++; "PASS: $name" }
try {
    $p = Start-Process $Setup -ArgumentList '/S', "/D=$dir", "/STARTMENU=$sm", "/DESKTOP=$dk", "/REGKEY=$reg", '/NOLAUNCH' -Wait -PassThru
    Check ($p.ExitCode -eq 0) 'Setup finishes without error'
    Check ((Test-Path "$dir\PortForge.exe") -and (Test-Path "$dir\Engine\scarletnx.exe") -and (Test-Path "$dir\Uninstall.exe")) 'The app, its engine and the uninstaller are installed'
    Check (-not (Test-Path "$dir\Source")) 'The source code is not installed'
    $sh = New-Object -ComObject WScript.Shell
    foreach ($f in "$sm\PortForge.lnk", "$dk\PortForge.lnk") { $l = $sh.CreateShortcut($f); Check ((Test-Path $f) -and $l.TargetPath -eq "$dir\PortForge.exe" -and $l.IconLocation -like '*PortForge.exe,0') "Shortcut $f points at the app with its logo" }
    $k = Get-ItemProperty "HKCU:\$reg"; Check ($k.DisplayName -eq 'PortForge' -and $k.InstallLocation -eq $dir -and $k.UninstallString -like '*Uninstall.exe*') 'Windows lists PortForge under Installed apps'
    Add-Type -AssemblyName System.Drawing
    Check ([Drawing.Icon]::ExtractAssociatedIcon("$dir\PortForge.exe").Width -ge 16) 'The app carries the logo as its icon'
    New-Item -ItemType Directory "$dir\NativeGames\Game" -Force | Out-Null; Set-Content "$dir\NativeGames\Game\keep.txt" 'player data'
    $u = Start-Process "$dir\Uninstall.exe" -ArgumentList '/S' -Wait -PassThru; Start-Sleep 4
    Check ($u.ExitCode -eq 0 -and -not (Test-Path "$dir\PortForge.exe") -and -not (Test-Path "$dir\Engine")) 'Uninstalling removes the program files'
    Check ((Test-Path "$dir\NativeGames\Game\keep.txt")) 'Uninstalling keeps the games the player converted'
    Check (-not (Test-Path "$sm\PortForge.lnk") -and -not (Test-Path "$dk\PortForge.lnk") -and -not (Test-Path "HKCU:\$reg")) 'Uninstalling removes the shortcuts and the Windows entry'
    "$checks installer checks passed."
} finally { Remove-Item "HKCU:\$reg" -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue }
