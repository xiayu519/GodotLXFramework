# Shared read-only planning for the existing lx check entry point.
function Get-LxCheckPlan([string]$ProjectRoot, [string[]]$Paths) {
    $repoRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
    $workspaceRoot = Split-Path -Parent $repoRoot
    $CommandArguments = @($Paths)
    if ($CommandArguments.Count -eq 0) { throw 'check requires one or more changed paths.' }
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

    $changedPaths = @($changedPaths | Sort-Object -Unique)
    $isDocumentation = @($changedPaths | Where-Object {
        ($_ -notlike '*.md' -and $_ -ne 'LICENSE') -or
        $_ -like '*AGENTS.md' -or $_ -like '.agents/*' -or $_ -like '.codex/*' -or
        (Test-Path -LiteralPath (Join-Path $repoRoot $_) -PathType Container)
    }).Count -eq 0
    $isWorkflow = @($changedPaths | Where-Object {
        $_ -notlike '*.md' -and $_ -ne 'LICENSE' -and
        $_ -notlike '.agents/*' -and $_ -notlike '.codex/*' -and $_ -notlike '.github/*'
    }).Count -eq 0
    if ($isDocumentation -or $isWorkflow) {
        return [pscustomobject]@{
            schema='lx.check-plan'; schemaVersion=1; changedPaths=@($changedPaths)
            stages=@(if ($isDocumentation) {'documents'} else {'documents';'workflow'})
            requiresDotnet=$false; requiresGodot=$false
            needs=@{workflow=(-not $isDocumentation); documents=$true}
            testFilter=$null
            reason='Only repository text/instructions changed; engine, SDK and model tools are not prerequisites.'
        }
    }
    $needsData = [bool]($changedPaths | Where-Object {
        $_ -like "game_design/schema/*" -or
        $_ -like "game_design/data/*" -or
        $_ -like "game_design/fixtures/*" -or
        $_ -in @(
            "game_design", "game_design/schema", "game_design/data", "game_design/fixtures",
            "content/data/luban",
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
    $needsData = $needsData -or -not (Test-Path -LiteralPath (Join-Path $repoRoot ".lx/luban/report.json") -PathType Leaf)
    $needsGenerate = [bool]($changedPaths | Where-Object {
        $_ -in @("content", "content/ui", "content/res", "content/input", "content/game", "content/features", "content/data", "scene", "scene/ui") -or
        $_ -like "content/*/*-manifest.json" -or
        ($_ -like "scene/ui/*" -and $_ -notlike "*.md") -or
        $_ -like "tools/LXFramework.Tools/*Generator.cs" -or
        $_ -like "tools/LXFramework.Tools/*Manifest.cs"
    })
    $needsTests = [bool]($changedPaths | Where-Object {
        $_ -in @("src", "src/LXFramework.Core", "tests", "tests/LXFramework.Core.Tests") -or
        ($_ -like "src/LXFramework.Core/*" -and $_ -notlike "*.md") -or
        ($_ -like "tests/LXFramework.Core.Tests/*" -and $_ -notlike "*.md")
    })
    $needsFrameworkSmoke = [bool]($changedPaths | Where-Object {
        $_ -in @("src", "src/LXFramework", "content", "content/res") -or
        ($_ -like "src/LXFramework/*" -and $_ -notlike "*.md") -or
        $_ -eq "tests/Runtime" -or
        ($_ -like "tests/Runtime/*" -and $_ -notlike "*.md") -or
        $_ -eq "tools/LXFramework.Tools/GodotSmoke.cs" -or
        ($_ -like "content/res/*" -and $_ -notlike "*.md") -or
        $_ -eq "scene/main.tscn" -or
        $_ -eq "project.godot"
    })
    $resourceManifestPath = Join-Path $repoRoot "content\res\res-manifest.json"
    if (-not $needsFrameworkSmoke -and
        (Test-Path -LiteralPath $resourceManifestPath -PathType Leaf)) {
        $resourceManifest = Get-Content -LiteralPath $resourceManifestPath -Raw -Encoding UTF8 |
            ConvertFrom-Json
        $registeredResourcePaths = @($resourceManifest.assets | ForEach-Object {
            ([string]$_.path).Replace("res://", "")
        })
        $needsFrameworkSmoke = [bool]($changedPaths | Where-Object {
            $changedResourcePath = $_
            $registeredResourcePaths | Where-Object {
                $_ -eq $changedResourcePath -or
                $_.StartsWith($changedResourcePath.TrimEnd("/") + "/", [System.StringComparison]::OrdinalIgnoreCase) -or
                $changedResourcePath.StartsWith($_.TrimEnd("/") + "/", [System.StringComparison]::OrdinalIgnoreCase)
            }
        })
    }
    $needsProductSmoke = [bool]($changedPaths | Where-Object {
        $_ -in @("scene", "script", "content") -or
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
        $_ -in @("src", "src/LXFramework", "src/LXFramework/UI", "scene", "scene/ui", "scene/ui/examples") -or
        ($_ -like "src/LXFramework/UI/*" -and $_ -notlike "*.md") -or
        ($_ -like "scene/ui/examples/*" -and $_ -notlike "*.md")
    })
    $needsSolutionBuild = [bool]($changedPaths | Where-Object {
        $_ -in @("src", "src/LXFramework.Core") -or
        ($_ -like "src/LXFramework.Core/*" -and $_ -notlike "*.md") -or
        $_ -eq "src/LXFramework.Core/LXFramework.Core.csproj" -or
        $_ -like "*.sln" -or
        $_ -eq "Directory.Build.props"
    })
    $needsProductBuild = [bool]($changedPaths | Where-Object {
        $_ -in @("src", "src/LXFramework", "script") -or
        ($_ -like "src/LXFramework/*" -and $_ -notlike "*.md") -or
        $_ -like "script/*.cs" -or
        $_ -eq "LXFramework.csproj"
    })
    $needsProductBuild = $needsProductBuild -or $needsGenerate -or $needsData
    $needsWorkflow = [bool]($changedPaths | Where-Object {
        $_ -like '*AGENTS.md' -or $_ -like '.agents/*' -or $_ -like '.codex/*' -or
        $_ -like '.github/*' -or $_ -like 'tools/*' -or $_ -eq 'lx.ps1'
    })
    $profile = @()
    if ($needsWorkflow) { $profile += "workflow" }
    if ($needsData) { $profile += "data" }
    if ($needsGenerate) { $profile += "generate" }
    $profile += "static-changed"
    if ($needsSolutionBuild) { $profile += "solution-build" }
    elseif ($needsProductBuild) { $profile += "product-build" }
    if ($needsTests) { $profile += "test" }
    if ($needsFrameworkSmoke) { $profile += "framework-smoke" }
    if ($needsProductSmoke) { $profile += "product-smoke-affected" }
    if ($needsProductSmoke) { $profile += "product-visual-affected" }
    if ($needsFrameworkVisual) { $profile += "framework-visual" }

    # Test-source-only edits can select a class; production Core changes retain
    # the small full Core suite because their dependency impact is shared.
    $testFilter = $null
    $testCandidates = @($changedPaths | Where-Object { $_ -notlike '*.md' -and $_ -ne 'LICENSE' })
    if ($needsTests -and @($testCandidates | Where-Object {
        $_ -notlike 'tests/LXFramework.Core.Tests/*Tests.cs'
    }).Count -eq 0) {
        $classes = @()
        foreach ($testPath in $testCandidates) {
            $file = Join-Path $repoRoot $testPath
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { $classes=@(); break }
            $source = Get-Content -LiteralPath $file -Raw -Encoding UTF8
            $classMatches = [regex]::Matches($source, '(?m)^public (?:sealed )?class (\w+Tests)\b')
            if ($classMatches.Count -ne 1) { $classes=@(); break }
            $classes += 'FullyQualifiedName~.' + $classMatches[0].Groups[1].Value + '.'
        }
        if ($classes.Count -gt 0) { $testFilter = $classes -join '|' }
    }
    return [pscustomobject]@{
        schema='lx.check-plan'; schemaVersion=1; changedPaths=@($changedPaths); stages=@($profile)
        requiresDotnet=$true
        requiresGodot=($needsFrameworkSmoke -or $needsProductSmoke -or $needsFrameworkVisual)
        needs=@{
            workflow=$needsWorkflow; documents=[bool]($changedPaths | Where-Object { $_ -like '*.md' })
            data=$needsData; generate=$needsGenerate
            solutionBuild=$needsSolutionBuild; productBuild=$needsProductBuild; tests=$needsTests
            frameworkSmoke=$needsFrameworkSmoke; productSmoke=$needsProductSmoke
            frameworkVisual=$needsFrameworkVisual
        }
        testFilter=$testFilter
        reason='Stages follow changed paths; product smoke/visual targets are selected by the manifest, never expanded to all.'
    }
}

function Test-LxChangedText([string]$ProjectRoot, [string[]]$Paths) {
    $workspace = Split-Path -Parent $ProjectRoot
    $utf8 = [System.Text.UTF8Encoding]::new($false,$true)
    foreach ($relative in $Paths) {
        $candidate = if ([System.IO.Path]::IsPathRooted($relative)) { $relative } else { Join-Path $workspace $relative }
        if (-not (Test-Path -LiteralPath $candidate)) { $candidate = Join-Path $ProjectRoot $relative }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        if ([System.IO.Path]::GetExtension($candidate) -notin @('.md','.ps1','.json','.toml','.yaml','.yml') -and
            [System.IO.Path]::GetFileName($candidate) -ne 'LICENSE') { continue }
        $content = $utf8.GetString([System.IO.File]::ReadAllBytes($candidate))
        if ($content -match '(?m)^(<<<<<<< |>>>>>>> )') { throw "Unresolved conflict in '$relative'." }
        if ($candidate.EndsWith('.ps1',[System.StringComparison]::OrdinalIgnoreCase)) {
            $tokens=$null; $parseErrors=$null
            [void][System.Management.Automation.Language.Parser]::ParseFile($candidate,[ref]$tokens,[ref]$parseErrors)
            if (@($parseErrors).Count) { throw "PowerShell syntax errors in '$relative': $($parseErrors.Message -join '; ')" }
        }
        if ($candidate.EndsWith('.json',[System.StringComparison]::OrdinalIgnoreCase)) {
            $null = $content | ConvertFrom-Json -ErrorAction Stop
        }
        if ($candidate.EndsWith('.md',[System.StringComparison]::OrdinalIgnoreCase)) {
            # Ignore examples in fenced blocks; validate actual relative links only.
            $markdown = [regex]::Replace($content, '(?ms)^[ \t]*`{3,}[^\r\n]*\r?\n.*?^[ \t]*`{3,}[ \t]*$', '')
            foreach ($link in [regex]::Matches($markdown, '\]\(([^)]+)\)')) {
                $target = $link.Groups[1].Value.Trim()
                if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#)' -or $target -match '[<> ]') { continue }
                $target = ($target -split '#',2)[0]
                if ($target.Length -eq 0) { continue }
                $destination = Join-Path (Split-Path -Parent $candidate) ([uri]::UnescapeDataString($target))
                if (-not (Test-Path -LiteralPath $destination)) { throw "Broken local link in '${relative}': $target" }
            }
        }
    }
    Write-Host 'Changed repository text passed; no engine or model tools invoked.'
}
