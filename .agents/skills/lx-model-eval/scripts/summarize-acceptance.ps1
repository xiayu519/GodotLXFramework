param([Parameter(Mandatory=$true)][string[]]$RunId)
$ErrorActionPreference='Stop'
$repoRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
. (Join-Path $PSScriptRoot 'eval-contract.ps1')
$schema=Read-EvalContract (Join-Path $PSScriptRoot '../evals/evals.json')
$graderHash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'eval-contract.ps1') -Algorithm SHA256).Hash
$latest=@{}; $sources=@{}; $taskHashes=@{}; $attempts=[System.Collections.Generic.List[object]]::new()
$utf8=[System.Text.UTF8Encoding]::new($false)
foreach($id in @($RunId | Sort-Object -Unique)){
    if($id -notmatch '^\d{8}-\d{6}-\d{3}$'){throw 'Invalid run ID.'}
    $run=Join-Path $repoRoot ".lx/model-evals/$id"
    $summary=Get-Content -LiteralPath (Join-Path $run 'summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $manifest=Get-Content -LiteralPath (Join-Path $run 'source-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $sources[$id]=$summary.source_hash
    # Repaired graders may differ; prompts, instructions, probes and product inputs must not.
    $taskLines=@($manifest.PSObject.Properties | Where-Object {$_.Name.Replace('\','/') -notlike '.agents/skills/lx-model-eval/scripts/*'} |
        Sort-Object Name | ForEach-Object {"$($_.Name)=$($_.Value)"})
    $sha=[System.Security.Cryptography.SHA256]::Create()
    try{$taskHashes[$id]=[BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes(($taskLines -join "`n")))).Replace('-','')}
    finally{$sha.Dispose()}
    foreach($result in $summary.results){
        $profile=@($schema.profiles | Where-Object id -eq $result.profile)
        if($profile.Count -ne 1 -or $result.model -ne $profile[0].model -or $result.reasoning -ne $profile[0].reasoning){throw 'Result has an invalid model profile.'}
        if($result.case -notin $schema.cases.id){throw 'Result references an unknown case.'}
        $key="$($result.profile)-$($result.case)"
        $artifact=Join-Path $run "artifacts/$key"
        $accepted=[bool]$result.passed
        $failures=@($result.failures)
        $recheckPath=Join-Path $artifact 'latest-recheck.json'
        $recheck=$null
        if(Test-Path -LiteralPath $recheckPath){
            $recheck=Get-Content -LiteralPath $recheckPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if($recheck.grader_hash -ne $graderHash -or $recheck.source_hash -ne $summary.source_hash -or
                $recheck.original_result -ne (Join-Path $artifact 'result.json') -or
                $recheck.profile -ne $result.profile -or $recheck.case -ne $result.case){throw "Stale or mismatched recheck for $key."}
            $accepted=[bool]$recheck.passed
            $failures=@($recheck.failures)
        }
        if($result.usage_available -ne $true -or $result.exit_code -ne 0){$accepted=$false}
        $entry=[pscustomobject]@{
            profile=$result.profile;case=$result.case;kind=$result.kind;model=$result.model;reasoning=$result.reasoning
            passed=$accepted;failures=$failures;run_id=$id;source_hash=$summary.source_hash;artifact=$artifact
            rechecked=($null -ne $recheck);recheck_receipt=$(if($recheck){$recheckPath}else{$null})
            efficiency_budget_passed=$result.efficiency_budget_passed;efficiency_warnings=@($result.efficiency_warnings)
            input_tokens=$result.input_tokens;cached_input_tokens=$result.cached_input_tokens
            uncached_input_tokens=$result.uncached_input_tokens;output_tokens=$result.output_tokens
            tool_calls=$result.tool_calls;failed_events=$result.failed_events;duration_seconds=$result.duration_seconds
        }
        $attempts.Add($entry)
        # Latest actual model attempt wins; never cherry-pick an earlier PASS over a later failure.
        $latest[$key]=$entry
    }
}
$results=@($latest.Values | Sort-Object profile,case)
$consistent=@($taskHashes.Values | Select-Object -Unique).Count -eq 1
$profiles=@(foreach($required in @($schema.profiles | Where-Object required)){
    $selected=@($results | Where-Object profile -eq $required.id)
    $coverage=Get-EvalCoverage $schema $required.required_suite $selected
    [pscustomobject]@{
        profile=$required.id;model=$required.model;reasoning=$required.reasoning
        coverage=$coverage;passed=($consistent -and $coverage.passed)
        functional_passes=@($selected | Where-Object passed).Count
        efficiency_budget_passed=@($selected | Where-Object {-not $_.efficiency_budget_passed}).Count -eq 0
        input_tokens=($selected|Measure-Object input_tokens -Sum).Sum
        cached_input_tokens=($selected|Measure-Object cached_input_tokens -Sum).Sum
        uncached_input_tokens=($selected|Measure-Object uncached_input_tokens -Sum).Sum
        output_tokens=($selected|Measure-Object output_tokens -Sum).Sum
        tool_calls=($selected|Measure-Object tool_calls -Sum).Sum
        duration_seconds=($selected|Measure-Object duration_seconds -Sum).Sum
    }
})
$report=[ordered]@{
    schema_version=2;generated_at_utc=(Get-Date).ToUniversalTime().ToString('o')
    all_required_passed=(@($profiles|Where-Object {-not $_.passed}).Count -eq 0)
    task_inputs_consistent=$consistent;same_source_hash=(@($sources.Values|Select-Object -Unique).Count -eq 1)
    source_hashes=$sources;task_input_hashes=$taskHashes;grader_hash=$graderHash
    method='Explicit multi-run acceptance; original results retained; deterministic rechecks use original model-produced files. Only evaluator scripts may differ between task snapshots.'
    usage_scope='Selected acceptance attempts only; diagnostic and interrupted runs are not a complete billing ledger.'
    profiles=$profiles;results=$results;attempts=@($attempts)
}
$path=Join-Path $repoRoot '.lx/model-evals/latest-acceptance.json'
[System.IO.File]::WriteAllText($path,($report|ConvertTo-Json -Depth 12),$utf8)
foreach($profile in $profiles){Write-Host "$($profile.profile): $($profile.functional_passes)/$($profile.coverage.expected); complete=$($profile.coverage.complete); accepted=$($profile.passed)"}
Write-Host "Task inputs consistent: $consistent. Acceptance: $path"
if(-not $report.all_required_passed){exit 1}
exit 0
