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

$requiredDotnetSdk = "8.0.416"
$dotnetCommand = Get-Command "dotnet" -ErrorAction SilentlyContinue
$detectedDotnetSdk = $null
if ($null -ne $dotnetCommand) {
    $detectedDotnetSdk = (& $dotnetCommand.Source --version 2>$null | Select-Object -First 1)
    if ($null -ne $detectedDotnetSdk) { $detectedDotnetSdk = $detectedDotnetSdk.Trim() }
}
if ($detectedDotnetSdk -ne $requiredDotnetSdk) {
    $message = "LXFramework requires .NET SDK $requiredDotnetSdk; found " +
        $(if ([string]::IsNullOrWhiteSpace($detectedDotnetSdk)) { "none" } else { $detectedDotnetSdk }) +
        ". Install the exact SDK, then rerun '.\lx.ps1 doctor'."
    if ($jsonMode) {
        [Console]::Out.WriteLine(([ordered]@{
            schema = "lx.command-report"
            schemaVersion = 1
            command = $Command.ToLowerInvariant()
            arguments = @($CommandArguments)
            success = $false
            exitCode = 1
            code = "LX_DOTNET_SDK_MISSING"
            startedAtUtc = [DateTimeOffset]::UtcNow.ToString("O", [System.Globalization.CultureInfo]::InvariantCulture)
            durationMs = 0
            diagnostics = @([ordered]@{
                code = "LX_DOTNET_SDK_MISSING"
                severity = "error"
                message = $message
            })
        } | ConvertTo-Json -Depth 6))
    }
    else {
        [Console]::Error.WriteLine($message)
    }
    exit 1
}

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
            if ($CommandArguments.Count -eq 1 -and
                $CommandArguments[0] -in @("--help", "-h", "help")) {
                Write-Host "Usage: lx check <changed-path> [changed-path ...]"
                Write-Host "Runs one deduplicated validation profile and fails uncovered product runtime paths."
                $lxExitCode = 0
                break
            }
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
                $normalized = $candidate.Replace("\", "/")
                while ($normalized.StartsWith("./", [System.StringComparison]::Ordinal)) {
                    $normalized = $normalized.Substring(2)
                }
                if ($normalized.StartsWith("godot_project/", [System.StringComparison]::OrdinalIgnoreCase)) {
                    $normalized = $normalized.Substring("godot_project/".Length)
                }
                if ([string]::IsNullOrWhiteSpace($normalized) -or
                    $normalized.StartsWith("/", [System.StringComparison]::Ordinal) -or
                    $normalized.IndexOf(":", [System.StringComparison]::Ordinal) -ge 0 -or
                    @($normalized.Split("/") | Where-Object { $_ -in @("", ".", "..") }).Count -ne 0) {
                    throw "check path '$candidate' must be a normalized path inside the LXFramework workspace."
                }
                $normalized
            }
            $needsData = [bool]($changedPaths | Where-Object {
                $_ -like "game_design/schema/*" -or
                $_ -like "game_design/data/*" -or
                $_ -like "game_design/fixtures/*" -or
                $_ -in @(
                    "game_design/build.bat",
                    "game_design/build.ps1",
                    "game_design/install-luban.ps1",
                    "game_design/luban.conf",
                    "game_design/toolchain.json",
                    "game_design/validation.json"
                ) -or
                $_ -like "content/data/luban/*" -or
                $_ -like "script/*/Generated/Luban/*" -or
                $_ -like "src/LXFramework.Core/Data/Luban*" -or
                $_ -eq "src/LXFramework/Content/ContentService.cs"
            })
            # A clean clone has no ignored .lx report or generated product-side
            # Luban output. Static validation requires both, so any cold check
            # must establish that prerequisite instead of failing and asking the
            # caller to discover and retry `lx data` manually.
            $needsData = $needsData -or -not (Test-Path -LiteralPath ".lx\luban\report.json" -PathType Leaf)
            $needsGenerate = [bool]($changedPaths | Where-Object {
                $_ -like "content/*/*-manifest.json" -or
                ($_ -like "scene/ui/*" -and $_ -notlike "*.md") -or
                $_ -like "tools/LXFramework.Tools/*Generator.cs" -or
                $_ -like "tools/LXFramework.Tools/*Manifest.cs"
            })
            $needsTests = [bool]($changedPaths | Where-Object {
                ($_ -like "src/LXFramework.Core/*" -and $_ -notlike "*.md") -or
                ($_ -like "tests/LXFramework.Core.Tests/*" -and $_ -notlike "*.md")
            })
            $needsFrameworkSmoke = [bool]($changedPaths | Where-Object {
                ($_ -like "src/LXFramework/*" -and $_ -notlike "*.md") -or
                $_ -eq "scene/main.tscn" -or
                $_ -eq "project.godot"
            })
            $needsProductSmoke = [bool]($changedPaths | Where-Object {
                ($_ -like "scene/*" -and $_ -notlike "*.md") -or
                $_ -like "script/*.cs" -or
                $_ -like "script/*.tscn" -or
                ($_ -like "content/*" -and $_ -notlike "*.md")
            })
            $needsProductSmoke = $needsProductSmoke -or $needsData
            $gameManifestPath = Join-Path $repoRoot "content\game\game-manifest.json"
            $hasProduct = $false
            if (Test-Path -LiteralPath $gameManifestPath -PathType Leaf) {
                $gameManifest = Get-Content -LiteralPath $gameManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
                $hasProduct = -not [string]::IsNullOrWhiteSpace([string]$gameManifest.name)
            }
            # A declared product must prove every changed path is either mapped to
            # a runtime gate or classified as non-runtime/static-only. The impact
            # analyzer exits before launching Godot when no scenario is selected.
            $needsProductSmoke = $needsProductSmoke -or $hasProduct
            $needsFrameworkVisual = [bool]($changedPaths | Where-Object {
                ($_ -like "src/LXFramework/UI/*" -and $_ -notlike "*.md") -or
                ($_ -like "scene/ui/examples/*" -and $_ -notlike "*.md")
            })
            $needsSolutionBuild = [bool]($changedPaths | Where-Object {
                ($_ -like "src/LXFramework.Core/*" -and $_ -notlike "*.md") -or
                $_ -eq "src/LXFramework.Core/LXFramework.Core.csproj" -or
                $_ -like "*.sln" -or
                $_ -eq "Directory.Build.props"
            })
            $needsProductBuild = [bool]($changedPaths | Where-Object {
                ($_ -like "src/LXFramework/*" -and $_ -notlike "*.md") -or
                $_ -like "script/*.cs" -or
                $_ -eq "LXFramework.csproj"
            })
            $needsProductBuild = $needsProductBuild -or $needsGenerate -or $needsData
            $profile = @()
            $profile += "workflow"
            if ($needsData) { $profile += "data" }
            if ($needsGenerate) { $profile += "generate" }
            $profile += "static"
            if ($needsSolutionBuild) { $profile += "solution-build" }
            elseif ($needsProductBuild) { $profile += "product-build" }
            if ($needsTests) { $profile += "test" }
            if ($needsFrameworkSmoke) { $profile += "framework-smoke" }
            if ($needsProductSmoke) { $profile += "product-smoke-affected" }
            if ($needsProductSmoke) { $profile += "product-visual-affected" }
            if ($needsFrameworkVisual) { $profile += "framework-visual" }
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
