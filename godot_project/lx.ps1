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

function Invoke-LxOperation {
    Push-Location $repoRoot
    $lxExitCode = 0
    try {
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
            & $designBuild
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject -- validate @CommandArguments
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet build "LXFramework.sln" --nologo --verbosity quiet
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet test "LXFramework.sln" --no-build --nologo --verbosity quiet
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject --no-build -- smoke
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            dotnet run --project $toolProject --no-build -- visual compare ui_components
        }
        "check" {
            if ($CommandArguments.Count -eq 0) {
                Write-Error "check requires one or more changed paths."
                $lxExitCode = 2
                break
            }

            $changedPaths = $CommandArguments | ForEach-Object {
                $candidate = $_
                if ([System.IO.Path]::IsPathRooted($candidate)) {
                    $absolute = [System.IO.Path]::GetFullPath($candidate)
                    $rootPrefix = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd("\", "/") +
                        [System.IO.Path]::DirectorySeparatorChar
                    $workspacePrefix = [System.IO.Path]::GetFullPath($workspaceRoot).TrimEnd("\", "/") +
                        [System.IO.Path]::DirectorySeparatorChar
                    if ($absolute.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $candidate = $absolute.Substring($rootPrefix.Length)
                    }
                    elseif ($absolute.StartsWith($workspacePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $candidate = $absolute.Substring($workspacePrefix.Length)
                    }
                    else {
                        throw "check path '$candidate' is outside the LXFramework workspace."
                    }
                }
                $normalized = $candidate.Replace("\", "/").TrimStart([char[]]@('.', '/'))
                if ($normalized.StartsWith("godot_project/", [System.StringComparison]::OrdinalIgnoreCase)) {
                    $normalized = $normalized.Substring("godot_project/".Length)
                }
                $normalized
            }
            $needsData = [bool]($changedPaths | Where-Object {
                $_ -like "game_design/*" -or
                $_ -like "content/data/luban/*" -or
                $_ -like "script/*/Generated/Luban/*" -or
                $_ -like "src/LXFramework.Core/Data/Luban*" -or
                $_ -eq "src/LXFramework/Content/ContentService.cs"
            })
            $needsGenerate = [bool]($changedPaths | Where-Object {
                $_ -like "content/*/*-manifest.json" -or
                $_ -like "scene/ui/*" -or
                $_ -like "tools/LXFramework.Tools/*Generator.cs" -or
                $_ -like "tools/LXFramework.Tools/*Manifest.cs"
            })
            $needsTests = [bool]($changedPaths | Where-Object {
                $_ -like "src/LXFramework.Core/*" -or
                $_ -like "tests/LXFramework.Core.Tests/*" -or
                $_ -like "script/*/Tools/*" -or
                $_ -like "script/*/Tests/*" -or
                $_ -like "script/*/Domain/*" -or
                $_ -like "script/*/Content/*"
            })
            $needsBuild = [bool]($changedPaths | Where-Object {
                $_ -like "src/*" -or $_ -like "tools/*" -or $_ -like "tests/*" -or
                $_ -like "script/*.cs" -or
                $_ -like "*.csproj" -or $_ -like "*.sln" -or
                $_ -eq "Directory.Build.props"
            })
            $needsBuild = $needsBuild -or $needsGenerate -or $needsData
            $needsSmoke = [bool]($changedPaths | Where-Object {
                $_ -like "src/LXFramework/*" -or $_ -like "scene/*" -or
                $_ -like "script/*.cs" -or $_ -like "script/*.tscn" -or
                $_ -like "content/*" -or $_ -eq "project.godot"
            })
            $needsSmoke = $needsSmoke -or $needsData
            $profile = @()
            $profile += "workflow"
            if ($needsData) { $profile += "data" }
            if ($needsGenerate) { $profile += "generate" }
            $profile += "static"
            if ($needsBuild) { $profile += "build" }
            if ($needsTests) { $profile += "test" }
            if ($needsSmoke) { $profile += "smoke" }
            Write-Host "check profile: $($profile -join '+')"

            & $workflowCheck
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            if ($needsData) {
                & $designBuild
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsGenerate) {
                dotnet run --project $toolProject -- generate
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            dotnet run --project $toolProject -- validate
            if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            if ($needsBuild) {
                dotnet build "LXFramework.sln" --nologo --verbosity quiet
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsTests) {
                $testArguments = @(
                    "test",
                    "LXFramework.sln",
                    "--nologo",
                    "--verbosity",
                    "quiet"
                )
                if ($needsBuild) { $testArguments += "--no-build" }
                dotnet @testArguments
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
            }
            if ($needsSmoke) {
                $smokeArguments = @("run", "--project", $toolProject, "--no-build", "--", "smoke")
                dotnet @smokeArguments
                if ($LASTEXITCODE -ne 0) { $lxExitCode = $LASTEXITCODE; break }
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

    $failureCode = if ($lxResultExitCode -eq 2) { "LX_CLI_USAGE" } else { "LX_COMMAND_FAILED" }
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
