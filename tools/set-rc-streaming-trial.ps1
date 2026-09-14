# Developer-only RC streaming smoke trial. Only fifteen named RC files may change.
param(
    [ValidateSet('Status','Enable','Disable')][string]$Action='Status',
    [string]$GameDirectory='C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2',
    [string]$BaselineManifest=(Join-Path $PSScriptRoot '../manifest.json'),
    [string]$CandidateManifest=(Join-Path $PSScriptRoot '../dist/profile-candidates/manifest-public-v3.json'),
    [string]$CandidateDirectory=(Join-Path $PSScriptRoot '../dist/profile-candidates/rc-lighter'),
    [string]$ReceiptPath=(Join-Path $PSScriptRoot '../dist/rc-streaming-runtime/install.json'),
    [string[]]$BlockedReceiptPaths=@((Join-Path $PSScriptRoot '../dist/geometry-candidates/install.json'),(Join-Path $PSScriptRoot '../dist/investigation-2026-09-13/geometry/control-install.json'))
)
$ErrorActionPreference='Stop'
$GameDirectory=[IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$ReceiptPath=[IO.Path]::GetFullPath($ReceiptPath)
$CandidateDirectory=[IO.Path]::GetFullPath($CandidateDirectory)
$data=Join-Path $GameDirectory 'data';$off=Join-Path $GameDirectory 'mods_off'
$names=@(foreach($index in @(254,256,258,260,261)){foreach($suffix in @('','.gpu_resources','.stream')){"9ba626afa44a3aa3.patch_$index$suffix"}})
function CheckPath([string]$path){
    $current=[IO.Path]::GetFullPath($path)
    while($current){
        if((Test-Path -LiteralPath $current) -and (Get-Item -LiteralPath $current -Force).LinkType -in @('Junction','SymbolicLink')){throw 'Linked paths unsupported'}
        $current=[IO.Path]::GetDirectoryName($current)
    }
}
function Hash([string]$path){CheckPath $path;(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
function CopyDurable([string]$source,[string]$destination){
    CheckPath $source;CheckPath $destination
    $inputStream=[IO.File]::OpenRead($source)
    try {
        $outputStream=New-Object IO.FileStream($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,1048576,[IO.FileOptions]::WriteThrough)
        try {$inputStream.CopyTo($outputStream);$outputStream.Flush($true)} finally {$outputStream.Dispose()}
    } finally {$inputStream.Dispose()}
}
function AtomicMove([string]$source,[string]$destination){
    if(-not ('RcStreamingTrialNative' -as [type])){
        Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class RcStreamingTrialNative { [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool MoveFileEx(string source, string target, int flags); }'
    }
    if([IO.Path]::GetDirectoryName($source) -ne [IO.Path]::GetDirectoryName($destination)){throw 'Atomic replacement requires the same directory'}
    if(-not [RcStreamingTrialNative]::MoveFileEx($source,$destination,9)){throw (New-Object ComponentModel.Win32Exception([Runtime.InteropServices.Marshal]::GetLastWin32Error()))}
}
function Save($value){
    CheckPath $ReceiptPath;[IO.Directory]::CreateDirectory((Split-Path -Parent $ReceiptPath))|Out-Null
    $temp=$ReceiptPath+'.tmp-'+[guid]::NewGuid().ToString('N')
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($value|ConvertTo-Json -Depth 12))
    $stream=New-Object IO.FileStream($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
    try {$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)} finally {$stream.Dispose()}
    AtomicMove $temp $ReceiptPath
}
function ValidateFiles($files){
    if($files.Count -ne 15 -or @($files.name|Select-Object -Unique).Count -ne 15){throw 'Exactly fifteen distinct RC files are required'}
    foreach($file in $files){
        if($file.name -cnotin $names -or $file.sourceSha256 -cnotmatch '^[a-f0-9]{64}$' -or $file.sha256 -cnotmatch '^[a-f0-9]{64}$' -or
           [string]$file.sourceSize -notmatch '^\d+$' -or [decimal]$file.sourceSize -gt [long]::MaxValue -or
           [string]$file.size -notmatch '^\d+$' -or [decimal]$file.size -gt [long]::MaxValue){throw 'Invalid RC filename, size or hash'}
    }
}
function ValidateReceipt($value){
    if($value.schemaVersion -ne 1 -or $value.trial -ne 'rc-streaming-fifteen' -or $value.gameDirectory -ne $GameDirectory -or
       $value.state -notin @('prepared','enabled','disabling','disabled') -or $value.sourcePack -ne '2026.09.12-r8' -or $value.candidatePack -ne '2026.09.13-r9'){throw 'Receipt belongs to a different game or trial'}
    ValidateFiles @($value.files)
    $backup=[IO.Path]::GetFullPath([string]$value.backupDirectory);$prefix=[IO.Path]::GetFileName($ReceiptPath)+'.backup-'
    if([IO.Path]::GetDirectoryName($backup) -ne (Split-Path -Parent $ReceiptPath) -or [IO.Path]::GetFileName($backup) -notmatch ('^'+[regex]::Escape($prefix)+'[a-f0-9]{32}$')){throw 'Invalid backup directory in receipt'}
    CheckPath $backup
    foreach($file in @($value.files)){
        $path=Join-Path $backup $file.name
        if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.sourceSize -or (Hash $path) -ne $file.sourceSha256){throw 'Backup missing or changed; no game files modified'}
    }
}
function Locate($file,[bool]$fresh){
    $locations=@(foreach($folder in @($data,$off)){
        $path=Join-Path $folder $file.name;CheckPath $path
        if(Test-Path -LiteralPath $path){if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Target is not a regular file'};$path}
    })
    if($locations.Count -ne 1){throw "Missing or ambiguous target: $($file.name)"}
    $path=$locations[0]
    if($fresh -and [IO.Path]::GetDirectoryName($path) -ne $data){throw 'Fresh trial requires active original RC files in data'}
    $hash=Hash $path;$length=(Get-Item -LiteralPath $path).Length
    $original=$hash -eq $file.sourceSha256 -and $length -eq $file.sourceSize
    $candidate=$hash -eq $file.sha256 -and $length -eq $file.size
    if(-not $original -and ($fresh -or -not $candidate)){throw "Unexpected target bytes; refusing overwrite: $($file.name)"}
    [pscustomobject]@{path=$path;hash=$hash;file=$file}
}
function ReplaceExact([string]$source,$target,[string]$expected){
    $temp=$target.path+'.rc-streaming-tmp-'+[guid]::NewGuid().ToString('N')
    try {
        CopyDurable $source $temp
        if((Hash $temp) -ne $expected){throw 'Staged replacement failed verification'}
        $current=Locate $target.file $false
        if($current.path -ne $target.path -or $current.hash -ne $target.hash){throw 'Target changed during operation; recovery receipt retained'}
        if(Get-Process helldivers2,Clonedivers -ErrorAction SilentlyContinue){throw 'Game or launcher started during trial; close both and Disable to restore'}
        AtomicMove $temp $target.path
        if((Hash $target.path) -ne $expected){throw 'Replacement failed verification; recovery receipt retained'}
    } finally {if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
}
function SelectManifestFile($manifest,[string]$name,[string]$profile){
    $matches=@($manifest.pack.files|Where-Object {
        $_.name -ceq $name -and (-not $_.modes -or $_.modes -contains 'commandos') -and
        (-not $_.textureProfiles -or $_.textureProfiles -contains $profile) -and
        (-not $_.option -or $_.option -eq 'commandos' -or ($_.option -eq 'skinny' -and $profile -eq 'lighter')) -and
        (-not $_.unlessOption -or ($_.unlessOption -eq 'skinny' -and $profile -eq 'full'))
    })
    if($matches.Count -ne 1){throw "Manifest does not select one $profile/commandos identity for $name"}
    $matches[0]
}
foreach($path in @($data,$off,$ReceiptPath)){CheckPath $path}
$receipt=if(Test-Path -LiteralPath $ReceiptPath){Get-Content -LiteralPath $ReceiptPath -Raw|ConvertFrom-Json}else{$null}
if($Action -eq 'Status'){
    [pscustomobject]@{state=if($receipt){$receipt.state}else{'absent'};files=15;receipt=$ReceiptPath;instruction='Developer-only RC streaming trial. Keep launcher closed; close the game before Enable/Disable. Disable restores exact originals.'};return
}
if(Get-Process helldivers2,Clonedivers -ErrorAction SilentlyContinue){throw 'Close game and launcher first'}
if(Test-Path -LiteralPath (Join-Path $env:APPDATA 'Clonedivers/apply-plan.json')){throw 'Finish pending launcher operation first'}
foreach($blocked in $BlockedReceiptPaths){
    CheckPath $blocked
    if(Test-Path -LiteralPath $blocked){$other=Get-Content -LiteralPath $blocked -Raw|ConvertFrom-Json;if($other.state -ne 'disabled'){throw 'Disable active geometry/control trial first'}}
}
if($Action -eq 'Disable'){
    if(-not $receipt -or $receipt.state -eq 'disabled'){Write-Output 'No active RC streaming trial.';return}
    ValidateReceipt $receipt
    $targets=@(foreach($file in @($receipt.files)){Locate $file $false})
    $receipt.state='disabling';Save $receipt
    foreach($target in $targets){if($target.hash -ne $target.file.sourceSha256){ReplaceExact (Join-Path $receipt.backupDirectory $target.file.name) $target $target.file.sourceSha256}}
    $receipt.state='disabled';Save $receipt
    Write-Output 'All fifteen exact RC originals restored. Backup and receipt retained.';return
}
foreach($path in @($BaselineManifest,$CandidateManifest,$CandidateDirectory)){CheckPath $path}
$baseline=Get-Content -LiteralPath $BaselineManifest -Raw|ConvertFrom-Json;$candidate=Get-Content -LiteralPath $CandidateManifest -Raw|ConvertFrom-Json
if($baseline.format -ne 2 -or $baseline.pack.version -ne '2026.09.12-r8' -or $candidate.format -ne 3 -or $candidate.pack.version -ne '2026.09.13-r9'){throw 'Trial requires the r8 baseline and r9 profile candidate'}
$files=@(foreach($name in $names){
    $source=SelectManifestFile $baseline $name 'full';$dest=SelectManifestFile $candidate $name 'lighter'
    [pscustomobject]@{name=$name;sourceSize=$source.size;sourceSha256=$source.sha256;size=$dest.size;sha256=$dest.sha256}
});ValidateFiles $files
foreach($file in $files){$path=Join-Path $CandidateDirectory $file.name;if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.size -or (Hash $path) -ne $file.sha256){throw 'Candidate differs from manifest'}}
if($receipt -and $receipt.state -ne 'disabled'){
    ValidateReceipt $receipt
    if($receipt.state -eq 'disabling' -or $receipt.candidateDirectory -ne $CandidateDirectory -or ($receipt.files|ConvertTo-Json -Depth 8 -Compress) -cne ($files|ConvertTo-Json -Depth 8 -Compress)){throw 'Finish Disable before changing candidate'}
    $targets=@(foreach($file in $files){Locate $file $false})
}else{
    $targets=@(foreach($file in $files){Locate $file $true})
    $backup=$ReceiptPath+'.backup-'+[guid]::NewGuid().ToString('N');CheckPath $backup;[IO.Directory]::CreateDirectory($backup)|Out-Null
    foreach($target in $targets){$path=Join-Path $backup $target.file.name;CopyDurable $target.path $path;if((Hash $path) -ne $target.file.sourceSha256){throw 'Source changed during backup; no game files modified'}}
    $receipt=[pscustomobject]@{schemaVersion=1;trial='rc-streaming-fifteen';sourcePack=$baseline.pack.version;candidatePack=$candidate.pack.version;state='prepared';gameDirectory=$GameDirectory;candidateDirectory=$CandidateDirectory;backupDirectory=$backup;files=$files;createdUtc=[DateTime]::UtcNow.ToString('o')}
    Save $receipt
}
foreach($target in $targets){if($target.hash -ne $target.file.sha256){ReplaceExact (Join-Path $CandidateDirectory $target.file.name) $target $target.file.sha256}}
$receipt.state='enabled';Save $receipt
Write-Output 'Fifteen-file RC streaming trial enabled. Keep launcher closed; Disable after the runtime check.'
