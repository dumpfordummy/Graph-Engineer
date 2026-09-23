param(
    [ValidateSet('CtrlC', 'PartialStart', 'ParentExit', 'All')][string]$Scenario = 'All',
    [ValidateRange(1024, 65520)][int]$FirstPort = 6280,
    [string]$Label = 'verification',
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This lifecycle test requires Windows.' }
if ($Label -notmatch '^[a-zA-Z0-9-]+$') { throw 'Label must contain letters, numbers, or hyphens.' }
$evidenceDirectory = Join-Path $repoRoot ".artifacts/m3/lifecycle-$Label-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null

# Each launcher receives a separate hidden Windows console. CTRL_C_EVENT is sent
# to that console only, so the test does not interrupt this terminal or other apps.
if (-not ('GraphEngineering.LifecycleConsole' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace GraphEngineering {
    public static class LifecycleConsole {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo {
            public int cb; public string reserved; public string desktop; public string title;
            public uint x, y, xSize, ySize, xCount, yCount, fill, flags;
            public short showWindow, reserved2; public IntPtr reservedPtr, input, output, error;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInfo { public IntPtr process, thread; public uint processId, threadId; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory,
            ref StartupInfo startup, out ProcessInfo information);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll")] static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GenerateConsoleCtrlEvent(uint kind, uint group);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
        public static Process Start(string executable, string arguments, string directory) {
            var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)), flags = 1, showWindow = 0 };
            ProcessInfo information;
            if (!CreateProcess(executable, new StringBuilder("\"" + executable + "\" " + arguments), IntPtr.Zero,
                IntPtr.Zero, false, 0x10, IntPtr.Zero, directory, ref startup, out information))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                var process = Process.GetProcessById((int)information.processId);
                var handle = process.Handle; // Retain a handle so exit codes remain readable after exit.
                return process;
            }
            finally { CloseHandle(information.thread); CloseHandle(information.process); }
        }
        public static void SendCtrlC(int processId) {
            FreeConsole();
            if (!AttachConsole((uint)processId)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                if (!SetConsoleCtrlHandler(IntPtr.Zero, true)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!GenerateConsoleCtrlEvent(0, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                System.Threading.Thread.Sleep(250);
            } finally { FreeConsole(); }
        }
    }
}
'@
}

function Test-Listening([int]$Port) {
    $socket = New-Object System.Net.Sockets.TcpClient
    try { $socket.Connect('127.0.0.1', $Port); return $true }
    catch [System.Net.Sockets.SocketException] { return $false }
    finally { $socket.Dispose() }
}

function Read-OwnedServices([string]$LogPath) {
    $owned = @()
    if (Test-Path -LiteralPath $LogPath) {
        $content = Get-Content -LiteralPath $LogPath -Raw
        foreach ($match in [regex]::Matches($content, 'Owned (API|web) process: (\d+)')) {
            $owned += [pscustomobject]@{ role = $match.Groups[1].Value; id = [int]$match.Groups[2].Value }
        }
    }
    return $owned
}

function Read-ProcessSnapshot($ServiceIds, $Launcher) {
    $all = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine, CreationDate)
    $apiDll = Join-Path $repoRoot 'src\GraphEngineering.Api\bin\Debug\net10.0\GraphEngineering.Api.dll'
    $viteJs = Join-Path $repoRoot 'apps\web\node_modules\vite\bin\vite.js'
    $roots = @($all | Where-Object {
        $_.ProcessId -in $ServiceIds -and $_.ParentProcessId -eq $Launcher.Id -and
        $_.CreationDate -ge $Launcher.StartTime -and $_.CommandLine -and (
            ($_.Name -eq 'dotnet.exe' -and $_.CommandLine.IndexOf('"' + $apiDll + '"', [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
            ($_.Name -eq 'node.exe' -and $_.CommandLine.IndexOf('"' + $viteJs + '"', [StringComparison]::OrdinalIgnoreCase) -ge 0)
        )
    })
    $parents = @{}
    foreach ($root in $roots) { $parents[[int]$root.ProcessId] = $root }
    $found = @()
    $ambiguous = @()
    do {
        $candidates = @($all | Where-Object { $parents.ContainsKey([int]$_.ParentProcessId) -and -not $parents.ContainsKey([int]$_.ProcessId) })
        $children = @($candidates | Where-Object { $_.CreationDate -and $_.CreationDate -ge $parents[[int]$_.ParentProcessId].CreationDate })
        $ambiguous += @($candidates | Where-Object { -not $_.CreationDate -or $_.CreationDate -lt $parents[[int]$_.ParentProcessId].CreationDate } | Select-Object ProcessId, ParentProcessId, Name, CreationDate)
        $found += $children
        foreach ($child in $children) { $parents[[int]$child.ProcessId] = $child }
    } while ($children.Count -gt 0)
    return [pscustomobject]@{ Services = $roots; Descendants = $found; Ambiguous = $ambiguous }
}

$results = @()
$cases = if ($Scenario -eq 'All') { @('CtrlC', 'PartialStart', 'ParentExit') } else { @($Scenario) }
foreach ($case in $cases) {
    $apiPort = $FirstPort + $results.Count * 2
    $webPort = $apiPort + 1
    if ((Test-Listening $apiPort) -or (Test-Listening $webPort)) { throw "Isolated port $apiPort or $webPort is occupied; nothing was stopped." }
    $caseDirectory = Join-Path $evidenceDirectory $case
    New-Item -ItemType Directory -Path $caseDirectory -Force | Out-Null
    $dataDirectory = Join-Path $caseDirectory 'data'
    if ($case -eq 'PartialStart') { Set-Content -LiteralPath $dataDirectory -Value 'Deliberate isolated startup failure: this path is a file.' }
    $logPath = Join-Path $caseDirectory 'launcher.log'
    $wrapperPath = Join-Path $caseDirectory 'invoke.ps1'
    $launcherPath = Join-Path $repoRoot 'scripts/dev.ps1'
    $wrapper = @"
`$ErrorActionPreference = 'Stop'
Start-Transcript -LiteralPath '$($logPath.Replace("'", "''"))' -Force | Out-Null
try {
    & '$($launcherPath.Replace("'", "''"))' -ApiPort $apiPort -WebPort $webPort -DataDirectory '$($dataDirectory.Replace("'", "''"))' $(if ($NoBuild) { '-NoBuild' })
} catch { Write-Host `$_; exit 1 }
finally { Stop-Transcript | Out-Null }
"@
    Set-Content -LiteralPath $wrapperPath -Value $wrapper
    # This separately owned synthetic Node process is deliberately outside the
    # launcher's Job Object. Cleanup must leave it alive in every scenario.
    $sentinelPath = Join-Path $caseDirectory 'untargeted-synthetic.cjs'
    Set-Content -LiteralPath $sentinelPath -Value 'setInterval(() => {}, 1000)'
    $sentinel = [GraphEngineering.LifecycleConsole]::Start((Get-Command node.exe).Source, "`"$sentinelPath`"", $repoRoot)
    $launcher = [GraphEngineering.LifecycleConsole]::Start((Get-Command powershell.exe).Source, "-NoProfile -File `"$wrapperPath`"", $repoRoot)
    $owned = @()
    $descendants = @()
    $ambiguousProcesses = @()
    $trackedHandles = @{}
    $verifiedServices = @()
    $readyPromptObserved = $false
    $listenersBefore = $null
    $failure = $null
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(90)
        do {
            Start-Sleep -Milliseconds 150
            $owned = @(Read-OwnedServices $logPath)
            $snapshot = Read-ProcessSnapshot @($owned.id) $launcher
            $descendants += @($snapshot.Descendants)
            $ambiguousProcesses += @($snapshot.Ambiguous)
            # Only directly launched, identity-checked repository services receive
            # retained handles. Enumerated descendants are read-only evidence.
            foreach ($service in $snapshot.Services) {
                if (-not $trackedHandles.ContainsKey([int]$service.ProcessId)) {
                    try {
                        $handle = Get-Process -Id $service.ProcessId -ErrorAction Stop
                        $null = $handle.Handle
                        if ([Math]::Abs(($handle.StartTime - $service.CreationDate).TotalMilliseconds) -le 1) {
                            $trackedHandles[[int]$service.ProcessId] = $handle
                            $verifiedServices += $service
                        } else { $handle.Dispose() }
                    } catch [Microsoft.PowerShell.Commands.ProcessCommandException] { }
                }
            }
            $launcher.Refresh()
            if ($case -eq 'PartialStart' -and $launcher.HasExited) { break }
            $readyPromptObserved = (Test-Path -LiteralPath $logPath) -and (Select-String -LiteralPath $logPath -SimpleMatch 'Press Ctrl+C to stop' -Quiet)
            if ($case -ne 'PartialStart' -and $readyPromptObserved -and (Test-Listening $apiPort) -and (Test-Listening $webPort)) { break }
            if ($launcher.HasExited) { throw "Launcher exited before both services listened. See $logPath" }
        } while ([DateTime]::UtcNow -lt $deadline)
        $owned = @(Read-OwnedServices $logPath)
        if ($owned.Count -ne 2) { throw "Expected both owned service IDs; found $($owned.Count). See $logPath" }
        if ($case -ne 'PartialStart' -and -not $readyPromptObserved) { throw 'The normal ready prompt was not reached.' }
        $listenersBefore = @{ api = (Test-Listening $apiPort); web = (Test-Listening $webPort) }
        $descendants = @($descendants | Sort-Object ProcessId, CreationDate -Unique)
        $ambiguousProcesses = @($ambiguousProcesses | Sort-Object ProcessId, CreationDate -Unique)
        if ($case -eq 'CtrlC') {
            if (-not $listenersBefore.api -or -not $listenersBefore.web) { throw 'Both services must listen before Ctrl+C.' }
            [GraphEngineering.LifecycleConsole]::SendCtrlC($launcher.Id)
        } elseif ($case -eq 'ParentExit') {
            if (-not $listenersBefore.api -or -not $listenersBefore.web) { throw 'Both services must listen before parent termination.' }
            $launcher.Kill()
        }
        if (-not $launcher.WaitForExit(15000)) { throw 'The owned launcher did not exit within 15 seconds.' }
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            $remainingIds = @($trackedHandles.Values | Where-Object { -not $_.HasExited } | Select-Object -ExpandProperty Id)
            $remainingObservedDescendants = @($descendants | Where-Object {
                $current = Get-CimInstance Win32_Process -Filter "ProcessId = $($_.ProcessId)"
                $current -and $current.CreationDate -eq $_.CreationDate
            } | Select-Object -ExpandProperty ProcessId)
            $listenersAfter = @{ api = (Test-Listening $apiPort); web = (Test-Listening $webPort) }
            if ($remainingIds.Count -eq 0 -and $remainingObservedDescendants.Count -eq 0 -and -not $listenersAfter.api -and -not $listenersAfter.web) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($remainingIds.Count -gt 0 -or $remainingObservedDescendants.Count -gt 0 -or $listenersAfter.api -or $listenersAfter.web) { $failure = 'Owned services, observed descendants, or listeners remained after launcher exit.' }
        if ($case -eq 'PartialStart' -and $launcher.ExitCode -eq 0) { $failure = 'Controlled partial-start failure unexpectedly returned success.' }
        $sentinel.Refresh()
        if ($sentinel.HasExited) { $failure = 'The untargeted synthetic Node process did not survive launcher cleanup.' }
        $results += [pscustomobject]@{
            scenario = $case; result = $(if ($failure) { 'FAIL' } else { 'PASS' }); failure = $failure
            launcherId = $launcher.Id; launcherExitCode = $launcher.ExitCode
            untargetedSyntheticId = $sentinel.Id; untargetedSyntheticSurvived = -not $sentinel.HasExited
            ownedServices = $owned; observedServiceDescendants = $descendants
            verifiedServiceIdentities = $verifiedServices
            excludedAmbiguousProcesses = $ambiguousProcesses
            descendantEvidence = $(if ($ambiguousProcesses.Count -gt 0) { 'Read-only observation; ambiguous process identities were excluded and remain unverified.' } else { 'Read-only PPID and creation-time observation; no enumerated descendant is a termination target.' })
            apiPort = $apiPort; webPort = $webPort; listenersBefore = $listenersBefore
            readyPromptObserved = $readyPromptObserved
            listenersAfter = $listenersAfter; remainingOwnedIds = $remainingIds
            remainingObservedDescendantIds = $remainingObservedDescendants
            interruption = $(if ($case -eq 'CtrlC') { 'Native CTRL_C_EVENT to isolated launcher console' } elseif ($case -eq 'ParentExit') { 'Terminate exact launcher process handle' } else { 'DataDirectory is an isolated regular file' })
        }
    } catch {
        $results += [pscustomobject]@{ scenario = $case; result = 'FAIL'; failure = $_.Exception.Message; launcherId = $launcher.Id; ownedServices = $owned }
    } finally {
        if (-not $launcher.HasExited) { $launcher.Kill(); $launcher.WaitForExit(10000) | Out-Null }
        # Never terminate inferred descendants. Only launcher and identity-checked
        # direct service handles are stopped; the production Job Object owns cleanup.
        foreach ($handle in $trackedHandles.Values) {
            if (-not $handle.HasExited) { $handle.Kill(); $handle.WaitForExit(10000) | Out-Null }
            $handle.Dispose()
        }
        $launcher.Dispose()
        # This is the exact synthetic process handle created by this harness,
        # stopped only after its survival was recorded; no name/ancestry lookup.
        if (-not $sentinel.HasExited) { $sentinel.Kill(); $sentinel.WaitForExit(10000) | Out-Null }
        $sentinel.Dispose()
    }
    $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'results.json')
}
$results | Select-Object scenario, result, failure | Format-Table -AutoSize
Write-Host "Lifecycle evidence: $evidenceDirectory"
if (@($results | Where-Object result -eq 'FAIL').Count -gt 0) { throw 'One or more lifecycle checks failed. Owned test processes were cleaned up.' }
