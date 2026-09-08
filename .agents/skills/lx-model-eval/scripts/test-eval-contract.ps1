param()
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'eval-contract.ps1')
$schema=Read-EvalContract (Join-Path $PSScriptRoot '../evals/evals.json')
$repoRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
Assert-EvalProjectConfig (Join-Path $repoRoot '.codex/config.toml') $schema
$checks=0
function Assert([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message};$script:checks++}
function Reject([scriptblock]$Action){$rejected=$false;try{& $Action | Out-Null}catch{$rejected=$true};Assert $rejected 'Invalid input was accepted.'}
$full=@(Select-EvalCases $schema full @() | ForEach-Object {[pscustomobject]@{case=$_.id;passed=$true}})
Assert (Get-EvalCoverage $schema full $full).passed 'Full suite should pass.'
$partial=Get-EvalCoverage $schema full @($full[0])
Assert (-not $partial.complete -and -not $partial.passed) 'Partial suite must never be reported as full.'
Assert (-not (Get-EvalCoverage $schema full @()).passed) 'Empty suite passed.'
Assert (-not (Get-EvalCoverage $schema full (@($full)+@($full[0]))).passed) 'Duplicate result passed.'
$failed=@($full | ForEach-Object {[pscustomobject]@{case=$_.case;passed=$false}})
Assert (-not (Get-EvalCoverage $schema full $failed).passed) 'Failed result passed.'
Assert (@(Select-EvalCases $schema foundation @()).Count -eq 23) 'Foundation coverage changed; review acceptance scope.'
Assert (@(Select-EvalCases $schema complex @()).Count -eq 3) 'Complex coverage changed; review acceptance scope.'
Reject { Select-EvalCases $schema full @('missing') }
Reject { Select-EvalCases $schema foundation @('async-reward-restart') }
Assert (@(Get-EvalResponseFailures ([pscustomobject]@{}) 'done').Count -eq 0) 'Absent optional assertions must not fail.'
Assert (@(Get-EvalResponseFailures ([pscustomobject]@{expected_terms=@('required')}) 'done').Count -eq 1) 'Missing mandatory assertion escaped.'
$paths=@(Get-EvalCheckPaths @('godot_project/script/Game/Root.cs','godot_project/script/Game/Root.cs.uid','godot_project/script/Game/Generated/Data.cs','godot_project/content/data/luban/data.bytes','AGENTS.md'))
Assert ($paths.Count -eq 1 -and $paths[0] -eq 'godot_project/script/Game/Root.cs') 'Derived metadata must not duplicate real source changes.'
Assert (@(Get-EvalCheckPaths @('godot_project/script/Game/Unchanged.cs.uid')).Count -eq 0) 'Import-generated sidecars must not invent gameplay changes.'
Assert (@(Get-EvalCheckPaths @('godot_project/content/game/game-manifest.json')).Count -eq 1) 'Runtime manifests must remain covered.'
$runner=Join-Path $PSScriptRoot 'run-model-evals.ps1'
$directPlan=& $runner -Profile astra-light -Suite foundation -CaseId @('create-game','native-node-context') -PrintPlan | ConvertFrom-Json
Assert ($directPlan.model -eq 'gpt-6-astra' -and $directPlan.reasoning -eq 'low' -and $directPlan.cases.Count -eq 2) 'Direct script invocation lost typed profile or case-array binding.'
$filePlan=& (Get-Process -Id $PID).Path -NoProfile -ExecutionPolicy Bypass -File $runner -Profile astra-xhigh -Suite complex -PrintPlan | ConvertFrom-Json
Assert ($filePlan.model -eq 'gpt-6-astra' -and $filePlan.reasoning -eq 'xhigh' -and $filePlan.cases.Count -eq 3) 'File invocation lost profile binding.'
$rpg=@($schema.cases | Where-Object id -eq 'new-rpg-driving-architecture')[0]
$regexGroups=@($rpg.expected_term_groups | Where-Object {$_.any_regex})
$runtimeGroup=$regexGroups[0]
$pcGroup=$regexGroups[1]
$phrases='{"positiveRuntime":"\u540c\u7c7b\u7b2c\u4e8c\u4efd\u5185\u5bb9\u53ea\u52a0\u811a\u672c/\u6570\u636e\uff0c\u4e0d\u6539\u8fd0\u884c\u65f6\u6216 GameRoot\u3002","negativeRuntime":"\u540c\u7c7b\u7b2c\u4e8c\u4efd\u5185\u5bb9\u5fc5\u987b\u4fee\u6539\u6267\u884c\u8fd0\u884c\u65f6\u3002","positivePc":"\u8303\u56f4\u4ec5 PC\u3002","negativePc":"\u65b0\u589e\u7f51\u7edc\u548c\u79fb\u52a8\u5e73\u53f0\u3002"}' | ConvertFrom-Json
Assert (Test-EvalTermGroup $phrases.positiveRuntime $runtimeGroup) 'Equivalent runtime wording rejected.'
Assert (-not (Test-EvalTermGroup $phrases.negativeRuntime $runtimeGroup)) 'Changed runtime contract accepted.'
Assert (Test-EvalTermGroup $phrases.positivePc $pcGroup) 'Equivalent PC scope rejected.'
Assert (-not (Test-EvalTermGroup $phrases.negativePc $pcGroup)) 'Scope expansion accepted.'
$migration=@($schema.cases | Where-Object id -eq 'legacy-lx-upgrade-plan')[0]
$migrationText='"\u67b6\u6784\u9a71\u52a8\uff1b\u5168\u90e8\u4ea7\u54c1 smoke"' | ConvertFrom-Json
Assert (Test-EvalTermGroup $migrationText $migration.expected_term_groups[0]) 'Equivalent architecture terminology rejected.'
Assert (Test-EvalTermGroup $migrationText $migration.expected_term_groups[1]) 'Equivalent bilingual smoke terminology rejected.'
$temp=Join-Path ([System.IO.Path]::GetTempPath()) ('lx-eval-contract-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try{
    $utf8=[System.Text.UTF8Encoding]::new($false)
    $configPath=Join-Path $temp 'config.toml'
    [System.IO.File]::WriteAllText($configPath,'model = "gpt-5.6-sol"'+ "`n" + 'model_reasoning_effort = "high"',$utf8)
    Reject { Assert-EvalProjectConfig $configPath $schema }
    [System.IO.File]::WriteAllText($configPath,'model = "gpt-6-astra"'+ "`n" + 'model_reasoning_effort = "low"'+ "`n" + 'plan_mode_reasoning_effort = "exhigh"',$utf8)
    Reject { Assert-EvalProjectConfig $configPath $schema }
    $bad=($schema | ConvertTo-Json -Depth 20 | ConvertFrom-Json)
    $bad.profiles[0].reasoning='ultra'
    $badPath=Join-Path $temp 'bad.json'
    [System.IO.File]::WriteAllText($badPath,($bad | ConvertTo-Json -Depth 20),$utf8)
    Reject { Read-EvalContract $badPath }
}
finally{
    $resolved=[System.IO.Path]::GetFullPath($temp)
    $allowed=[System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')+'\lx-eval-contract-'
    if(-not $resolved.StartsWith($allowed,[System.StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe temporary cleanup.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host "Evaluation contract passed: $checks positive/negative assertions; no model calls."
