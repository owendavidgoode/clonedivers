# Private sparse archive replacements. Keep the launcher closed until Disable.
param(
    [ValidateSet('Status','Enable','Disable')][string]$Action='Status',
    [ValidateSet('shadows','balanced')][string]$Variant='shadows',
    [ValidateSet('full','lighter')][string]$Profile='full',
    [string]$GameDirectory='C:/Program Files (x86)/Steam/steamapps/common/Helldivers 2',
    [string]$CandidateRoot=(Join-Path $PSScriptRoot '../dist/geometry-candidates/layout-preserved'),
    [string]$ReceiptPath=(Join-Path $PSScriptRoot '../dist/geometry-candidates/install.json'),
    [string]$ControlReceiptPath=(Join-Path $PSScriptRoot '../dist/investigation-2026-09-13/geometry/control-install.json')
)
$ErrorActionPreference='Stop'
$GameDirectory=[IO.Path]::GetFullPath($GameDirectory).TrimEnd('\','/')
$CandidateRoot=[IO.Path]::GetFullPath($CandidateRoot)
$ReceiptPath=[IO.Path]::GetFullPath($ReceiptPath)
$ControlReceiptPath=[IO.Path]::GetFullPath($ControlReceiptPath)
$data=Join-Path $GameDirectory 'data';$off=Join-Path $GameDirectory 'mods_off'
function CheckPath([string]$path) {
    $current=$path
    while($current){
        if(Test-Path -LiteralPath $current){
            # OneDrive placeholders are also reparse points; reject actual path redirection only.
            if((Get-Item -LiteralPath $current -Force).LinkType -in @('Junction','SymbolicLink')){throw 'Linked paths unsupported'}
        }
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
    if(-not ('GeometryTrialNative' -as [type])){
        Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class GeometryTrialNative { [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool MoveFileEx(string source, string target, int flags); }'
    }
    # Same-directory rename, REPLACE_EXISTING | WRITE_THROUGH; no copy/delete fallback.
    if([IO.Path]::GetDirectoryName($source) -ne [IO.Path]::GetDirectoryName($destination)){throw 'Atomic replacement requires the same directory'}
    if(-not [GeometryTrialNative]::MoveFileEx($source,$destination,9)){
        throw (New-Object ComponentModel.Win32Exception([Runtime.InteropServices.Marshal]::GetLastWin32Error()))
    }
}
function Save($value){
    CheckPath $ReceiptPath
    [IO.Directory]::CreateDirectory((Split-Path -Parent $ReceiptPath))|Out-Null
    $temp=$ReceiptPath+'.tmp-'+[guid]::NewGuid().ToString('N')
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($value|ConvertTo-Json -Depth 12))
    $stream=New-Object IO.FileStream($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::WriteThrough)
    try {$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)} finally {$stream.Dispose()}
    AtomicMove $temp $ReceiptPath
}
function ValidateFiles($files){
    if($files.Count -lt 1 -or $files.Count -gt 64 -or @($files.name|Select-Object -Unique).Count -ne $files.Count){throw 'Invalid replacement file list'}
    foreach($file in $files){
        if($file.name -cnotmatch '^9ba626afa44a3aa3\.patch_\d+(\.gpu_resources)?$' -or
           $file.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $file.sourceSha256 -cnotmatch '^[a-f0-9]{64}$' -or
           [string]$file.size -notmatch '^\d+$' -or [decimal]$file.size -gt [long]::MaxValue){throw 'Invalid replacement filename, size, or hash'}
    }
}
function ValidateReceipt($value){
    if($value.schemaVersion -ne 2 -or $value.packaging -ne 'preserved-source-layout'){throw 'Legacy or invalid geometry receipt: restore that trial with its original recovery procedure first'}
    if($value.gameDirectory -ne $GameDirectory -or $value.profile -notin @('full','lighter') -or $value.variant -notin @('shadows','balanced') -or
       $value.state -notin @('prepared','enabled','disabling','disabled') -or $value.pack -ne '2026.09.12-r8'){throw 'Receipt belongs to a different game or trial'}
    ValidateFiles @($value.files)
    $backup=[IO.Path]::GetFullPath([string]$value.backupDirectory)
    $prefix=[IO.Path]::GetFileName($ReceiptPath)+'.backup-'
    if([IO.Path]::GetDirectoryName($backup) -ne (Split-Path -Parent $ReceiptPath) -or
       [IO.Path]::GetFileName($backup) -notmatch ('^'+[regex]::Escape($prefix)+'[a-f0-9]{32}$')){throw 'Invalid backup directory in receipt'}
    CheckPath $backup
    foreach($file in @($value.files)){
        $path=Join-Path $backup $file.name
        if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.size -or (Hash $path) -ne $file.sourceSha256){throw 'Backup missing or changed; no game files modified'}
    }
}
function Locate($file,[bool]$fresh){
    $locations=@(foreach($folder in @($data,$off)){
        $path=Join-Path $folder $file.name;CheckPath $path
        if(Test-Path -LiteralPath $path){if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Target is not a regular file'};$path}
    })
    if($locations.Count -ne 1){throw "Missing or ambiguous replacement target: $($file.name)"}
    $path=$locations[0]
    if($fresh -and [IO.Path]::GetDirectoryName($path) -ne $data){throw 'Select the matching normal modded pack first; source files must be active in data'}
    $hash=Hash $path
    if((Get-Item -LiteralPath $path).Length -ne $file.size -or ($hash -ne $file.sourceSha256 -and ($fresh -or $hash -ne $file.sha256))){throw "Target differs from exact $Profile source/candidate bytes: $($file.name)"}
    [pscustomobject]@{path=$path;hash=$hash;file=$file}
}
function ReplaceExact([string]$source,$target,[string]$expected){
    # A complete flushed sibling replaces the existing file atomically, never a partial copy.
    $temp=$target.path+'.geometry-tmp-'+[guid]::NewGuid().ToString('N')
    try {
        CopyDurable $source $temp
        if((Hash $temp) -ne $expected){throw 'Staged replacement failed verification'}
        $current=Locate $target.file $false
        if($current.path -ne $target.path -or $current.hash -ne $target.hash){throw 'Target changed during operation; recovery receipt retained'}
        AtomicMove $temp $target.path
        if((Hash $target.path) -ne $expected){throw 'Replacement failed verification; recovery receipt retained'}
    } finally {if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
}
foreach($path in @($data,$off,$CandidateRoot,$ReceiptPath,$ControlReceiptPath)){CheckPath $path}
$receipt=if(Test-Path -LiteralPath $ReceiptPath){Get-Content -LiteralPath $ReceiptPath -Raw|ConvertFrom-Json}else{$null}
if($Action -eq 'Status'){
    [pscustomobject]@{state=if($receipt){$receipt.state}else{'absent'};profile=if($receipt){$receipt.profile}else{$null};variant=if($receipt){$receipt.variant}else{$null};instruction='Close game and launcher for changes. Disable before launcher use or changing profile/variant. Status changes no files.'};return
}
if(Get-Process helldivers2,Clonedivers -ErrorAction SilentlyContinue){throw 'Close game and launcher first'}
if(Test-Path -LiteralPath (Join-Path $env:APPDATA 'Clonedivers/apply-plan.json')){throw 'Finish pending launcher operation first'}
if($Action -eq 'Disable'){
    if(-not $receipt -or $receipt.state -eq 'disabled'){Write-Output 'No active geometry trial.';return}
    ValidateReceipt $receipt
    $targets=@(foreach($file in @($receipt.files)){Locate $file $false})
    $receipt.state='disabling';Save $receipt
    foreach($target in $targets){if($target.hash -ne $target.file.sourceSha256){ReplaceExact (Join-Path $receipt.backupDirectory $target.file.name) $target $target.file.sourceSha256}}
    $receipt.state='disabled';Save $receipt
    Write-Output 'Exact original archives restored. Backup and receipt retained.';return
}
if(Test-Path -LiteralPath $ControlReceiptPath){$control=Get-Content -LiteralPath $ControlReceiptPath -Raw|ConvertFrom-Json;if($control.state -ne 'disabled'){throw 'Disable the original-dropship control first'}}
if($receipt -and $receipt.state -ne 'disabled' -and ($receipt.profile -ne $Profile -or $receipt.variant -ne $Variant)){
    throw 'Finish Disable before changing trial or candidate'
}
$candidate=Join-Path (Join-Path $CandidateRoot $Profile) $Variant
$reportPath=Join-Path $candidate 'build-report.json';CheckPath $reportPath
$report=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
if($report.schemaVersion -ne 2 -or $report.packaging -ne 'preserved-source-layout' -or $report.profile -ne $Profile -or
   $report.variant -ne $Variant -or $report.pack -ne '2026.09.12-r8' -or @($report.resources).Count -ne 8){throw 'Unexpected geometry candidate report'}
$files=@($report.files);ValidateFiles $files
foreach($file in $files){
    $path=Join-Path $candidate $file.name;CheckPath $path
    if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.size -or (Hash $path) -ne $file.sha256){throw 'Candidate differs from build report'}
}
if($receipt -and $receipt.state -ne 'disabled'){
    ValidateReceipt $receipt
    if($receipt.state -eq 'disabling' -or $receipt.profile -ne $Profile -or $receipt.variant -ne $Variant -or $receipt.reportSha256 -ne (Hash $reportPath) -or $receipt.candidateDirectory -ne $candidate){throw 'Finish Disable before changing trial or candidate'}
    if(($receipt.files|ConvertTo-Json -Compress -Depth 5) -ne ($files|ConvertTo-Json -Compress -Depth 5)){throw 'Receipt and candidate file list differ'}
    $targets=@(foreach($file in $files){Locate $file $false})
}else{
    # Preflight every source before creating any backup or changing any game file.
    $targets=@(foreach($file in $files){Locate $file $true})
    $backup=$ReceiptPath+'.backup-'+[guid]::NewGuid().ToString('N');CheckPath $backup
    [IO.Directory]::CreateDirectory($backup)|Out-Null
    foreach($target in $targets){
        $path=Join-Path $backup $target.file.name;CopyDurable $target.path $path
        if((Hash $path) -ne $target.file.sourceSha256){throw 'Source changed while backing up; no game files modified'}
    }
    $receipt=[pscustomobject]@{schemaVersion=2;packaging='preserved-source-layout';pack=$report.pack;state='prepared';profile=$Profile;variant=$Variant;gameDirectory=$GameDirectory;candidateDirectory=$candidate;reportSha256=(Hash $reportPath);backupDirectory=$backup;files=$files;createdUtc=[DateTime]::UtcNow.ToString('o')}
    Save $receipt
}
foreach($target in $targets){if($target.hash -ne $target.file.sha256){ReplaceExact (Join-Path $candidate $target.file.name) $target $target.file.sha256}}
$receipt.state='enabled';Save $receipt
Write-Output "Private $Profile/$Variant trial enabled. Keep launcher closed until Disable; gameplay validation is still required."
