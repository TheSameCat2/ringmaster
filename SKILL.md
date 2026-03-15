---
name: ringmaster-agent
description: Use this skill when you need an agent to operate a repository through Ringmaster: initialize Ringmaster, validate prerequisites, create jobs, run or resume work, handle blocked jobs, inspect logs and worktrees, publish PRs, and clean up artifacts without treating ad-hoc shell state as authoritative.
---

# Ringmaster Agent

Use this skill when the task is to drive work through the `ringmaster` CLI instead of inventing an ad-hoc workflow.

## What Ringmaster owns

Ringmaster is the system of record for:

- repo config in `ringmaster.json`
- runtime state under `.ringmaster/`
- per-job state under `.ringmaster/jobs/<jobId>/`
- linked worktree locations
- verification, repair, review, and PR publication flow

Do not manually edit `.ringmaster` runtime files unless the user explicitly asks for low-level recovery work. Prefer CLI commands and normal repo edits.

## When to use this skill

Use it when asked to:

- set up Ringmaster in a repository
- create or run autonomous engineering jobs
- resume, unblock, cancel, or inspect jobs
- inspect Ringmaster logs, worktrees, or PR state
- automate the normal operator loop for a repo that already uses Ringmaster

Do not use it for general coding work that is unrelated to Ringmaster operation.

## Core workflow

### 1. Initialize or validate the repo

From the repository root:

```bash
ringmaster init --base-branch <branch>
ringmaster doctor
```

Notes:

- `ringmaster init` creates `.ringmaster/`, adds `.ringmaster/` to `.gitignore` if needed, and scaffolds `ringmaster.json` when missing.
- `ringmaster doctor` is the fastest way to confirm `git`, `codex`, `gh`, config validity, and writable runtime directories.
- Ringmaster uses explicit operator exit codes:
  - `0` success
  - `10` blocked
  - `20` failed
  - `30` tool or config error

If `ringmaster.json` exists, treat it as authoritative and edit it only when the verification profile is wrong or incomplete.

### 2. Create a durable job

Prefer a task file over an inline description when the task is more than one short sentence.

```bash
ringmaster job create \
  --title "Add retry handling to webhook consumer" \
  --task-file task.md \
  --verify-profile default
```

Capture the printed job id and use it for all later operations.

### 3. Run or schedule work

Foreground execution:

```bash
ringmaster job run <jobId>
```

Resume a blocked or interrupted job:

```bash
ringmaster job resume <jobId>
```

Long-running queue worker:

```bash
ringmaster queue run --max-parallel 1 --watch
```

Single scheduling pass:

```bash
ringmaster queue once
```

## Operator actions

### Status and logs

Use these first when you need to understand what happened:

```bash
ringmaster status --job-id <jobId>
ringmaster status --watch
ringmaster logs <jobId>
ringmaster logs <jobId> --run <runId>
ringmaster logs <jobId> --follow
ringmaster worktree open <jobId>
```

Important files to check when needed:

- `.ringmaster/jobs/<jobId>/JOB.md`
- `.ringmaster/jobs/<jobId>/PLAN.md`
- `.ringmaster/jobs/<jobId>/NOTES.md`
- `.ringmaster/jobs/<jobId>/REVIEW.md`
- `.ringmaster/jobs/<jobId>/PR.md`
- `.ringmaster/jobs/<jobId>/runs/<runId>/run.json`

### Human unblock flow

If a job returns exit code `10` or enters `BLOCKED`, inspect the blocker context and append a durable human answer:

```bash
ringmaster job unblock <jobId> \
  --message "Use in-memory retry only; no scheduler changes."
```

This records the answer in `NOTES.md` and resumes from the stored `resumeState`.

### Cancel flow

If the user wants to stop a queued, blocked, or ready-for-pr job:

```bash
ringmaster job cancel <jobId>
```

That records an operator cancellation and moves the job to terminal `FAILED`.

### PR publication

If auto-open was disabled and the job is `READY_FOR_PR`:

```bash
ringmaster pr open <jobId>
```

## Guardrails

- Run commands from the repository root unless a specific command requires otherwise.
- Do not treat a linked worktree as the source of truth; Ringmaster job files are the durable record.
- Do not hand-edit state transitions in `.ringmaster/jobs/.../STATUS.json` or event logs unless the task is explicit repair.
- Prefer `ringmaster status`, `ringmaster logs`, and `ringmaster worktree open` over manual filesystem guesses.
- If verification fails repeatedly, inspect the latest run logs before deciding whether to unblock, resume, or change config.

## References

Read these only as needed:

- `README.md` for end-to-end examples
- `docs/CLI.md` for command semantics
- `planning/PRODUCT.md` for product behavior and operator expectations
- `planning/IMPLEMENTATION.md` for what has already been built
