[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceRoot,

    [Parameter(Mandatory = $true)]
    [string]$CommandId,

    [Parameter(Mandatory = $true)]
    [string]$DisplayName,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CommandArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$reportDirectory = Join-Path $WorkspaceRoot '.lx'
$reportPath = Join-Path $reportDirectory 'editor-command.json'
$argumentsPath = Join-Path $reportDirectory ("editor-command-{0}.arguments.json" -f $CommandId)
$stdoutPath = Join-Path $reportDirectory ("editor-command-{0}.stdout" -f $CommandId)
$stderrPath = Join-Path $reportDirectory ("editor-command-{0}.stderr" -f $CommandId)
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)

New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null

function Write-EditorCommandReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$State,

        [AllowNull()]
        [Nullable[int]]$ExitCode,

        [string]$StandardOutput = '',

        [string]$StandardError = ''
    )

    $payload = [ordered]@{
        version = 1
        commandId = $CommandId
        displayName = $DisplayName
        state = $State
        processId = $PID
        exitCode = $ExitCode
        stdout = $StandardOutput
        stderr = $StandardError
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $json = $payload | ConvertTo-Json -Depth 4
    $temporaryPath = "{0}.{1}.tmp" -f $reportPath, $CommandId
    [System.IO.File]::WriteAllText($temporaryPath, $json, $utf8WithoutBom)
    Move-Item -LiteralPath $temporaryPath -Destination $reportPath -Force
}

Write-EditorCommandReport -State 'running' -ExitCode $null

try {
    $invokerScript = Join-Path $PSScriptRoot 'invoke-command.ps1'
    $argumentsJson = ConvertTo-Json -InputObject @($CommandArguments) -Compress
    [System.IO.File]::WriteAllText($argumentsPath, $argumentsJson, $utf8WithoutBom)
    $childArguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        ('"{0}"' -f $invokerScript),
        '-WorkspaceRoot',
        ('"{0}"' -f $WorkspaceRoot),
        '-ArgumentsFile',
        ('"{0}"' -f $argumentsPath)
    )
    $childProcess = Start-Process `
        -FilePath 'powershell.exe' `
        -ArgumentList $childArguments `
        -NoNewWindow `
        -PassThru `
        -Wait `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath
    $commandExitCode = $childProcess.ExitCode
    $standardOutput = [System.IO.File]::ReadAllText($stdoutPath)
    $standardError = [System.IO.File]::ReadAllText($stderrPath)

    $state = 'failed'
    if ($commandExitCode -eq 0) {
        $state = 'succeeded'
    }
    Write-EditorCommandReport `
        -State $state `
        -ExitCode $commandExitCode `
        -StandardOutput $standardOutput `
        -StandardError $standardError
}
catch {
    Write-EditorCommandReport `
        -State 'failed' `
        -ExitCode 1 `
        -StandardError $_.Exception.ToString()
}
finally {
    foreach ($temporaryPath in @($argumentsPath, $stdoutPath, $stderrPath)) {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}
