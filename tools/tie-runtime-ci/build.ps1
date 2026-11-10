param([switch]$Run)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if(-not $Run){Write-Output 'HELD: artifact-only hosted build; explicit -Run required.';return}
if($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -ne 'owendavidgoode/clonedivers' -or
   $env:GITHUB_REF -ne 'refs/heads/codex/tie-runtime-ci-20261110'){throw 'Wrong hosted scope'}
$root=[IO.Path]::GetFullPath($env:GITHUB_WORKSPACE)
$tree=Join-Path $root 'hellforge-build'
$publish=Join-Path $root 'artifact-publish'
$private=Join-Path $root 'build-inputs'
$cache=Join-Path $root 'build-cache'
$commit='d8a8b46ea830d0a3b8811199180563a803a79bbb'
function Need($condition,[string]$message){if(-not $condition){throw $message}}
function Pin([string]$path,[string]$expected,[string]$algorithm='SHA256'){
    Need ((Get-FileHash -LiteralPath $path -Algorithm $algorithm).Hash -eq $expected) "Input pin differs: $path"
}
function Owned([string]$path){
    $resolved=[IO.Path]::GetFullPath($path)
    Need ($resolved.StartsWith($tree+'\',[StringComparison]::OrdinalIgnoreCase)) 'Cleanup escaped dedicated checkout'
    if(Test-Path -LiteralPath $resolved){
        Need (-not ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'Cleanup reparse target'
    }
    return $resolved
}
Need ([IO.Path]::GetFullPath($tree) -eq "$root\hellforge-build") 'Unexpected checkout location'
Need (-not ((Get-Item -LiteralPath $tree).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'Checkout is reparse point'
Need ((git -C $tree rev-parse HEAD) -eq $commit) 'Wrong upstream commit'
Need (-not (git -C $tree status --porcelain)) 'Baseline checkout is dirty'
Need (-not (Test-Path -LiteralPath $publish)) 'Output already consumed'
New-Item -ItemType Directory -Path $publish,$private,$cache | Out-Null
$patch=Join-Path $PSScriptRoot 'eagle-base.patch'
Pin $patch 'af87df3dc045d24dae640566bb34597ae1fa6cc6030988a38aee147f11c50ddb'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
Need (Test-Path -LiteralPath $vswhere) 'Hosted VS discovery unavailable'
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json
Need ($LASTEXITCODE -eq 0 -and @($vs|ConvertFrom-Json).Count -gt 0) 'Hosted C++ toolchain unavailable'
$vs | Set-Content -LiteralPath (Join-Path $publish 'hosted-vs.json') -Encoding utf8
@{source=$commit;runnerImage=$env:ImageOS;runnerVersion=$env:ImageVersion;run=$env:GITHUB_RUN_ID;attempt=$env:GITHUB_RUN_ATTEMPT;complete=$false}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $publish 'job.json') -Encoding utf8
$sdkZip=Join-Path $private 'dotnet.zip'
Invoke-WebRequest 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-win-x64.zip' -OutFile $sdkZip
Pin $sdkZip '7a114c318fa7f1d9822991b534b2814d0e78386ceda816b371f5141acefe741d87ccb606ede06cadac8f9ac5070e32b708a67bbd1619ff5e5a7858a27874ef48' 'SHA512'
$dotnet=Join-Path $cache 'dotnet'
[IO.Compression.ZipFile]::ExtractToDirectory($sdkZip,$dotnet)
$env:PATH=$dotnet+';'+$env:PATH
$env:DOTNET_ROOT=$dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
foreach($pair in @(@('DOTNET_CLI_HOME','dotnet-home'),@('NUGET_PACKAGES','nuget'),@('NUGET_HTTP_CACHE_PATH','nuget-http'),@('ZIG_GLOBAL_CACHE_DIR','zig-global'),@('ZIG_LOCAL_CACHE_DIR','zig-local'),@('TEMP','temp'),@('TMP','temp'))){
    $value=Join-Path $cache $pair[1]
    New-Item -ItemType Directory -Path $value -Force | Out-Null
    [Environment]::SetEnvironmentVariable($pair[0],$value,'Process')
}
dotnet --info | Set-Content -LiteralPath (Join-Path $publish 'dotnet-info.txt') -Encoding utf8
Need ($LASTEXITCODE -eq 0 -and (dotnet --version) -eq '10.0.403') 'Unexpected SDK'
$base='https://raw.githubusercontent.com/rlabrecque/SteamworksSDK/1263fbb403ca89f11cdf33860b565538e0836f6c'
$steam=Join-Path $private 'steam_api64.dll'
$steamSdk=Join-Path $private 'steam_api.json'
Invoke-WebRequest "$base/redistributable_bin/win64/steam_api64.dll" -OutFile $steam
Invoke-WebRequest "$base/public/steam/steam_api.json" -OutFile $steamSdk
Pin $steam '1db3fd414039d3e5815a5721925dd2e0a3a9f2549603c6cab7c49b84966a1af3'
Pin $steamSdk '80a6c2196f6048749ee35a141c91083b29613fd7c56c2207c9fc73ef87566547'
foreach($variant in @('baseline','patched')){
    if($variant -eq 'patched'){
        # Clean only generated directories in this dedicated public checkout.
        $generated=@((Join-Path $tree 'artifacts'))
        foreach($parent in @('src','tests','examples')){
            $generated+=@(Get-ChildItem -LiteralPath (Join-Path $tree $parent) -Directory -Recurse -Force |
                Where-Object {$_.Name -in @('bin','obj')} | Select-Object -ExpandProperty FullName)
        }
        foreach($dir in $generated|Sort-Object Length -Descending -Unique){
            $target=Owned $dir
            if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force}
        }
        Need (-not (git -C $tree status --porcelain)) 'Baseline build modified tracked files'
        git -C $tree apply --check $patch
        Need ($LASTEXITCODE -eq 0) 'Patch check failed'
        git -C $tree apply $patch
        Need ($LASTEXITCODE -eq 0) 'Patch apply failed'
        $changed=@(git -C $tree diff --name-only)|Sort-Object
        Need (($changed -join '|') -eq 'mappings/references.json|native/mod-loader/reference_data.h|scripts/ExportReferences.py') 'Unexpected source delta'
    }
    foreach($stage in @('test','engine','package')){
        $stdout=Join-Path $publish "$variant-$stage.stdout.log"
        $stderr=Join-Path $publish "$variant-$stage.stderr.log"
        $args=@('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $PSScriptRoot 'run-official.ps1')+'"'),'-Tree',('"'+$tree+'"'),'-Stage',$stage,'-SteamApi',('"'+$steam+'"'),'-SteamSdk',('"'+$steamSdk+'"'))
        $process=Start-Process -FilePath 'powershell.exe' -ArgumentList $args -WindowStyle Hidden -PassThru -Wait `
            -WorkingDirectory $tree -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        Need ($process.ExitCode -eq 0) "Official $variant/$stage failed: $($process.ExitCode)"
    }
    uv run --no-project --python 3.13 python -B (Join-Path $PSScriptRoot 'verify.py') --tree $tree --variant $variant --output $publish
    Need ($LASTEXITCODE -eq 0) 'Artifact verification failed'
}
uv run --no-project --python 3.13 python -B (Join-Path $PSScriptRoot 'verify.py') --compare --output $publish
Need ($LASTEXITCODE -eq 0) 'Pair verification failed'
Write-Output 'Complete held baseline/patched artifacts; no release or installation.'
