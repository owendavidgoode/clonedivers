param([string]$CliPath='D:\HellForge\app\cli\HellForge.Cli.exe')
$ErrorActionPreference='Stop'
$mod=$PSScriptRoot
$workspace=Join-Path $mod 'build\workspace'
$stage=Join-Path $workspace 'mods\chaos-invasions'
$sdk=Join-Path $workspace 'app\sdk'
$compiler=Join-Path $workspace 'tools\zig'
$out=Join-Path $mod 'dist'
$fixture=Join-Path $mod ('build\fixture-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage,$sdk,(Split-Path $compiler),$fixture,$out | Out-Null
Copy-Item 'D:\ClaudeWork\hellforge-src\native\mod-sdk\*.h' $sdk -Force
if(!(Test-Path -LiteralPath $compiler)) { New-Item -ItemType Junction -Path $compiler -Target 'D:\HellForge\tools\zig' | Out-Null }
if((Get-Item -LiteralPath $compiler).Target[0] -ne 'D:\HellForge\tools\zig') { throw 'Compiler junction differs' }
& 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe' "$mod\tools\delivery.py" prepare
if($LASTEXITCODE -ne 0) { throw 'Lua archive preparation failed' }
Copy-Item "$mod\src","$mod\data" $stage -Recurse -Force
Copy-Item "$mod\hellforge-mod.json","$mod\README.md","$mod\CREDITS.md","$mod\options.ini" $stage -Force
$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $oldGlobal=$env:ZIG_GLOBAL_CACHE_DIR; $oldLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP=Join-Path $mod 'build'; $env:TMP=$env:TEMP
    $env:ZIG_GLOBAL_CACHE_DIR=Join-Path $mod 'build\zig-global'
    $env:ZIG_LOCAL_CACHE_DIR=Join-Path $mod 'build\zig-local'
    & $CliPath build-mod --mod $stage | Tee-Object "$mod\build\build.log"
    if($LASTEXITCODE -ne 0) { throw 'Native build failed' }
    Copy-Item "$stage\chaos_invasions.dll" $mod -Force
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Werror -Wno-unused-function -DCHAOS_UNIT_TEST -I $sdk "$mod\tests\chaos_test.c" -o "$mod\build\chaos_test.exe"
    if($LASTEXITCODE -ne 0) { throw 'Fixture build failed' }
    & "$mod\build\chaos_test.exe" $fixture | Tee-Object "$mod\build\tests.log"
    if($LASTEXITCODE -ne 0) { throw 'Fixture failed' }
    & 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe' "$mod\tests\menu_test.py" | Tee-Object "$mod\build\menu-tests.log"
    if($LASTEXITCODE -ne 0) { throw 'Menu fixture failed' }
    & $CliPath package-mod --mod $stage --out $out | Tee-Object "$mod\build\package.log"
    if($LASTEXITCODE -ne 0) { throw 'Package failed' }
    & 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe' "$mod\tools\delivery.py" verify | Tee-Object "$mod\build\verify.log"
    if($LASTEXITCODE -ne 0) { throw 'Delivery verification failed' }
    $zipPath=Join-Path $out 'chaos-invasions-0.3.0.zip'
    $zipItem=Get-Item -LiteralPath $zipPath
    $zipSha=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $folderHashJson=& $CliPath mod-hash --mod $stage
    if($LASTEXITCODE -ne 0) { throw 'mod-hash failed' }
    $folderHash=($folderHashJson | ConvertFrom-Json).hash
    Write-Host "ZIP=$zipPath"
    Write-Host "Bytes=$($zipItem.Length)"
    Write-Host "Sha256=$zipSha"
    Write-Host "ContentHash=$folderHash"
} finally {
    $env:TEMP=$oldTemp; $env:TMP=$oldTmp; $env:ZIG_GLOBAL_CACHE_DIR=$oldGlobal; $env:ZIG_LOCAL_CACHE_DIR=$oldLocal
}
