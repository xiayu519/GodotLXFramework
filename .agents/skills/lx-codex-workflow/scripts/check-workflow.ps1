param()

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\..\.."))
$errors = [System.Collections.Generic.List[string]]::new()
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$expectedSkills = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot ".agents/skills") -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "SKILL.md") } |
    Select-Object -ExpandProperty Name | Sort-Object)

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
    ".codex/start-codex.ps1",
    ".agents/skills/lx-model-eval/scripts/eval-contract.ps1",
    ".agents/skills/lx-model-eval/scripts/test-eval-contract.ps1",
    ".agents/skills/lx-model-eval/scripts/test-outcome.ps1",
    ".agents/skills/lx-model-eval/scripts/recheck-model-eval.ps1",
    ".agents/skills/lx-model-eval/scripts/summarize-acceptance.ps1",
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
        ".codex/start-codex.ps1",
        ".agents/skills/lx-model-eval/scripts/eval-contract.ps1",
        ".agents/skills/lx-model-eval/scripts/test-eval-contract.ps1",
        ".agents/skills/lx-model-eval/scripts/test-outcome.ps1",
        ".agents/skills/lx-model-eval/scripts/recheck-model-eval.ps1",
        ".agents/skills/lx-model-eval/scripts/summarize-acceptance.ps1",
        "lx.ps1",
        "godot_project/lx.ps1",
        "godot_project/tools/LXFramework.Tools/CheckPlan.ps1",
        "godot_project/tools/LXFramework.Tools/TestCheckPlan.ps1",
        "godot_project/tools/LXFramework.Tools/TestIncrementalValidation.ps1",
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

    . (Resolve-RepoPath ".agents/skills/lx-model-eval/scripts/eval-contract.ps1")
    try {
        $evals = Read-EvalContract (Resolve-RepoPath ".agents/skills/lx-model-eval/evals/evals.json")
        Assert-EvalProjectConfig (Resolve-RepoPath ".codex/config.toml") $evals
    } catch { Add-WorkflowError $_.Exception.Message }

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

    # Remote engine provisioning is opt-in, independent of a normal Git push.
    $ci = Read-WorkflowText '.github/workflows/validate.yml'
    $events = [regex]::Match($ci, '(?ms)^on:\s*\r?\n(?<events>.*?)(?=^\S|\z)')
    $triggers = @([regex]::Matches($events.Groups['events'].Value, '(?m)^  ([a-z_]+):') |
        ForEach-Object { $_.Groups[1].Value })
    if (-not $events.Success -or $triggers.Count -ne 1 -or $triggers[0] -ne 'workflow_dispatch') {
        Add-WorkflowError 'Remote validation must have only an explicit workflow_dispatch trigger.'
    }

    $budgets = @{
        "AGENTS.md" = 4096
        ".codex/memory/INDEX.md" = 1536
    }
    foreach ($item in $budgets.GetEnumerator()) {
        $length = (Get-Item -LiteralPath (Resolve-RepoPath $item.Key)).Length
        if ($length -gt $item.Value) {
            Write-Warning "'$($item.Key)' is $length bytes; review the $($item.Value)-byte context target."
        }
    }
    # Repository validation must not require an editor-bundled search executable.
    # Prune generated/cache directories before traversal, not after enumeration.
    $agentFiles = [System.Collections.Generic.List[string]]::new()
    $pendingDirectories = [System.Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push($repoRoot)
    while ($pendingDirectories.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pendingDirectories.Pop() -Force) {
            if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { continue }
            if ($entry.PSIsContainer) {
                if ($entry.Name -notin @('.git','.lx','.godot','.mono','.tools','bin','obj','build','artifacts','research')) {
                    $pendingDirectories.Push($entry.FullName)
                }
            }
            elseif ($entry.Name -eq 'AGENTS.md') {
                $agentFiles.Add($entry.FullName.Substring($repoRoot.Length + 1).Replace('\','/'))
            }
        }
    }
    foreach ($relativeAgent in $agentFiles) {
        $path = Get-Item -LiteralPath (Resolve-RepoPath $relativeAgent)
        if ($relativeAgent -ne 'AGENTS.md' -and $path.Length -gt 1800) {
            Write-Warning "Nested instruction '$relativeAgent' exceeds the 1800-byte context target."
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
        if ($skill -notmatch ("\A---\r?\nname: " + [regex]::Escape($skillName) + "\r?\n")) {
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
            Write-Warning "Skill '$skillName' entrypoint is $skillBytes bytes; review the 3072-byte context target."
        }
        $referenceDirectory = Resolve-RepoPath ".agents/skills/$skillName/references"
        $referenceCount = if (Test-Path -LiteralPath $referenceDirectory -PathType Container) {
            @(Get-ChildItem -LiteralPath $referenceDirectory -File).Count
        }
        else { 0 }
        if ($referenceCount -gt 5) {
            Write-Warning "Skill '$skillName' owns $referenceCount references; review routing and semantic scope."
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
        Write-Warning "Skill discovery descriptions total $totalDescriptionBytes bytes; review the 2048-byte context target."
    }

    foreach ($category in @("problems", "decisions", "feedback", "references")) {
        if (-not (Test-Path -LiteralPath (Resolve-RepoPath ".codex/memory/$category") -PathType Container)) {
            Add-WorkflowError "Project Knowledge is missing the '$category/' category."
        }
    }

    try {
        $evals = Read-EvalContract (Resolve-RepoPath ".agents/skills/lx-model-eval/evals/evals.json")
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
        Write-Error "Codex workflow: $errorMessage" -ErrorAction Continue
    }
    exit 1
}

Write-Host "Codex workflow check passed: native layering, isolated Skill budgets/routes, project knowledge, and Astra multi-effort eval schema are valid."
exit 0
