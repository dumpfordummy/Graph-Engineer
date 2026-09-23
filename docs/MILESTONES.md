# Milestone plan and review gates

Implement one milestone at a time. Completion is based on demonstrated behavior and test evidence, not a feature checklist marked by the implementing agent.

## M1 — Foundation and persistent editor (AUTHORIZED NOW)

Build the repository, local API/database, workflow list/editor, node inspector, save/load, document export/import, and structural validation for Start/Model Call/End. No provider calls, credential entry, commands, or execution simulation.

Gate: create and edit a workflow; save; restart both services; reopen and verify node configuration, edges, positions, and metadata. Demonstrate validation, import/export, save-error recovery, and stale-revision handling. See the detailed M1 task.

## M2 — Provider setup and local security

Implement provider profiles, secure secret storage, local request/session protections, endpoint validation, and a real connection test. Select one initial protocol using an actual non-sensitive provider test; add Responses or Chat Completions independently rather than pretending they are interchangeable. The user enters secrets through the local app, never through a Codex prompt.

Gate: a real test succeeds; incorrect credentials and unreachable endpoints show sanitized errors; profile updates preserve secrets; no secret appears in normal GETs, exports, logs, or child environments. Automated tests use synthetic credentials and controlled fixtures. Real credential verification is explicitly reported separately.

## M3 — First real execution

Support Start → Model Call → End and a sequence of Model Call nodes with explicit data references. Persist the run snapshot, resolved inputs, attempts, output artifacts, and event sequence. Add Runs view, cancellation, SignalR notifications, and browser reconnect recovery. Keep one worker and conservative restart semantics.

Gate: a real response is persisted and displayed; browser refresh/reconnect does not create another call; bad output and provider failure fail honestly; backend restart reports interrupted work rather than automatically replaying uncertain effects.

## M4 — Coding agent, commands, and approvals

Add durable Human Approval, Tool/Command, and Coding Agent nodes. Build an adapter for the installed local Codex version. Start with a disposable synthetic repository, one writer per workspace, environment restrictions, and explicit human authorization. Bind approval to exact evidence. Do not add arbitrary graph loops yet.

Gate: Entry Approval → Coding Agent → Build + Tests → Exit Approval produces a real diff and genuine command results. A rejected or duplicate approval cannot dispatch work improperly. Restart at a waiting gate retains the request. Cancellation records real partial effects.

## M5 — Reviewer routing and engineering pilot

Add validated structured reviewer results, a limited condition/router, bounded repair iterations, and task-specific templates. Define loop semantics explicitly; do not simply remove cycle validation. First template: Entry Approval → Converter → Build + Tests → Reviewer → Exit Approval, with a capped repair route back to Converter.

Gate: an intentionally seeded defect triggers a reproducible failing test and focused correction; no unrelated files are changed; attempts remain inspectable; reaching the cap stops for the human. Only after synthetic tests pass should approved company repositories and slot-game materials be used.

## M6 — Operational hardening and packaging

Expand crash/fault tests, protect/import versions, add artifact retention, backups, dependency/security checks, workspace-lock diagnostics, and reproducible local distribution. Test recovery when a process finishes but the database acknowledgement fails. Document limitations and supported runtime/provider versions.

Gate: a second trusted engineer can install from the documented steps, configure their own credentials, run the pilot, and diagnose a failure without hidden developer setup.

## Explicitly deferred

Multi-user/cloud hosting, remote workers, billing, arbitrary plugins, vector databases/RAG, automatic graph generation, collaborative editing, unrestricted subgraphs, parallel code-writing agents, auto-merge/deploy, and slot-math certification.

These are not forbidden forever. They require a demonstrated need and a separate design decision after the core workflow is reliable.
