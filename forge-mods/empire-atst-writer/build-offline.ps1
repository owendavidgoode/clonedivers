param(
    [string]$Zig = 'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\source\tools\zig',
    [string]$SdkRoot = 'D:\ClaudeWork\hellforge-0.5-probe\hf05\app\sdk',
    [string]$WorkStage = 'D:\ClaudeWork\cache\tmp\atst-hf-stage'
)

$ErrorActionPreference = 'Stop'

$ModRoot = $PSScriptRoot
$BuildDir = Join-Path $ModRoot 'build'
$DistDir = Join-Path $ModRoot 'dist'
New-Item -ItemType Directory -Force $BuildDir, $DistDir | Out-Null

$ZigExe = Join-Path $Zig 'zig.exe'
if (!(Test-Path -LiteralPath $ZigExe)) { throw "Offline Zig compiler not found at $ZigExe" }
if (!(Test-Path -LiteralPath (Join-Path $SdkRoot 'hellforge_mod.h'))) { throw "SDK v20 headers not found at $SdkRoot" }

$Manifest = Get-Content -LiteralPath (Join-Path $ModRoot 'hellforge-mod.json') -Raw | ConvertFrom-Json
$Version = $Manifest.version
$DllName = $Manifest.code.dll

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

    # 1. Strict zig cc build (-Wall -Wextra -Werror against SDK v20)
    $Sources = Get-ChildItem -LiteralPath (Join-Path $ModRoot 'src') -Filter '*.c' | Sort-Object Name | ForEach-Object { $_.FullName }
    $BuiltDll = Join-Path $BuildDir $DllName
    & $ZigExe cc -std=c11 -target x86_64-windows-gnu -shared -O2 -ffp-contract=off -fno-fast-math -Wall -Wextra -Werror -s -I $SdkRoot -I (Join-Path $ModRoot 'src') @Sources -o $BuiltDll
    if ($LASTEXITCODE -ne 0) { throw "Strict zig cc -Wall -Wextra -Werror DLL compile failed" }

    # 2. Build & run full C11 unit suite (-Wall -Wextra -Werror)
    $CoreSources = $Sources | Where-Object { [IO.Path]::GetFileName($_) -ne 'atst_mod_main.c' }
    $TestExe = Join-Path $BuildDir 'test_empire_atst_writer.exe'
    & $ZigExe cc -std=c11 -target x86_64-windows-gnu -O2 -ffp-contract=off -fno-fast-math -Wall -Wextra -Werror -I $SdkRoot -I (Join-Path $ModRoot 'src') -I (Join-Path $ModRoot 'tests') (Join-Path $ModRoot 'tests\test_empire_atst_writer.c') @CoreSources -o $TestExe
    if ($LASTEXITCODE -ne 0) { throw "Unit test compile failed" }
    Push-Location $BuildDir
    try {
        & $TestExe | Tee-Object -FilePath (Join-Path $BuildDir 'tests.log')
        if ($LASTEXITCODE -ne 0) { throw "Unit test suite failed" }
    } finally {
        Pop-Location
    }

    # 3. Deterministic zip packaging & ModSets.ContentHash (exact match to HellForge.Core/ModPackage.cs)
    $StageMod = Join-Path $WorkStage 'empire-atst-writer-pkg'
    if (Test-Path -LiteralPath $StageMod) { Remove-Item -LiteralPath $StageMod -Recurse -Force }
    New-Item -ItemType Directory -Force $StageMod | Out-Null
    Copy-Item -LiteralPath (Join-Path $ModRoot 'src') -Destination $StageMod -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $ModRoot 'hellforge-mod.json') -Destination $StageMod -Force
    if (Test-Path -LiteralPath (Join-Path $ModRoot 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $ModRoot 'README.md') -Destination $StageMod -Force
    }

    $ZipFile = Join-Path $DistDir ("empire-atst-writer-$Version.zip")
    if (Test-Path -LiteralPath $ZipFile) { Remove-Item -LiteralPath $ZipFile -Force }

    $PyScript = Join-Path $TmpRun 'pack_mod.py'
    @'
import hashlib, os, sys, zipfile

stage, out_zip, mod_id, mod_ver, dll_name = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5]

def collect_source_files(root):
    rels = []
    for dp, dns, fns in os.walk(root):
        dns[:] = [d for d in dns if not d.startswith('.') and d.lower() != 'build' and d.lower() != 'dist']
        for fn in fns:
            if fn.startswith('.') or fn.startswith('~$'): continue
            low = fn.lower()
            if low.endswith(('.user', '.tmp', '.lib', '.pdb')) or low == dll_name.lower(): continue
            full = os.path.join(dp, fn)
            rel = os.path.relpath(full, root).replace('\\', '/')
            rels.append(rel)
    rels.sort()
    return rels

src_rels = collect_source_files(stage)
block_lines = [f"{mod_id}|{mod_ver}\n"]
for rel in src_rels:
    with open(os.path.join(stage, rel.replace('/', os.sep)), 'rb') as fp:
        f_sha = hashlib.sha256(fp.read()).hexdigest().upper()
    block_lines.append(f"{rel}|{f_sha}\n")
content_hash = hashlib.sha256("".join(block_lines).encode('utf-8')).hexdigest().lower()

with zipfile.ZipFile(out_zip, 'w', compression=zipfile.ZIP_DEFLATED) as zf:
    for rel in src_rels:
        zi = zipfile.ZipInfo(f"empire-atst-writer/{rel}", date_time=(2026, 1, 1, 0, 0, 0))
        zi.compress_type = zipfile.ZIP_DEFLATED
        src_p = os.path.join(stage, rel.replace('/', os.sep))
        with open(src_p, 'rb') as fp:
            zf.writestr(zi, fp.read())

print(content_hash)
'@ | Set-Content -LiteralPath $PyScript -Encoding utf8

    $ContentHash = (& uv run python $PyScript $StageMod $ZipFile $Manifest.id $Version $DllName).Trim()
    if ($LASTEXITCODE -ne 0 -or $ContentHash.Length -ne 64) { throw "Deterministic packaging failed" }

    $ZipBytes = (Get-Item -LiteralPath $ZipFile).Length
    $ZipSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipFile).Hash.ToLowerInvariant()
    $DllSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $BuiltDll).Hash.ToLowerInvariant()

    Write-Output "BUILD_OK version=$Version zip=$ZipFile bytes=$ZipBytes zip_sha256=$ZipSha content_hash=$ContentHash dll_sha256=$DllSha"
} finally {
    $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal
    $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
}
