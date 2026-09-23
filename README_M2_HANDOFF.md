# Graph Engineering — M2 handoff

This is an additive instruction pack, not application code and not a replacement for the M1 repository.

## Apply

1. Keep the implemented repository. Do not extract the original starter over it.
2. Copy this pack's `docs/` contents into the existing repository's `docs/` directory, merging directories. Review a collision rather than overwriting an existing file with the same name.
3. Keep the user's submitted M1 report at `docs/handoffs/M1_REPORT.md`.
4. Open Codex in the existing repository root and paste `docs/tasks/M2_CODEX_PROMPT.md`.
5. Preserve the existing AGENTS.md. The assignment explicitly authorizes updating only stale M1 scope statements; all other safety and architecture rules continue to apply.

The prompt does not authorize automatic commits, pushes, merges, production deployment, application-provider credential discovery, or M3.

## Scope

M2 implements provider settings, local request/session protection, Windows-protected credential storage, a Responses text connection test, and graph-node profile selection. It also investigates the documented startup/shutdown carry-over. The workflow Run button stays disabled.

A fixed connection probe is an intentional real provider request when the user clicks Test. It is not workflow execution. Automated acceptance uses explicitly synthetic credentials and a controlled provider fixture, never the user's production profile.

## Acceptance boundary

M1 is conditionally accepted for development progression on the submitted report and screenshot. The repository and raw test logs were not supplied to the lead reviewer. See `docs/handoffs/M1_LEAD_REVIEW.md`.

M2 can be implementation-complete without real credentials being available to Codex. In that case report `READY FOR USER PROVIDER CHECK`, not a real-provider PASS. The user performs that final check locally.
