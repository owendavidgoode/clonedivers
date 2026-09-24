# Reproducible prerequisite for the launcher build; no installer or permissions changes.
$ErrorActionPreference='Stop'
$target=Join-Path $PSScriptRoot '../dist/PresentMon.exe'
$expected='9bec3083069f58f911e6a512f4806db51a27bd096103087bc1d05ef54c80a191'
if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expected) { Write-Output 'PresentMon 2.5.1 verified.'; exit }
New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
$temporary=$target+'.download'
Invoke-WebRequest 'https://github.com/GameTechDev/PresentMon/releases/download/v2.5.1/PresentMon-2.5.1-x64.exe' -OutFile $temporary
if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'PresentMon checksum mismatch; recorder was not installed.' }
Move-Item -LiteralPath $temporary -Destination $target -Force
Write-Output 'PresentMon 2.5.1 downloaded and verified.'
