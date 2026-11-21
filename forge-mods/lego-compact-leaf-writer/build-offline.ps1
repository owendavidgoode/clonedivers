param([string]$Cli='D:\HellForge\app\cli\HellForge.Cli.exe',[string]$Zig='D:\HellForge\tools\zig',
 [string]$RetainedRun='D:\ClaudeWork\clonedivers-lab\lego-skinning-invocation-evidence-astra\runs\skinning-invocation-20261110-101531-101608',[switch]$QueueOnly)
$ErrorActionPreference='Stop'
$workspace=Join-Path $PSScriptRoot 'workspace'
$stage=Join-Path $workspace 'mods\lego-compact-leaf-writer'
$sdk=Join-Path $workspace 'app\sdk'
$tools=Join-Path $workspace 'tools'
$out=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $stage,$sdk,$tools,$out | Out-Null
$SdkSource = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'sdk')) { Join-Path $PSScriptRoot 'sdk' } else { Join-Path (Split-Path (Split-Path $Cli -Parent) -Parent) 'sdk' }
Copy-Item -Path (Join-Path $SdkSource '*.h') -Destination $sdk -Force
$compiler=Join-Path $tools 'zig'
if(!(Test-Path -LiteralPath $compiler)){New-Item -ItemType Junction -Path $compiler -Target $Zig | Out-Null}
if(@((Get-Item -LiteralPath $compiler).Target)[0] -ne $Zig){throw 'Compiler junction differs'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src'),(Join-Path $PSScriptRoot 'tests') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'hellforge-mod.json'),(Join-Path $PSScriptRoot 'README.md') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PROTOCOL.md'),(Join-Path $PSScriptRoot 'FAULT-PROTOCOL.md') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SAMPLER-PROTOCOL.md') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QUEUE-POLICY.md') -Destination $stage -Force
$garmentJson = Join-Path $PSScriptRoot 'lego-garments.json'
if (Test-Path -LiteralPath $garmentJson) { Copy-Item -LiteralPath $garmentJson -Destination $stage -Force }
$oldGlobal=$env:ZIG_GLOBAL_CACHE_DIR; $oldLocal=$env:ZIG_LOCAL_CACHE_DIR
try {
 $env:ZIG_GLOBAL_CACHE_DIR=Join-Path $PSScriptRoot 'build\zig-global'
 $env:ZIG_LOCAL_CACHE_DIR=Join-Path $PSScriptRoot 'build\zig-local'
 if(-not $QueueOnly) {
  & $Cli build-mod --mod $stage | Tee-Object -FilePath (Join-Path $PSScriptRoot 'build\build.log')
  if($LASTEXITCODE -ne 0){throw 'Official build failed'}
 }
 foreach($fixture in @('leaf_test','leaf_integration_test','sampler_integration_test','queue_integration_test','coop_writer_test')) {
  if($QueueOnly -and $fixture -ne 'queue_integration_test'){continue}
  $sources=@((Join-Path $PSScriptRoot ('tests\'+$fixture+'.c')),(Join-Path $PSScriptRoot 'src\writer_core.c'),(Join-Path $PSScriptRoot 'src\shadow_math.c'),(Join-Path $PSScriptRoot 'src\compact_math.c'),(Join-Path $PSScriptRoot 'src\pinned_data.c'),(Join-Path $PSScriptRoot 'src\coop_avatar_registry.c'))
  $sources+=Join-Path $PSScriptRoot 'src\leaf_plan.c'
  if($fixture -eq 'leaf_test'){$sources+=Join-Path $PSScriptRoot 'src\envelope.c'}
  $binary=Join-Path $PSScriptRoot ('build\'+$fixture+'.exe')
  & "$Zig\zig.exe" cc -std=c11 -DGW_TEST_FATAL_CAS_PAUSE -target x86_64-windows-gnu -O2 -ffp-contract=off -fno-fast-math -Wall -Werror -Wno-unused-parameter -Wno-unused-function -I $sdk @sources -o $binary
  if($LASTEXITCODE -ne 0){throw "Fixture compile failed: $fixture"}
  if($fixture -eq 'leaf_test') {
   & $binary $RetainedRun | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build\leaf-test.log')
  } else {
   $fixtureDirectory=Join-Path $PSScriptRoot ('build\'+$fixture+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))
   New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
   if($fixture -eq 'queue_integration_test') {
    $faultInput = Join-Path $PSScriptRoot 'inputs/133609-leaf-fault-first.bin'
    if (!(Test-Path -LiteralPath $faultInput)) { Write-Host "SKIP queue_integration_test (missing optional $faultInput)"; continue }
    & $binary $RetainedRun $fixtureDirectory $faultInput | Set-Content -LiteralPath (Join-Path $fixtureDirectory 'test.log')
   } else {
    & $binary $RetainedRun $fixtureDirectory | Set-Content -LiteralPath (Join-Path $fixtureDirectory 'test.log')
   }
   @{directory=$fixtureDirectory;exit=$LASTEXITCODE}|ConvertTo-Json|Set-Content (Join-Path $PSScriptRoot ('build\'+$fixture+'-location.json'))
  }
  if($LASTEXITCODE -ne 0){throw "Fixture failed: $fixture"}
 }
 & $Cli package-mod --mod $stage --out $out | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'build\package.log')
 if($LASTEXITCODE -ne 0){throw 'Package failed'}
 $zipPath=Join-Path $out 'lego-compact-leaf-writer-0.2.0.zip'
 $zipItem=Get-Item -LiteralPath $zipPath
 $zipSha=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
 $folderHash=($(& $Cli mod-hash --mod $stage) | ConvertFrom-Json).hash
 Write-Host "ZIP=$zipPath"
 Write-Host "Bytes=$($zipItem.Length)"
 Write-Host "Sha256=$zipSha"
 Write-Host "ContentHash=$folderHash"
} finally {$env:ZIG_GLOBAL_CACHE_DIR=$oldGlobal; $env:ZIG_LOCAL_CACHE_DIR=$oldLocal}
