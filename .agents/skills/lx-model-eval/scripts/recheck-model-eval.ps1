param(
    [Parameter(Mandatory=$true)][string]$RunId,
    [Parameter(Mandatory=$true)][string[]]$CaseId
)
# Regrade retained evidence after grader repairs; never calls a model or edits original results.
$ErrorActionPreference='Stop'
$repoRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
. (Join-Path $PSScriptRoot 'eval-contract.ps1')
if($RunId -notmatch '^\d{8}-\d{6}-\d{3}$'){throw 'Invalid run ID.'}
$run=Join-Path $repoRoot ".lx/model-evals/$RunId"
$inputs=Join-Path $run 'inputs'
if(-not (Test-Path -LiteralPath $inputs -PathType Container)){throw 'Recheck requires a frozen input snapshot.'}
$schema=Read-EvalContract (Join-Path $inputs '.agents/skills/lx-model-eval/evals/evals.json')
$manifest=Get-Content -LiteralPath (Join-Path $run 'source-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$preflight=Get-Content -LiteralPath (Join-Path $run 'preflight.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$utf8=[System.Text.UTF8Encoding]::new($false)
$hostExe=(Get-Process -Id $PID).Path
$env:LX_GODOT=(Get-Content -LiteralPath (Join-Path $repoRoot 'godot_project/.lx/doctor.json') -Raw -Encoding UTF8 | ConvertFrom-Json).checks.godotDotnet
$env:LX_LUBAN_DLL=(Get-Content -LiteralPath (Join-Path $repoRoot 'godot_project/.lx/luban/report.json') -Raw -Encoding UTF8 | ConvertFrom-Json).toolAssembly
function Invoke-Logged([string[]]$Arguments,[string]$Log) {
    $saved=$ErrorActionPreference
    $ErrorActionPreference='Continue'
    try{$lines=@(& $hostExe -NoProfile -ExecutionPolicy Bypass @Arguments 2>&1);$code=$LASTEXITCODE}
    finally{$ErrorActionPreference=$saved}
    [System.IO.File]::WriteAllText($Log,(($lines|ForEach-Object {$_.ToString()}) -join "`n"),$utf8)
    return $code
}
$allPassed=$true
foreach($id in $CaseId){
    $case=@($schema.cases|Where-Object id -eq $id)
    if($case.Count -ne 1){throw "Unknown case '$id'."}
    $case=$case[0]
    $candidates=@(Get-ChildItem -LiteralPath (Join-Path $run 'artifacts') -Directory | Where-Object {$_.Name.EndsWith("-$id") -and (Test-Path -LiteralPath (Join-Path $_.FullName 'result.json'))})
    if($candidates.Count -ne 1){throw "Expected one completed result for '$id'."}
    $artifact=$candidates[0].FullName
    $original=Get-Content -LiteralPath (Join-Path $artifact 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $answer=Get-Content -LiteralPath (Join-Path $artifact 'last-message.txt') -Raw -Encoding UTF8
    $failures=[System.Collections.Generic.List[string]]::new()
    foreach($failure in $original.failures){
        if($failure -notlike 'Final response is missing *' -and
            $failure -notlike 'Independent changed-path check failed *' -and
            $failure -notlike 'Deterministic final validation failed *' -and
            $failure -notlike "Independent outcome '*"){ $failures.Add($failure) }
    }
    foreach($failure in @(Get-EvalResponseFailures $case $answer)){$failures.Add($failure)}
    $out=Join-Path $artifact ('recheck-'+(Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    $replayRoot=Join-Path $repoRoot '.lx/model-evals/_replays'
    $fixture=Join-Path $replayRoot ([guid]::NewGuid().ToString('N').Substring(0,10))
    $checkExit=$null; $validateExit=$null; $outcomeExit=$null
    try {
        if($case.expected_write){
            foreach($entry in $manifest.PSObject.Properties){
                $source=Join-Path $inputs $entry.Name
                if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Value){throw "Frozen input hash mismatch: $($entry.Name)"}
                $target=Join-Path $fixture $entry.Name
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $source -Destination $target -Force
            }
            $game=Get-Content -LiteralPath (Join-Path $fixture 'godot_project/content/game/game-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            if($game.name){throw 'Replay currently requires the clean framework input used by this acceptance suite.'}
            if($case.setup -eq 'game'){
                $setupExit=Invoke-Logged @('-File',(Join-Path $fixture 'lx.ps1'),'create','game','EvalBase') (Join-Path $out 'setup.log')
                if($setupExit -ne 0){throw 'Replay setup failed.'}
            }
            $changes=Join-Path $artifact 'changed-files'
            $changedPaths=@(Get-ChildItem -LiteralPath $changes -File -Recurse | ForEach-Object {
                $relative=$_.FullName.Substring($changes.Length+1).Replace('\','/')
                $target=Join-Path $fixture $relative
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $target -Force
                $relative
            })
            foreach($expected in @($case.expected_files)){
                if($expected -and -not (Test-Path -LiteralPath (Join-Path $fixture $expected) -PathType Leaf)){throw "Missing expected file: $expected"}
            }
            if($case.outcome){
                $outcomeExit=Invoke-Logged @('-File',(Join-Path $inputs '.agents/skills/lx-model-eval/scripts/test-outcome.ps1'),'-Outcome',$case.outcome,'-Fixture',$fixture,'-OutputDirectory',(Join-Path $out 'oracle')) (Join-Path $out 'outcome.log')
                if($outcomeExit -ne 0){$failures.Add('Independent outcome failed during replay.')}
            }
            $paths=@(Get-EvalCheckPaths $changedPaths)
            if($paths.Count){
                $checkExit=Invoke-Logged (@('-File',(Join-Path $fixture 'lx.ps1'),'check')+$paths) (Join-Path $out 'changed-path-check.log')
                if($checkExit -ne 0){$failures.Add('Independent changed-path check failed during replay.')}
            }
            if($case.validate){
                $validateExit=Invoke-Logged @('-File',(Join-Path $fixture 'lx.ps1'),'validate') (Join-Path $out 'validation.log')
                if($validateExit -ne 0){$failures.Add('Final validation failed during replay.')}
            }
        }
    } catch { $failures.Add($_.Exception.Message) }
    finally {
        $resolved=[System.IO.Path]::GetFullPath($fixture)
        $allowed=[System.IO.Path]::GetFullPath($replayRoot).TrimEnd('\')+'\'
        if(-not $resolved.StartsWith($allowed,[System.StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe replay cleanup.'}
        if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
    }
    $receipt=[ordered]@{
        schema_version=2;case=$id;profile=$original.profile;model=$original.model;reasoning=$original.reasoning
        passed=($failures.Count -eq 0);failures=@($failures);model_calls=$false
        source_hash=$preflight.source_hash;original_result=(Join-Path $artifact 'result.json')
        original_failures=@($original.failures);check_exit=$checkExit;validation_exit=$validateExit;outcome_exit=$outcomeExit
        grader_hash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'eval-contract.ps1') -Algorithm SHA256).Hash
        evidence_path=$out
    }
    $json=$receipt|ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText((Join-Path $out 'receipt.json'),$json,$utf8)
    [System.IO.File]::WriteAllText((Join-Path $artifact 'latest-recheck.json'),$json,$utf8)
    Write-Host "Recheck $id passed=$($receipt.passed); no model calls; $out"
    foreach($failure in $failures){Write-Host "  $failure"}
    if(-not $receipt.passed){$allPassed=$false}
}
if(-not $allPassed){exit 1}
exit 0
