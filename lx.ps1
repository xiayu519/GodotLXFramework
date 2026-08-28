param(
    [Parameter(Position = 0)]
    [string]$Command = "doctor",

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$CommandArguments = @()
)

$ErrorActionPreference = "Stop"
$projectRoot = Join-Path $PSScriptRoot "godot_project"
$projectScript = Join-Path $projectRoot "lx.ps1"
if (-not (Test-Path -LiteralPath $projectScript -PathType Leaf)) {
    throw "LXFramework project entry was not found at '$projectScript'."
}

$forwardArguments = @($CommandArguments | Where-Object { $null -ne $_ -and $_ -ne "" })
if ($Command.Equals("check", [System.StringComparison]::OrdinalIgnoreCase)) {
    for ($index = 0; $index -lt $forwardArguments.Count; $index++) {
        $candidate = $forwardArguments[$index]
        if ([System.IO.Path]::IsPathRooted($candidate)) {
            $absolute = [System.IO.Path]::GetFullPath($candidate)
            $workspacePrefix = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd("\", "/") +
                [System.IO.Path]::DirectorySeparatorChar
            if (-not $absolute.StartsWith($workspacePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            $candidate = $absolute.Substring($workspacePrefix.Length)
        }

        $normalized = $candidate.Replace("\", "/").TrimStart([char[]]@('.', '/'))
        if ($normalized.StartsWith("godot_project/", [System.StringComparison]::OrdinalIgnoreCase)) {
            $forwardArguments[$index] = $normalized.Substring("godot_project/".Length)
        }
    }
}

& $projectScript $Command @forwardArguments
exit $LASTEXITCODE
