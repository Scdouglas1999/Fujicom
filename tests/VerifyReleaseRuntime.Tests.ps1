$ErrorActionPreference = 'Stop'
$verify = Join-Path $PSScriptRoot '../build/verify-release-runtime.ps1'
$package = Join-Path ([IO.Path]::GetTempPath()) ('fujicom-runtime-test-' + [guid]::NewGuid())
$files = @('LibRawWrapper.dll', 'libraw.dll')
$releaseImports = "MSVCP140.dll`0MSVCP140_1.dll`0MSVCP140_2.dll`0VCRUNTIME140_1.dll`0MSVCP140_ATOMIC_WAIT.dll`0ucrtbase.dll`0"
$checks = 0

function Write-Imports([string]$Name, [string]$Imports) {
    # The verifier scans binary strings; these fixtures isolate that contract.
    [IO.File]::WriteAllBytes((Join-Path $package $Name), [Text.Encoding]::ASCII.GetBytes($Imports))
}

function Assert-Rejected([string]$ExpectedMessage) {
    $failure = $null
    try { & $verify -PackagePath $package | Out-Null }
    catch { $failure = $_.Exception.Message }
    if (-not $failure -or -not $failure.Contains($ExpectedMessage)) {
        throw "Expected rejection containing '$ExpectedMessage'; got '$failure'."
    }
}

try {
    New-Item -ItemType Directory -Path $package | Out-Null
    foreach ($file in $files) { Write-Imports $file $releaseImports }
    & $verify -PackagePath $package
    $checks++

    foreach ($file in $files) {
        foreach ($debugImport in @('MSVCP140D.dll', 'VCRUNTIME140D.dll', 'ucrtbased.dll',
                'VCRUNTIME140_1D.dll', 'msvcp140_1d.DLL', 'MSVCP140_2D.dll',
                'MSVCP140D_ATOMIC_WAIT.dll', 'MSVCP140D_CODECVT_IDS.dll', 'CONCRT140D.dll')) {
            Write-Imports $file ($releaseImports + $debugImport + "`0")
            Assert-Rejected $debugImport
            $checks++
        }
        Write-Imports $file $releaseImports
        Remove-Item -LiteralPath (Join-Path $package $file)
        Assert-Rejected 'Missing required release file'
        $checks++
        Write-Imports $file $releaseImports
    }
    Write-Host "All $checks release-runtime verifier checks passed."
}
finally {
    if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Recurse -Force }
}
