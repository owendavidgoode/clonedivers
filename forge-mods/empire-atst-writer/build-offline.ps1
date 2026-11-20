param(
    [string]$Cli = 'D:\HellForge\app\cli\HellForge.Cli.exe',
    [string]$Zig = 'D:\HellForge\tools\zig',
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
$SdkRoot = if (Test-Path -LiteralPath (Join-Path $ModRoot 'sdk')) { Join-Path $ModRoot 'sdk' } else { Join-Path (Split-Path (Split-Path $Cli -Parent) -Parent) 'sdk' }
Copy-Item -Path (Join-Path $SdkRoot '*') -Destination $StageSdk -Force
Copy-Item -LiteralPath (Join-Path $ModRoot 'hellforge-mod.json') -Destination $StageMod -Force

$oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR
$oldLocal = $env:ZIG_LOCAL_CACHE_DIR
$ZigCache = Join-Path $WorkStage 'atst-zig-cache'
New-Item -ItemType Directory -Force $ZigCache | Out-Null

try {
    $env:ZIG_GLOBAL_CACHE_DIR = $ZigCache
    $env:ZIG_LOCAL_CACHE_DIR = $ZigCache

    # 1. Strict bare zig cc check (-Wall -Werror with NO -Wno-unused-parameter)
    $Sources = Get-ChildItem (Join-Path $ModRoot 'src') -Filter '*.c' | ForEach-Object { $_.FullName }
    $StrictDll = Join-Path $BuildDir 'empire_atst_writer.strict.dll'
    & "$Zig\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -ffp-contract=off -fno-fast-math -Wall -Werror -I $SdkRoot @Sources -o $StrictDll
    if ($LASTEXITCODE -ne 0) { throw "Strict zig cc -Wall -Werror compile failed" }

    # 2. Build via HellForge CLI SDK-16 builder (outputs DLL beside manifest in $StageMod)
    & $Cli build-mod --mod $StageMod | Tee-Object -FilePath (Join-Path $BuildDir 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "HellForge.Cli build-mod failed" }

    Copy-Item -LiteralPath (Join-Path $StageMod 'empire_atst_writer.dll') -Destination (Join-Path $BuildDir 'empire_atst_writer.dll') -Force

    # 3. Package via HellForge CLI
    & $Cli package-mod --mod $StageMod --out $DistDir | Tee-Object -FilePath (Join-Path $BuildDir 'package.log')
    if ($LASTEXITCODE -ne 0) { throw "HellForge.Cli package-mod failed" }

    $ZipFile = Join-Path $DistDir 'empire-atst-writer-0.1.0.zip'
    if (!(Test-Path -LiteralPath $ZipFile)) { throw "Expected zip not found: $ZipFile" }

    $HashOut = & $Cli mod-hash --mod $StageMod | Tee-Object -FilePath (Join-Path $BuildDir 'mod-hash.log')
    $ZipSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipFile).Hash.ToLowerInvariant()
    $DllSha = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $BuildDir 'empire_atst_writer.dll')).Hash.ToLowerInvariant()

    @{
        zip_path = $ZipFile
        zip_sha256 = $ZipSha
        dll_path = (Join-Path $BuildDir 'empire_atst_writer.dll')
        dll_sha256 = $DllSha
        cli_mod_hash = ($HashOut -join "`n")
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $BuildDir 'build-receipt.json') -Encoding utf8

    Write-Output "BUILD_OK zip=$ZipFile zip_sha256=$ZipSha dll_sha256=$DllSha"
} finally {
    $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal
    $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
    if (Test-Path -LiteralPath $StageRoot) {
        if (Test-Path -LiteralPath $StageZig) { cmd /c rmdir "`"$StageZig`"" 2>$null }
        Remove-Item -LiteralPath $StageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
