param(
    [string]$CliPath = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$ZigRoot = ''
)
$ErrorActionPreference='Stop'
$mod=$PSScriptRoot
$zigRoot = if ($ZigRoot) { $ZigRoot } elseif (Test-Path -LiteralPath 'D:\HellForge\tools\zig\zig.exe') { 'D:\HellForge\tools\zig' } else { 'D:\ClaudeWork\workstreams-v21\code-mods\tools\zig' }
$globalZigCache = Join-Path $mod 'build\zig-global'
$workspace=Join-Path $mod 'build\workspace'
$stage=Join-Path $workspace 'mods\faster-corpse-cleanup'
$sdk=Join-Path $workspace 'app\sdk'
$compiler=Join-Path $workspace 'tools\zig'
$out=Join-Path $mod 'dist'
$fixture=Join-Path $mod ('build\fixture-'+[guid]::NewGuid().ToString('N'))

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage,$sdk,(Split-Path $compiler),$fixture,$out,$globalZigCache | Out-Null
Copy-Item 'D:\ClaudeWork\hellforge-src\native\mod-sdk\*.h' $sdk -Force
if (!(Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Junction -Path $compiler -Target $zigRoot | Out-Null
}

Copy-Item "$mod\src","$mod\notices","$mod\reference" $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path (Split-Path $mod -Parent) 'shared\clonedivers_host_options.h') -Destination "$stage\src\clonedivers_host_options.h" -Force
Copy-Item "$mod\hellforge-mod.json","$mod\config.ini","$mod\LICENSE.txt","$mod\LICENSE-CLIENT-INTERFACE.txt","$mod\LICENSE-MENU-ADAPTER.txt","$mod\LICENSE-SDK.txt","$mod\README.md","$mod\CHANGELOG.md","$mod\PATCH_NOTES.md","$mod\THIRD-PARTY-NOTICES.md" $stage -Force

$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $oldGlobal=$env:ZIG_GLOBAL_CACHE_DIR; $oldLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP=Join-Path $mod 'build'; $env:TMP=$env:TEMP
    $env:ZIG_GLOBAL_CACHE_DIR=$globalZigCache
    $env:ZIG_LOCAL_CACHE_DIR=Join-Path $mod 'build\zig-local'

    # 1. Explicit strict zig cc -O2 -Wall -Werror check across all C translation units
    $cSources = Get-ChildItem -LiteralPath "$stage\src" -Filter '*.c' | Sort-Object Name | ForEach-Object { $_.FullName }
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Werror -Wno-unused-function -s -I $sdk -I "$stage\src" @cSources -o "$mod\build\strict_corpse_cleanup.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Strict zig cc -Wall -Werror DLL build failed' }

    # 2. Official HellForge.Cli build-mod
    & $CliPath build-mod --mod $stage | Tee-Object "$mod\build\build.log"
    if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli build-mod failed' }
    Copy-Item "$stage\corpse_cleanup.dll" $mod -Force

    # 3. Build and execute host/joiner + host-options unit test suite
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Werror -Wno-unused-function -I $sdk -I "$mod\src" "$mod\tests\corpse_cleanup_test.c" -o "$mod\build\corpse_cleanup_test.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Unit test compilation failed' }
    & "$mod\build\corpse_cleanup_test.exe" $fixture | Tee-Object "$mod\build\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Unit test execution failed' }

    # 4. Package zip & compute SHA-256 + ContentHash
    Get-ChildItem -LiteralPath $out -Filter '*.zip' -ErrorAction SilentlyContinue | Remove-Item -Force
    & $CliPath package-mod --mod $stage --out $out | Tee-Object "$mod\build\package.log"
    if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli package-mod failed' }

    $zipPath = Join-Path $out 'faster-corpse-cleanup-1.0.6-clonedivers.2.zip'
    if (!(Test-Path -LiteralPath $zipPath)) {
        $found = Get-ChildItem -LiteralPath $out -Filter '*.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($found) { Copy-Item -LiteralPath $found.FullName -Destination $zipPath -Force }
    }
    $zipItem = Get-Item -LiteralPath $zipPath
    $zipSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $folderHashJson = & $CliPath mod-hash --mod $stage
    if ($LASTEXITCODE -ne 0) { throw 'mod-hash failed' }
    $folderHash = ($folderHashJson | ConvertFrom-Json).hash
    Write-Host "ZIP=$zipPath"
    Write-Host "Bytes=$($zipItem.Length)"
    Write-Host "Sha256=$zipSha"
    Write-Host "ContentHash=$folderHash"
} finally {
    $env:TEMP=$oldTemp; $env:TMP=$oldTmp; $env:ZIG_GLOBAL_CACHE_DIR=$oldGlobal; $env:ZIG_LOCAL_CACHE_DIR=$oldLocal
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
