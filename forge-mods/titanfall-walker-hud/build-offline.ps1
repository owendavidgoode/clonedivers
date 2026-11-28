param(
    [string]$ZigRoot = ''
)
$ErrorActionPreference = 'Stop'
$mod = $PSScriptRoot
$zigRoot = if ($ZigRoot) { $ZigRoot }
    elseif (Test-Path -LiteralPath 'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\source\tools\zig\zig.exe') { 'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\source\tools\zig' }
    elseif (Test-Path -LiteralPath 'D:\ClaudeWork\workstreams-v21\code-mods\tools\zig\zig.exe') { 'D:\ClaudeWork\workstreams-v21\code-mods\tools\zig' }
    else { throw 'Offline Zig toolchain not found on D:\ClaudeWork' }

$sdkSrc = if (Test-Path -LiteralPath 'D:\ClaudeWork\hellforge-0.5-probe\hf05\app\sdk\hellforge_mod.h') {
    'D:\ClaudeWork\hellforge-0.5-probe\hf05\app\sdk'
} else {
    throw 'HellForge SDK v20 headers not found at D:\ClaudeWork\hellforge-0.5-probe\hf05\app\sdk'
}

$workspace = Join-Path $mod 'build\workspace'
$stage     = Join-Path $workspace 'mods\titanfall-walker-hud'
$sdk       = Join-Path $workspace 'app\sdk'
$compiler  = Join-Path $workspace 'tools\zig'
$out       = Join-Path $mod 'dist'
$zigGlobal = Join-Path $mod 'build\zig-global'
$zigLocal  = Join-Path $mod 'build\zig-local'
$tmpDir    = Join-Path $mod 'build\tmp'
$fixture   = Join-Path $mod ('build\fixture-' + [guid]::NewGuid().ToString('N'))

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $compiler) {
    $item = Get-Item -LiteralPath $compiler -Force
    if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Remove-Item -LiteralPath $compiler -Recurse -Force
    }
}
New-Item -ItemType Directory -Force $stage,$sdk,(Split-Path $compiler),$fixture,$out,$zigGlobal,$zigLocal,$tmpDir | Out-Null
Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $sdkSrc -Filter '*.h').FullName -Destination $sdk -Force
if (!(Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Junction -Path $compiler -Target $zigRoot | Out-Null
}

Copy-Item -LiteralPath "$mod\src" -Destination $stage -Recurse -Force
Copy-Item -LiteralPath "$mod\..\shared\clonedivers_em_layout.h" -Destination "$stage\src\clonedivers_em_layout.h" -Force
Copy-Item -LiteralPath "$mod\hellforge-mod.json","$mod\options.ini","$mod\README.md" -Destination $stage -Force

$manifest = Get-Content -LiteralPath "$stage\hellforge-mod.json" -Raw | ConvertFrom-Json
$ver      = $manifest.version
$modId    = $manifest.id
$dllName  = $manifest.code.dll

$oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR; $oldLocal = $env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP = $tmpDir; $env:TMP = $tmpDir
    $env:ZIG_GLOBAL_CACHE_DIR = $zigGlobal
    $env:ZIG_LOCAL_CACHE_DIR  = $zigLocal

    # 1. Strict -std=c11 -O2 -Wall -Wextra -Werror DLL build against SDK v20
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Wextra -Werror -s `
        -I $sdk -I "$stage\src" "$stage\src\titanfall_walker_hud.c" -luser32 -o "$stage\$dllName"
    if ($LASTEXITCODE -ne 0) { throw 'Strict zig cc -Wall -Wextra -Werror DLL build failed' }

    Get-ChildItem -LiteralPath $stage -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in '.lib','.pdb' } | Remove-Item -Force
    Copy-Item -LiteralPath "$stage\$dllName" -Destination $mod -Force

    # 2. Strict -std=c11 -O2 -Wall -Wextra -Werror C11 unit & mock-HfDraw verification suite
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Wextra -Werror `
        -I $sdk -I "$mod\src" -I "$mod\..\shared" "$mod\tests\titanfall_hud_test.c" -luser32 -o "$mod\build\titanfall_hud_test.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Unit test compilation failed' }

    & "$mod\build\titanfall_hud_test.exe" $fixture | Tee-Object "$mod\build\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Unit test execution failed' }

    # 3. Deterministic HellForge ModPackage zip (exact match to ModPackage.Create & ModSets.ContentHash)
    $zipPath = Join-Path $out ("titanfall-walker-hud-$ver.zip")
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $relFiles = Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        $parts = $rel.Split('/')
        $isWs = ($parts | Where-Object { $_.StartsWith('.') -or $_.StartsWith('~$') }).Count -gt 0 -or
                $parts[0] -eq 'build' -or
                $rel.EndsWith('.user', [StringComparison]::OrdinalIgnoreCase) -or
                $rel.EndsWith('.tmp', [StringComparison]::OrdinalIgnoreCase)
        $isSrcOut = ($rel -eq $dllName) -or
                    $rel.EndsWith('.lib', [StringComparison]::OrdinalIgnoreCase) -or
                    $rel.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)
        if (-not $isWs -and -not $isSrcOut) { $rel }
    }
    $sortedList = [System.Collections.Generic.List[string]]::new()
    foreach ($r in $relFiles) { [void]$sortedList.Add($r) }
    $sortedList.Sort([System.StringComparer]::Ordinal)

    $stamp = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $fs = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite)
    try {
        $za = [System.IO.Compression.ZipArchive]::new($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($rel in $sortedList) {
                $entry = $za.CreateEntry("titanfall-walker-hud/$rel", [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $stamp
                $inBytes = [System.IO.File]::ReadAllBytes((Join-Path $stage ($rel.Replace('/', '\'))))
                $es = $entry.Open()
                try { $es.Write($inBytes, 0, $inBytes.Length) } finally { $es.Dispose() }
            }
        } finally { $za.Dispose() }
    } finally { $fs.Dispose() }

    # Compute ModSets.ContentHash (excluding generated DLL to match HellForge source-tree ContentHash, plus DLL SHA256)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append("$modId|$ver`n")
    foreach ($rel in $sortedList) {
        if ($rel -eq $dllName) { continue }
        $fSha = (Get-FileHash -LiteralPath (Join-Path $stage ($rel.Replace('/', '\'))) -Algorithm SHA256).Hash.ToUpperInvariant()
        [void]$sb.Append("$rel|$fSha`n")
    }
    $shaAlg = [System.Security.Cryptography.SHA256]::Create()
    $blockBytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
    $folderHash = ([System.BitConverter]::ToString($shaAlg.ComputeHash($blockBytes)).Replace('-', '')).ToLowerInvariant()

    $zipItem = Get-Item -LiteralPath $zipPath
    $zipSha  = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $dllSha  = (Get-FileHash -LiteralPath "$stage\$dllName" -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "ZIP=$zipPath"
    Write-Host "Bytes=$($zipItem.Length)"
    Write-Host "Sha256=$zipSha"
    Write-Host "DllSha256=$dllSha"
    Write-Host "ContentHash=$folderHash"
} finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal; $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
