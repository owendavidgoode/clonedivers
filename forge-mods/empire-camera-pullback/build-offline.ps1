param(
    [string]$ZigRoot = 'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\source\tools\zig',
    [string]$SdkRoot = 'D:\ClaudeWork\hellforge-0.5-probe\hf05\app\sdk'
)
$ErrorActionPreference = 'Stop'
$mod = $PSScriptRoot
$zigExe = Join-Path $ZigRoot 'zig.exe'
if (!(Test-Path -LiteralPath $zigExe)) { throw "Offline Zig compiler not found at $zigExe" }
if (!(Test-Path -LiteralPath (Join-Path $SdkRoot 'hellforge_mod.h'))) { throw "SDK v20 headers not found at $SdkRoot" }

$manifest = Get-Content -LiteralPath (Join-Path $mod 'hellforge-mod.json') -Raw | ConvertFrom-Json
$version = $manifest.version
$dllName = $manifest.code.dll

$stage = Join-Path $mod 'build\stage'
$out = Join-Path $mod 'dist'
$zigGlobal = Join-Path $mod 'build\zig-global'
$zigLocal = Join-Path $mod 'build\zig-local'
$tmpDir = Join-Path $mod 'build\tmp'
$fixture = Join-Path $mod ('build\fixture-' + [guid]::NewGuid().ToString('N'))

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage, $fixture, $out, $zigGlobal, $zigLocal, $tmpDir | Out-Null

Copy-Item -LiteralPath "$mod\src" -Destination $stage -Recurse -Force
Copy-Item -LiteralPath "$mod\..\shared\clonedivers_em_layout.h" -Destination "$stage\src\clonedivers_em_layout.h" -Force
Copy-Item -LiteralPath "$mod\hellforge-mod.json", "$mod\options.ini", "$mod\README.md", "$mod\CREDITS.md" -Destination $stage -Force

$oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR; $oldLocal = $env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP = $tmpDir; $env:TMP = $tmpDir
    $env:ZIG_GLOBAL_CACHE_DIR = $zigGlobal
    $env:ZIG_LOCAL_CACHE_DIR = $zigLocal

    # 1. Strict zig cc -std=c11 -O2 -Wall -Wextra -Werror DLL build against SDK v20
    $builtDll = Join-Path $mod ("build\$dllName")
    & $zigExe cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Wextra -Werror -s -I $SdkRoot -I "$stage\src" "$stage\src\camera_pullback.c" -o $builtDll
    if ($LASTEXITCODE -ne 0) { throw 'Strict zig cc -Wall -Wextra -Werror DLL build failed' }

    # 2. Full C11 unit & offline-capture verification suite (-Wall -Wextra -Werror)
    & $zigExe cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Wextra -Werror -I $SdkRoot -I "$mod\src" -I "$mod\..\shared" "$mod\tests\camera_pullback_test.c" -o "$mod\build\camera_pullback_test.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Unit test build failed' }
    & "$mod\build\camera_pullback_test.exe" $fixture | Tee-Object "$mod\build\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Unit test execution failed' }

    # 3. Deterministic zip package & ModSets.ContentHash (exact match to HellForge.Core/ModPackage.cs)
    Get-ChildItem -LiteralPath $out -Filter '*.zip' -ErrorAction SilentlyContinue | Remove-Item -Force
    $zipPath = Join-Path $out ("empire-camera-pullback-$version.zip")

    $pyPack = Join-Path $tmpDir 'pack_mod.py'
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
        zi = zipfile.ZipInfo(f"empire-camera-pullback/{rel}", date_time=(2026, 1, 1, 0, 0, 0))
        zi.compress_type = zipfile.ZIP_DEFLATED
        src_p = os.path.join(stage, rel.replace('/', os.sep))
        with open(src_p, 'rb') as fp:
            zf.writestr(zi, fp.read())

print(content_hash)
'@ | Set-Content -LiteralPath $pyPack -Encoding utf8

    $folderHash = (& uv run python $pyPack $stage $zipPath $manifest.id $version $dllName).Trim()
    if ($LASTEXITCODE -ne 0 -or $folderHash.Length -ne 64) { throw 'Deterministic packaging failed' }

    $zipItem = Get-Item -LiteralPath $zipPath
    $zipSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $dllSha = (Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "ZIP=$zipPath"
    Write-Host "Bytes=$($zipItem.Length)"
    Write-Host "Sha256=$zipSha"
    Write-Host "DllSha256=$dllSha"
    Write-Host "ContentHash=$folderHash"
} finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal; $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
