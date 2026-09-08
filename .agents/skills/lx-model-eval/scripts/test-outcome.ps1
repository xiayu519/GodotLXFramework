param(
    [Parameter(Mandatory=$true)][ValidateSet('round-state','reward-ledger','help-ui')][string]$Outcome,
    [Parameter(Mandatory=$true)][string]$Fixture,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$utf8=[System.Text.UTF8Encoding]::new($false)
if($Outcome -eq 'help-ui'){
    $manifest=Get-Content -LiteralPath (Join-Path $Fixture 'godot_project/content/ui/ui-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $screen=@($manifest.screens | Where-Object id -eq 'help')
    if($screen.Count -ne 1 -or $screen[0].layer -ne 'Screen'){throw 'Expected one full-screen help registration.'}
    $scene=Get-Content -LiteralPath (Join-Path $Fixture 'godot_project/scene/ui/help.tscn') -Raw -Encoding UTF8
    if($screen[0].scenePath -ne 'res://scene/ui/help.tscn' -or $scene -notmatch 'type="[^"]*Container"'){throw 'Help must be a registered static container scene.'}
    if($scene -notmatch 'text = "\u64cd\u4f5c\u8bf4\u660e"' -or $scene -notmatch 'text = "H\uff1a\u6253\u5f00\u673a\u5e93"'){throw 'Help scene is missing the requested static copy.'}
    Write-Host 'LX_OUTCOME_PASS help-ui'
    exit 0
}
$sourceName=if($Outcome -eq 'round-state'){'RoundState'}else{'RewardLedger'}
$probeName=if($Outcome -eq 'round-state'){'RoundProbe'}else{'RewardProbe'}
$source=Join-Path $Fixture "godot_project/script/EvalBase/Gameplay/$sourceName.cs"
if(-not (Test-Path -LiteralPath $source -PathType Leaf)){throw "Missing behavior implementation: $source"}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sourceXml=[System.Security.SecurityElement]::Escape([System.IO.Path]::GetFullPath($source))
$project=@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <LangVersion>12</LangVersion><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup><Compile Include="$sourceXml"/><Compile Include="Program.cs"/></ItemGroup>
</Project>
"@
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory 'Probe.csproj'),$project,$utf8)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "../assets/probes/$probeName.cs") -Destination (Join-Path $OutputDirectory 'Program.cs') -Force
& dotnet run --project (Join-Path $OutputDirectory 'Probe.csproj') --configuration Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw "Independent $Outcome probe failed with exit code $LASTEXITCODE."}
exit 0
