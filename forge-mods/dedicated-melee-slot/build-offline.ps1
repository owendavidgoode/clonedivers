param(
    [string]$CliPath = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$ZigRoot = ''
)
$ErrorActionPreference='Stop'
$mod=$PSScriptRoot
$zigRoot = if ($ZigRoot) { $ZigRoot } elseif (Test-Path -LiteralPath 'D:\HellForge\tools\zig\zig.exe') { 'D:\HellForge\tools\zig' } else { 'D:\ClaudeWork\workstreams-v21\code-mods\tools\zig' }
$py='C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe'
$workspace=Join-Path $mod 'build\workspace'
$stage=Join-Path $workspace 'mods\dedicated-melee-slot'
$sdk=Join-Path $workspace 'app\sdk'
$compiler=Join-Path $workspace 'tools\zig'
$out=Join-Path $mod 'dist'
$zigGlobal=Join-Path $mod 'build\zig-global'
$zigLocal=Join-Path $mod 'build\zig-local'
$tmpDir=Join-Path $mod 'build\tmp'
$fixture=Join-Path $mod ('build\fixture-'+[guid]::NewGuid().ToString('N'))

if (Test-Path -LiteralPath "$mod\slot") { Remove-Item -LiteralPath "$mod\slot" -Recurse -Force }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage,$sdk,(Split-Path $compiler),$fixture,$out,$zigGlobal,$zigLocal,$tmpDir | Out-Null
Copy-Item 'D:\ClaudeWork\hellforge-src\native\mod-sdk\*.h' $sdk -Force
if (!(Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Junction -Path $compiler -Target $zigRoot | Out-Null
}

# 1. Build Lua picker into data/9ba626afa44a3aa3.patch_920 (+ .stream, .gpu_resources)
$env:PYTHONDONTWRITEBYTECODE='1'
if (Test-Path -LiteralPath "$mod\picker\__pycache__") { Remove-Item -LiteralPath "$mod\picker\__pycache__" -Recurse -Force }
& $py -B "$mod\picker\build.py" | Tee-Object "$mod\build\picker-build.log"
if ($LASTEXITCODE -ne 0) { throw 'Picker Lua patch_920 build failed' }

Copy-Item -LiteralPath "$mod\src","$mod\data","$mod\picker" -Destination $stage -Recurse -Force
if (Test-Path -LiteralPath "$stage\picker\__pycache__") { Remove-Item -LiteralPath "$stage\picker\__pycache__" -Recurse -Force }
Copy-Item -LiteralPath "$mod\hellforge-mod.json","$mod\LICENSE","$mod\CREDITS.md","$mod\README.md" -Destination $stage -Force

$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $oldGlobal=$env:ZIG_GLOBAL_CACHE_DIR; $oldLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP=$tmpDir; $env:TMP=$tmpDir
    $env:ZIG_GLOBAL_CACHE_DIR=$zigGlobal
    $env:ZIG_LOCAL_CACHE_DIR=$zigLocal

    # 2. Strict zig cc -std=c11 -O2 -Wall -Werror check
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Werror -s -I $sdk -I "$stage\src" "$stage\src\melee_slot.c" -o "$mod\build\strict_melee_weapon_slot.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Strict zig cc -Wall -Werror DLL build failed' }

    # 3. Official HellForge.Cli build-mod
    & $CliPath build-mod --mod $stage | Tee-Object "$mod\build\build.log"
    if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli build-mod failed' }
    Copy-Item -LiteralPath "$stage\melee_weapon_slot.dll" -Destination $mod -Force

    # 4. Full C11 unit & offline-capture verification suite
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Werror -I $sdk -I "$mod\src" "$mod\tests\melee_slot_test.c" -o "$mod\build\melee_slot_test.exe"
    if ($LASTEXITCODE -ne 0) { throw 'C11 unit test build failed' }
    & "$mod\build\melee_slot_test.exe" $fixture | Tee-Object "$mod\build\c-tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'C11 unit test execution failed' }

    # 5. Lua51 bytecode load + patch_920 + game.dll signature verification suite
    & $py "$mod\tests\melee_picker_test.py" | Tee-Object "$mod\build\lua-tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Lua picker verification failed' }

    # 6. Package & mod-hash
    Get-ChildItem -LiteralPath $out -Filter '*.zip' -ErrorAction SilentlyContinue | Remove-Item -Force
    & $CliPath package-mod --mod $stage --out $out | Tee-Object "$mod\build\package.log"
    if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli package-mod failed' }

    $zipLower=Join-Path $out 'dedicated-melee-slot-1.5.2-clonedivers.2.zip'
    if (!(Test-Path -LiteralPath $zipLower)) { throw "Expected package not found: $zipLower" }

    $zipItem=Get-Item -LiteralPath $zipLower
    $zipSha=(Get-FileHash -LiteralPath $zipLower -Algorithm SHA256).Hash.ToLowerInvariant()
    $folderHashJson=& $CliPath mod-hash --mod $stage
    if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli mod-hash failed' }
    $folderHash=($folderHashJson | ConvertFrom-Json).hash
    Write-Host "ZIP=$zipLower"
    Write-Host "Bytes=$($zipItem.Length)"
    Write-Host "Sha256=$zipSha"
    Write-Host "ContentHash=$folderHash"
} finally {
    $env:TEMP=$oldTemp; $env:TMP=$oldTmp; $env:ZIG_GLOBAL_CACHE_DIR=$oldGlobal; $env:ZIG_LOCAL_CACHE_DIR=$oldLocal
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
