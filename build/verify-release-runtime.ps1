param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path
# Debug suffixes can follow a component number (_1d) or precede a named
# component (d_atomic_wait). Release names such as vcruntime140_1 stay valid.
$debugRuntime = [regex]::new('(?i)\b(?:(?:msvcp|vcruntime|concrt)\d+(?:_\d+)*d(?:_[a-z0-9]+)*|ucrtbased)\.dll\b')

foreach ($name in @('LibRawWrapper.dll', 'libraw.dll')) {
    $path = Join-Path $package $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required release file: $path"
    }

    $contents = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($path))
    $runtimeMatches = @($debugRuntime.Matches($contents) | ForEach-Object Value | Sort-Object -Unique)
    if ($runtimeMatches.Count -gt 0) {
        throw "$name depends on non-redistributable Visual C++ debug runtimes: $($runtimeMatches -join ', '). Rebuild the x64 Release payload."
    }

    Write-Host "${name}: no Visual C++ debug runtime dependency found"
}
