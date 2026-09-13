param([Parameter(Mandatory=$true)][string]$Template,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$lock=Get-Content (Join-Path $root 'build-inputs/rc/inputs.lock.json') -Raw | ConvertFrom-Json
if ((Get-ContentHash $Template) -ne $lock.templateSha256) {throw 'Template hash differs'}
if (Test-Path -LiteralPath $Output) {throw 'Choose a new archive path'}
$sources=@{}
foreach ($f in @($lock.sources) + @($lock.dependencies)) {
    $path=[IO.Path]::GetFullPath((Join-Path $root $f.path))
    if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Input path escapes workspace'}
    if ((Get-ContentHash $path) -ne $f.sha256) {throw "Input hash differs: $($f.path)"}
    $sources[$f.path]=$path
}
foreach ($file in (Get-ChildItem (Join-Path $root 'build-inputs/rc') -File)) { $sources['build-inputs/rc/' + $file.Name]=$file.FullName }
$sources['dist/rc-source-templates/full-clone-voice.patch']=$Template
$license=Join-Path $root 'dist/rc-upgrade/wwav/LICENSE'
$sources['dist/rc-upgrade/wwav/LICENSE']=$license
$zip=[IO.Compression.ZipFile]::Open([IO.Path]::GetFullPath($Output),[IO.Compression.ZipArchiveMode]::Create)
try { foreach ($entry in $sources.GetEnumerator()) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.Value,$entry.Key.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal) } }
finally {$zip.Dispose()}
Write-ContractJson "$Output.receipt.json" ([ordered]@{entries=$sources.Count;sha256=(Get-ContentHash $Output);sourceCount=$lock.sources.Count;templateSha256=$lock.templateSha256})
"Archived $($sources.Count) verified inputs for offline rebuilding."
