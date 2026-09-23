Continue the existing Graph Engineering repository. Implement M2 only.

Read first:
1. AGENTS.md and the current docs/PROGRESS.md.
2. docs/ARCHITECTURE.md and docs/CONTRACTS.md.
3. docs/handoffs/M1_REPORT.md.
4. docs/handoffs/M1_LEAD_REVIEW.md.
5. docs/tasks/M2_PROVIDERS_SECURITY.md.
6. docs/handoffs/M2_REPORT_TEMPLATE.md and docs/M2_SOURCES.md.

The lead has conditionally accepted M1 for progression and authorized M2.
This instruction supersedes stale M1-only scope statements, not other
repository rules. Update only those scope/status statements in AGENTS.md,
MILESTONES.md, and PROGRESS.md after inspecting their current contents.

Inspect the working tree and preserve all existing/uncommitted work.
Run the existing M1 baseline before implementation. Investigate and address
the documented launcher/process-cleanup carry-over with isolated tests.
Do not reinstall dependencies while owned dev processes hold their files.
Do not broadly kill node/dotnet processes.

State a short plan, define provider/security contracts, then implement
end to end. Do not stop at planning or scaffolding.

Deliver provider profiles, a local browser session and request protections,
Windows DPAPI-protected secrets, a backend Responses text connection probe,
and actual profile selection in Model Call nodes. Preserve M1 data and UI.
Implement Responses only. Keep deferred capabilities explicitly untested.
Keep Run disabled. No graph execution, shell nodes, agent invocation, M3,
automatic commits, pushes, merges, or unrelated refactoring.

Use parallel workers only after shared contracts are agreed and with
non-overlapping file ownership. Integrate and run the whole application.

Use synthetic secrets and controlled provider fixtures for automated tests.
Never inspect Codex auth files, discover real keys, or read the user's live
credential store. The user will enter real provider details in the local
application and run the final synthetic connection check personally.
Do not capture real credentials in logs, screenshots, browser traces, HARs,
environment variables, or the handoff. Do not bypass security to make tests pass.

Run all relevant backend/frontend/browser checks and the full M1 regression.
Test actual Windows protected storage with synthetic data and record the
security/lifecycle tests specified in the task. Review the final diff.

Write docs/handoffs/M2_REPORT.md with actual evidence and update PROGRESS.md.
Without a user-operated real-provider result, mark that check NOT RUN and
report READY FOR USER PROVIDER CHECK rather than inventing a PASS.
Provide exact startup, pairing, and real-provider-test instructions.
Stop at the M2 gate.
