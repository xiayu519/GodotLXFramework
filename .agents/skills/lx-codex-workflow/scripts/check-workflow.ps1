param()

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\.."))
$errors = [System.Collections.Generic.List[string]]::new()
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)

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
    ".agents/skills/lx-dev/SKILL.md",
    ".agents/skills/lx-dev/agents/openai.yaml",
    ".agents/skills/lx-dev/references/data-workflow.md",
    ".agents/skills/lx-dev/references/migration-workflow.md",
    ".agents/skills/lx-dev/references/persistence-workflow.md",
    ".agents/skills/lx-dev/references/tooling-workflow.md",
    ".agents/skills/lx-codex-workflow/SKILL.md",
    ".agents/skills/lx-codex-workflow/agents/openai.yaml",
    ".agents/skills/lx-codex-workflow/references/codex-native-workflow.md",
    ".agents/skills/lx-codex-workflow/references/project-knowledge.md",
    ".agents/skills/lx-codex-workflow/references/model-evaluation.md",
    ".agents/skills/lx-codex-workflow/evals/evals.json",
    ".agents/skills/lx-codex-workflow/scripts/check-workflow.ps1",
    ".agents/skills/lx-codex-workflow/scripts/run-model-evals.ps1",
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

foreach ($relative in @(
    ".codex/framework.json",
    ".codex/validation-map.json",
    ".codex/memory/PROJECT.md"
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
        ".agents/skills/lx-codex-workflow/scripts/run-model-evals.ps1"
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
        "lx-dev",
        "lx-codex-workflow",
        '修改目标前读取沿途最近的 `AGENTS.md`'
    )) {
        if ($rootAgents.IndexOf($legacy, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Add-WorkflowError "Root AGENTS.md contains redundant or legacy routing '$legacy'."
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
        ".agents/skills/lx-dev/SKILL.md" = 6144
        ".agents/skills/lx-codex-workflow/SKILL.md" = 6144
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

    foreach ($skillName in @("lx-dev", "lx-codex-workflow")) {
        $skill = Read-WorkflowText ".agents/skills/$skillName/SKILL.md"
        if (-not $skill.StartsWith("---`nname: $skillName`n", [System.StringComparison]::Ordinal)) {
            Add-WorkflowError "Skill '$skillName' is missing the expected frontmatter name."
        }
        if ($skill.IndexOf("`ndescription:", [System.StringComparison]::Ordinal) -lt 0) {
            Add-WorkflowError "Skill '$skillName' is missing its description."
        }
        $metadata = Read-WorkflowText ".agents/skills/$skillName/agents/openai.yaml"
        $expectedPromptMarker = '$' + $skillName
        if ($metadata.IndexOf($expectedPromptMarker, [System.StringComparison]::Ordinal) -lt 0 -or
            $metadata.IndexOf("allow_implicit_invocation: true", [System.StringComparison]::Ordinal) -lt 0) {
            Add-WorkflowError "Skill '$skillName' metadata is missing an explicit default prompt or implicit invocation policy."
        }
    }

    foreach ($category in @("problems", "decisions", "feedback", "references")) {
        if (-not (Test-Path -LiteralPath (Resolve-RepoPath ".codex/memory/$category") -PathType Container)) {
            Add-WorkflowError "Project Knowledge is missing the '$category/' category."
        }
    }

    try {
        $evals = Read-WorkflowText ".agents/skills/lx-codex-workflow/evals/evals.json" | ConvertFrom-Json
        $profiles = @($evals.profiles)
        if ($profiles.Count -ne 1 -or
            $profiles[0].id -ne "sol-high" -or
            $profiles[0].model -ne "gpt-5.6-sol" -or
            $profiles[0].reasoning -ne "high" -or
            $profiles[0].required -ne $true) {
            Add-WorkflowError "Model evaluation must contain only the required sol-high profile."
        }
        foreach ($case in $evals.cases) {
            if ([string]::IsNullOrWhiteSpace($case.id) -or [string]::IsNullOrWhiteSpace($case.prompt)) {
                Add-WorkflowError "A model evaluation case is missing id or prompt."
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

Write-Host "Codex workflow check passed: native layering, skills, project knowledge, and the Sol/high eval profile are valid."
exit 0
