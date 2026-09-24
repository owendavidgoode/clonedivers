# Owner-only helper: prepare the one-time squad setup information for private sharing.
$ErrorActionPreference='Stop'
$saved=Import-Clixml -LiteralPath (Join-Path $env:APPDATA 'Clonedivers/owner/telemetry-secrets.clixml')
$invite=[Net.NetworkCredential]::new('',$saved.ENROLLMENT_TOKEN).Password
$text="In Clonedivers, open Help & diagnostics > Performance sharing. Enter your squad nickname, enable Share performance with Owen, and use:`nCollector: https://clonedivers-telemetry.goodecraft.com/`nInvitation: $invite`nUse Set up FPS permission once, then sign out of Windows and back in. Keep the launcher open/minimized while playing."
Set-Clipboard -Value $text
Write-Output 'Squad setup instructions copied. Share privately; the invitation enrolls devices but cannot read reports.'
