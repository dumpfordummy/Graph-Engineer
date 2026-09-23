# Project progress

Updated 2026-09-24: **M2 is READY FOR USER PROVIDER CHECK.** Provider profiles, Windows DPAPI storage, local browser pairing/Host/Origin/antiforgery protection, the explicit backend Responses probe and per-node provider selection are implemented. Run remains disabled until M3. The final full suite passed with exit 0: 39 Core + 102 API tests, 57 frontend tests, type-check/lint/build and 17 browser checks. Real workflow/profile/credential persistence across backend restart passed using isolated synthetic fixtures. All three final launcher lifecycle scenarios passed, with a corrected harness incident recorded transparently in [M2_REPORT.md](handoffs/M2_REPORT.md).

Actual user-provider verification is **NOT RUN**. The user must enter actual details in the local application, explicitly test, restart, pair again and repeat. Automated fixtures do not establish real-provider compatibility. Exact instructions, evidence, limitations and the next gate are in the M2 handoff and README.

| Milestone | Status |
|---|---|
| M1: Persistent editor | Conditionally accepted for progression; M1-C1 lifecycle verification completed in M2 with documented harness incident |
| M2: Providers and local security | Implemented and automatically verified; READY FOR USER PROVIDER CHECK; actual provider NOT RUN |
| M3: Real model workflow execution | Not started; not authorized |
| M4: Coding agent, commands, approvals | Not started; not authorized |
| M5: Bounded review/repair pilot | Not started; not authorized |
| M6: Hardening and packaging | Not started; not authorized |

No commits, pushes, merges, or M3+ implementation are authorized. M2 is explicitly authorized by the lead/user assignment; final acceptance remains a lead/user decision.

Post-handoff install repair, 2026-09-23: a still-running project Vite process held Rolldown's native binary and blocked `npm ci` with Windows EPERM. The exact project processes were identified and stopped, installation succeeded without manifest/lockfile changes, and frontend type-check, 37 tests, production build, backend build, and API/frontend startup smoke passed again. README now separates installation from routine startup. See the handoff addendum.
