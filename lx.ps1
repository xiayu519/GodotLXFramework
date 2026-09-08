param(
    [Parameter(Position = 0)]
    [string]$Command = "doctor",

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$CommandArguments = @()
)

$ErrorActionPreference = "Stop"
$projectRoot = Join-Path $PSScriptRoot "godot_project"
$projectScript = Join-Path $projectRoot "lx.ps1"
if (-not (Test-Path -LiteralPath $projectScript -PathType Leaf)) {
    throw "LXFramework project entry was not found at '$projectScript'."
}

$forwardArguments = @($CommandArguments | Where-Object { $null -ne $_ -and $_ -ne "" })
# The project entry owns path validation; preserve explicit roots for text checks.

& $projectScript $Command @forwardArguments
exit $LASTEXITCODE
