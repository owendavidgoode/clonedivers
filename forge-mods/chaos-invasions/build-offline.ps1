param(
    [string]$CliPath = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$ZigRoot = ''
)
$ErrorActionPreference='Stop'
$mod=$PSScriptRoot
$zigRoot = if ($ZigRoot) { $ZigRoot } elseif (Test-Path -LiteralPath 'D:\HellForge\tools\zig\zig.exe') { 'D:\HellForge\tools\zig' } else { 'D:\ClaudeWork\workstreams-v21\code-mods\tools\zig' }
$globalZigCache = Join-Path $mod 'build\zig-global'
$workspace=Join-Path $mod 'build\workspace'
$stage=Join-Path $workspace 'mods\chaos-invasions'
$sdk=Join-Path $workspace 'app\sdk'
$compiler=Join-Path $workspace 'tools\zig'
$out=Join-Path $mod 'dist'
$fixture=Join-Path $mod ('build\fixture-'+[guid]::NewGuid().ToString('N'))

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage,$sdk,(Split-Path $compiler),$fixture,$out,$globalZigCache | Out-Null
$cliSdk = Join-Path (Split-Path (Split-Path $CliPath -Parent) -Parent) 'sdk'
$sdkSource = if (Test-Path -LiteralPath (Join-Path $cliSdk 'hellforge_mod.h')) { $cliSdk } else { 'D:\ClaudeWork\hellforge-src\native\mod-sdk' }
Copy-Item (Join-Path $sdkSource '*.h') $sdk -Force
if (Test-Path -LiteralPath (Join-Path $sdkSource 'tools.json')) { Copy-Item -LiteralPath (Join-Path $sdkSource 'tools.json') -Destination $sdk -Force }
if (!(Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Junction -Path $compiler -Target $zigRoot | Out-Null
}
$sharedAssetCache = 'D:\ClaudeWork\hellforge-0.5-probe\asset-kit-cache'
New-Item -ItemType Directory -Force $sharedAssetCache,(Join-Path $workspace 'temp') | Out-Null
Set-Content -LiteralPath (Join-Path $workspace 'hellforge-install.json') -Value '{"schemaVersion":1,"buildId":"25480438","createdUtc":"2026-11-28T00:00:00Z"}' -Encoding UTF8
$assetKitLink = Join-Path $workspace 'temp\asset-kit'
if (!(Test-Path -LiteralPath $assetKitLink)) { New-Item -ItemType Junction -Path $assetKitLink -Target $sharedAssetCache | Out-Null }
$gameLink = Join-Path $workspace 'game'
if (!(Test-Path -LiteralPath $gameLink) -and (Test-Path -LiteralPath 'D:\HellForge\game\data\bundles.nxa')) {
    New-Item -ItemType Junction -Path $gameLink -Target 'D:\HellForge\game' | Out-Null
}

& 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe' "$mod\tools\delivery.py" prepare
if ($LASTEXITCODE -ne 0) { throw 'Lua archive preparation failed' }

Copy-Item "$mod\src","$mod\data" $stage -Recurse -Force
Copy-Item "$mod\..\shared\clonedivers_host_options.h" "$stage\src\clonedivers_host_options.h" -Force
Copy-Item "$mod\..\shared\clonedivers_em_layout.h" "$stage\src\clonedivers_em_layout.h" -Force
Copy-Item "$mod\hellforge-mod.json","$mod\README.md","$mod\CREDITS.md","$mod\options.ini" $stage -Force

$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $oldGlobal=$env:ZIG_GLOBAL_CACHE_DIR; $oldLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP=Join-Path $mod 'build'; $env:TMP=$env:TEMP
    $env:ZIG_GLOBAL_CACHE_DIR=$globalZigCache
    $env:ZIG_LOCAL_CACHE_DIR=Join-Path $mod 'build\zig-local'

    # 1. Explicit strict zig cc -O2 -Wall -Werror check
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Werror -Wno-unused-function -s -I $sdk -I "$stage\src" "$stage\src\chaos_invasions.c" -o "$mod\build\strict_chaos_invasions.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Strict zig cc -Wall -Werror DLL build failed' }

    # 2. Official HellForge.Cli build-mod
    $buildJson = & $CliPath build-mod --mod $stage | Tee-Object "$mod\build\build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    $builtDll = (($buildJson | Out-String) | ConvertFrom-Json).dll
    Copy-Item -LiteralPath $builtDll -Destination "$mod\chaos_invasions.dll" -Force

    # 3. Full C11 unit test suite
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Werror -Wno-unused-function -DCHAOS_UNIT_TEST -I $sdk -I "$mod\src" -I "$mod\..\shared" "$mod\tests\chaos_test.c" -o "$mod\build\chaos_test.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
    & "$mod\build\chaos_test.exe" $fixture | Tee-Object "$mod\build\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Fixture failed' }

    # 4. Standalone LuaJIT menu bridge suite
    & 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe' "$mod\tests\menu_test.py" | Tee-Object "$mod\build\menu-tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Menu fixture failed' }

    # 5. Package & verify
    & $CliPath package-mod --mod $stage --out $out | Tee-Object "$mod\build\package.log"
    if ($LASTEXITCODE -ne 0) { throw 'Package failed' }
    Get-ChildItem -LiteralPath $out -Filter '*.asset-report.md' -ErrorAction SilentlyContinue | Move-Item -Destination "$mod\build" -Force
    & 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe' "$mod\tools\delivery.py" verify | Tee-Object "$mod\build\verify.log"
    if ($LASTEXITCODE -ne 0) { throw 'Delivery verification failed' }

    $zipPath=Join-Path $out 'chaos-invasions-0.5.2.zip'
    $zipItem=Get-Item -LiteralPath $zipPath
    $zipSha=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $folderHashJson=& $CliPath mod-hash --mod $stage
    if ($LASTEXITCODE -ne 0) { throw 'mod-hash failed' }
    $folderHash=($folderHashJson | ConvertFrom-Json).hash
    Write-Host "ZIP=$zipPath"
    Write-Host "Bytes=$($zipItem.Length)"
    Write-Host "Sha256=$zipSha"
    Write-Host "ContentHash=$folderHash"
} finally {
    $env:TEMP=$oldTemp; $env:TMP=$oldTmp; $env:ZIG_GLOBAL_CACHE_DIR=$oldGlobal; $env:ZIG_LOCAL_CACHE_DIR=$oldLocal
    if (Test-Path -LiteralPath $gameLink) { cmd /c rmdir "$gameLink" 2>$null | Out-Null }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
