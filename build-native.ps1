param([switch]$Test, [string]$BackendPath)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$nativeRoot = Join-Path $projectRoot 'build\PortForgeNative'
New-Item -ItemType Directory -Force -Path $nativeRoot,(Join-Path $nativeRoot 'NativeTest') | Out-Null
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
$conversionSources = @('AssemblyInfo.cs','Core.cs','ConversionFeedback.cs','ConversionFiles.cs','GameOptimization.cs','GlobalSettings.cs','FrameTrace.cs','NativeConversion.cs','GameDownloads.cs','GameDiagnostics.cs','CodexDiagnosis.cs','AppUpdates.cs','GameStateGuard.cs','HardwareProfile.cs','PcOptimization.cs','CodePatches.cs','AutoFix.cs','LiveDoctor.cs','EngineManager.cs','EngineLab.cs','CloudSaves.cs','CloudSaveStores.cs','Controllers.cs') | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }
$downloadReferences = @('/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll','/r:System.Web.Extensions.dll','/r:System.Security.dll')
& $compiler /nologo /codepage:65001 /optimize+ /target:winexe /platform:x64 /r:System.Core.dll /r:System.Xml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/win32icon:' + (Join-Path $projectRoot 'assets\portforge.ico'))) (('/out:' + (Join-Path $nativeRoot 'PortForge.exe'))) @conversionSources (Join-Path $projectRoot 'src\AppIcon.cs') (Join-Path $projectRoot 'src\NativeApp.cs') (Join-Path $projectRoot 'src\GameSettingsWindow.cs') (Join-Path $projectRoot 'src\PreparingScreen.cs') (Join-Path $projectRoot 'src\DownloadWindow.cs') (Join-Path $projectRoot 'src\UpdateWindow.cs') (Join-Path $projectRoot 'src\DoctorWindow.cs') (Join-Path $projectRoot 'src\EnginePanel.cs') (Join-Path $projectRoot 'src\CloudSaveWindow.cs') (Join-Path $projectRoot 'src\ControllerWindow.cs') (Join-Path $projectRoot 'src\GlobalSettingsPanel.cs') (Join-Path $projectRoot 'src\FrameTraceWindow.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native frontend compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $nativeRoot 'PortForge.exe.config')
Copy-Item -LiteralPath (Join-Path $projectRoot 'USERGUIDE.md') -Destination (Join-Path $nativeRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'NATIVE.md') -Destination (Join-Path $nativeRoot 'DEVELOPING.md')
$enginePackage = Join-Path $projectRoot 'engine\package'
if (Test-Path -LiteralPath (Join-Path $enginePackage 'scarletnx.exe')) {
    $engineTarget = Join-Path $nativeRoot 'Engine'
    if (Test-Path -LiteralPath $engineTarget) { Remove-Item -LiteralPath $engineTarget -Recurse -Force }
    Copy-Item -LiteralPath $enginePackage -Destination $engineTarget -Recurse
}
$localBackend = if ($BackendPath) { [IO.Path]::GetFullPath($BackendPath) } else { Join-Path $projectRoot 'research\ScarletNX-Windows\bin\scarletnx.exe' }
if (Test-Path -LiteralPath $localBackend) { [IO.File]::WriteAllText((Join-Path $nativeRoot 'backend-location.txt'),$localBackend) }
$testNro = Join-Path $nativeRoot 'NativeTest\original-test.nro'
& (Join-Path $projectRoot 'tests\Create-NativeFixture.ps1') -OutputFile $testNro
if ($Test) {
    $errorBackendRoot = Join-Path $projectRoot 'build\key-error-backend'
    New-Item -ItemType Directory -Force -Path $errorBackendRoot | Out-Null
    & $compiler /nologo /optimize+ /target:exe /platform:x64 (('/out:' + (Join-Path $errorBackendRoot 'scarletnx.exe'))) (Join-Path $projectRoot 'tests\KeyErrorBackend.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Error scenario backend compilation failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll /r:System.Xml.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/out:' + (Join-Path $projectRoot 'build\NativeTests.exe'))) @conversionSources (Join-Path $projectRoot 'tests\NativeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Native tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $projectRoot 'build\NativeTests.exe.config')
    & (Join-Path $projectRoot 'build\NativeTests.exe') $localBackend $testNro
    if ($LASTEXITCODE -ne 0) { throw 'Native tests failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /main:NativeInterfaceTests /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/r:' + (Join-Path $nativeRoot 'PortForge.exe'))) (('/out:' + (Join-Path $nativeRoot 'InterfaceTests.exe'))) (Join-Path $projectRoot 'src\AssemblyInfo.cs') (Join-Path $projectRoot 'tests\NativeInterfaceTests.cs') (Join-Path $projectRoot 'tests\DownloadTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Interface tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $nativeRoot 'InterfaceTests.exe.config')
    & (Join-Path $nativeRoot 'InterfaceTests.exe') (Join-Path $errorBackendRoot 'scarletnx.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Interface tests failed.' }
    & $compiler /nologo /optimize+ /target:exe /platform:x64 (('/out:' + (Join-Path $projectRoot 'build\TuningFixture.exe'))) (Join-Path $projectRoot 'tests\TuningFixture.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Tuning fixture compilation failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll /r:System.Xml.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll @downloadReferences (('/r:' + (Join-Path $nativeRoot 'PortForge.exe'))) (('/out:' + (Join-Path $nativeRoot 'CloudSaveTests.exe'))) (Join-Path $projectRoot 'tests\CloudSaveTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Cloud save tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $nativeRoot 'CloudSaveTests.exe.config')
    & (Join-Path $nativeRoot 'CloudSaveTests.exe') (Join-Path $projectRoot 'build\TuningFixture.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Cloud save tests failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll /r:System.Xml.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/out:' + (Join-Path $projectRoot 'build\OptimizationTests.exe'))) @conversionSources (Join-Path $projectRoot 'tests\OptimizationTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Optimization tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $projectRoot 'build\OptimizationTests.exe.config')
    & (Join-Path $projectRoot 'build\OptimizationTests.exe') (Join-Path $projectRoot 'build\TuningFixture.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Optimization tests failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll /r:System.Xml.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/out:' + (Join-Path $projectRoot 'build\DownloadTests.exe'))) @conversionSources (Join-Path $projectRoot 'tests\DownloadTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Download tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $projectRoot 'build\DownloadTests.exe.config')
    & (Join-Path $projectRoot 'build\DownloadTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Download tests failed.' }
    & $compiler /nologo /optimize+ /target:exe /platform:x64 (('/out:' + (Join-Path $projectRoot 'build\DiagnosisFixture.exe'))) (Join-Path $projectRoot 'tests\DiagnosisFixture.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Diagnosis fixture compilation failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll /r:System.Xml.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/out:' + (Join-Path $projectRoot 'build\DiagnosticTests.exe'))) @conversionSources (Join-Path $projectRoot 'tests\DiagnosticTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $projectRoot 'build\DiagnosticTests.exe.config')
    & (Join-Path $projectRoot 'build\DiagnosticTests.exe') (Join-Path $projectRoot 'build\TuningFixture.exe') (Join-Path $projectRoot 'build\DiagnosisFixture.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic tests failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll /r:System.Xml.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/out:' + (Join-Path $projectRoot 'build\UpdateTests.exe'))) @conversionSources (Join-Path $projectRoot 'tests\UpdateTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Update tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $projectRoot 'build\UpdateTests.exe.config')
    & (Join-Path $projectRoot 'build\UpdateTests.exe') (Join-Path $nativeRoot 'PortForge.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Update tests failed.' }
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll /r:System.Xml.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/out:' + (Join-Path $projectRoot 'build\DoctorTests.exe'))) @conversionSources (Join-Path $projectRoot 'tests\DoctorTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Doctor tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $projectRoot 'build\DoctorTests.exe.config')
    & (Join-Path $projectRoot 'build\DoctorTests.exe') (Join-Path $projectRoot 'build\TuningFixture.exe') (Join-Path $projectRoot 'build\DiagnosisFixture.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Doctor tests failed.' }

    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll /r:System.Xml.dll @downloadReferences (('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) (('/out:' + (Join-Path $projectRoot 'build\EngineTests.exe'))) @conversionSources (Join-Path $projectRoot 'tests\EngineTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Engine tests compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $projectRoot 'build\EngineTests.exe.config')
    & (Join-Path $projectRoot 'build\EngineTests.exe') (Join-Path $projectRoot 'build\TuningFixture.exe') (Join-Path $errorBackendRoot 'scarletnx.exe') (Join-Path $projectRoot 'build\DiagnosisFixture.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Engine tests failed.' }

    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /r:System.Core.dll (('/out:' + (Join-Path $projectRoot 'build\FgCadenceTests.exe'))) (Join-Path $projectRoot 'tests\FgCadenceTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'FG cadence tests compilation failed.' }
    & (Join-Path $projectRoot 'build\FgCadenceTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'FG cadence tests failed.' }

}
Write-Output ('Built native frontend: ' + (Join-Path $nativeRoot 'PortForge.exe'))

