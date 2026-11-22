param([Parameter(Mandatory=$true)][string]$HellForgeRoot)
$ErrorActionPreference='Stop'
$compiler=Join-Path $HellForgeRoot 'tools/zig/zig.exe'
$sdk=Join-Path $HellForgeRoot 'app/sdk'
$cache=Join-Path $PSScriptRoot '.build-cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$env:ZIG_GLOBAL_CACHE_DIR=Join-Path $cache 'global'
$env:ZIG_LOCAL_CACHE_DIR=Join-Path $cache 'local'
$flags=@('-target','x86_64-windows-gnu','-std=c11','-Wall','-Wextra','-Werror','-Wno-unused-function','-I',$sdk,'-I',(Join-Path $PSScriptRoot 'src'))
$sources=@('corpse_cleanup','c6_spike','c6_guard','c6_sha256','c6_mapping','c6_client','c6_trace','mom_adapter') | ForEach-Object {Join-Path $PSScriptRoot ('src/'+$_+'.c')}
& $compiler cc @flags -O2 -s -shared @sources -o (Join-Path $PSScriptRoot 'corpse_cleanup.dll')
if ($LASTEXITCODE -ne 0) {throw 'Corpse cleanup compilation failed.'}
Write-Output 'Built experimental Faster Corpse Cleanup 1.0.6-alpha.5 (SDK 15); not executed.'
