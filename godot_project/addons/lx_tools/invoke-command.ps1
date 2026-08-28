[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceRoot,

    [Parameter(Mandatory = $true)]
    [string]$ArgumentsFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lxScript = Join-Path $WorkspaceRoot 'lx.ps1'
if (-not (Test-Path -LiteralPath $lxScript -PathType Leaf)) {
    throw "找不到 LX 命令入口：$lxScript"
}

$argumentsJson = [System.IO.File]::ReadAllText($ArgumentsFile)
[string[]]$commandArguments = $argumentsJson | ConvertFrom-Json
& $lxScript @commandArguments '--json'
exit $LASTEXITCODE
