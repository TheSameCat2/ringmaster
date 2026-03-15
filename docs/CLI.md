# Ringmaster CLI

`ringmaster` is a local operator CLI for durable Codex-driven job execution.

## Common commands

### Initialize a repository

```bash
ringmaster init --base-branch main --pr-provider github
ringmaster init --json
```

`init`:

- creates `.ringmaster/runtime` and `.ringmaster/jobs`
- creates `.ringmaster/runtime/notifications.jsonl`
- adds `.ringmaster/` to `.gitignore` when needed
- scaffolds `ringmaster.json` when it does not already exist
- seeds `dotnet build/test` verification commands when the repo root has a single solution file

### Check prerequisites

```bash
ringmaster doctor
ringmaster doctor --json
```

Checks:

- `git` availability
- `codex` availability and login status
- `gh` availability and auth status
- `ringmaster.json` validity
- worktree root writability
- runtime folder writability

Exit code is `0` when all checks pass and `30` when any prerequisite or config check fails.

### Create a job

```bash
ringmaster job create \
  --title "Add retry handling to the scheduler" \
  --task-file task.md \
  --verify-profile default \
  --priority 75 \
  --auto-open-pr \
  --draft-pr true \
  --label automation \
  --label codex
```

Use `--description` for inline task text instead of `--task-file`.

### Run work

```bash
ringmaster job run <jobId>
ringmaster job resume <jobId>
ringmaster job unblock <jobId> --message "Use in-memory retry only."
ringmaster job cancel <jobId>
ringmaster queue once
ringmaster queue run --max-parallel 2 --watch
```

`job run` executes one job in the foreground.

`job resume` continues a blocked job, an abandoned active-stage job, or a `READY_FOR_PR` job that still needs PR publication.

`job unblock` appends durable human guidance to `NOTES.md` and resumes from the stored `resumeState`.

`job cancel` records an operator cancellation and moves the job to the terminal `FAILED` state.

`queue once` runs a single scheduler pass.

`queue run` keeps polling for runnable jobs and uses the scheduler lock to ensure only one worker loop owns the repository at a time. `--watch` is accepted as an explicit alias for this long-running mode.

If a job reaches `READY_FOR_PR` and `--auto-open-pr` was enabled at creation time, Ringmaster pushes the job branch and publishes the PR automatically.

### Inspect status

```bash
ringmaster status
ringmaster status --job-id <jobId> --json
ringmaster status --watch
```

`status --watch` refreshes a live human-readable dashboard with:

- state
- current stage
- active run id
- elapsed time
- retry count
- last failure summary
- PR URL when present

Blocked jobs return exit code `10`. Failed jobs return exit code `20`.

### Inspect logs

```bash
ringmaster logs <jobId>
ringmaster logs <jobId> --run 0004-verifying-system
ringmaster logs <jobId> --follow
ringmaster logs <jobId> --json
```

`logs` selects the latest run by default, resolves the primary log artifact for that run, and either prints or follows it.

### Publish a pull request

```bash
ringmaster pr open <jobId>
ringmaster pr open <jobId> --json
```

`pr open`:

- requires the job to be `READY_FOR_PR` or already `DONE`
- pushes `origin/<jobBranch>`
- checks for an existing PR by head branch before creating one
- creates a PR with explicit `gh` automation flags when needed
- records the PR URL and status durably

Successful publication transitions the job to `DONE`.

### Inspect the linked worktree

```bash
ringmaster worktree open <jobId>
ringmaster worktree open <jobId> --json
```

This prints the current linked worktree path when it still exists. If cleanup has already pruned the worktree, the command returns a non-zero exit code and includes the last recorded path.

### Retention cleanup

```bash
ringmaster cleanup
ringmaster cleanup --retain-days 7 --artifact-retain-days 30
ringmaster cleanup --json
```

Cleanup currently:

- removes linked worktrees for retained `DONE` and `FAILED` jobs
- requires durable PR state before removing a `DONE` worktree
- skips jobs with a recent lease heartbeat
- prunes old `*.log` files from job run folders while keeping durable JSON and Markdown records

## Exit codes

- `0`: success
- `10`: command completed and the resulting job state is `BLOCKED`
- `20`: command completed and the resulting job state is `FAILED`
- `30`: tool error, config error, invalid operator request, or missing artifact/state

## JSON output

Most operator commands support `--json` for scripting. JSON output is the preferred interface for automation and CI probes.
