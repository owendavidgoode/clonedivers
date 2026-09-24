# Owner-only helper: copy the private dashboard key, then open the dashboard.
# Credentials stay encrypted on disk; never print them or put them in a URL.
$ErrorActionPreference='Stop'
$saved=Import-Clixml -LiteralPath (Join-Path $env:APPDATA 'Clonedivers/owner/telemetry-secrets.clixml')
Set-Clipboard -Value ([Net.NetworkCredential]::new('',$saved.ADMIN_TOKEN).Password)
Start-Process 'https://clonedivers-telemetry.goodecraft.com/'
Write-Output 'Owner key copied. Paste it into the dashboard login field. This key is for Owen only.'
