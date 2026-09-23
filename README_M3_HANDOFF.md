# Graph Engineering — M3 handoff

Prepared: 2026-09-24. This is an additive implementation assignment, not application code.

## Lead decision

M2 is conditionally accepted for continued development. The user supplied a screenshot of a successful saved-profile text probe reporting GLM-5.2, 865 ms, connection version 1, and GE_CONNECTION_OK. A before/after-backend-restart pair has not been established. Complete that human-operated check separately; do not infer it from the screenshot or repeat a live probe through Codex.

M3 implementation and synthetic-fixture verification are authorized now. Real-provider M3 acceptance remains a separate user-operated gate. Neither milestone is a production/security certification.

## Apply

Merge this package's docs/ directory into the existing repository's docs/ directory. Do not replace the repository, re-extract the original starter, overwrite application files, or replace AGENTS.md. If any destination file already has user edits, inspect and reconcile it rather than overwriting it.

Read docs/tasks/M3_CODEX_PROMPT.md and paste its contents into Codex in the existing repository. The assignment authorizes M3 only; narrowly update stale milestone statements after inspecting current instructions.

The detailed assignment is docs/tasks/M3_RUN_EXECUTION.md. Data semantics and the manual example are in docs/tasks/M3_DATA_BINDINGS.md. The lead review is docs/handoffs/M2_LEAD_REVIEW.md.

## Operator boundary

Use the existing local app for real credentials and live calls. Codex must use isolated data, synthetic secrets, and controlled providers. Never read the user's live database, pairing material, credential store, or Codex authentication files during development/testing. Do not copy live databases into source checkpoints or this handoff.

Keep the M2 process-cleanup correction. The report records an unrelated Windows Update notification process being stopped by an earlier test harness. M3 must not repeat ancestry-based termination.

## Return

Return docs/handoffs/M3_REPORT.md and synthetic-only UI evidence. Separately report the M2 post-restart probe and user-operated M3 run results. No automatic commit, push, merge, release, or M4 implementation is authorized.
