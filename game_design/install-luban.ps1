[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$toolchainPath = Join-Path $PSScriptRoot "toolchain.json"
$toolchain = Get-Content -LiteralPath $toolchainPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($toolchain.schemaVersion -ne 1) {
    throw "Unsupported Luban toolchain schema version '$($toolchain.schemaVersion)'."
}

$installRoot = Join-Path $workspaceRoot ".tools\luban\$($toolchain.version)"
$assemblyPath = Join-Path $installRoot $toolchain.assembly
if (Test-Path -LiteralPath $assemblyPath -PathType Leaf) {
    Write-Host "Luban $($toolchain.version) already installed -> $assemblyPath"
    exit 0
}

$sourceRoot = Join-Path $workspaceRoot ".tools\luban\source-$($toolchain.version)"
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot ".git") -PathType Container)) {
    if (Test-Path -LiteralPath $sourceRoot) {
        throw "Luban source target '$sourceRoot' exists but is not a Git checkout. Move it aside and retry."
    }

    git clone --branch $toolchain.version --depth 1 $toolchain.repository $sourceRoot
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$actualCommit = (git -C $sourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
if (-not $actualCommit.Equals($toolchain.commit, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Luban checkout is '$actualCommit'; expected pinned commit '$($toolchain.commit)'."
}

$projectPath = Join-Path $sourceRoot "src\Luban\Luban.csproj"
dotnet build $projectPath -c Release -o $installRoot --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Luban build completed without producing '$assemblyPath'."
}

Write-Host "Luban $($toolchain.version) installed from $actualCommit -> $assemblyPath"
