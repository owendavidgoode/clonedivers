# Promote an already validated full-option manifest, preserving existing hosted variants.
# Writes only staging output. Publishing and changing the shared manifest are separate steps.
param(
    [Parameter(Mandatory=$true)][string]$TestedManifest,
    [Parameter(Mandatory=$true)][string]$AssetDirectory,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$prior = Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json
$tested = Get-Content -LiteralPath $TestedManifest -Raw | ConvertFrom-Json
$known = @{}
foreach ($f in $prior.pack.files) { if ($f.size -gt 0) { $known[$f.sha256] = $f.url } }
$plan = @{}
$tag = "pack-$Version-files"
$empty = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
foreach ($f in $tested.pack.files) {
    if ($f.size -eq 0) {
        if ($f.sha256 -ne $empty) { throw "Invalid empty file: $($f.name)" }
        $f.url = ''
    } elseif ($known.ContainsKey($f.sha256)) {
        $f.url = $known[$f.sha256]
    } else {
        $source = Join-Path $AssetDirectory $f.sha256
        if (!(Test-Path -LiteralPath $source)) { throw "Missing new asset: $source" }
        if ((Get-Item -LiteralPath $source).Length -ne $f.size -or
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $f.sha256) {
            throw "New asset verification failed: $($f.name)"
        }
        $f.url = "https://github.com/owendavidgoode/clonedivers/releases/download/$tag/$($f.sha256)"
        $plan[$f.sha256] = [ordered]@{ sha=$f.sha256; size=$f.size; localPath=[IO.Path]::GetFullPath($source); tag=$tag }
    }
    if ($f.size -gt 0 -and $f.url -notmatch '^https://github.com/owendavidgoode/clonedivers/releases/download/') {
        throw "Non-release URL for $($f.name)"
    }
}
$tested.pack.version = $Version
$tested.pack.notes = 'Choose your universe.'
$tested.pack.options | Where-Object id -eq 'commandos' | ForEach-Object {
    $_.name = 'Commandodivers'; $_.description = 'Delta Squad is elite.'
}
$out = [ordered]@{ format=2; app=$prior.app; pack=$tested.pack }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'manifest.json'), ($out | ConvertTo-Json -Depth 12), $utf8)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'upload-plan.json'), (ConvertTo-Json -InputObject @($plan.Values) -Depth 6), $utf8)
[long]$bytes = 0
foreach ($asset in $plan.Values) { $bytes += $asset.size }
"Prepared $Version; $($tested.pack.files.Count) full-option entries; $($plan.Count) new assets; $bytes new bytes."
