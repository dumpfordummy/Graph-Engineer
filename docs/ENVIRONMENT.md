# Implementation environment

Inspected 2026-09-23 on Windows, repository `C:\Users\USER\Desktop\Personal\Graph Engineer\Graph-Engineer`.

| Tool | Observed version |
|---|---|
| .NET SDK selected | 10.0.301, MSBuild 18.6.4 |
| Installed .NET 10 runtime / ASP.NET Core | 10.0.9 / 10.0.9 |
| Other installed SDKs | 8.0.425, 9.0.311, 9.0.318 (not selected) |
| Node.js | 24.11.1 (LTS line) |
| npm | 11.6.2 |
| Git | 2.52.0.windows.1 |
| Codex CLI on npm PATH | 0.106.0 |
| Codex app bundled CLI | 0.155.0-alpha.16.3 (metadata only; not an application dependency) |
| OS reported by .NET | Windows 10.0.26200, win-x64 |

No application files or package manager existed initially. Branch was `main`, HEAD `e9ce367` (Initial commit). README.md was already modified, and AGENTS.md plus docs/ were untracked user-provided planning files. Preserve those inputs. No commits, pushes, or global setting changes are authorized.

The installed .NET 10 SDK is pinned in global.json; Node/npm are pinned in the frontend package metadata and .node-version. No machine-wide prerequisites are installed by project scripts. The installed runtime is older than the currently published .NET servicing patch; this task uses the available SDK/runtime and does not silently update the machine. See [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core). Node 24 meets [Vite's documented runtime requirements](https://vite.dev/guide/).

Inspection limitations: the WinGet `rg.exe` alias could not launch (no associated application); PowerShell file enumeration and Select-String were used instead. Win32_OperatingSystem CIM inspection returned Access denied; .NET supplied OS details. No authentication files were accessed.

## Dependency selection

Official npm/NuGet registries were queried on 2026-09-23 through the normal permission process after sandbox registry access returned EACCES. Exact direct versions and all transitives are recorded in package.json/package-lock.json and project files/packages.lock.json.

Selected: Vue 3.5.43, Vue Router 5.3.1, Pinia 4.0.3, Vue Flow core 1.48.2 (background 1.3.2, controls 1.1.3, minimap 1.5.4), Vite 8.3.0, plugin-vue 6.0.9, TypeScript 6.0.3, vue-tsc 3.3.11, Vitest 5.0.1, Playwright 1.63.0, ESLint 10.11.0, jsdom 29.1.1, Vue Test Utils 2.4.11. Backend: EF Core SQLite/Design and MVC.Testing 10.0.12, Microsoft.NET.Test.Sdk 18.10.1, xUnit 2.9.3 and VS runner 4.0.0.

Compatibility selections are intentional, not unexplained downgrades:
- TypeScript 7.0.2 is registry-latest, but typescript-eslint 8.70.1 requires TypeScript <6.1; 6.0.3 is the newest compatible stable version.
- jsdom 30.1.1 requires Node 24.15+, whereas installed Node is 24.11.1; 29.1.1 supports this runtime.
- Vue Test Utils 2.5.1 pulls js-beautify 2 and nopt 10/abbrev 5, whose Node engines require 24.15+. The initial install exposed those warnings; 2.4.11 is the newest stable test-utils version using the compatible dependency line.
- @types/node is pinned to 24.13.6 to match the selected runtime major, rather than the latest 26.x type definitions.

The application uses stable packages; the observed bundled Codex alpha is environment metadata only. No application dependency relies on it.

## Installation and execution observations

- Frontend installation used `npm install --prefix apps/web --no-fund --no-audit` through normal approval. After the documented compatibility adjustment, installation completed without Node engine warnings. npm printed a transitive glob 10.5.0 deprecation warning from the test tooling; the subsequent `npm audit --prefix apps/web --json` returned **0 vulnerabilities**.
- `dotnet restore GraphEngineering.slnx` completed through normal approval and generated four NuGet lockfiles. No global .NET tools or machine-wide SDKs were installed.
- Chrome was already installed at `C:\Program Files\Google\Chrome\Application\chrome.exe`; Playwright uses that channel, with no browser download needed.
- Windows sandbox process cleanup is reliable when the browser fixture launches Vite directly with Node and owns that PID. The delivered fixture avoids an npm/cmd child-process chain.
- A deliberately failed SQLite write exposed a Windows EventLog permission failure in the default logging provider. The application explicitly uses console logging; the final error-path integration test verifies the intended HTTP 503 response without requiring Event Log privileges.
- Final application/check results and evidence paths are recorded in [M1_REPORT.md](handoffs/M1_REPORT.md).

## M2 environment refresh (2026-09-23)

Starting HEAD: d973bf8 (Init project), branch main. Six user-provided M2 handoff/task/source documents were untracked and are preserved. The M1 implementation is committed at that HEAD. No implementation changes existed before this work.

Installed SDKs: 8.0.425, 9.0.311, 9.0.318, 10.0.401. The former global.json pin 10.0.301 is no longer installed; the untouched baseline failed for that reason. global.json now pins installed stable 10.0.401 with roll-forward disabled. Installed .NET/ASP.NET Core runtime is 10.0.12. Node 24.11.1, npm 11.6.2, Git 2.52.0.windows.1 and PATH Codex CLI 0.106.0 are unchanged. No prerequisite was installed by this task. First use of SDK 10.0.401 printed the SDK's standard development-certificate initialization message; this task did not trust a certificate or change machine trust.

The sandbox NuGet restore was denied socket access. The normal approval path allowed locked restore. The first approved build found orphan API PID 30024 holding the repository DLL; targeted inspection confirmed the exact DLL/loopback port 5080 and absent parent 12912. Only that verified orphan was stopped. Baseline then passed with exit 0: 39 Core + 15 SQLite API tests, 37 frontend tests, type-check/lint/build, and 12 real browser checks. Evidence: .artifacts/m2/m1-baseline*.log. Initial failures are retained, not replaced by an invented pass.

M2 details: System.Security.Cryptography.ProtectedData 10.0.12 was verified against the official NuGet stable version list and added as an exact direct dependency; restore updated the API and API-test lockfiles. No frontend dependency changes were needed. Save performs bounded DNS resolution (five seconds) for named provider hosts without sending inference. Provider JSON input is capped at 24 KiB, output at 256 KiB, preview at 500 characters. Vite development/preview CORS is explicitly disabled; the application uses its same-origin proxy. See the M2 report for final counts and security/Windows/lifecycle evidence.

The final combined check initially exposed a fixture/environment defect: DirectoryInfo.Create with a restrictive ACL could apply it to missing shared temporary ancestors. Security tests now use unique flat temporary roots, and LocalLaunch creates ordinary data-directory ancestors before restricting only runtime. No existing shared/global ACL was changed to force tests to pass. The first failure log is retained as .artifacts/m2/final-check-initial-acl-fixture-failure.log; the corrected full run passed. Final toolchain/test execution was 2026-09-23 local time; handoff finalized 2026-09-24 Asia/Kuala_Lumpur.
