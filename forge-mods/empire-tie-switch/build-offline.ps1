param(
    [string]$Zig = 'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\source\tools\zig',
    [string]$SdkRoot = 'D:\ClaudeWork\hellforge-0.5-probe\hf05\app\sdk',
    [string]$BuildRoot = 'D:\ClaudeWork\cache\tmp\empire-tie-switch-build-040',
    [string]$Out = (Join-Path $PSScriptRoot 'dist'),
    [switch]$RequireFiveTiers
)
$ErrorActionPreference = 'Stop'
$mod = $PSScriptRoot
$zigExe = Join-Path $Zig 'zig.exe'
if (!(Test-Path -LiteralPath $zigExe)) { throw "Offline Zig compiler not found at $zigExe" }
if (!(Test-Path -LiteralPath (Join-Path $SdkRoot 'hellforge_mod.h'))) { throw "SDK v20 headers not found at $SdkRoot" }

$workspace = Join-Path $BuildRoot 'workspace-0.4.0'
$stage = Join-Path $workspace 'mods\empire-tie-switch'
$sdk = Join-Path $workspace 'app\sdk'
$stagedData = Join-Path $BuildRoot 'staged-data'
$assetInput = 'D:\ClaudeWork\forge-sdk\mods\empire-tie-switch\build\asset-input\data'

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force $BuildRoot, $sdk, $stage, $Out, $stagedData | Out-Null

Get-ChildItem -LiteralPath 'D:\ClaudeWork\empire-forge\empire-forge-r45-v4' -File | ForEach-Object {
    if ($_.Name -match '^[a-f0-9]{16}\.patch_\d+(\.stream|\.gpu_resources)?$') {
        $target = Join-Path $stagedData $_.Name
        if (!(Test-Path -LiteralPath $target)) { New-Item -ItemType HardLink -Path $target -Target $_.FullName | Out-Null }
    }
}
Get-ChildItem -LiteralPath $assetInput -File | ForEach-Object {
    $target = Join-Path $stagedData ($_.Name.Replace('.patch_0', '.patch_2000'))
    if (!(Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $_.FullName -Destination $target -Force }
}
Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $SdkRoot -Filter '*.h').FullName -Destination $sdk -Force

Copy-Item -LiteralPath "$mod\src" -Destination $stage -Recurse -Force
Copy-Item -LiteralPath "$mod\hellforge-mod.json", "$mod\README.md", "$mod\CREDITS.md" -Destination $stage -Force

$manifest = Get-Content -LiteralPath "$stage\hellforge-mod.json" -Raw | ConvertFrom-Json
$ver = $manifest.version
$modId = $manifest.id
$dllName = $manifest.code.dll

$oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR; $oldLocal = $env:ZIG_LOCAL_CACHE_DIR
$fixture = Join-Path $BuildRoot ('fixtures-' + [guid]::NewGuid().ToString('N'))
try {
    $env:TEMP = $BuildRoot; $env:TMP = $env:TEMP
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $BuildRoot 'zig-global'
    $env:ZIG_LOCAL_CACHE_DIR = Join-Path $BuildRoot 'zig-local'
    New-Item -ItemType Directory -Force $env:ZIG_GLOBAL_CACHE_DIR, $env:ZIG_LOCAL_CACHE_DIR | Out-Null

    # 1. Strict zig cc -std=c11 -O2 -Wall -Wextra -Werror DLL build against SDK v20
    $builtDll = Join-Path $BuildRoot $dllName
    & $zigExe cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Wextra -Werror -s -I $sdk -I "$stage\src" "$stage\src\empire_tie_switch.c" -o $builtDll
    if ($LASTEXITCODE -ne 0) { throw 'Strict zig cc -Wall -Wextra -Werror DLL build failed' }
    Copy-Item -LiteralPath $builtDll -Destination (Join-Path $mod $dllName) -Force

    # 2. Strict zig cc -std=c11 -O2 -Wall -Wextra -Werror C11 regression suite
    & $zigExe cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Wextra -Werror -I $sdk -I "$mod\src" "$mod\tests\tie_test.c" -o "$BuildRoot\tie_test.exe" -municode
    if ($LASTEXITCODE -ne 0) { throw 'Test host compilation failed' }
    & "$BuildRoot\tie_test.exe" $stagedData $fixture | Tee-Object -FilePath "$BuildRoot\tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed' }

    # 3. Scan installable Empire & Clones staged packs
    '' | Set-Content -LiteralPath "$BuildRoot\candidate-gate.log" -Encoding UTF8
    $r52Root = 'D:\ClaudeWork\cache\tmp\tie-r52-tiers'
    if (Test-Path -LiteralPath $r52Root) {
        foreach ($tier in @('full', 'lighter', 'reduced1024', 'reduced512', 'potato256')) {
            $tierDir = Join-Path $r52Root $tier
            if (!(Test-Path -LiteralPath $tierDir)) { throw "Missing hydrated r52 tier folder: $tierDir" }
            & "$BuildRoot\tie_test.exe" --scan $tierDir | Tee-Object -FilePath "$BuildRoot\candidate-gate.log" -Append
            if ($LASTEXITCODE -ne 0) { throw "Installable r52 closure failed on tier $tier" }
        }
    } elseif ($RequireFiveTiers) {
        throw "Hydrated 5-tier r52 root '$r52Root' is required when -RequireFiveTiers is set."
    } elseif (Test-Path -LiteralPath 'D:\ClaudeWork\empire-forge\empire-forge-r47-tie') {
        & "$BuildRoot\tie_test.exe" --scan 'D:\ClaudeWork\empire-forge\empire-forge-r47-tie' | Tee-Object -FilePath "$BuildRoot\candidate-gate.log" -Append
        if ($LASTEXITCODE -ne 0) { throw 'Installable Empire candidate closure failed' }
    }

    $clonesR26Root = 'D:\ClaudeWork\swarm-packs\clones-r26\staged\all-droids'
    if (Test-Path -LiteralPath $clonesR26Root) {
        foreach ($tier in @('full', 'lighter', 'reduced1024', 'reduced512', 'potato256')) {
            $tierDir = Join-Path $clonesR26Root $tier
            if (Test-Path -LiteralPath $tierDir) {
                & "$BuildRoot\tie_test.exe" --scan $tierDir | Tee-Object -FilePath "$BuildRoot\candidate-gate.log" -Append
                if ($LASTEXITCODE -ne 0) { throw "Clonedivers r26 closure failed on tier $tier" }
            }
        }
    }

    if (Test-Path -LiteralPath (Join-Path $stage 'data')) { throw 'Code-only package unexpectedly holds data' }

    # 4. Deterministic ModPackage zip & ModSets.ContentHash (exact match to HellForge.Core/ModPackage.cs)
    Get-ChildItem -LiteralPath $Out -Filter '*.zip' -ErrorAction SilentlyContinue | Remove-Item -Force
    $zipPath = Join-Path $Out ("empire-tie-switch-$ver.zip")

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
                $entry = $za.CreateEntry("empire-tie-switch/$rel", [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $stamp
                $inBytes = [System.IO.File]::ReadAllBytes((Join-Path $stage ($rel.Replace('/', '\'))))
                $es = $entry.Open()
                try { $es.Write($inBytes, 0, $inBytes.Length) } finally { $es.Dispose() }
            }
        } finally { $za.Dispose() }
    } finally { $fs.Dispose() }

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append("$modId|$ver`n")
    foreach ($rel in $sortedList) {
        $fSha = (Get-FileHash -LiteralPath (Join-Path $stage ($rel.Replace('/', '\'))) -Algorithm SHA256).Hash.ToUpperInvariant()
        [void]$sb.Append("$rel|$fSha`n")
    }
    $shaAlg = [System.Security.Cryptography.SHA256]::Create()
    $blockBytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
    $modHash = ([System.BitConverter]::ToString($shaAlg.ComputeHash($blockBytes)).Replace('-', '')).ToLowerInvariant()

    $zipSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $dllSha = (Get-FileHash -LiteralPath $builtDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipLen = (Get-Item -LiteralPath $zipPath).Length
    Write-Output "ZIP=$zipPath"
    Write-Output "Bytes=$zipLen"
    Write-Output "Sha256=$zipSha"
    Write-Output "DllSha256=$dllSha"
    Write-Output "ContentHash=$modHash"
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal; $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
}
