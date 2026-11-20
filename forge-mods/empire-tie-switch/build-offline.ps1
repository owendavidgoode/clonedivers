param(
    [string]$Cli = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$Source = 'D:\ClaudeWork\hellforge-src',
    [string]$Zig = 'D:\HellForge\tools\zig',
    [string]$Out = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$mod = $PSScriptRoot
$workspace = Join-Path $mod 'build\workspace-0.3.0'
$stage = Join-Path $workspace 'mods\empire-tie-switch'
$sdk = Join-Path $workspace 'app\sdk'
$tools = Join-Path $workspace 'tools'
$stagedData = Join-Path $mod 'build\staged-data'
$assetInput = 'D:\ClaudeWork\forge-sdk\mods\empire-tie-switch\build\asset-input\data'
New-Item -ItemType Directory -Force $sdk,$tools,$stage,$Out,$stagedData | Out-Null
Get-ChildItem -LiteralPath 'D:\ClaudeWork\empire-forge\empire-forge-r45-v4' -File | ForEach-Object {
    if ($_.Name -match '^[a-f0-9]{16}\.patch_\d+(\.stream|\.gpu_resources)?$') {
        $target = Join-Path $stagedData $_.Name
        if (!(Test-Path -LiteralPath $target)) { New-Item -ItemType HardLink -Path $target -Target $_.FullName | Out-Null }
    }
}
Get-ChildItem -LiteralPath $assetInput -File | ForEach-Object {
    $target = Join-Path $stagedData ($_.Name.Replace('.patch_0','.patch_2000'))
    if (!(Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $_.FullName -Destination $target -Force }
}
Copy-Item "$Source\native\mod-sdk\*.h" $sdk -Force
$compiler = Join-Path $tools 'zig'
if (!(Test-Path -LiteralPath $compiler)) { New-Item -ItemType Junction -Path $compiler -Target $Zig | Out-Null }
if ((Get-Item -LiteralPath $compiler).Target[0] -ne $Zig) { throw 'Compiler junction target differs' }
# The installable appearance candidate owns the closure; this package is code only.
Copy-Item "$mod\src" $stage -Recurse -Force
Copy-Item "$mod\hellforge-mod.json","$mod\README.md","$mod\CREDITS.md" $stage -Force
$oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR; $oldLocal = $env:ZIG_LOCAL_CACHE_DIR
$fixture = Join-Path $mod ('build\fixtures-' + [guid]::NewGuid().ToString('N'))
try {
    $env:TEMP = Join-Path $mod 'build'; $env:TMP = $env:TEMP
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $tools 'zig-cache'
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $mod 'build\zig-local'
    & $Cli build-mod --mod $stage | Tee-Object -FilePath "$mod\build\build.log"
    if ($LASTEXITCODE -ne 0) { throw 'build-mod failed' }
    Copy-Item "$stage\empire_tie_switch.dll" $mod -Force
    & "$Zig\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Werror -I $sdk "$mod\tests\tie_test.c" -o "$mod\build\tie_test.exe" -municode
    if ($LASTEXITCODE -ne 0) { throw 'Test host compilation failed' }
    & "$mod\build\tie_test.exe" $stagedData $fixture | Tee-Object -FilePath "$mod\build\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }
    & "$mod\build\tie_test.exe" --scan 'D:\ClaudeWork\empire-forge\empire-forge-r47-tie' | Tee-Object -FilePath "$mod\build\candidate-gate.log"
    if ($LASTEXITCODE -ne 0) { throw 'Installable candidate closure failed' }
    if (Test-Path -LiteralPath (Join-Path $stage 'data')) { throw 'Code-only package unexpectedly holds data' }
    & $Cli package-mod --mod $stage --out $Out | Tee-Object -FilePath "$mod\build\package.log"
    if ($LASTEXITCODE -ne 0) { throw 'package-mod failed' }
    $zipPath = Join-Path $Out 'empire-tie-switch-0.3.0.zip'
    if (Test-Path -LiteralPath $zipPath) {
        $zipSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $zipLen = (Get-Item -LiteralPath $zipPath).Length
        $modHash = ((& $Cli mod-hash --mod $stage) | ConvertFrom-Json).hash
        Write-Output "ZIP=$zipPath"
        Write-Output "Bytes=$zipLen"
        Write-Output "Sha256=$zipSha"
        Write-Output "ContentHash=$modHash"
    }
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal; $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
}
