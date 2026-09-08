param()
$ErrorActionPreference='Stop'
$projectRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$workspaceRoot=Split-Path -Parent $projectRoot
. (Join-Path $PSScriptRoot 'CheckPlan.ps1')
$assertions=0
function Assert-Check([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:assertions++
}
function Assert-Rejected([scriptblock]$Action,[string]$Message) {
    $rejected=$false
    try { & $Action | Out-Null } catch { $rejected=$true }
    Assert-Check $rejected $Message
}

$docs=Get-LxCheckPlan $projectRoot @('README.md','Books/AI-Development-Workflow.md','README.md')
Assert-Check (($docs.stages -join ',') -eq 'documents') 'Documentation selected engine checks.'
Assert-Check (-not $docs.requiresDotnet -and -not $docs.requiresGodot) 'Documentation requires an SDK.'
Assert-Check ($docs.changedPaths.Count -eq 2) 'Changed paths were not deduplicated.'
$workflow=Get-LxCheckPlan $projectRoot @('AGENTS.md','.codex/config.toml')
Assert-Check (($workflow.stages -join ',') -eq 'documents,workflow') 'Instructions were treated as plain documentation.'
$nested=Get-LxCheckPlan $projectRoot @('godot_project/src/LXFramework.Core/AGENTS.md')
Assert-Check $nested.needs.workflow 'Nested AGENTS.md skipped instruction checks.'
$core=Get-LxCheckPlan $projectRoot @('godot_project/src/LXFramework.Core/Events/EventHub.cs')
Assert-Check ($core.needs.tests -and $core.needs.solutionBuild) 'Core code lost build/test coverage.'
Assert-Check ([string]::IsNullOrEmpty($core.testFilter)) 'Production Core code incorrectly narrowed shared tests.'
$test=Get-LxCheckPlan $projectRoot @('tests/LXFramework.Core.Tests/EventHubTests.cs')
Assert-Check ($test.testFilter -eq 'FullyQualifiedName~.EventHubTests.') 'Test-only edit did not select its class.'
$testAndDocs=Get-LxCheckPlan $projectRoot @('README.md','tests/LXFramework.Core.Tests/EventHubTests.cs')
Assert-Check ($testAndDocs.testFilter -eq $test.testFilter) 'Unrelated documentation expanded a test-only edit.'
$coreDirectory=Get-LxCheckPlan $projectRoot @('src/LXFramework.Core')
Assert-Check ($coreDirectory.needs.tests -and $coreDirectory.needs.solutionBuild) 'A Core directory lost descendant coverage.'
$dataDirectory=Get-LxCheckPlan $projectRoot @('game_design/data')
Assert-Check $dataDirectory.needs.data 'A Luban source directory skipped generation.'
$uiDirectory=Get-LxCheckPlan $projectRoot @('scene/ui')
Assert-Check ($uiDirectory.needs.generate -and $uiDirectory.needs.frameworkVisual) 'A UI directory lost descendant coverage.'
$sharedTest=Get-LxCheckPlan $projectRoot @('tests/LXFramework.Core.Tests/Usings.cs')
Assert-Check ([string]::IsNullOrEmpty($sharedTest.testFilter) -and $sharedTest.needs.tests) 'Shared test setup narrowed coverage.'
$mixed=Get-LxCheckPlan $projectRoot @('README.md','script/Demo/Actor.cs')
Assert-Check ($mixed.needs.documents -and $mixed.needs.productBuild -and $mixed.needs.productSmoke) 'Mixed documentation/code paths lost coverage.'
$ui=Get-LxCheckPlan $projectRoot @('scene/ui/Example.tscn')
Assert-Check ($ui.needs.generate -and $ui.needs.productSmoke -and -not $ui.needs.frameworkVisual) 'Product UI selected unrelated framework visuals.'
$frameworkUi=Get-LxCheckPlan $projectRoot @('src/LXFramework/UI/UIScreen.cs')
Assert-Check ($frameworkUi.needs.frameworkVisual -and $frameworkUi.needs.frameworkSmoke) 'Framework UI lost smoke/visual coverage.'
$data=Get-LxCheckPlan $projectRoot @('game_design/data/design_probe.json')
Assert-Check $data.needs.data 'Luban source change skipped generation.'
$tool=Get-LxCheckPlan $projectRoot @('tools/LXFramework.Tools/Validator.cs')
Assert-Check $tool.needs.workflow 'Verification tooling skipped workflow validation.'
$absolute=Get-LxCheckPlan $projectRoot @((Join-Path $projectRoot 'src/LXFramework.Core/Events/EventHub.cs'))
Assert-Check (($absolute.changedPaths -join ',') -eq ($core.changedPaths -join ',')) 'Absolute and relative inputs differ.'
Assert-Rejected { Get-LxCheckPlan $projectRoot @('../outside.cs') } 'Parent traversal was accepted.'
Assert-Rejected { Get-LxCheckPlan $projectRoot @() } 'Empty check input was accepted.'
Assert-Rejected { Get-LxCheckPlan $projectRoot @('C:/outside-lx-test.cs') } 'Outside workspace path was accepted.'

$testDirectory=Join-Path $workspaceRoot ('.lx/check-plan-tests/'+[guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path (Join-Path $testDirectory 'godot_project') -Force)
$utf8=[System.Text.UTF8Encoding]::new($false)
try {
    $fixtureProject=Join-Path $testDirectory 'godot_project'
    $fixtureReadme=Join-Path $testDirectory 'README.md'
    [System.IO.File]::WriteAllText($fixtureReadme,'[missing](missing.md)',$utf8)
    Assert-Rejected { Test-LxChangedText $fixtureProject @('README.md') } 'A broken documentation link passed.'
    [System.IO.File]::WriteAllBytes($fixtureReadme,[byte[]]@(0xff,0xfe,0x00))
    Assert-Rejected { Test-LxChangedText $fixtureProject @('README.md') } 'Invalid UTF-8 passed.'
    [System.IO.File]::WriteAllText($fixtureReadme,'<<<<<<< ours',$utf8)
    Assert-Rejected { Test-LxChangedText $fixtureProject @('README.md') } 'Merge conflict markers passed.'
    # A cold documentation check must not create Luban outputs or require SDK discovery.
    [System.IO.File]::WriteAllText($fixtureReadme,'# Example',$utf8)
    $nestedReadme=Join-Path $fixtureProject 'README.md'
    [System.IO.File]::WriteAllText($nestedReadme,'[missing](missing.md)',$utf8)
    Assert-Rejected { Test-LxChangedText $fixtureProject @('godot_project/README.md') } 'Explicit Godot text resolved to a same-named workspace file.'
    Assert-Rejected { Test-LxChangedText $fixtureProject @($nestedReadme) } 'Absolute Godot text resolved to a same-named workspace file.'
    $cold=Get-LxCheckPlan $fixtureProject @('README.md')
    Assert-Check (-not $cold.requiresDotnet -and -not $cold.needs.data) 'Cold documentation selected Luban.'
    $savedPath=$env:PATH
    try {
        $env:PATH=''
        $shellExecutable=(Get-Process -Id $PID).Path
        $noTools=@(& $shellExecutable -NoProfile -ExecutionPolicy Bypass -File (Join-Path $workspaceRoot 'lx.ps1') check README.md --json 2>&1)
        Assert-Check ($LASTEXITCODE -eq 0) "Documentation failed without external tools: $noTools"
        $json=($noTools -join "`n") | ConvertFrom-Json
        Assert-Check ($json.schema -eq 'lx.command-report' -and $json.success) 'Documentation JSON contract failed.'
        $planOutput=@(& $shellExecutable -NoProfile -ExecutionPolicy Bypass -File (Join-Path $workspaceRoot 'lx.ps1') check --plan src/LXFramework.Core/Events/EventHub.cs 2>&1)
        Assert-Check ($LASTEXITCODE -eq 0) 'Read-only planning probed the SDK.'
        $emptyPathPlan=($planOutput -join "`n") | ConvertFrom-Json
        Assert-Check $emptyPathPlan.needs.tests 'SDK-free planning lost required code tests.'
        $noRg=@(& $shellExecutable -NoProfile -ExecutionPolicy Bypass -File (Join-Path $workspaceRoot '.agents/skills/lx-codex-workflow/scripts/check-workflow.ps1') 2>&1)
        Assert-Check ($LASTEXITCODE -eq 0) "Repository validation requires an external CLI: $noRg"
    }
    finally { $env:PATH=$savedPath }
}
finally {
    $resolved=[System.IO.Path]::GetFullPath($testDirectory)
    $allowed=[System.IO.Path]::GetFullPath((Join-Path $workspaceRoot '.lx/check-plan-tests')).TrimEnd('\')+'\'
    if (-not $resolved.StartsWith($allowed,[System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host "Check planning passed: $assertions assertions; no model calls."
exit 0
