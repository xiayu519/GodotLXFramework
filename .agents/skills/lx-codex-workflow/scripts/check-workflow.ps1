param()

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\.."))
$errors = [System.Collections.Generic.List[string]]::new()
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$expectedSkills = @(
    "lx-capabilities",
    "lx-codex-workflow",
    "lx-content",
    "lx-data",
    "lx-editor-tools",
    "lx-framework",
    "lx-game",
    "lx-input",
    "lx-maintenance",
    "lx-migrate",
    "lx-model-eval",
    "lx-persistence",
    "lx-project-knowledge",
    "lx-resources",
    "lx-runtime-observe",
    "lx-ui"
)

function Add-WorkflowError([string]$message) {
    $errors.Add($message)
}

function Resolve-RepoPath([string]$relativePath) {
    Join-Path $repoRoot ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
}

function Read-WorkflowText([string]$relativePath) {
    $path = Resolve-RepoPath $relativePath
    try {
        $strictUtf8.GetString([System.IO.File]::ReadAllBytes($path))
    }
    catch {
        Add-WorkflowError "'$relativePath' is not valid UTF-8."
        ""
    }
}

$requiredFiles = @(
    "AGENTS.md",
    "godot_project/AGENTS.md",
    "README.md",
    "Books/AI-Development-Workflow.md",
    ".codex/config.toml",
    ".codex/memory/INDEX.md",
    "game_design/AGENTS.md",
    "game_design/README.md",
    "game_design/build.bat",
    "game_design/build.ps1",
    "game_design/install-luban.ps1",
    "game_design/luban.conf",
    "game_design/toolchain.json",
    "game_design/schema/design.xml",
    "game_design/data/design_probe.json",
    ".agents/skills/lx-codex-workflow/SKILL.md",
    ".agents/skills/lx-codex-workflow/agents/openai.yaml",
    ".agents/skills/lx-codex-workflow/references/codex-native-workflow.md",
    ".agents/skills/lx-codex-workflow/scripts/check-workflow.ps1",
    ".agents/skills/lx-model-eval/evals/evals.json",
    ".agents/skills/lx-model-eval/references/model-evaluation.md",
    ".agents/skills/lx-model-eval/scripts/run-model-evals.ps1",
    "godot_project/src/LXFramework.Core/AGENTS.md",
    "godot_project/src/LXFramework/AGENTS.md",
    "godot_project/tools/LXFramework.Tools/AGENTS.md",
    "godot_project/content/AGENTS.md",
    "godot_project/scene/AGENTS.md"
)
foreach ($relative in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Resolve-RepoPath $relative) -PathType Leaf)) {
        Add-WorkflowError "Required workflow file '$relative' is missing."
    }
}
foreach ($skillName in $expectedSkills) {
    foreach ($relative in @(
        ".agents/skills/$skillName/SKILL.md",
        ".agents/skills/$skillName/agents/openai.yaml"
    )) {
        if (-not (Test-Path -LiteralPath (Resolve-RepoPath $relative) -PathType Leaf)) {
            Add-WorkflowError "Required skill file '$relative' is missing."
        }
    }
}

foreach ($relative in @(
    ".codex/framework.json",
    ".codex/validation-map.json",
    ".codex/memory/PROJECT.md",
    ".agents/skills/lx-dev",
    ".agents/skills/lx-ai-control"
)) {
    if (Test-Path -LiteralPath (Resolve-RepoPath $relative)) {
        Add-WorkflowError "Legacy non-native entry '$relative' must be removed."
    }
}

if ($errors.Count -eq 0) {
    foreach ($relative in @(
        "lx.ps1",
        "godot_project/lx.ps1",
        ".agents/skills/lx-codex-workflow/scripts/check-workflow.ps1",
        ".agents/skills/lx-model-eval/scripts/run-model-evals.ps1"
    )) {
        $tokens = $null
        $parseErrors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            (Resolve-RepoPath $relative),
            [ref]$tokens,
            [ref]$parseErrors)
        foreach ($parseError in @($parseErrors)) {
            Add-WorkflowError "PowerShell syntax error in '$relative' at line $($parseError.Extent.StartLineNumber): $($parseError.Message)"
        }
    }

    $config = Read-WorkflowText ".codex/config.toml"
    foreach ($marker in @(
        'model = "gpt-5.6-sol"',
        'model_reasoning_effort = "high"',
        'plan_mode_reasoning_effort = "high"'
    )) {
        if ($config.IndexOf($marker, [System.StringComparison]::Ordinal) -lt 0) {
            Add-WorkflowError ".codex/config.toml is missing '$marker'."
        }
    }

    $rootAgents = Read-WorkflowText "AGENTS.md"
    foreach ($marker in @(
        "./lx.ps1 check <changed-path> [...]",
        "./lx.ps1 validate",
        "LX.UI.*",
        "LX.Res.*"
    )) {
        if ($rootAgents.IndexOf($marker, [System.StringComparison]::Ordinal) -lt 0) {
            Add-WorkflowError "Root AGENTS.md is missing '$marker'."
        }
    }
    foreach ($legacy in @(
        "T0-T3",
        "Direct/Planned/Deep",
        ".codex/framework.json",
        ".codex/validation-map.json",
        "gpt-5.6-sol",
        "reasoning",
        '修改目标前读取沿途最近的 `AGENTS.md`'
    )) {
        if ($rootAgents.IndexOf($legacy, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Add-WorkflowError "Root AGENTS.md contains redundant or legacy routing '$legacy'."
        }
    }
    foreach ($skillName in $expectedSkills) {
        if ($rootAgents.IndexOf($skillName, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Add-WorkflowError "Root AGENTS.md contains redundant Skill routing '$skillName'."
        }
    }

    $lxEntry = Read-WorkflowText "godot_project/lx.ps1"
    foreach ($marker in @("lx.command-report", "LX_OK", "LX_CLI_USAGE", "LX_COMMAND_FAILED")) {
        if ($lxEntry.IndexOf($marker, [System.StringComparison]::Ordinal) -lt 0) {
            Add-WorkflowError "lx JSON command contract is missing '$marker'."
        }
    }

    $budgets = @{
        "AGENTS.md" = 3072
        ".codex/memory/INDEX.md" = 1536
    }
    foreach ($item in $budgets.GetEnumerator()) {
        $length = (Get-Item -LiteralPath (Resolve-RepoPath $item.Key)).Length
        if ($length -gt $item.Value) {
            Add-WorkflowError "'$($item.Key)' is $length bytes and exceeds the $($item.Value)-byte budget."
        }
    }
    foreach ($path in Get-ChildItem -LiteralPath $repoRoot -Filter "AGENTS.md" -File -Recurse) {
        $relativeAgent = $path.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        $agentSegments = @($relativeAgent.Split('/'))
        $ignoredAgent = @($agentSegments) | Where-Object {
            $_ -in @("bin", "obj") -or $_.StartsWith(".", [System.StringComparison]::Ordinal)
        }
        if ($ignoredAgent.Count -eq 0 -and
            $path.FullName -ne (Resolve-RepoPath "AGENTS.md") -and
            $path.Length -gt 1800) {
            Add-WorkflowError "Nested instruction '$relativeAgent' exceeds 1800 bytes."
        }
    }

    $skillRoot = Resolve-RepoPath ".agents/skills"
    $actualSkills = @(Get-ChildItem -LiteralPath $skillRoot -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "SKILL.md") } |
        Select-Object -ExpandProperty Name |
        Sort-Object)
    foreach ($unexpected in @($actualSkills | Where-Object { $_ -notin $expectedSkills })) {
        Add-WorkflowError "Unexpected repository Skill '$unexpected' is not in the semantic routing contract."
    }
    foreach ($missing in @($expectedSkills | Where-Object { $_ -notin $actualSkills })) {
        Add-WorkflowError "Expected repository Skill '$missing' is missing."
    }

    $totalDescriptionBytes = 0
    foreach ($skillName in $expectedSkills) {
        $skill = Read-WorkflowText ".agents/skills/$skillName/SKILL.md"
        if (-not $skill.StartsWith("---`nname: $skillName`n", [System.StringComparison]::Ordinal)) {
            Add-WorkflowError "Skill '$skillName' is missing the expected frontmatter name."
        }
        $descriptionMatch = [System.Text.RegularExpressions.Regex]::Match(
            $skill,
            '(?m)^description:\s*(.+)$')
        if (-not $descriptionMatch.Success) {
            Add-WorkflowError "Skill '$skillName' is missing its description."
        }
        else {
            $descriptionBytes = $strictUtf8.GetByteCount($descriptionMatch.Groups[1].Value.Trim())
            $totalDescriptionBytes += $descriptionBytes
            if ($descriptionBytes -gt 512) {
                Add-WorkflowError "Skill '$skillName' description is $descriptionBytes bytes and exceeds 512 bytes."
            }
        }
        $skillBytes = (Get-Item -LiteralPath (Resolve-RepoPath ".agents/skills/$skillName/SKILL.md")).Length
        if ($skillBytes -gt 3072) {
            Add-WorkflowError "Skill '$skillName' entrypoint is $skillBytes bytes and exceeds 3072 bytes."
        }
        $referenceDirectory = Resolve-RepoPath ".agents/skills/$skillName/references"
        $referenceCount = if (Test-Path -LiteralPath $referenceDirectory -PathType Container) {
            @(Get-ChildItem -LiteralPath $referenceDirectory -File).Count
        }
        else { 0 }
        if ($referenceCount -gt 5) {
            Add-WorkflowError "Skill '$skillName' owns $referenceCount references; split independent semantic domains."
        }
        foreach ($match in [System.Text.RegularExpressions.Regex]::Matches(
            $skill,
            '`references/([^`]+)`')) {
            $reference = $match.Groups[1].Value
            if (-not (Test-Path -LiteralPath (Resolve-RepoPath ".agents/skills/$skillName/references/$reference") -PathType Leaf)) {
                Add-WorkflowError "Skill '$skillName' links missing reference '$reference'."
            }
        }
        $metadata = Read-WorkflowText ".agents/skills/$skillName/agents/openai.yaml"
        $expectedPromptMarker = '$' + $skillName
        if ($metadata.IndexOf($expectedPromptMarker, [System.StringComparison]::Ordinal) -lt 0 -or
            $metadata.IndexOf("allow_implicit_invocation: true", [System.StringComparison]::Ordinal) -lt 0) {
            Add-WorkflowError "Skill '$skillName' metadata is missing an explicit default prompt or implicit invocation policy."
        }
    }
    if ($totalDescriptionBytes -gt 2048) {
        Add-WorkflowError "Skill discovery descriptions total $totalDescriptionBytes bytes and exceed 2048 bytes."
    }

    foreach ($category in @("problems", "decisions", "feedback", "references")) {
        if (-not (Test-Path -LiteralPath (Resolve-RepoPath ".codex/memory/$category") -PathType Container)) {
            Add-WorkflowError "Project Knowledge is missing the '$category/' category."
        }
    }

    try {
        $evals = Read-WorkflowText ".agents/skills/lx-model-eval/evals/evals.json" | ConvertFrom-Json
        $profiles = @($evals.profiles)
        if ($profiles.Count -ne 1 -or
            $profiles[0].id -ne "sol-high" -or
            $profiles[0].model -ne "gpt-5.6-sol" -or
            $profiles[0].reasoning -ne "high" -or
            $profiles[0].required -ne $true) {
            Add-WorkflowError "Model evaluation must contain only the required sol-high profile."
        }
        $coveredSkills = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal)
        foreach ($case in $evals.cases) {
            if ([string]::IsNullOrWhiteSpace($case.id) -or [string]::IsNullOrWhiteSpace($case.prompt)) {
                Add-WorkflowError "A model evaluation case is missing id or prompt."
            }
            $caseExpected = @($case.expected_skills | Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_)
            })
            if ($caseExpected.Count -eq 0) {
                Add-WorkflowError "Model evaluation '$($case.id)' does not declare expected_skills."
            }
            foreach ($skillName in $caseExpected) {
                if ($skillName -notin $expectedSkills) {
                    Add-WorkflowError "Model evaluation '$($case.id)' expects unknown Skill '$skillName'."
                }
                [void]$coveredSkills.Add([string]$skillName)
            }
            foreach ($skillName in @($case.forbidden_skills | Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_)
            })) {
                if ($skillName -notin $expectedSkills) {
                    Add-WorkflowError "Model evaluation '$($case.id)' forbids unknown Skill '$skillName'."
                }
                if ($skillName -in $caseExpected) {
                    Add-WorkflowError "Model evaluation '$($case.id)' both expects and forbids Skill '$skillName'."
                }
            }
        }
        foreach ($skillName in $expectedSkills) {
            if (-not $coveredSkills.Contains($skillName)) {
                Add-WorkflowError "Semantic routing evals do not cover Skill '$skillName'."
            }
        }
    }
    catch {
        Add-WorkflowError "Model evaluation schema is invalid: $($_.Exception.Message)"
    }
}

if ($errors.Count -gt 0) {
    foreach ($errorMessage in $errors) {
        Write-Error "Codex workflow: $errorMessage"
    }
    exit 1
}

Write-Host "Codex workflow check passed: native layering, isolated Skill budgets/routes, project knowledge, and Sol/high eval schema are valid."
exit 0
