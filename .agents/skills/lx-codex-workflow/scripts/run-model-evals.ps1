param(
    [ValidateSet("smoke", "full")]
    [string]$Suite = "full",

    [string[]]$CaseId = @(),

    [int]$TimeoutMinutes = 20,

    [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\.."))
$projectDirectory = "godot_project"
$evalPath = Join-Path $repoRoot ".agents\skills\lx-codex-workflow\evals\evals.json"
$utf8 = [System.Text.UTF8Encoding]::new($false)
$evals = $utf8.GetString([System.IO.File]::ReadAllBytes($evalPath)) | ConvertFrom-Json
$profiles = @($evals.profiles)
if ($profiles.Count -ne 1 -or
    $profiles[0].id -ne "sol-high" -or
    $profiles[0].model -ne "gpt-5.6-sol" -or
    $profiles[0].reasoning -ne "high" -or
    $profiles[0].required -ne $true) {
    throw "Evaluation schema must contain only the required sol-high profile."
}
$profile = $profiles[0]
$cases = if ($Suite -eq "smoke") {
    @($evals.cases | Where-Object { $_.suite -eq "smoke" })
}
else {
    @($evals.cases)
}
if ($CaseId.Count -gt 0) {
    $cases = @($cases | Where-Object { $_.id -in $CaseId })
    foreach ($requestedCase in $CaseId) {
        if ($requestedCase -notin @($cases.id)) {
            throw "Unknown case '$requestedCase' for suite '$Suite'."
        }
    }
}
if ($cases.Count -eq 0) {
    throw "No cases selected."
}

$codexCommand = Get-Command codex -ErrorAction Stop
$codexExecutable = [string]$codexCommand.Source
if ($codexCommand.CommandType -ne [System.Management.Automation.CommandTypes]::Application -or
    [string]::IsNullOrWhiteSpace($codexExecutable) -or
    -not (Test-Path -LiteralPath $codexExecutable -PathType Leaf)) {
    throw "Codex CLI must resolve to an executable application, got '$($codexCommand.CommandType)' at '$codexExecutable'."
}
$hostExe = (Get-Process -Id $PID).Path
$runId = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$outputRoot = Join-Path $repoRoot ".lx\model-evals"
$runRoot = Join-Path $outputRoot $runId
$fixtureRoot = Join-Path $runRoot "fixtures"
$artifactRoot = Join-Path $runRoot "artifacts"
New-Item -ItemType Directory -Path $fixtureRoot, $artifactRoot -Force | Out-Null

function Write-Utf8([string]$path, [string]$content) {
    $parent = Split-Path -Parent $path
    if ($parent) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($path, $content, $utf8)
}

function Copy-EvalRepository([string]$destination) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Push-Location $repoRoot
    try {
        $relativeFiles = @(& rg --files --hidden)
        if ($LASTEXITCODE -ne 0) {
            throw "rg failed while enumerating the evaluation fixture."
        }
    }
    finally {
        Pop-Location
    }
    foreach ($relative in $relativeFiles) {
        $normalized = $relative.Replace('\', '/')
        $segments = @($normalized.Split('/'))
        if (@($segments | Where-Object {
            $_ -in @(".git", ".godot", ".lx", ".tools", "bin", "obj", "artifacts", "TestResults")
        }).Count -gt 0) {
            continue
        }
        $source = Join-Path $repoRoot $relative
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
    }
}

function Invoke-ChildPowerShell([string[]]$arguments, [string]$logPath) {
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $lines = @(& $hostExe -NoProfile -ExecutionPolicy Bypass @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    Write-Utf8 $logPath (($lines | ForEach-Object { $_.ToString() }) -join "`n")
    return $exitCode
}

function Remove-EvalFixture([string]$fixture) {
    $resolvedFixture = [System.IO.Path]::GetFullPath($fixture)
    $allowedPrefix = [System.IO.Path]::GetFullPath($fixtureRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove fixture outside the run fixture root: $resolvedFixture"
    }
    if (Test-Path -LiteralPath $resolvedFixture) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}

function Remove-FixturePath([string]$fixture, [string]$target, [switch]$Recurse) {
    $resolvedFixture = [System.IO.Path]::GetFullPath($fixture).TrimEnd('\', '/')
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    $allowedPrefix = $resolvedFixture + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTarget.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove evaluation content outside its fixture: $resolvedTarget"
    }
    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Force -Recurse:$Recurse
    }
}

function Resolve-FixtureResourcePath([string]$fixture, [string]$resourcePath) {
    if (-not $resourcePath.StartsWith("res://", [System.StringComparison]::Ordinal)) {
        throw "Evaluation fixture contains an invalid resource path '$resourcePath'."
    }
    return [System.IO.Path]::GetFullPath((Join-Path $fixture $resourcePath.Substring(6)))
}

function Prepare-CleanFixture([string]$fixture, [string]$logPath) {
    $fixtureProject = Join-Path $fixture $projectDirectory
    $gamePath = Join-Path $fixtureProject "content\game\game-manifest.json"
    $featurePath = Join-Path $fixtureProject "content\features\feature-manifest.json"
    $uiPath = Join-Path $fixtureProject "content\ui\ui-manifest.json"
    $contentPath = Join-Path $fixtureProject "content\data\content-manifest.json"
    $inputPath = Join-Path $fixtureProject "content\input\input-manifest.json"
    $resPath = Join-Path $fixtureProject "content\res\res-manifest.json"

    $game = $utf8.GetString([System.IO.File]::ReadAllBytes($gamePath)) | ConvertFrom-Json
    $features = $utf8.GetString([System.IO.File]::ReadAllBytes($featurePath)) | ConvertFrom-Json
    $ui = $utf8.GetString([System.IO.File]::ReadAllBytes($uiPath)) | ConvertFrom-Json
    $content = $utf8.GetString([System.IO.File]::ReadAllBytes($contentPath)) | ConvertFrom-Json
    $input = $utf8.GetString([System.IO.File]::ReadAllBytes($inputPath)) | ConvertFrom-Json
    $resources = $utf8.GetString([System.IO.File]::ReadAllBytes($resPath)) | ConvertFrom-Json

    $productFeatures = @($features.features | Where-Object { $_.scope -ne "Framework" })
    $productScreens = @($ui.screens | Where-Object { $_.scope -ne "Framework" })
    $productTables = @($content.tables | Where-Object { $_.scope -ne "Framework" })
    $productResources = @($resources.assets | Where-Object { $_.scope -ne "Framework" })
    $registeredPaths = @($game.worlds.scenePath) +
        @($productFeatures.scenePath) +
        @($productScreens.scenePath) +
        @($productTables.path) +
        @($productResources.path)
    foreach ($resourcePath in @($registeredPaths | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique)) {
        $target = Resolve-FixtureResourcePath $fixtureProject ([string]$resourcePath)
        Remove-FixturePath $fixtureProject $target
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$game.sourceRoot)) {
        $productRoot = [System.IO.Path]::GetFullPath((Join-Path $fixtureProject ([string]$game.sourceRoot)))
        $productPrefix = $productRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        $solutionPath = Join-Path $fixtureProject "LXFramework.sln"
        $solutionProjects = @(& dotnet sln $solutionPath list 2>&1) | Where-Object {
            $_.ToString().Trim().EndsWith(".csproj", [System.StringComparison]::OrdinalIgnoreCase)
        }
        if ($LASTEXITCODE -ne 0) {
            throw "Could not enumerate projects in the clean evaluation fixture solution."
        }
        foreach ($solutionProject in $solutionProjects) {
            $solutionProjectPath = [System.IO.Path]::GetFullPath(
                (Join-Path $fixtureProject $solutionProject.ToString().Trim()))
            if (-not $solutionProjectPath.StartsWith(
                    $productPrefix,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            & dotnet sln $solutionPath remove $solutionProjectPath | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Could not remove product project '$solutionProjectPath' from the clean evaluation fixture."
            }
        }

        $mainProjectPath = Join-Path $fixtureProject "LXFramework.csproj"
        [xml]$mainProject = $utf8.GetString([System.IO.File]::ReadAllBytes($mainProjectPath))
        foreach ($projectReference in @($mainProject.Project.ItemGroup.ProjectReference)) {
            $include = [string]$projectReference.Include
            if ([string]::IsNullOrWhiteSpace($include)) {
                continue
            }
            $referencePath = [System.IO.Path]::GetFullPath((Join-Path $fixtureProject $include))
            if ($referencePath.StartsWith(
                    $productPrefix,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                [void]$projectReference.ParentNode.RemoveChild($projectReference)
            }
        }
        $xmlSettings = [System.Xml.XmlWriterSettings]::new()
        $xmlSettings.Encoding = $utf8
        $xmlSettings.Indent = $true
        $xmlSettings.OmitXmlDeclaration = $true
        $xmlWriter = [System.Xml.XmlWriter]::Create($mainProjectPath, $xmlSettings)
        try {
            $mainProject.Save($xmlWriter)
        }
        finally {
            $xmlWriter.Dispose()
        }

        Remove-FixturePath $fixtureProject $productRoot -Recurse
    }

    $productName = [string]$game.name
    $game.name = ""
    $game.rootNamespace = "Game"
    $game.sourceRoot = ""
    $game.initialWorldId = ""
    $game.worlds = @()
    $features.features = @($features.features | Where-Object { $_.scope -eq "Framework" })
    $ui.screens = @($ui.screens | Where-Object { $_.scope -eq "Framework" })
    $content.tables = @($content.tables | Where-Object { $_.scope -eq "Framework" })
    $input.actions = @($input.actions | Where-Object { $_.scope -eq "Framework" })
    $resources.assets = @($resources.assets | Where-Object { $_.scope -eq "Framework" })
    Write-Utf8 $gamePath (($game | ConvertTo-Json -Depth 20) + "`n")
    Write-Utf8 $featurePath (($features | ConvertTo-Json -Depth 20) + "`n")
    Write-Utf8 $uiPath (($ui | ConvertTo-Json -Depth 20) + "`n")
    Write-Utf8 $contentPath (($content | ConvertTo-Json -Depth 20) + "`n")
    Write-Utf8 $inputPath (($input | ConvertTo-Json -Depth 20) + "`n")
    Write-Utf8 $resPath (($resources | ConvertTo-Json -Depth 20) + "`n")

    $mainPath = Join-Path $fixtureProject "scene\main.tscn"
    $main = $utf8.GetString([System.IO.File]::ReadAllBytes($mainPath))
    Write-Utf8 $mainPath ($main.Replace(
        "ShowFrameworkStatus = false",
        "ShowFrameworkStatus = true"))

    $projectPath = Join-Path $fixtureProject "project.godot"
    $project = $utf8.GetString([System.IO.File]::ReadAllBytes($projectPath))
    if (-not [string]::IsNullOrWhiteSpace($productName)) {
        $project = $project.Replace(
            ('config/name="' + $productName + '"'),
            'config/name="LXFramework"')
    }
    Write-Utf8 $projectPath $project

    $generateExit = Invoke-ChildPowerShell @(
        "-File", (Join-Path $fixture "lx.ps1"), "generate"
    ) $logPath
    if ($generateExit -ne 0) {
        throw "Clean evaluation fixture generation failed with exit code $generateExit."
    }
}

function Read-JsonEvents([string]$path) {
    $events = @()
    if (-not (Test-Path -LiteralPath $path)) {
        return $events
    }
    foreach ($line in [System.IO.File]::ReadAllLines($path, $utf8)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        try {
            $events += ($line | ConvertFrom-Json)
        }
        catch {
        }
    }
    return $events
}

$doctorReportPath = Join-Path $repoRoot "$projectDirectory\.lx\doctor.json"
if (-not (Test-Path -LiteralPath $doctorReportPath -PathType Leaf)) {
    $doctorExit = Invoke-ChildPowerShell @(
        "-File", (Join-Path $repoRoot "lx.ps1"), "doctor"
    ) (Join-Path $runRoot "doctor.log")
    if ($doctorExit -ne 0) {
        throw "LXFramework doctor failed before model evaluation."
    }
}
$doctorReport = $utf8.GetString([System.IO.File]::ReadAllBytes($doctorReportPath)) | ConvertFrom-Json
$godotPath = [string]$doctorReport.checks.godotDotnet
if ([string]::IsNullOrWhiteSpace($godotPath) -or -not (Test-Path -LiteralPath $godotPath -PathType Leaf)) {
    throw "Godot .NET path is unavailable; run './lx.ps1 doctor' before model evaluation."
}
$env:LX_GODOT = $godotPath

$lubanReportPath = Join-Path $repoRoot "$projectDirectory\.lx\luban\report.json"
if (-not (Test-Path -LiteralPath $lubanReportPath -PathType Leaf)) {
    $lubanExit = Invoke-ChildPowerShell @(
        "-File", (Join-Path $repoRoot "lx.ps1"), "data"
    ) (Join-Path $runRoot "luban.log")
    if ($lubanExit -ne 0) {
        throw "LXFramework Luban generation failed before model evaluation."
    }
}
$lubanReport = $utf8.GetString([System.IO.File]::ReadAllBytes($lubanReportPath)) | ConvertFrom-Json
$lubanPath = [string]$lubanReport.toolAssembly
if ([string]::IsNullOrWhiteSpace($lubanPath) -or -not (Test-Path -LiteralPath $lubanPath -PathType Leaf)) {
    throw "Pinned Luban path is unavailable; run './lx.ps1 data' before model evaluation."
}
$env:LX_LUBAN_DLL = $lubanPath

$preflightFixture = Join-Path $fixtureRoot "_preflight"
try {
    Copy-EvalRepository $preflightFixture
    Prepare-CleanFixture $preflightFixture (Join-Path $runRoot "preflight-clean.log")
    $jsonContractLog = Join-Path $runRoot "preflight-command-json.log"
    $jsonContractExit = Invoke-ChildPowerShell @(
        "-File", (Join-Path $preflightFixture "lx.ps1"), "help", "--json"
    ) $jsonContractLog
    if ($jsonContractExit -ne 0) {
        throw "Evaluation preflight JSON command contract failed (exit $jsonContractExit)."
    }
    try {
        $jsonContract = $utf8.GetString([System.IO.File]::ReadAllBytes($jsonContractLog)) | ConvertFrom-Json
    }
    catch {
        throw "Evaluation preflight JSON command contract was not valid JSON: $($_.Exception.Message)"
    }
    if ($jsonContract.schema -ne "lx.command-report" -or
        [int]$jsonContract.schemaVersion -ne 1 -or
        $jsonContract.success -ne $true -or
        [int]$jsonContract.exitCode -ne 0 -or
        $jsonContract.code -ne "LX_OK") {
        throw "Evaluation preflight JSON command contract has invalid required fields."
    }
    $preflightCreateExit = Invoke-ChildPowerShell @(
        "-File", (Join-Path $preflightFixture "lx.ps1"), "create", "game", "EvalPreflight"
    ) (Join-Path $runRoot "preflight-create.log")
    if ($preflightCreateExit -ne 0) {
        throw "Evaluation preflight could not create a game (exit $preflightCreateExit)."
    }
    $architectureProbe = Join-Path $preflightFixture "godot_project\script\EvalPreflight\ArchitectureViolationProbe.cs"
    Write-Utf8 $architectureProbe @"
using Loader = Godot.ResourceLoader;

namespace EvalPreflight;

internal static class ArchitectureViolationProbe
{
    public static object? Load() => Loader.Load("res://probe.tres");
}
"@
    $documentationProbe = Join-Path $preflightFixture "godot_project\src\LXFramework\Validation\DocumentationViolationProbe.cs"
    Write-Utf8 $documentationProbe @"
namespace LX.Validation;

public enum DocumentationViolationProbe
{
    MissingComment,
}
"@
    $architectureLog = Join-Path $runRoot "preflight-architecture-negative.log"
    $architectureExit = Invoke-ChildPowerShell @(
        "-File", (Join-Path $preflightFixture "lx.ps1"), "validate"
    ) $architectureLog
    $architectureOutput = $utf8.GetString([System.IO.File]::ReadAllBytes($architectureLog))
    if ($architectureExit -eq 0 -or
        $architectureOutput.IndexOf("LX_ARCH_003", [System.StringComparison]::Ordinal) -lt 0) {
        throw "Evaluation preflight syntax-tree boundary probe did not produce LX_ARCH_003."
    }
    if ($architectureOutput.IndexOf("LX_DOC_001", [System.StringComparison]::Ordinal) -lt 0) {
        throw "Evaluation preflight public enum documentation probe did not produce LX_DOC_001."
    }
    Remove-FixturePath $preflightFixture $architectureProbe
    Remove-FixturePath $preflightFixture $documentationProbe
    $preflightScaffolds = @(
        [pscustomobject]@{ Name = "world"; Arguments = @("create", "world", "Probe", "probe_world") },
        [pscustomobject]@{ Name = "feature"; Arguments = @("create", "feature", "Probe", "probe_feature") },
        [pscustomobject]@{ Name = "node"; Arguments = @("create", "node", "ProbeBody", "CharacterBody2D", "probe_body") },
        [pscustomobject]@{ Name = "screen"; Arguments = @("create", "screen", "Probe", "probe_screen") },
        [pscustomobject]@{ Name = "content"; Arguments = @("create", "content", "Probe", "probe_content") },
        [pscustomobject]@{ Name = "input"; Arguments = @("create", "input", "Probe", "lx_probe", "F10") },
        [pscustomobject]@{ Name = "res"; Arguments = @("create", "res", "probe_icon", "Texture2D", "res://icon.svg", "Cached") }
    )
    foreach ($scaffold in $preflightScaffolds) {
        $scaffoldArguments = @(
            "-File", (Join-Path $preflightFixture "lx.ps1")
        ) + @($scaffold.Arguments)
        $scaffoldExit = Invoke-ChildPowerShell `
            $scaffoldArguments `
            (Join-Path $runRoot "preflight-$($scaffold.Name).log")
        if ($scaffoldExit -ne 0) {
            throw "Evaluation preflight '$($scaffold.Name)' scaffold failed (exit $scaffoldExit)."
        }
    }
    $preflightValidateExit = Invoke-ChildPowerShell @(
        "-File", (Join-Path $preflightFixture "lx.ps1"), "validate"
    ) (Join-Path $runRoot "preflight-validate.log")
    if ($preflightValidateExit -ne 0) {
        throw "Evaluation preflight validation failed (exit $preflightValidateExit)."
    }
}
finally {
    Remove-EvalFixture $preflightFixture
}
if ($PreflightOnly) {
    Write-Host "Evaluation preflight passed: JSON contract, syntax-tree boundary, game, world, feature, native node, screen, content, input, res, and validate."
    exit 0
}

$results = [System.Collections.Generic.List[object]]::new()
$totalRuns = $cases.Count
$runNumber = 0
foreach ($case in $cases) {
        $runNumber++
        $caseKey = "$($profile.id)-$($case.id)"
        $fixture = Join-Path $fixtureRoot $caseKey
        $artifacts = Join-Path $artifactRoot $caseKey
        New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
        Write-Host "[$runNumber/$totalRuns] $($profile.model)/$($profile.reasoning) :: $($case.id)"
        try {
        $started = Get-Date
        $failures = [System.Collections.Generic.List[string]]::new()
        $exitCode = -1
        $validateExitCode = $null
        $changedFiles = @()
        $finalMessage = ""
        $events = @()
        try {
            Copy-EvalRepository $fixture

            if ($case.setup -in @("clean", "game")) {
                Prepare-CleanFixture $fixture (Join-Path $artifacts "clean-setup.log")
            }

            if ($case.setup -eq "game") {
                $setupLog = Join-Path $artifacts "setup.log"
                $setupExit = Invoke-ChildPowerShell @(
                    "-File", (Join-Path $fixture "lx.ps1"), "create", "game", "EvalBase"
                ) $setupLog
                if ($setupExit -ne 0) {
                    throw "Fixture game setup failed with exit code $setupExit."
                }
            }

            & git -C $fixture init --quiet
            if ($LASTEXITCODE -ne 0) { throw "git init failed for fixture." }
            & git -C $fixture config user.name "LX Eval"
            & git -C $fixture config user.email "lx-eval@invalid.local"
            Write-Utf8 (Join-Path $fixture ".git\info\exclude") ".eval-output/`n"
            & git -C $fixture add -A
            & git -C $fixture commit --quiet -m "evaluation baseline"
            if ($LASTEXITCODE -ne 0) { throw "git baseline commit failed for fixture." }

            $evalOutput = Join-Path $fixture ".eval-output"
            New-Item -ItemType Directory -Path $evalOutput -Force | Out-Null
            $promptPath = Join-Path $evalOutput "prompt.txt"
            $eventsPath = Join-Path $evalOutput "events.jsonl"
            $stderrPath = Join-Path $evalOutput "stderr.log"
            $lastMessagePath = Join-Path $evalOutput "last-message.txt"
            Write-Utf8 $promptPath ([string]$case.prompt)

            $codexArgs = @(
                "exec",
                "--ephemeral",
                "--ignore-user-config",
                "--ignore-rules",
                "--disable", "apps",
                "--disable", "plugins",
                "--disable", "remote_plugin",
                "--disable", "browser_use",
                "--disable", "computer_use",
                "--disable", "image_generation",
                "--disable", "multi_agent",
                "--json",
                "--color", "never",
                "--sandbox", "danger-full-access",
                "--cd", $fixture,
                "--model", [string]$profile.model,
                "--config", "model_reasoning_effort=$($profile.reasoning)",
                "--config", "approval_policy='never'",
                "--output-last-message", $lastMessagePath,
                "-"
            )
            $quotedArgs = @($codexArgs | ForEach-Object {
                '"' + ([string]$_).Replace('"', '\"') + '"'
            }) -join ' '
            $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = $codexExecutable
            $startInfo.Arguments = $quotedArgs
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
            $startInfo.RedirectStandardInput = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $startInfo.StandardOutputEncoding = $utf8
            $startInfo.StandardErrorEncoding = $utf8
            $process = [System.Diagnostics.Process]::new()
            $process.StartInfo = $startInfo
            if (-not $process.Start()) {
                throw "Failed to start Codex CLI."
            }
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            $promptBytes = [System.IO.File]::ReadAllBytes($promptPath)
            $process.StandardInput.BaseStream.Write($promptBytes, 0, $promptBytes.Length)
            $process.StandardInput.Close()
            if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
                $process.Kill()
                $process.WaitForExit()
                $failures.Add("Codex timed out after $TimeoutMinutes minutes.")
            }
            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
            Write-Utf8 $eventsPath $stdout
            Write-Utf8 $stderrPath $stderr
            $exitCode = [int]$process.ExitCode
            if ($exitCode -ne 0) {
                $failures.Add("Codex exited with code $exitCode.")
            }

            foreach ($name in @("events.jsonl", "stderr.log", "last-message.txt", "prompt.txt")) {
                $sourceArtifact = Join-Path $evalOutput $name
                if (Test-Path -LiteralPath $sourceArtifact) {
                    Copy-Item -LiteralPath $sourceArtifact -Destination (Join-Path $artifacts $name)
                }
            }
            if (Test-Path -LiteralPath $lastMessagePath) {
                $finalMessage = $utf8.GetString([System.IO.File]::ReadAllBytes($lastMessagePath))
            }
            $events = @(Read-JsonEvents $eventsPath)
            $changedFiles = @(& git -C $fixture status --porcelain=v1)

            if ($case.expected_write -eq $false -and $changedFiles.Count -gt 0) {
                $failures.Add("Read-only case changed files: $($changedFiles -join ', ')")
            }
            if ($case.expected_write -eq $true -and $changedFiles.Count -eq 0) {
                $failures.Add("Write case produced no repository changes.")
            }
            foreach ($term in @($case.expected_terms | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })) {
                if ($finalMessage.IndexOf([string]$term, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                    $failures.Add("Final response is missing expected term '$term'.")
                }
            }
            foreach ($group in @($case.expected_term_groups)) {
                $groupMatched = $false
                foreach ($term in @($group.any)) {
                    if ($finalMessage.IndexOf([string]$term, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                        $groupMatched = $true
                        break
                    }
                }
                if (-not $groupMatched) {
                    $failures.Add("Final response is missing every term in semantic group: $(@($group.any) -join ', ').")
                }
            }
            foreach ($relative in @($case.expected_files | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })) {
                if (-not (Test-Path -LiteralPath (Join-Path $fixture ([string]$relative)) -PathType Leaf)) {
                    $failures.Add("Expected file '$relative' is missing.")
                }
            }
            if ($case.id -eq "create-game") {
                $manifestPath = Join-Path $fixture "$projectDirectory\content\game\game-manifest.json"
                if (Test-Path -LiteralPath $manifestPath) {
                    $manifest = $utf8.GetString([System.IO.File]::ReadAllBytes($manifestPath)) | ConvertFrom-Json
                    if ($manifest.name -ne "EvalGame") {
                        $failures.Add("Game manifest name is not EvalGame.")
                    }
                }
            }
            if ($null -ne $case.question_count) {
                $questionCount = [regex]::Matches($finalMessage, "[?\uFF1F]").Count
                if ($questionCount -eq 0) {
                    $questionCount = [regex]::Matches($finalMessage, "(?m)^\s*\d+\.\s+\S").Count
                }
                $confirmMarker = ([char]0x8BF7).ToString() + [char]0x786E + [char]0x8BA4
                if ($questionCount -eq 0 -and
                    $finalMessage.IndexOf($confirmMarker, [System.StringComparison]::Ordinal) -ge 0) {
                    $questionCount = 1
                }
                if ($questionCount -lt [int]$case.question_count.min -or
                    $questionCount -gt [int]$case.question_count.max) {
                    $failures.Add("Expected $($case.question_count.min)-$($case.question_count.max) questions, got $questionCount.")
                }
            }
            if ($case.validate -eq $true) {
                $validationLog = Join-Path $artifacts "validation.log"
                $validateExitCode = Invoke-ChildPowerShell @(
                    "-File", (Join-Path $fixture "lx.ps1"), "validate"
                ) $validationLog
                if ($validateExitCode -ne 0) {
                    $failures.Add("Deterministic final validation failed with exit code $validateExitCode.")
                }
            }
        }
        catch {
            $failures.Add($_.Exception.Message)
        }

        $usageEvent = @($events | Where-Object { $_.type -eq "turn.completed" } | Select-Object -Last 1)
        $usage = if ($usageEvent.Count -gt 0) { $usageEvent[0].usage } else { $null }
        $toolCalls = @($events | Where-Object {
            $_.type -eq "item.completed" -and
            $_.item.type -in @("command_execution", "mcp_tool_call", "file_change", "web_search")
        }).Count
        $retries = @($events | Where-Object {
            $_.type -in @("turn.failed", "error") -or
            ($_.type -eq "item.completed" -and
                $_.item.type -eq "command_execution" -and
                $_.item.status -eq "failed" -and
                -not ($_.item.exit_code -eq 1 -and $_.item.command -match '(?i)\brg(?:\.exe)?\s'))
        }).Count
        $inputTokens = if ($null -ne $usage) { [long]$usage.input_tokens } else { 0 }
        $cachedInputTokens = if ($null -ne $usage) { [long]$usage.cached_input_tokens } else { 0 }
        $uncachedInputTokens = $inputTokens - $cachedInputTokens
        $outputTokens = if ($null -ne $usage) { [long]$usage.output_tokens } else { 0 }
        if ($null -ne $case.budgets) {
            foreach ($budget in @(
                @{ Name = "tool_calls"; Actual = $toolCalls },
                @{ Name = "retries"; Actual = $retries },
                @{ Name = "input_tokens"; Actual = $inputTokens },
                @{ Name = "uncached_input_tokens"; Actual = $uncachedInputTokens },
                @{ Name = "output_tokens"; Actual = $outputTokens }
            )) {
                $limit = $case.budgets.($budget.Name)
                if ($null -ne $limit -and [long]$budget.Actual -gt [long]$limit) {
                    $budgetMessage = "$($budget.Name) budget exceeded: $($budget.Actual) > $limit."
                    $failures.Add($budgetMessage)
                }
            }
        }
        $durationSeconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 2)
        $result = [pscustomobject][ordered]@{
            profile = [string]$profile.id
            model = [string]$profile.model
            reasoning = [string]$profile.reasoning
            case = [string]$case.id
            passed = ($failures.Count -eq 0)
            failures = @($failures)
            efficiency_budget_passed = (-not @($failures | Where-Object { $_ -like "* budget exceeded:*" }).Count)
            efficiency_warnings = @()
            exit_code = $exitCode
            validation_exit_code = $validateExitCode
            changed_file_count = $changedFiles.Count
            tool_calls = $toolCalls
            retries = $retries
            input_tokens = $inputTokens
            cached_input_tokens = $cachedInputTokens
            uncached_input_tokens = $uncachedInputTokens
            output_tokens = $outputTokens
            duration_seconds = $durationSeconds
            artifact_path = ".lx/model-evals/$runId/artifacts/$caseKey"
        }
        $results.Add($result)
        $status = if ($result.passed) { "PASS" } else { "FAIL" }
        Write-Host "  $status | tokens=$($result.input_tokens + $result.output_tokens) | tools=$toolCalls | seconds=$durationSeconds"
        if (-not $result.passed) {
            foreach ($failure in $failures) {
                Write-Host "    $failure"
            }
        }
        }
        finally {
            Remove-EvalFixture $fixture
        }
}

$profileResults = @($results)
$profileSummary = [pscustomobject][ordered]@{
        profile = [string]$profile.id
        model = [string]$profile.model
        reasoning = [string]$profile.reasoning
        passed = @($profileResults | Where-Object { $_.passed }).Count
        total = $profileResults.Count
        pass_rate = if ($profileResults.Count -gt 0) {
            [math]::Round(@($profileResults | Where-Object { $_.passed }).Count / $profileResults.Count, 4)
        } else { 0 }
        input_tokens = ($profileResults | Measure-Object input_tokens -Sum).Sum
        cached_input_tokens = ($profileResults | Measure-Object cached_input_tokens -Sum).Sum
        uncached_input_tokens = ($profileResults | Measure-Object uncached_input_tokens -Sum).Sum
        output_tokens = ($profileResults | Measure-Object output_tokens -Sum).Sum
        tool_calls = ($profileResults | Measure-Object tool_calls -Sum).Sum
        retries = ($profileResults | Measure-Object retries -Sum).Sum
        duration_seconds = [math]::Round(($profileResults | Measure-Object duration_seconds -Sum).Sum, 2)
    }
$summary = [pscustomobject][ordered]@{
    schema_version = 1
    run_id = $runId
    generated_at_utc = (Get-Date).ToUniversalTime().ToString("o")
    codex_cli = (& $codexExecutable --version)
    suite = $Suite
    all_passed = (@($results | Where-Object { -not $_.passed }).Count -eq 0)
    profiles = @($profileSummary)
    results = @($results)
}
$summaryJson = $summary | ConvertTo-Json -Depth 10
$summaryPath = Join-Path $runRoot "summary.json"
Write-Utf8 $summaryPath $summaryJson
Write-Utf8 (Join-Path $outputRoot "latest.json") $summaryJson
Write-Host "Summary: $summaryPath"
Write-Host "  $($profileSummary.profile): $($profileSummary.passed)/$($profileSummary.total), tokens=$($profileSummary.input_tokens + $profileSummary.output_tokens), tools=$($profileSummary.tool_calls), seconds=$($profileSummary.duration_seconds)"
if (-not $summary.all_passed) {
    exit 1
}
exit 0
