param(
    [string]$Cli = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$Zig = $(if (Test-Path -LiteralPath 'D:\HellForge\tools\zig\zig.exe') { 'D:\HellForge\tools\zig' } else { 'D:\ClaudeWork\workstreams-v21\code-mods\tools\zig' }),
    [string]$WorkStage = 'D:\ClaudeWork\cache\tmp\atst-hf-stage'
)

$ErrorActionPreference = 'Stop'

$ModRoot = $PSScriptRoot
$BuildDir = Join-Path $ModRoot 'build'
$DistDir = Join-Path $ModRoot 'dist'
New-Item -ItemType Directory -Force $BuildDir, $DistDir | Out-Null

# Prepare isolated child offline stage inside $WorkStage (never writes into D:\HellForge\ or deletes $WorkStage itself)
# ModSdk.Find expects <root>\app\sdk\hellforge_mod.h and <root>\tools\zig\zig.exe
$StageRoot = Join-Path $WorkStage 'empire-atst-writer-stage'
$StageMod = Join-Path $StageRoot 'mods\empire-atst-writer'
$StageSdk = Join-Path $StageRoot 'app\sdk'
$StageTools = Join-Path $StageRoot 'tools'
$StageZig = Join-Path $StageTools 'zig'
if (Test-Path -LiteralPath $StageRoot) {
    if (Test-Path -LiteralPath $StageZig) { cmd /c rmdir "`"$StageZig`"" 2>$null }
    Remove-Item -LiteralPath $StageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force $StageMod, $StageSdk, $StageTools | Out-Null
New-Item -ItemType Junction -Path $StageZig -Target $Zig | Out-Null

Copy-Item -LiteralPath (Join-Path $ModRoot 'src') -Destination $StageMod -Recurse -Force
$CliSdk = Join-Path (Split-Path (Split-Path $Cli -Parent) -Parent) 'sdk'
$SdkRoot = if (Test-Path -LiteralPath (Join-Path $CliSdk 'hellforge_mod.h')) {
    $CliSdk
} elseif (Test-Path -LiteralPath (Join-Path $ModRoot 'sdk\hellforge_mod.h')) {
    Join-Path $ModRoot 'sdk'
} else {
    'D:\ClaudeWork\hellforge-src\native\mod-sdk'
}
Copy-Item -Path (Join-Path $SdkRoot '*') -Destination $StageSdk -Force
Copy-Item -LiteralPath (Join-Path $ModRoot 'hellforge-mod.json') -Destination $StageMod -Force
if (Test-Path -LiteralPath (Join-Path $ModRoot 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $ModRoot 'README.md') -Destination $StageMod -Force
}

$oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR
$oldLocal = $env:ZIG_LOCAL_CACHE_DIR
$oldTemp = $env:TEMP
$oldTmp = $env:TMP
$ZigCache = Join-Path $WorkStage 'atst-zig-cache'
$TmpRun = Join-Path $WorkStage 'atst-tmp'
New-Item -ItemType Directory -Force $ZigCache, $TmpRun | Out-Null

try {
    $env:ZIG_GLOBAL_CACHE_DIR = $ZigCache
    $env:ZIG_LOCAL_CACHE_DIR = $ZigCache
    $env:TEMP = $TmpRun
    $env:TMP = $TmpRun

    # 1. Strict bare zig cc check (-Wall -Werror with NO -Wno-unused-parameter)
    $Sources = Get-ChildItem (Join-Path $ModRoot 'src') -Filter '*.c' | ForEach-Object { $_.FullName }
    $StrictDll = Join-Path $BuildDir 'empire_atst_writer.strict.dll'
    & "$Zig\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -ffp-contract=off -fno-fast-math -Wall -Werror -I $SdkRoot @Sources -o $StrictDll
    if ($LASTEXITCODE -ne 0) { throw "Strict zig cc -Wall -Werror compile failed" }

    # 2. Build via HellForge CLI SDK-16 builder (outputs DLL beside manifest in $StageMod)
    & $Cli build-mod --mod $StageMod | Tee-Object -FilePath (Join-Path $BuildDir 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "HellForge.Cli build-mod failed" }

    Copy-Item -LiteralPath (Join-Path $StageMod 'empire_atst_writer.dll') -Destination (Join-Path $BuildDir 'empire_atst_writer.dll') -Force

    # 3. Package via HellForge CLI (v0.2.6)
    $ZipFile = Join-Path $DistDir 'empire-atst-writer-0.2.6.zip'
    if (Test-Path -LiteralPath $ZipFile) { Remove-Item -LiteralPath $ZipFile -Force }
    & $Cli package-mod --mod $StageMod --out $DistDir | Tee-Object -FilePath (Join-Path $BuildDir 'package.log')
    if ($LASTEXITCODE -ne 0) { throw "HellForge.Cli package-mod failed" }
    if (!(Test-Path -LiteralPath $ZipFile)) { throw "Expected zip not found: $ZipFile" }

    $SharedDist = Join-Path (Split-Path $ModRoot -Parent) 'dist'
    if (Test-Path -LiteralPath $SharedDist) {
        Copy-Item -LiteralPath $ZipFile -Destination (Join-Path $SharedDist 'empire-atst-writer-0.2.6.zip') -Force
    }

    $HashOut = & $Cli mod-hash --mod $StageMod | Tee-Object -FilePath (Join-Path $BuildDir 'mod-hash.log')
    if ($LASTEXITCODE -ne 0) { throw "HellForge.Cli mod-hash failed" }
    $ContentHash = (($HashOut -join "`n") | ConvertFrom-Json).hash
    $ZipBytes = (Get-Item -LiteralPath $ZipFile).Length
    $ZipSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipFile).Hash.ToLowerInvariant()
    $DllSha = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $BuildDir 'empire_atst_writer.dll')).Hash.ToLowerInvariant()

    [ordered]@{
        version = '0.2.6'
        zip_path = $ZipFile
        shared_dist_path = (Join-Path $SharedDist 'empire-atst-writer-0.2.6.zip')
        zip_bytes = $ZipBytes
        zip_sha256 = $ZipSha
        content_hash = $ContentHash
        dll_path = (Join-Path $BuildDir 'empire_atst_writer.dll')
        dll_sha256 = $DllSha
        cli_mod_hash = ($HashOut -join "`n")
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $BuildDir 'build-receipt.json') -Encoding utf8

    Write-Output "BUILD_OK zip=$ZipFile bytes=$ZipBytes zip_sha256=$ZipSha content_hash=$ContentHash dll_sha256=$DllSha"
} finally {
    $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal
    $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
    if (Test-Path -LiteralPath $StageRoot) {
        if (Test-Path -LiteralPath $StageZig) { cmd /c rmdir "`"$StageZig`"" 2>$null }
        Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
