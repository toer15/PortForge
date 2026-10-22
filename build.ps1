param([switch]$Test, [string]$BackendPath)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-native.ps1') -Test:$Test -BackendPath $BackendPath
