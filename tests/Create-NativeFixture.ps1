param([Parameter(Mandatory=$true)][string]$OutputFile)
$ErrorActionPreference = 'Stop'
# An original synthetic AArch64 program in a minimal NRO image. It prints a marker
# through Horizon OutputDebugString and exits. No game code, assets or keys.
[byte[]]$fixtureImage = New-Object byte[] 512
function Write-Instruction([int]$offset,[uint32]$value) { [BitConverter]::GetBytes($value).CopyTo($fixtureImage,$offset) }
Write-Instruction 0 0x14000020 # B +0x80
[Text.Encoding]::ASCII.GetBytes('NRO0').CopyTo($fixtureImage,0x10)
foreach ($segment in @(@(0x18,0x200),@(0x20,0),@(0x24,0x100),@(0x28,0x100),@(0x2c,0x80),@(0x30,0x180),@(0x34,0x80))) { Write-Instruction $segment[0] $segment[1] }
$message = [Text.Encoding]::ASCII.GetBytes("PortForge native AArch64 test`n")
$message.CopyTo($fixtureImage,0x100)
Write-Instruction 0x80 0x10000400 # ADR X0, +0x80
Write-Instruction 0x84 ([uint32]::Parse('D2800001',[Globalization.NumberStyles]::HexNumber) -bor ($message.Length -shl 5)) # MOV X1, length
Write-Instruction 0x88 ([uint32]::Parse('D40004E1',[Globalization.NumberStyles]::HexNumber)) # SVC OutputDebugString
Write-Instruction 0x8c ([uint32]::Parse('D40000E1',[Globalization.NumberStyles]::HexNumber)) # SVC ExitProcess
Write-Instruction 0x90 ([uint32]::Parse('D4200000',[Globalization.NumberStyles]::HexNumber)) # Unreachable BRK
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($OutputFile),$fixtureImage)
