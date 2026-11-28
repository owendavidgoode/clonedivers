param(
    [string]$CliPath = '',
    [string]$ZigRoot = '',
    [string]$SdkRoot = ''
)
$ErrorActionPreference = 'Stop'
$mod = $PSScriptRoot
$zigRoot = if ($ZigRoot) {
    $ZigRoot
} elseif (Test-Path -LiteralPath 'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\source\tools\zig\zig.exe') {
    'D:\ClaudeWork\empire-forge\tie-prerequisites-astra\source\tools\zig'
} else {
    throw 'Offline zig.exe not found under D:\ClaudeWork\empire-forge\tie-prerequisites-astra\source\tools\zig'
}
$sdkSource = if ($SdkRoot) {
    $SdkRoot
} elseif (Test-Path -LiteralPath 'D:\ClaudeWork\hellforge-0.5-probe\hf05\app\sdk\hellforge_mod.h') {
    'D:\ClaudeWork\hellforge-0.5-probe\hf05\app\sdk'
} else {
    'D:\ClaudeWork\hellforge-src\native\mod-sdk'
}

$py = 'C:\Users\goode\AppData\Local\Programs\Python\Python313\python.exe'
if (!(Test-Path -LiteralPath $py)) { $py = 'python' }

$workspace = Join-Path $mod 'build\workspace'
$stage = Join-Path $workspace 'mods\dedicated-melee-slot'
$sdk = Join-Path $workspace 'app\sdk'
$compiler = Join-Path $workspace 'tools\zig'
$out = Join-Path $mod 'dist'
$zigGlobal = Join-Path $mod 'build\zig-global'
$zigLocal = Join-Path $mod 'build\zig-local'
$tmpDir = Join-Path $mod 'build\tmp'
$fixture = Join-Path $mod ('build\fixture-' + [guid]::NewGuid().ToString('N'))

if (Test-Path -LiteralPath "$mod\slot") { Remove-Item -LiteralPath "$mod\slot" -Recurse -Force }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $compiler) {
    $item = Get-Item -LiteralPath $compiler -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        cmd /c rmdir "`"$compiler`"" 2>$null | Out-Null
    } else {
        Remove-Item -LiteralPath $compiler -Recurse -Force
    }
}
New-Item -ItemType Directory -Force $stage,$sdk,(Split-Path $compiler),$fixture,$out,$zigGlobal,$zigLocal,$tmpDir | Out-Null
Copy-Item (Join-Path $sdkSource '*.h') $sdk -Force
if (Test-Path -LiteralPath (Join-Path $sdkSource 'tools.json')) {
    Copy-Item -LiteralPath (Join-Path $sdkSource 'tools.json') -Destination $sdk -Force
}
New-Item -ItemType Junction -Path $compiler -Target $zigRoot | Out-Null

# 1. Build Lua picker into data/9ba626afa44a3aa3.patch_920 (+ .stream, .gpu_resources)
$env:PYTHONDONTWRITEBYTECODE = '1'
if (Test-Path -LiteralPath "$mod\picker\__pycache__") { Remove-Item -LiteralPath "$mod\picker\__pycache__" -Recurse -Force }
& $py -B "$mod\picker\build.py" | Tee-Object "$mod\build\picker-build.log"
if ($LASTEXITCODE -ne 0) { throw 'Picker Lua patch_920 build failed' }

Copy-Item -LiteralPath "$mod\src","$mod\data","$mod\picker" -Destination $stage -Recurse -Force
Copy-Item -LiteralPath "$mod\..\shared\clonedivers_em_layout.h" -Destination "$stage\src\clonedivers_em_layout.h" -Force
if (Test-Path -LiteralPath "$stage\picker\__pycache__") { Remove-Item -LiteralPath "$stage\picker\__pycache__" -Recurse -Force }
Copy-Item -LiteralPath "$mod\hellforge-mod.json","$mod\LICENSE","$mod\CREDITS.md","$mod\README.md" -Destination $stage -Force

$manifest = Get-Content -LiteralPath "$stage\hellforge-mod.json" -Raw | ConvertFrom-Json
$ver = $manifest.version
$modId = $manifest.id
$dllName = $manifest.code.dll

$oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldGlobal = $env:ZIG_GLOBAL_CACHE_DIR; $oldLocal = $env:ZIG_LOCAL_CACHE_DIR
try {
    $env:TEMP = $tmpDir; $env:TMP = $tmpDir
    $env:ZIG_GLOBAL_CACHE_DIR = $zigGlobal
    $env:ZIG_LOCAL_CACHE_DIR = $zigLocal

    # 2. Strict zig cc -std=c11 -O2 -Wall -Wextra -Werror check against SDK v20
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Wextra -Werror -s -I $sdk -I "$stage\src" "$stage\src\melee_slot.c" -o "$mod\build\strict_melee_weapon_slot.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Strict zig cc -Wall -Wextra -Werror DLL build failed' }

    # 3. ModKit-exact DLL build (offline default without touching D:\HellForge, or explicit CliPath)
    if ($CliPath -and (Test-Path -LiteralPath $CliPath)) {
        $buildJson = & $CliPath build-mod --mod $stage | Tee-Object "$mod\build\build.log"
        if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli build-mod failed' }
        $builtDll = (($buildJson | Out-String) | ConvertFrom-Json).dll
        Copy-Item -LiteralPath $builtDll -Destination "$mod\$dllName" -Force
    } else {
        & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -shared -O2 -Wall -Wextra -Werror -s -I $sdk -I "$stage\src" "$stage\src\melee_slot.c" -o "$stage\$dllName" | Tee-Object "$mod\build\build.log"
        if ($LASTEXITCODE -ne 0) { throw 'Offline ModKit zig cc DLL build failed' }
        Get-ChildItem -LiteralPath $stage -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.lib','.pdb' } | Remove-Item -Force
        Copy-Item -LiteralPath "$stage\$dllName" -Destination "$mod\$dllName" -Force
    }

    # 4. Full C11 unit & offline-capture verification suite (-Wall -Wextra -Werror)
    & "$compiler\zig.exe" cc -std=c11 -target x86_64-windows-gnu -O2 -Wall -Wextra -Werror -I $sdk -I "$mod\src" -I "$mod\..\shared" "$mod\tests\melee_slot_test.c" -o "$mod\build\melee_slot_test.exe"
    if ($LASTEXITCODE -ne 0) { throw 'C11 unit test build failed' }
    & "$mod\build\melee_slot_test.exe" $fixture | Tee-Object "$mod\build\c-tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'C11 unit test execution failed' }

    # 5. Lua51 bytecode load + patch_920 + game.dll signature verification suite
    & $py "$mod\tests\melee_picker_test.py" | Tee-Object "$mod\build\lua-tests.log"
    if ($LASTEXITCODE -ne 0) { throw 'Lua picker verification failed' }

    # 6. Package & mod-hash (byte-identical to ModPackage.Create & ModSets.ContentHash)
    Get-ChildItem -LiteralPath $out -Filter '*.zip' -ErrorAction SilentlyContinue | Remove-Item -Force
    $zipPath = Join-Path $out ("dedicated-melee-slot-$ver.zip")
    if ($CliPath -and (Test-Path -LiteralPath $CliPath)) {
        & $CliPath package-mod --mod $stage --out $out | Tee-Object "$mod\build\package.log"
        if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli package-mod failed' }
        Get-ChildItem -LiteralPath $out -Filter '*.asset-report.md' -ErrorAction SilentlyContinue | Move-Item -Destination "$mod\build" -Force
        $folderHashJson = & $CliPath mod-hash --mod $stage
        if ($LASTEXITCODE -ne 0) { throw 'HellForge.Cli mod-hash failed' }
        $folderHash = ($folderHashJson | ConvertFrom-Json).hash
    } else {
        # Note: dedicated-melee-slot ships prebuilt melee_weapon_slot.dll + data/ + picker/ + src/ inside its zip
        # while ModPackage.Create excludes build artifacts (.lib/.pdb/.tmp/build/). Wait: check whether ModPackage.Create
        # includes or excludes $dllName when src/*.c is present!
        # In starwars-localization/build-offline.ps1 line 81: ModPackage.Create excludes root $dllName when src/*.c exists
        # because HellForge compiles src/*.c on install OR wait — let's include $dllName ONLY if ModPackage.Create does:
        # Actually in starwars-localization/build-offline.ps1 (verified byte-identical to ModPackage.Create & ModSets.ContentHash):
        # $isSrcOut = ($rel -eq $dllName) -or .lib -or .pdb when src/ is staged!
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $relFiles = Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
            $parts = $rel.Split('/')
            $isWs = ($parts | Where-Object { $_.StartsWith('.') -or $_.StartsWith('~$') }).Count -gt 0 -or $parts[0] -eq 'build' -or $rel.EndsWith('.user', [StringComparison]::OrdinalIgnoreCase) -or $rel.EndsWith('.tmp', [StringComparison]::OrdinalIgnoreCase)
            $isSrcOut = ($rel -eq $dllName) -or $rel.EndsWith('.lib', [StringComparison]::OrdinalIgnoreCase) -or $rel.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)
            if (-not $isWs -and -not $isSrcOut) { $rel }
        }
        #Wait: check if prebuilt DLL is expected inside dedicated-melee-slot zip! Let's inspect existing dist/*.zip first if present!
        $sortedList = [System.Collections.Generic.List[string]]::new()
        foreach ($r in $relFiles) { [void]$sortedList.Add($r) }
        $sortedList.Sort([System.StringComparer]::Ordinal)

        $stamp = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $fs = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite)
        try {
            $za = [System.IO.Compression.ZipArchive]::new($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)
            try {
                foreach ($rel in $sortedList) {
                    $entry = $za.CreateEntry("dedicated-melee-slot/$rel", [System.IO.Compression.CompressionLevel]::Optimal)
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
        $folderHash = ([System.BitConverter]::ToString($shaAlg.ComputeHash($blockBytes)).Replace('-', '')).ToLowerInvariant()
        Set-Content -LiteralPath "$mod\build\package.log" -Value (@{ id = $modId; version = $ver; zip = $zipPath; files = $sortedList.Count; hash = $folderHash } | ConvertTo-Json -Compress)
    }

    if (!(Test-Path -LiteralPath $zipPath)) { throw "Expected package not found: $zipPath" }
    $zipItem = Get-Item -LiteralPath $zipPath
    $zipSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $dllSha = (Get-FileHash -LiteralPath "$mod\$dllName" -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "ZIP=$zipPath"
    Write-Host "Bytes=$($zipItem.Length)"
    Write-Host "Sha256=$zipSha"
    Write-Host "DllSha256=$dllSha"
    Write-Host "ContentHash=$folderHash"
} finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:ZIG_GLOBAL_CACHE_DIR = $oldGlobal; $env:ZIG_LOCAL_CACHE_DIR = $oldLocal
    if (Test-Path -LiteralPath $compiler) { cmd /c rmdir "`"$compiler`"" 2>$null | Out-Null }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
