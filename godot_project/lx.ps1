param(
    [Parameter(Position = 0)]
    [string]$Command = "doctor",

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$CommandArguments = @()
)

# Native tools commonly use stderr for diagnostics even when their exit code is the
# authoritative result. Keep those records non-terminating so lx can preserve the
# documented child exit code; PowerShell helpers still throw explicitly on failure.
$ErrorActionPreference = "Continue"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolProject = Join-Path $repoRoot "tools\LXFramework.Tools\LXFramework.Tools.csproj"
$workspaceRoot = Split-Path -Parent $repoRoot
$workflowCheck = Join-Path $workspaceRoot ".agents\skills\lx-codex-workflow\scripts\check-workflow.ps1"
$designBuild = Join-Path $workspaceRoot "game_design\build.ps1"

$jsonMode = $false
$filteredArguments = [System.Collections.Generic.List[string]]::new()
foreach ($argument in @($CommandArguments)) {
    if ($argument -eq "--json") {
        $jsonMode = $true
    }
    else {
        $filteredArguments.Add($argument)
    }
}
$CommandArguments = $filteredArguments.ToArray()

. (Join-Path $repoRoot "tools/LXFramework.Tools/CheckPlan.ps1")

function Assert-LxDotnet {
    $requiredSdk = "8.0.416"
    $dotnet = Get-Command "dotnet" -ErrorAction SilentlyContinue
    $actualSdk = if ($null -ne $dotnet) { (& $dotnet.Source --version 2>$null | Select-Object -First 1) } else { $null }
    if ([string]$actualSdk -ne $requiredSdk) {
        $script:lxFailureCode = "LX_DOTNET_SDK_MISSING"
        throw "LXFramework requires .NET SDK $requiredSdk; found '$actualSdk'. Install it for engine/code tasks."
    }
}

function Invoke-LxOperation {
    Push-Location $repoRoot
    $lxExitCode = 0
    try {
    # Native processes update the global automatic variable; do not shadow it locally.
    $global:LASTEXITCODE = 0
    $checkPlan = $null
    if ($Command.ToLowerInvariant() -eq 'check') {
        if ($CommandArguments.Count -eq 1 -and $CommandArguments[0] -in @('--help','-h','help')) {
            Write-Host 'Usage: lx check [--plan] <changed-path> [changed-path ...]'
            Write-Host '--plan reports selected stages without SDK checks, generation or execution.'
            $script:lxResultExitCode = 0
            return
        }
        $planOnly = '--plan' -in $CommandArguments
        $paths = @($CommandArguments | Where-Object { $_ -ne '--plan' })
        if ($paths.Count -eq 0) { $script:lxResultExitCode=2; Write-Error 'check requires one or more changed paths.'; return }
        $checkPlan = Get-LxCheckPlan $repoRoot $paths
        if ($planOnly) {
            Write-Output ($checkPlan | ConvertTo-Json -Depth 5)
            $script:lxResultExitCode = 0
            return
        }
        Write-Host "check profile: $($checkPlan.stages -join '+')"
        if (-not $checkPlan.requiresDotnet) {
            Test-LxChangedText $repoRoot $paths
            if ($checkPlan.needs.workflow) {
                & $workflowCheck
                $script:lxResultExitCode = $LASTEXITCODE
            }
            else { $script:lxResultExitCode = 0 }
            return
        }
    }
    Assert-LxDotnet
    switch ($Command.ToLowerInvariant()) {
        "build" {
            dotnet build "LXFramework.sln" @CommandArguments
        }
        "test" {
            dotnet test "LXFramework.sln" @CommandArguments
        }
        "data" {
            & $designBuild @CommandArguments
        }
        "validate" {
            & $workflowCheck
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            & (Join-Path $repoRoot 'tools/LXFramework.Tools/TestCheckPlan.ps1')
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            & $designBuild
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject -- validate @CommandArguments
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet build "LXFramework.sln" --nologo --verbosity quiet
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            & (Join-Path $repoRoot 'tools/LXFramework.Tools/TestIncrementalValidation.ps1')
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet test "LXFramework.sln" --no-build --nologo --verbosity quiet
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject --no-build -- benchmark
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject --no-build -- smoke
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject --no-build -- smoke product all
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject --no-build -- visual compare ui_components
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject --no-build -- visual compare product
        }
        "check" {
            $changedPaths = $checkPlan.changedPaths
            $needsData = $checkPlan.needs.data
            $needsGenerate = $checkPlan.needs.generate
            $needsSolutionBuild = $checkPlan.needs.solutionBuild
            $needsProductBuild = $checkPlan.needs.productBuild
            $needsTests = $checkPlan.needs.tests
            $needsFrameworkSmoke = $checkPlan.needs.frameworkSmoke
            $needsProductSmoke = $checkPlan.needs.productSmoke
            $needsFrameworkVisual = $checkPlan.needs.frameworkVisual
            if ($checkPlan.needs.documents) { Test-LxChangedText $repoRoot $paths }
            if ($checkPlan.needs.workflow) {
                & $workflowCheck
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsData) {
                & $designBuild
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsGenerate) {
                dotnet run --project $toolProject -- generate
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            $staticArguments = @("run", "--project", $toolProject, "--", "validate", "--changed") +
                @($changedPaths)
            dotnet @staticArguments
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            if ($needsSolutionBuild) {
                dotnet build "LXFramework.sln" --nologo --verbosity quiet
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            elseif ($needsProductBuild) {
                dotnet build "LXFramework.csproj" --nologo --verbosity quiet
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsTests) {
                $testArguments = @(
                    "test",
                    "tests/LXFramework.Core.Tests/LXFramework.Core.Tests.csproj",
                    "--nologo",
                    "--verbosity",
                    "quiet"
                )
                if ($needsSolutionBuild) { $testArguments += "--no-build" }
                if ($checkPlan.testFilter) { $testArguments += @("--filter",$checkPlan.testFilter) }
                dotnet @testArguments
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsFrameworkSmoke) {
                $smokeArguments = @("run", "--project", $toolProject, "--no-build", "--", "smoke")
                dotnet @smokeArguments
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsProductSmoke) {
                $productSmokeArguments = @(
                    "run", "--project", $toolProject, "--no-build", "--",
                    "smoke", "product", "affected"
                ) + @($changedPaths)
                dotnet @productSmokeArguments
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
                $productVisualArguments = @(
                    "run", "--project", $toolProject, "--no-build", "--",
                    "visual", "compare", "affected"
                ) + @($changedPaths)
                dotnet @productVisualArguments
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsFrameworkVisual) {
                dotnet run --project $toolProject --no-build -- visual compare ui_components
            }
        }
        default {
            dotnet run --project $toolProject -- $Command @CommandArguments
        }
    }

    if ($lxExitCode -eq 0) { $lxExitCode = $LASTEXITCODE }
    }
    finally {
        Pop-Location
    }
    $script:lxResultExitCode = $lxExitCode
}

$lxResultExitCode = 0
$lxFailureCode = $null
$startedAt = [DateTimeOffset]::UtcNow
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
if ($jsonMode) {
    $records = @()
    try {
        $records = @(& { Invoke-LxOperation } *>&1)
    }
    catch {
        $records += $_
        $lxResultExitCode = 1
    }
    finally {
        $stopwatch.Stop()
    }

    $failureCode = if ($lxFailureCode) { $lxFailureCode } elseif ($lxResultExitCode -eq 2) { "LX_CLI_USAGE" } else { "LX_COMMAND_FAILED" }
    $diagnostics = @($records | ForEach-Object {
        $isError = $_ -is [System.Management.Automation.ErrorRecord]
        $message = if ($isError) { $_.Exception.Message.Trim() } else { ($_ | Out-String).Trim() }
        if ($message.Length -eq 0) { return }
        [ordered]@{
            code = if ($isError) { $failureCode } else { "LX_COMMAND_OUTPUT" }
            severity = if ($isError) { "error" } else { "info" }
            message = $message
        }
    })
    if ($lxResultExitCode -ne 0 -and -not ($diagnostics | Where-Object { $_.severity -eq "error" })) {
        $diagnostics += [ordered]@{
            code = $failureCode
            severity = "error"
            message = "Command '$Command' exited with code $lxResultExitCode."
        }
    }

    $report = [ordered]@{
        schema = "lx.command-report"
        schemaVersion = 1
        command = $Command.ToLowerInvariant()
        arguments = @($CommandArguments)
        success = $lxResultExitCode -eq 0
        exitCode = $lxResultExitCode
        code = if ($lxResultExitCode -eq 0) { "LX_OK" } else { $failureCode }
        startedAtUtc = $startedAt.ToString("O", [System.Globalization.CultureInfo]::InvariantCulture)
        durationMs = $stopwatch.ElapsedMilliseconds
        diagnostics = $diagnostics
    }
    [Console]::Out.WriteLine(($report | ConvertTo-Json -Depth 6))
}
else {
    try {
        Invoke-LxOperation
    }
    catch {
        Write-Error "lx: $($_.Exception.Message)" -ErrorAction Continue
        $lxResultExitCode = 1
    }
    finally {
        $stopwatch.Stop()
    }
}
exit $lxResultExitCode
