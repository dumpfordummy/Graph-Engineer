# Project progress

Updated 2026-09-24: **M3 is implemented — READY FOR USER WORKFLOW CHECK.** Required automated checks A01–A16 passed against isolated fixtures. The final integrated `scripts/check.ps1` returned exit 0 at 01:35:17 Asia/Kuala_Lumpur: **269 backend tests (110 Core + 159 API), 81 frontend tests and 22 browser tests**, with no failures/skips, plus locked restore, backend build, type-check, lint and frontend production build. Evidence and the complete matrix are in [M3_REPORT.md](handoffs/M3_REPORT.md) and `.artifacts/m3/`.

M3 adds explicit versioned bindings, sequential protected Responses execution, immutable saved-revision snapshots, durable SQLite work/state/attempts/artifacts/events, idempotent submission, cancellation, conservative restart recovery, authenticated SignalR and the Vue Runs view. Legacy editor documents remain drafts until explicitly upgraded. Real process replacement verified completed-history persistence and interruption without replay, including a crash after provider completion before result persistence. Browser verification observed actual authenticated reconnects through the Vite proxy while backend work continued. Ordinary Ctrl+C, partial-start failure and abrupt launcher exit passed; an untargeted synthetic Node process survived each scenario. Production ownership protections remain intact.

M2 is conditionally accepted for development progression. Its baseline was rerun before implementation and passed with exit 0: 39 Core + 102 API tests, 57 frontend tests, type-check/lint/build and 17 browser checks (`.artifacts/m3/m2-baseline.log`). Historical M2 verification and its harness incident remain in [M2_REPORT.md](handoffs/M2_REPORT.md). M3's report records unsuccessful intermediate checks and their verified fixes.

The user supplied a successful real-provider text-test screenshot. The same-profile before/after-restart probe (M2-U1) remains **NOT RUN / unconfirmed**; do not infer it from that screenshot or fixtures. The user-operated M3 real workflow check (U02) is **NOT RUN**. Actual provider details and both checks remain user-operated.

| Milestone | Status |
|---|---|
| M1: Persistent editor | Conditionally accepted for progression; M1-C1 lifecycle verification completed in M2 with documented harness incident |
| M2: Providers and local security | Conditionally accepted for progression; successful user text-probe screenshot supplied; same-profile restart unconfirmed |
| M3: Real model workflow execution | Implemented; required isolated automated checks PASS; READY FOR USER WORKFLOW CHECK |
| M4: Coding agent, commands, approvals | Not started; not authorized |
| M5: Bounded review/repair pilot | Not started; not authorized |
| M6: Hardening and packaging | Not started; not authorized |

No staging, commits, pushes, merges, or M4+ implementation were performed. M3 is explicitly authorized by the lead/user assignment; final acceptance remains a lead/user decision. Exact startup, local pairing, literal-text and two-node JSON workflow steps, restart verification and synthetic test commands are in [README.md](../README.md) and [M3_REPORT.md](handoffs/M3_REPORT.md). Successful fixture runs do not close either user-operated check.

Post-handoff install repair, 2026-09-23: a still-running project Vite process held Rolldown's native binary and blocked `npm ci` with Windows EPERM. The exact project processes were identified and stopped, installation succeeded without manifest/lockfile changes, and frontend type-check, 37 tests, production build, backend build, and API/frontend startup smoke passed again. README now separates installation from routine startup. See the handoff addendum.
