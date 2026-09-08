# Shared, side-effect-free configuration and coverage contract.
function Assert-EvalProjectConfig([string]$Path,$Schema) {
    $config=Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $values=@{}
    foreach($line in ($config -split "\r?\n")){
        if($line -match '^\s*(model|model_reasoning_effort|plan_mode_reasoning_effort)\s*=\s*["'']([^"'']+)["'']\s*(?:#.*)?$'){
            if($values.ContainsKey($Matches[1])){throw "Duplicate project configuration: $($Matches[1])"}
            $values[$Matches[1]]=$Matches[2]
        }
    }
    $default=@($Schema.profiles | Where-Object id -eq $Schema.default_profile)[0]
    if($values.model -ne $default.model -or $values.model_reasoning_effort -ne $default.reasoning){throw 'Project defaults disagree with the evaluation contract.'}
    if($values.ContainsKey('plan_mode_reasoning_effort') -and $values.plan_mode_reasoning_effort -notin @('low','medium','high','xhigh','max')){throw 'Unsupported Plan reasoning effort.'}
    if($config -match '(?m)^\s*(?:profile\s*=|\[profiles[.\]])'){throw 'Named profiles belong in user configuration, not project configuration.'}
}

function Read-EvalContract([string]$Path) {
    $schema=Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if($schema.schema_version -ne 2){throw 'Expected evaluation schema version 2.'}
    $ids=@($schema.profiles.id)
    if(@($ids | Select-Object -Unique).Count -ne $ids.Count){throw 'Duplicate model profile.'}
    if($schema.default_profile -notin $ids){throw 'Unknown default model profile.'}
    if(@($schema.profiles.reasoning | Select-Object -Unique).Count -ne $ids.Count){throw 'Duplicate reasoning effort.'}
    foreach($required in @(@{reasoning='low';suite='foundation'},@{reasoning='xhigh';suite='complex'})){
        $match=@($schema.profiles | Where-Object {$_.reasoning -eq $required.reasoning -and $_.required -eq $true -and $_.required_suite -eq $required.suite})
        if($match.Count -ne 1){throw 'Missing mandatory Light/foundation or xhigh/complex acceptance profile.'}
    }
    foreach($profile in $schema.profiles){
        if($profile.model -ne 'gpt-6-astra' -or $profile.reasoning -notin @('low','medium','high','xhigh','max')){throw 'Unsupported Astra profile.'}
        if($profile.required -and $profile.required_suite -notin @('foundation','complex','full','smoke')){throw 'Required profile lacks an acceptance suite.'}
    }
    $cases=@($schema.cases.id)
    if(@($cases | Select-Object -Unique).Count -ne $cases.Count){throw 'Duplicate evaluation case.'}
    foreach($id in $schema.complex_case_ids){if($id -notin $cases){throw "Unknown complex case: $id"}}
    foreach($case in $schema.cases){
        if($case.kind -notin @('routing','implementation','behavior') -or $case.minimum_effort -notin @('low','medium','high','xhigh','max')){throw "Invalid case contract: $($case.id)"}
        if($case.kind -eq 'behavior' -and [string]::IsNullOrWhiteSpace($case.outcome)){throw 'Behavior cases require an independent executable oracle.'}
    }
    return $schema
}

function Test-EvalTermGroup([string]$Text,$Group) {
    # Canonicalize equivalent bilingual architecture/smoke terminology; not task completion.
    $Text=$Text -replace '\u67b6\u6784\u9a71\u52a8','\u9a71\u52a8\u67b6\u6784'
    $Text=$Text.Replace('\u9a71\u52a8\u67b6\u6784',([string][char]0x9a71+[char]0x52a8+[char]0x67b6+[char]0x6784))
    $Text=$Text -replace '\u4ea7\u54c1\s*smoke','product smoke'
    foreach($term in @($Group.any)){
        if(-not [string]::IsNullOrWhiteSpace([string]$term) -and $Text.IndexOf([string]$term,[System.StringComparison]::OrdinalIgnoreCase) -ge 0){return $true}
    }
    foreach($pattern in @($Group.any_regex)){
        if(-not [string]::IsNullOrWhiteSpace([string]$pattern) -and $Text -match [string]$pattern){return $true}
    }
    return $false
}

function Get-EvalResponseFailures($Case,[string]$Text) {
    foreach($term in @($Case.expected_terms | Where-Object {-not [string]::IsNullOrWhiteSpace([string]$_)})){
        if($Text.IndexOf([string]$term,[System.StringComparison]::OrdinalIgnoreCase) -lt 0){ "Final response is missing expected term '$term'." }
    }
    foreach($group in @($Case.expected_term_groups | Where-Object {$null -ne $_})){
        if(-not (Test-EvalTermGroup $Text $group)){ "Final response is missing every term in semantic group: $(@($group.any) -join ', ')." }
    }
}

function Get-EvalCheckPaths([string[]]$Files) {
    @($Files | ForEach-Object {
        $path=$_.Replace('\','/')
        if($path.StartsWith('godot_project/',[System.StringComparison]::OrdinalIgnoreCase) -and
            -not $path.EndsWith('.uid',[System.StringComparison]::OrdinalIgnoreCase) -and
            $path -notmatch '(^|/)(Generated|generated)/|^godot_project/content/data/luban/'){
            # Generated .uid sidecars stay in the archived diff and Godot import validation.
            # They are not additional gameplay source changes (including UIDs of unchanged scripts).
            $path
        }
    } | Select-Object -Unique)
}

function Select-EvalCases($Schema,[string]$Suite,[string[]]$CaseId=@()) {
    $selected=@(switch($Suite){
        'smoke' {$Schema.cases | Where-Object suite -eq 'smoke'}
        'foundation' {$Schema.cases | Where-Object minimum_effort -eq 'low'}
        'complex' {$Schema.cases | Where-Object {$_.id -in $Schema.complex_case_ids}}
        'full' {$Schema.cases}
        default {throw "Unknown suite: $Suite"}
    })
    if($CaseId.Count){
        foreach($id in $CaseId){if($id -notin $selected.id){throw "Unknown case '$id' for suite '$Suite'."}}
        $selected=@($selected | Where-Object {$_.id -in $CaseId})
    }
    if(-not $selected.Count){throw 'No cases selected.'}
    return $selected
}

function Get-EvalCoverage($Schema,[string]$Suite,$Results) {
    $expected=@(Select-EvalCases $Schema $Suite @() | ForEach-Object {$_.id})
    $actual=@($Results | ForEach-Object {$_.case} | Select-Object -Unique)
    $missing=@($expected | Where-Object {$_ -notin $actual})
    $unexpected=@($actual | Where-Object {$_ -notin $expected})
    $complete=$missing.Count -eq 0 -and $unexpected.Count -eq 0 -and @($Results).Count -eq $expected.Count
    [pscustomobject]@{suite=$Suite;expected=$expected.Count;executed=$actual.Count;missing=$missing;unexpected=$unexpected;complete=$complete;passed=($complete -and @($Results | Where-Object {-not $_.passed}).Count -eq 0)}
}

function Resolve-EvalCodex([string]$ExplicitPath='') {
    if($ExplicitPath){
        $exe=Get-Item -LiteralPath $ExplicitPath -ErrorAction Stop
        if($exe.Extension -ne '.exe'){throw 'Codex executable must be a native .exe on Windows.'}
        return $exe.FullName
    }
    $candidate=Get-Command codex -All -ErrorAction Stop | Where-Object {$_.CommandType -eq 'Application' -and $_.Source -like '*.exe'} | Select-Object -First 1
    if(-not $candidate){throw 'No native Codex executable found. Pass -CodexPath explicitly.'}
    return [string]$candidate.Source
}
