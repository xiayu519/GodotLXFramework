param(
    [ValidateSet('low','medium','high','xhigh','max')][string]$Effort='low',
    [string]$CodexPath='',
    [switch]$PrintOnly,
    [Parameter(ValueFromRemainingArguments=$true)][string[]]$CodexArguments=@()
)
$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $workspaceRoot '.agents\skills\lx-model-eval\scripts\eval-contract.ps1')
$schema=Read-EvalContract (Join-Path $workspaceRoot '.agents\skills\lx-model-eval\evals\evals.json')
$profile=$schema.profiles | Where-Object reasoning -eq $Effort
$exe=Resolve-EvalCodex $CodexPath
$arguments=@('--cd',$workspaceRoot,'--model',$profile.model,'--config',('model_reasoning_effort="'+$Effort+'"'),'--config',('plan_mode_reasoning_effort="'+$Effort+'"'))+@($CodexArguments)
if($PrintOnly){[pscustomobject]@{executable=$exe;version=(& $exe --version);model=$profile.model;reasoning=$Effort;planReasoning=$Effort;arguments=$arguments}|ConvertTo-Json -Depth 3;exit 0}
# Runs in the user's invoking terminal; no hidden interactive helper is spawned.
& $exe @arguments
exit $LASTEXITCODE
