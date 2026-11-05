param(
    [Parameter(Mandatory=$true)][string]$Manifest,
    [Parameter(Mandatory=$true)][string]$ArtifactFolder,
    [switch]$RequirePublishable,
    [switch]$RequirePlayerReady
)
$ErrorActionPreference='Stop'
function Need([bool]$condition,[string]$reason){if(-not $condition){throw $reason}}
$m=Get-Content -LiteralPath $Manifest -Raw|ConvertFrom-Json
Need ($m.Format -eq 1 -and $m.Channel -ceq 'forge-preview-build25480438') 'Unexpected Forge channel/schema.'
Need ($m.GameBuild -ceq '25480438' -and $m.Engine -ceq '0.1.41' -and $m.HellForge -ceq '0.3.0-beta' -and $m.Sdk -eq 15) 'Pinned engine/game compatibility changed.'
Need ($m.SandboxRequired -eq $true -and $m.IsolationRequired -eq $true -and $m.LegacyFeedsUnchanged -eq $true -and $m.UpdateMode -ceq 'manual-only') 'Isolation or separate update-channel policy changed.'
foreach($flag in @('SandboxRequired','IsolationRequired','LegacyFeedsUnchanged')){Need ($m.$flag -is [bool]) ('Policy field must be boolean: '+$flag)}
Need ($m.TagProposal -cmatch '^forge-preview-[0-9]{4}-[0-9]{2}-[0-9]{2}$') 'Invalid proposed release tag.'
$distribution=@('AuthorAcquisitionReady','PrivateAdaptationPermissionReady','NoPrivateDataInArchives','ExplicitPublicationApproval')
$runtime=@('WindowsMissionPersistence','MarkerOffCombat','LinuxRuntimeAndAccount','SeparatePcIntendedSquad')
foreach($group in @(@{Object=$m.DistributionGates;Names=$distribution},@{Object=$m.RuntimeAcceptance;Names=$runtime})){
    foreach($key in $group.Names){Need ($group.Object.PSObject.Properties.Name -ccontains $key) ('Missing acceptance field: '+$key);Need ($group.Object.$key -is [bool]) ('Acceptance field must be boolean: '+$key)}
}
if($RequirePublishable -or $RequirePlayerReady){foreach($key in $distribution){Need ($m.DistributionGates.$key -eq $true) ('Publication blocked: '+$key)}}
if($RequirePlayerReady){foreach($key in $runtime){Need ($m.RuntimeAcceptance.$key -eq $true) ('Player-ready qualification missing: '+$key)}}
Need ($m.Artifacts.Count -eq 2 -and @($m.Artifacts.Platform|Sort-Object -Unique).Count -eq 2) 'Exactly one Windows and Linux artifact required.'
$folder=[IO.Path]::GetFullPath($ArtifactFolder)
foreach($entry in $m.Artifacts){
    Need ($entry.Platform -cin @('win-x64','linux-x64')) 'Unsupported platform.'
    Need ($entry.Name -cmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.(zip|tar\.gz)$') 'Unsafe artifact filename.'
    Need (($entry.Platform -ceq 'win-x64' -and $entry.Name.EndsWith('.zip')) -or ($entry.Platform -ceq 'linux-x64' -and $entry.Name.EndsWith('.tar.gz'))) 'Wrong artifact type for platform.'
    $expected='https://github.com/owendavidgoode/clonedivers/releases/download/'+$m.TagProposal+'/'+$entry.Name
    Need ($entry.UrlProposal -ceq $expected) 'Artifact proposal must target this exact repository/tag/filename.'
    Need ($entry.Sha256 -cmatch '^[0-9a-f]{64}$' -and $entry.Bytes -gt 0) 'Invalid artifact checksum/size.'
    $file=Join-Path $folder $entry.Name
    for($part=[IO.Path]::GetFullPath($file);$part;$part=[IO.Path]::GetDirectoryName($part)){
        if((Test-Path -LiteralPath $part)-and ((Get-Item -LiteralPath $part -Force).Attributes-band [IO.FileAttributes]::ReparsePoint)){throw 'Linked artifact paths refused.'}
    }
    Need ((Get-Item -LiteralPath $file).Length -eq $entry.Bytes) ('Artifact size mismatch: '+$entry.Name)
    Need ((Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() -ceq $entry.Sha256) ('Artifact hash mismatch: '+$entry.Name)
}
[pscustomobject]@{Channel=$m.Channel;ArtifactsVerified=2;Publishable=@($distribution|Where-Object {-not $m.DistributionGates.$_}).Count -eq 0;RuntimeQualified=@($runtime|Where-Object {-not $m.RuntimeAcceptance.$_}).Count -eq 0;Published=$false;Mode='Read-only validation; no release/feed/network mutation'}
