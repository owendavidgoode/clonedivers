# Exercises the real detached argument builder with a stub launcher; no process or GitHub operation is started.
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'publish-pack.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors.Message -join '; ') }
$detach = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -eq '$Detach' }, $true)
if (-not $detach) { throw 'Detached launch block not found' }
$body = $detach.Clauses[0].Item2.Extent.Text
$launch = [scriptblock]::Create($body.Substring(1, $body.Length - 2))
function Start-Process {
    param($FilePath, $ArgumentList, $WindowStyle, [switch]$PassThru, $RedirectStandardOutput, $RedirectStandardError)
    $script:captured = @($ArgumentList)
    throw 'qa-launch-captured'
}
$Version = 'qa-only'; $Repo = 'qa/unused'; $MaxAssetsPerRelease = 900; $BatchSize = 40; $Status = 'ok'
$Notes = ''; $StatusNotes = ''; $GameDir = ''; $VerifyZips = ''; $AllowPendingGameUpdate = $false
$NoPush = $true; $dist = $PSScriptRoot
foreach ($PlanOnly in @($true, $false)) {
    $script:captured = $null
    try { & $launch } catch { if ($_.Exception.Message -ne 'qa-launch-captured') { throw } }
    if (-not $script:captured) { throw 'Process launcher was not reached' }
    if (($script:captured -contains '-PlanOnly') -ne $PlanOnly) { throw 'PlanOnly was not preserved' }
    if ($script:captured -notcontains '-NoPush') { throw 'NoPush was not preserved' }
    Write-Host "PASS: detached PlanOnly=$PlanOnly preserves the requested publication flags"
}
