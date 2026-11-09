param(
    [string]$Cli = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$Source = 'D:\ClaudeWork\hellforge-src',
    [string]$Zig = 'D:\HellForge\tools\zig',
    [string]$Candidate = 'D:\ClaudeWork\empire-forge\empire-forge-r45-v3',
    [string]$Out = 'D:\ClaudeWork\forge-sdk\dist'
)
$ErrorActionPreference = 'Stop'
$mod = $PSScriptRoot
$workspace = Join-Path $mod 'build\workspace'
$stage = Join-Path $workspace 'mods\empire-lego-yoda'
$sdk = Join-Path $workspace 'app\sdk'
$tools = Join-Path $workspace 'tools'
New-Item -ItemType Directory -Force $sdk,$tools,$stage,$Out | Out-Null
Copy-Item "$Source\native\mod-sdk\*.h" $sdk -Force
$compiler = Join-Path $tools 'zig'
if (!(Test-Path -LiteralPath $compiler)) { New-Item -ItemType Junction -Path $compiler -Target $Zig | Out-Null }
if ((Get-Item -LiteralPath $compiler).Target[0] -ne $Zig) { throw 'Compiler junction target differs; refusing build.' }
Copy-Item "$mod\src","$mod\audio","$mod\tests" $stage -Recurse -Force
Copy-Item "$mod\hellforge-mod.json","$mod\README.md","$mod\CREDITS.md","$mod\build-offline.ps1" $stage -Force
$oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR; $oldLocal = $env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP = Join-Path $mod 'build'; $env:TMP = $env:TEMP
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $tools 'zig-cache'
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $mod 'build\zig-local'
    & $Cli build-mod --mod $stage | Tee-Object -FilePath "$mod\build\build.log"
    if ($LASTEXITCODE -ne 0) { throw 'build-mod failed' }
    Copy-Item "$stage\empire_lego_yoda.dll" $mod -Force
    & "$Zig\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Werror -Wno-unused-parameter -I $sdk "$mod\tests\yoda_test.c" -o "$mod\build\yoda_test.exe" -municode
    if ($LASTEXITCODE -ne 0) { throw 'Test host compilation failed' }
    $fixture = Join-Path $mod ('build\fixtures-' + [guid]::NewGuid().ToString('N'))
    & "$mod\build\yoda_test.exe" $Candidate $fixture | Tee-Object -FilePath "$mod\build\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Offline regression checks failed' }
    & $Cli package-mod --mod $stage --out $Out | Tee-Object -FilePath "$mod\build\package.log"
    if ($LASTEXITCODE -ne 0) { throw 'package-mod failed' }
} finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal; $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
}
