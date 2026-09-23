param([switch]$SkipBrowser)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    & dotnet restore GraphEngineering.slnx --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'FAIL: locked backend restore' }
    & dotnet build GraphEngineering.slnx --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'FAIL: backend build' }
    & dotnet test GraphEngineering.slnx --no-build --no-restore -- RunConfiguration.TreatNoTestsAsError=true
    if ($LASTEXITCODE -ne 0) { throw 'FAIL: backend tests' }
    foreach ($check in @('type-check', 'lint', 'test', 'build')) {
        & npm --prefix apps/web run $check
        if ($LASTEXITCODE -ne 0) { throw "FAIL: frontend $check" }
    }
    if ($SkipBrowser) {
        Write-Host 'NOT RUN: browser checks (-SkipBrowser explicitly selected).'
    } else {
        & npm --prefix apps/web run test:e2e
        if ($LASTEXITCODE -ne 0) { throw 'FAIL: Playwright browser acceptance' }
    }
    Write-Host 'PASS: all requested checks completed.'
} finally { Pop-Location }
