# Ringmaster CLI

`ringmaster` is a local operator CLI for durable Codex-driven job execution.

## Common commands

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

Exit code is `0` when all checks pass and `1` when any check fails.

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
ringmaster queue once
ringmaster queue run --max-parallel 2
```

`job run` executes one job in the foreground.

`queue once` runs a single scheduler pass.

`queue run` keeps polling for runnable jobs and uses the scheduler lock to ensure only one worker loop owns the repository at a time.

If a job reaches `READY_FOR_PR` and `--auto-open-pr` was enabled at creation time, Ringmaster pushes the job branch and publishes the PR automatically.

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

## JSON output

Most operator commands support `--json` for scripting. JSON output is the preferred interface for automation and CI probes.
