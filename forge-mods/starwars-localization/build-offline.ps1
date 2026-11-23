param(
    [string]$CliPath = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$ZigRoot = ''
)
$ErrorActionPreference='Stop'
$mod=$PSScriptRoot
$zigRoot = if ($ZigRoot) { $ZigRoot } elseif (Test-Path -LiteralPath 'D:\HellForge\tools\zig\zig.exe') { 'D:\HellForge\tools\zig' } else { 'D:\ClaudeWork\workstreams-v21\code-mods\tools\zig' }
$workspace=Join-Path $mod 'build\workspace'
$stage=Join-Path $workspace 'mods\starwars-localization'
$sdk=Join-Path $workspace 'app\sdk'
$compiler=Join-Path $workspace 'tools\zig'
$out=Join-Path $mod 'dist'
$zigGlobal=Join-Path $mod 'build\zig-global'
$zigLocal=Join-Path $mod 'build\zig-local'
$tmpDir=Join-Path $mod 'build\tmp'
$fixture=Join-Path $mod ('build\fixture-'+[guid]::NewGuid().ToString('N'))

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $compiler) {
    $item = Get-Item -LiteralPath $compiler -Force
    if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $compiler -Recurse -Force
    }
}
New-Item -ItemType Directory -Force $stage,$sdk,(Split-Path $compiler),$fixture,$out,$zigGlobal,$zigLocal,$tmpDir | Out-Null
Copy-Item 'D:\ClaudeWork\hellforge-src\native\mod-sdk\*.h' $sdk -Force
if (!(Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Junction -Path $compiler -Target $zigRoot | Out-Null
}

Copy-Item -LiteralPath "$mod\src" -Destination $stage -Recurse -Force
Copy-Item -LiteralPath "$mod\hellforge-mod.json","$mod\README.md" -Destination $stage -Force

$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $oldGlobal=$env:ZIG_GLOBAL_CACHE_DIR; $oldLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP=$tmpDir; $env:TMP=$tmpDir
    $env:ZIG_GLOBAL_CACHE_DIR=$zigGlobal
    $env:ZIG_LOCAL_CACHE_DIR=$zigLocal

    # 1. Strict zig cc -std=c11 -O2 -Wall -Werror check
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Werror -s -I $sdk -I "$stage\src" "$stage\src\starwars_localization.c" -o "$mod\build\strict_starwars_localization.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Strict zig cc -Wall -Werror DLL build failed' }

    # 2. Official HellForge.Cli build-mod
    & $CliPath build-mod --mod $stage | Tee-Object "$mod\build\build.log"
    if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli build-mod failed' }
    Copy-Item -LiteralPath "$stage\starwars_localization.dll" -Destination $mod -Force

    # 3. Full C11 unit & real-archive verification suite
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Werror -I $sdk -I "$mod\src" "$mod\tests\starwars_localization_test.c" -o "$mod\build\starwars_localization_test.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Unit test build failed' }
    & "$mod\build\starwars_localization_test.exe" $fixture | Tee-Object "$mod\build\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Unit test execution failed' }

    # 4. Package & mod-hash
    $zipPath=Join-Path $out 'starwars-localization-0.1.0.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    & $CliPath package-mod --mod $stage --out $out | Tee-Object "$mod\build\package.log"
    if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli package-mod failed' }

    if (!(Test-Path -LiteralPath $zipPath)) { throw "Expected package not found: $zipPath" }
    $zipItem=Get-Item -LiteralPath $zipPath
    $zipSha=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $folderHashJson=& $CliPath mod-hash --mod $stage
    if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli mod-hash failed' }
    $folderHash=($folderHashJson | ConvertFrom-Json).hash
    Write-Host "ZIP=$zipPath"
    Write-Host "Bytes=$($zipItem.Length)"
    Write-Host "Sha256=$zipSha"
    Write-Host "ContentHash=$folderHash"
} finally {
    $env:TEMP=$oldTemp; $env:TMP=$oldTmp; $env:ZIG_GLOBAL_CACHE_DIR=$oldGlobal; $env:ZIG_LOCAL_CACHE_DIR=$oldLocal
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
