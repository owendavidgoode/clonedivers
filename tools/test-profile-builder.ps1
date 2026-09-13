$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'deployment-contract.ps1')
$root=Join-Path $PSScriptRoot '../dist/profile-builder-tests'
$work=Join-Path $root ([guid]::NewGuid().ToString('N'))
foreach($folder in 'full','lighter','reduced'){New-Item -ItemType Directory -Path (Join-Path $work $folder) -Force|Out-Null}
foreach($profile in 'full','lighter','reduced'){
    [IO.File]::WriteAllText((Join-Path $work "$profile/9ba626afa44a3aa3.patch_0"),$(if($profile -eq 'full'){'base'}else{'alternate'}))
    [IO.File]::WriteAllText((Join-Path $work "$profile/9ba626afa44a3aa3.patch_1"),"RC $profile")
}
$base=Get-PatchInventory (Join-Path $work full)
$manifest=[ordered]@{format=2;pack=[ordered]@{version='fixture';options=@([ordered]@{id='commandos';name='Commandodivers';default=$true});files=@($base|ForEach-Object{[ordered]@{name=$_.name;size=$_.size;sha256=$_.sha256;url="https://example.com/$($_.sha256)";option=$(if($_.name -match 'patch_1$'){'commandos'}else{$null})}})}}
Write-ContractJson (Join-Path $work 'base.json') $manifest
$spec=@(foreach($profile in 'lighter','reduced'){
    Write-VariantReceipt (Join-Path $work $profile) $base 'test fixture'
    [ordered]@{id=$profile;name=$profile;description='fixture';directory=(Join-Path $work $profile)}
})
Write-ContractJson (Join-Path $work 'profiles.json') $spec
$argsMap=@{BaseManifest=(Join-Path $work base.json);BaseDirectory=(Join-Path $work full);ProfilesPath=(Join-Path $work profiles.json);Version='fixture3';OutPath=(Join-Path $work manifest.json);UploadPlanPath=(Join-Path $work uploads.json)}
& (Join-Path $PSScriptRoot 'build-profile-manifest.ps1') @argsMap
$actual=Get-Content $argsMap.OutPath -Raw|ConvertFrom-Json
if($actual.format -ne 3 -or @($actual.pack.textureProfiles).Count -ne 3){throw 'Missing catalog'}
$shared=@($actual.pack.files|Where-Object {$_.name -match 'patch_0$' -and @($_.textureProfiles).Count -eq 2})
if($shared.Count -ne 1 -or $shared[0].textureProfiles -notcontains 'lighter' -or $shared[0].textureProfiles -notcontains 'reduced'){throw 'Identical variant hashes not aggregated'}
$rc=@($actual.pack.files|Where-Object name -match 'patch_1$')
if($rc.Count -ne 3 -or @($rc|Where-Object {@($_.modes).Count -ne 1 -or $_.modes[0] -ne 'commandos'}).Count){throw 'RC leaked from independent profile gating'}
[IO.File]::AppendAllText((Join-Path $work 'reduced/9ba626afa44a3aa3.patch_1'),'tampered')
$argsMap.OutPath=Join-Path $work tampered.json;$argsMap.UploadPlanPath=Join-Path $work tampered-uploads.json
$rejected=$false
try{& (Join-Path $PSScriptRoot 'build-profile-manifest.ps1') @argsMap}catch{if($_ -notmatch 'changed after verification'){throw};$rejected=$true}
if(!$rejected){throw 'Tampered profile accepted'}
Write-Host "PASS catalog, shared profile aggregation, transformed RC mode isolation, receipt tamper rejection. Native fixture: $(Join-Path $work manifest.json)"
