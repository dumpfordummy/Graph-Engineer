param(
    [ValidateRange(1024, 65535)][int]$ApiPort = 5080,
    [ValidateRange(1024, 65535)][int]$WebPort = 5173,
    [switch]$Stop
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDll = Join-Path $repoRoot 'src\GraphEngineering.Api\bin\Debug\net10.0\GraphEngineering.Api.dll'
$viteJs = Join-Path $repoRoot 'apps\web\node_modules\vite\bin\vite.js'
$apiArguments = '--urls\s+"?http://127\.0\.0\.1:' + $ApiPort + '"?(?:\s|$)'
$webArguments = '--port\s+' + $WebPort + '(?:\s|$)'
$matches = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'node.exe'" | Where-Object {
    $_.CommandLine -and (
        ($_.Name -eq 'dotnet.exe' -and $_.CommandLine.IndexOf('"' + $apiDll + '"', [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $_.CommandLine -match $apiArguments) -or
        ($_.Name -eq 'node.exe' -and $_.CommandLine.IndexOf('"' + $viteJs + '"', [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $_.CommandLine -match $webArguments -and $_.CommandLine -match '--host\s+127\.0\.0\.1(?:\s|$)')
    )
})
if ($matches.Count -eq 0) { Write-Host 'No launcher services match this exact repository and these ports.'; return }
$matches | Select-Object ProcessId, ParentProcessId, Name, CommandLine | Format-List
if (-not $Stop) {
    Write-Host 'Inspection only. Prefer Ctrl+C in the original launcher terminal.'
    Write-Host "For confirmed orphan services, repeat with -Stop -ApiPort $ApiPort -WebPort $WebPort."
    return
}
foreach ($candidate in $matches) {
    $process = Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue
    if (-not $process) { continue }
    try {
        $null = $process.Handle
        # Recheck after retaining the handle so a recycled PID cannot select another command.
        $current = Get-CimInstance Win32_Process -Filter "ProcessId = $($candidate.ProcessId)"
        if ($current -and $current.CreationDate -eq $candidate.CreationDate -and $current.CommandLine -eq $candidate.CommandLine -and -not $process.HasExited) {
            $process.Kill()
            if (-not $process.WaitForExit(10000)) { throw "Owned service $($candidate.ProcessId) did not exit within 10 seconds." }
            Write-Host "Stopped exact matching project service $($candidate.ProcessId)."
        }
    } finally { $process.Dispose() }
}
