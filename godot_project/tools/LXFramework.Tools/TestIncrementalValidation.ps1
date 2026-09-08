param()
$ErrorActionPreference='Stop'
$projectRoot=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$workspaceRoot=Split-Path -Parent $projectRoot
$toolDll=Join-Path $PSScriptRoot 'bin/Debug/net8.0/LXFramework.Tools.dll'
if (-not (Test-Path -LiteralPath $toolDll)) { throw 'Build LXFramework.Tools before running incremental validation regression.' }
$fixture=Join-Path $workspaceRoot ('.lx/incremental-validation-tests/'+[guid]::NewGuid().ToString('N'))
$fixtureProject=Join-Path $fixture 'godot_project'
$utf8=[System.Text.UTF8Encoding]::new($false)
$assertions=0
function Assert-Incremental([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:assertions++
}
function Write-Fixture([string]$Relative,[string]$Content) {
    $path=Join-Path $fixtureProject $Relative
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force)
    [System.IO.File]::WriteAllText($path,$Content,$utf8)
}
function Invoke-FixtureValidation([string[]]$Paths=@()) {
    Push-Location $fixtureProject
    try {
        $validationArguments=@('validate')
        if ($Paths.Count) { $validationArguments+=@('--changed')+$Paths }
        # Negative cases intentionally write stderr; use the native exit code as the verdict.
        $previousPreference=$ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            $lines=@(& dotnet $toolDll @validationArguments 2>&1)
            $code=$LASTEXITCODE
        }
        finally { $ErrorActionPreference=$previousPreference }
        $reportFile=if($Paths.Count){'.lx/validation-changed.json'}else{'.lx/validation.json'}
        $report=Get-Content -LiteralPath $reportFile -Raw -Encoding UTF8 | ConvertFrom-Json
        return [pscustomobject]@{code=$code;report=$report;log=($lines -join "`n")}
    }
    finally { Pop-Location }
}
try {
    # Verify that the PowerShell entry preserves native failures, not just diagnostics.
    $shellExecutable=(Get-Process -Id $PID).Path
    $wrapperOutput=@(& $shellExecutable -NoProfile -ExecutionPolicy Bypass -File (Join-Path $workspaceRoot 'lx.ps1') __validation_unknown_command__ --json)
    $wrapperCode=$LASTEXITCODE
    $wrapperReport=($wrapperOutput -join "`n") | ConvertFrom-Json
    Assert-Incremental ($wrapperCode -ne 0 -and -not $wrapperReport.success) 'The command wrapper masked a native failure exit code.'
    # Copy source only; never copy user credentials, generated caches or build outputs.
    $pending=[System.Collections.Generic.Stack[string]]::new()
    $pending.Push($workspaceRoot)
    while($pending.Count) {
        foreach($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint){continue}
            if($entry.PSIsContainer) {
                if($entry.Name -notin @('.git','.lx','.godot','.mono','.tools','bin','obj','build','artifacts','research','TestResults')){$pending.Push($entry.FullName)}
            }
            else {
                if($entry.Name -in @('auth.json','.env')){continue}
                $target=Join-Path $fixture $entry.FullName.Substring($workspaceRoot.Length+1)
                [void](New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force)
                Copy-Item -LiteralPath $entry.FullName -Destination $target
            }
        }
    }
    # The static gate consumes an existing local Luban receipt; it does not install tools.
    $lubanReport=Join-Path $fixtureProject '.lx/luban/report.json'
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $lubanReport) -Force)
    Copy-Item -LiteralPath (Join-Path $projectRoot '.lx/luban/report.json') -Destination $lubanReport
    $baseline=Invoke-FixtureValidation
    Assert-Incremental ($baseline.code -eq 0) "Clean full static fixture failed: $($baseline.log)"
    $fullHash=(Get-FileHash -LiteralPath (Join-Path $fixtureProject '.lx/validation.json')).Hash

    Write-Fixture 'content/story/invalid.json' '{ invalid'
    $unrelated=Invoke-FixtureValidation @('tests/LXFramework.Core.Tests/EventHubTests.cs')
    Assert-Incremental ($unrelated.code -eq 0) 'Test-only validation scanned unrelated content.'
    Assert-Incremental ('tool-protocols' -notin $unrelated.report.checks -and 'public-api' -notin $unrelated.report.checks) 'Incremental validation ran unrelated global checks.'
    Assert-Incremental ((Get-FileHash -LiteralPath (Join-Path $fixtureProject '.lx/validation.json')).Hash -eq $fullHash) 'An incremental check overwrote full validation evidence.'
    $changed=Invoke-FixtureValidation @('content/story/invalid.json')
    Assert-Incremental ($changed.code -ne 0 -and $changed.log -match 'invalid.json') 'Invalid changed JSON was accepted.'
    $full=Invoke-FixtureValidation
    Assert-Incremental ($full.code -ne 0 -and $full.log -match 'invalid.json') 'Full validation no longer checks all JSON.'
    $fallback=Invoke-FixtureValidation @('tools/LXFramework.Tools/Validator.cs')
    Assert-Incremental ($fallback.code -ne 0 -and 'tool-protocols' -in $fallback.report.checks) 'Unknown/shared tooling did not conservatively select full static checks.'
    Write-Fixture 'content/story/invalid.json' '{}'

    Write-Fixture 'src/LXFramework.Core/Events/ScopeViolation.cs' 'using Godot; namespace LX.Core.Events; internal sealed class ScopeViolation {}'
    $architecture=Invoke-FixtureValidation @('src/LXFramework.Core/Events/ScopeViolation.cs')
    Assert-Incremental ($architecture.code -ne 0 -and $architecture.log -match 'LX_ARCH_001') 'Changed Core architecture violation was accepted.'
    Write-Fixture 'src/LXFramework.Core/Events/ScopeViolation.cs' 'namespace LX.Core.Events; internal sealed class ScopeViolation {}'

    Write-Fixture 'src/LXFramework/ScopeEnum.cs' 'namespace LX; public enum ScopeEnum { MissingDocumentation }'
    $documentation=Invoke-FixtureValidation @('src/LXFramework/ScopeEnum.cs')
    Assert-Incremental ($documentation.code -ne 0 -and $documentation.log -match 'LX_DOC_001') 'Changed public API documentation violation was accepted.'
    Write-Fixture 'src/LXFramework/ScopeEnum.cs' 'namespace LX; internal enum ScopeEnum { Internal }'

    $generated=Get-ChildItem -LiteralPath (Join-Path $fixtureProject 'src/LXFramework/Generated') -Filter '*.g.cs' -Recurse | Select-Object -First 1
    if($null -eq $generated){throw 'Fixture has no generated catalog.'}
    [System.IO.File]::AppendAllText($generated.FullName,"`n// injected drift`n",$utf8)
    $generatedRelative=$generated.FullName.Substring($fixtureProject.Length+1).Replace('\','/')
    $drift=Invoke-FixtureValidation @($generatedRelative)
    Assert-Incremental ($drift.code -ne 0 -and $drift.log -match 'is stale') 'Changed generated output drift was accepted.'

    # Reintroducing automatic remote provisioning must fail the instruction gate.
    $ciFile=Join-Path $fixture '.github/workflows/validate.yml'
    $ciText=Get-Content -LiteralPath $ciFile -Raw -Encoding UTF8
    [System.IO.File]::WriteAllText($ciFile,$ciText.Replace("on:","on:`n  push:"),$utf8)
    $shellExecutable=(Get-Process -Id $PID).Path
    $previousPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $ciOutput=@(& $shellExecutable -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture '.agents/skills/lx-codex-workflow/scripts/check-workflow.ps1') 2>&1)
        $ciCode=$LASTEXITCODE
    }
    finally { $ErrorActionPreference=$previousPreference }
    Assert-Incremental ($ciCode -ne 0 -and ($ciOutput -join "`n") -match 'workflow_dispatch trigger') 'Automatic remote validation was accepted.'
}
finally {
    $resolved=[System.IO.Path]::GetFullPath($fixture)
    $allowed=[System.IO.Path]::GetFullPath((Join-Path $workspaceRoot '.lx/incremental-validation-tests')).TrimEnd('\')+'\'
    if(-not $resolved.StartsWith($allowed,[System.StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe incremental fixture cleanup.'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
Write-Host "Incremental static validation passed: $assertions assertions in an isolated source fixture."
exit 0
