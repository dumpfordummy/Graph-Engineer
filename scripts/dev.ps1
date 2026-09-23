param(
    [ValidateRange(1024, 65535)][int]$ApiPort = 5080,
    [ValidateRange(1024, 65535)][int]$WebPort = 5173,
    [string]$DataDirectory,
    [switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiProcess = $null
$webProcess = $null
$oldDataDirectory = $env:GRAPH_ENGINEERING_DATA_DIR
$oldApiTarget = $env:VITE_API_TARGET

function Assert-FreePort([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        throw "Port $Port is already in use. Choose another port; no existing process was stopped."
    } catch [System.Net.Sockets.SocketException] {
        # Connection refused means the loopback port is available.
    } finally { $client.Dispose() }
}

Push-Location $repoRoot
try {
    if ($ApiPort -eq $WebPort) { throw 'API and web ports must be different.' }
    foreach ($command in @('dotnet', 'node', 'npm')) { Get-Command $command -ErrorAction Stop | Out-Null }
    if (-not (Test-Path 'apps/web/node_modules/vite/bin/vite.js')) {
        throw 'Frontend dependencies are missing. Run: npm ci --prefix apps/web'
    }
    Assert-FreePort $ApiPort
    Assert-FreePort $WebPort
    & dotnet build GraphEngineering.slnx --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed. If packages are missing, run: dotnet restore GraphEngineering.slnx --locked-mode' }
    $logDirectory = Join-Path $repoRoot '.artifacts/dev'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    if ($DataDirectory) { $env:GRAPH_ENGINEERING_DATA_DIR = [System.IO.Path]::GetFullPath($DataDirectory) }
    $env:VITE_API_TARGET = "http://127.0.0.1:$ApiPort"
    $apiDll = Join-Path $repoRoot 'src/GraphEngineering.Api/bin/Debug/net10.0/GraphEngineering.Api.dll'
    $viteJs = Join-Path $repoRoot 'apps/web/node_modules/vite/bin/vite.js'
    $apiProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @("`"$apiDll`"", '--urls', "http://127.0.0.1:$ApiPort") -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory 'api.log') -RedirectStandardError (Join-Path $logDirectory 'api-error.log')
    $webProcess = Start-Process -FilePath (Get-Command node).Source -ArgumentList @("`"$viteJs`"", '--host', '127.0.0.1', '--port', "$WebPort", '--strictPort') -WorkingDirectory (Join-Path $repoRoot 'apps/web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory 'web.log') -RedirectStandardError (Join-Path $logDirectory 'web-error.log')
    $healthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($apiProcess.HasExited -or $webProcess.HasExited) { throw "A development service exited. Inspect $logDirectory." }
        try {
            $health = Invoke-RestMethod "http://127.0.0.1:$ApiPort/api/health" -TimeoutSec 2
            $web = Invoke-WebRequest "http://127.0.0.1:$WebPort" -UseBasicParsing -TimeoutSec 2
            if ($health.status -eq 'ok' -and $web.StatusCode -eq 200) { $healthy = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $healthy) { throw "Services did not become healthy. Inspect $logDirectory." }
    Write-Host "Graph Engineering: http://127.0.0.1:$WebPort"
    Write-Host "API health: http://127.0.0.1:$ApiPort/api/health"
    Write-Host "Logs: $logDirectory"
    if ($SmokeTest) {
        Write-Host 'PASS: API health and frontend responded; stopping owned smoke-test services.'
        return
    }
    Write-Host 'Press Ctrl+C to stop only the two processes started by this script.'
    while (-not $apiProcess.HasExited -and -not $webProcess.HasExited) { Start-Sleep -Seconds 1 }
    throw "A development service exited. Inspect $logDirectory."
} finally {
    foreach ($process in @($webProcess, $apiProcess)) {
        if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
    }
    $env:GRAPH_ENGINEERING_DATA_DIR = $oldDataDirectory
    $env:VITE_API_TARGET = $oldApiTarget
    Pop-Location
}
