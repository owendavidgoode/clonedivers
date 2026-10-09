# Read-only final process/shortcut/preferences/camera evidence after actual-binary peer.
param(
    [string]$BinaryPeerDirectory = 'dist/production-r23-2026-10-08/local-promotion-v1/peer-installed-v1',
    [string]$OutputDirectory = 'dist/production-r23-2026-10-08/local-promotion-v1/peer-metadata-v1'
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
function Need([bool]$Ok,[string]$Why) { if (!$Ok) { throw $Why } }
function Pin([string]$Path) { @{path=[IO.Path]::GetFullPath($Path);bytes=(Get-Item -LiteralPath $Path).Length;sha256=(Get-ContentHash $Path)} }
function Closed {
    $found=@()
    foreach ($name in @('helldivers2','Clonedivers')) {
        $processes=[Diagnostics.Process]::GetProcessesByName($name)
        try { $found+=@($processes | ForEach-Object { @{name=$_.ProcessName;id=$_.Id} }) }
        finally { foreach ($process in $processes) { $process.Dispose() } }
    }
    Need ($found.Count -eq 0) 'Game and launcher must remain closed for final read-only checks.'
    @{helldivers2=0;Clonedivers=0}
}
$output=[IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$scope=[IO.Path]::GetFullPath((Join-Path $root 'dist/production-r23-2026-10-08/local-promotion-v1'))+[IO.Path]::DirectorySeparatorChar
Need ($output.StartsWith($scope,[StringComparison]::OrdinalIgnoreCase) -and !(Test-Path -LiteralPath $output)) 'Fresh scoped metadata output required.'
$closedBefore=Closed
$peerPath=Join-Path (Join-Path $root $BinaryPeerDirectory) 'report.json'
$peer=Get-Content -LiteralPath $peerPath -Raw | ConvertFrom-Json
Need ($peer.passed -and $peer.actualBinaryEmpireReceiptSelectionExact -and $peer.freshEmpireInventory -and $peer.allThreeModesZeroDownloads -and $peer.watchedInputsUnchanged) 'Actual desktop assembly peer has not passed.'
$settingsPath='C:/Users/goode/AppData/Roaming/Clonedivers/config.json'
$cameraPath='C:/Users/goode/AppData/Local/CowboyBingus/Helldivers2/Logs/ModOptionsMenu.values'
$shortcutPath='C:/Users/goode/OneDrive/Desktop/Clonedivers.lnk'
$appPath=Join-Path $root 'dist/local-current/Clonedivers.exe'
$snapshotPath=Join-Path $root 'dist/production-r23-2026-10-08/local-promotion-v1/installed-v1/settings-before.json'
$planPath=Join-Path $root 'dist/production-r23-2026-10-08/local-promotion-v1/installed-v1/plan.json'
$installedPath=Join-Path $root 'dist/production-r23-2026-10-08/local-promotion-v1/installed-v1/verified-install.json'
$watched=@($settingsPath,$cameraPath,$shortcutPath,$appPath)
$before=@{};foreach($path in $watched){$before[$path]=Pin $path}
$settings=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$original=Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json
$plan=Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
$installed=Get-Content -LiteralPath $installedPath -Raw | ConvertFrom-Json
Need ($installed.passed -and $installed.Version -eq '2026.10.08-r23') 'Root installed transaction not complete.'
Need ($settings.InstalledPackVersion -eq '2026.10.08-r23' -and $null -eq $settings.ManifestUrl -and $settings.InstalledGameBuild -eq '25480438') 'Installed version/build/default public feed differs.'
foreach($key in @('GamePath','TextureProfile','Telemetry')) {
    Need (($settings.$key | ConvertTo-Json -Depth 8 -Compress) -ceq ($original.$key | ConvertTo-Json -Depth 8 -Compress)) "Preference changed: $key"
}
$optionsA=@($settings.Options.PSObject.Properties | Sort-Object Name | ForEach-Object { $_.Name+'='+[string]$_.Value }) -join ';'
$optionsB=@($original.Options.PSObject.Properties | Sort-Object Name | ForEach-Object { $_.Name+'='+[string]$_.Value }) -join ';'
Need ($optionsA -ceq $optionsB) 'Saved options changed.'
$camera=Get-Content -LiteralPath $cameraPath
Need (@($camera | Where-Object { $_ -clike "firstperson.enabled`t*" }).Count -eq 1 -and $camera -ccontains "firstperson.enabled`tfalse") 'Camera startup must stay off.'
Need (@($camera | Where-Object { $_ -clike "firstperson.bridge2`t*" }).Count -eq 1 -and $camera -ccontains "firstperson.bridge2`t1") 'Camera bridge2 preference differs.'
Need ((Get-ContentHash $cameraPath) -ceq $plan.cameraSha256) 'Complete camera preference file changed during deployment.'
Need ((Get-ContentHash $appPath) -ceq '2effe777eb2673d7237b5fc9aed1cbe85434ba86f3c31fbae9f73129a06cd51e') 'Actual desktop launcher bytes differ.'
$shell=New-Object -ComObject WScript.Shell
$link=$shell.CreateShortcut([IO.Path]::GetFullPath($shortcutPath))
try {
    $shortcut=@{target=$link.TargetPath;arguments=$link.Arguments;workingDirectory=$link.WorkingDirectory;icon=$link.IconLocation}
    Need ([IO.Path]::GetFullPath($shortcut.target) -ieq $appPath) 'Desktop shortcut does not target the verified launcher.'
    Need ($shortcut.arguments -ceq '' -and [IO.Path]::GetFullPath($shortcut.workingDirectory) -ieq (Split-Path $appPath)) 'Desktop shortcut arguments or working directory differs.'
} finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)|Out-Null;[Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)|Out-Null }
$closedAfter=Closed
foreach($path in $watched){Need ((Get-ContentHash $path) -ceq $before[$path].sha256) "Read-only input changed: $path"}
$report=[ordered]@{
    passed=$true;utc=[DateTimeOffset]::UtcNow.ToString('o');tool=(Pin $PSCommandPath);actualDesktopBinaryPeer=(Pin $peerPath)
    rootTransaction=(Pin $installedPath);closedBefore=$closedBefore;closedAfter=$closedAfter;shortcut=$shortcut;watchedInputs=$before
    desktopLauncherVersion='1.7.3';packVersion='2026.10.08-r23';officialDefaultFeed=$true;preferencesPreserved=$true
    wholeCameraPreferenceFilePreserved=$true;cameraStartsOff=$true;bridge2=1;noProcessStarted=$true;externalWrites=0;gameplayTested=$false
}
Write-ContractJson (Join-Path $output 'report.json') $report
'PASS: read-only shortcut, closed-process guard, all saved preferences and full camera file match the completed r23 installation.'
