# Ringmaster

Ringmaster is a filesystem-first workflow engine for durable Codex-driven engineering jobs.

It owns:

- queued job state under `.ringmaster/jobs`
- linked git worktrees for isolated implementation
- verification and review artifacts
- repair-loop and retry decisions
- pull request publication through GitHub CLI

Codex remains a replaceable worker. Ringmaster owns state, retries, verification, and git side effects.

## Install

Build and pack the local .NET tool:

```bash
dotnet pack src/Ringmaster.App/Ringmaster.App.csproj -c Release
```

Install from the local package output:

```bash
dotnet tool install --global Ringmaster.Tool --add-source artifacts/packages
```

Update an existing installation:

```bash
dotnet tool update --global Ringmaster.Tool --add-source artifacts/packages
```

## Quick start

1. Ensure `git`, `codex`, and `gh` are installed and authenticated.
2. Add a repository config at `ringmaster.json`.
3. Run `ringmaster doctor`.
4. Create work with `ringmaster job create`.
5. Execute with `ringmaster job run` or `ringmaster queue run`.

## Minimal config

See [`samples/sample-repo/ringmaster.json`](samples/sample-repo/ringmaster.json) for a concrete example.

The smallest useful config looks like this:

```json
{
  "schemaVersion": 1,
  "baseBranch": "master",
  "verificationProfiles": {
    "default": {
      "commands": [
        {
          "name": "build",
          "fileName": "dotnet",
          "arguments": ["build", "Ringmaster.sln"],
          "timeoutSeconds": 900
        },
        {
          "name": "test",
          "fileName": "dotnet",
          "arguments": ["test", "Ringmaster.sln", "--no-build"],
          "timeoutSeconds": 1200
        }
      ]
    }
  }
}
```

## Operator commands

- `ringmaster doctor`
- `ringmaster job create`
- `ringmaster job show`
- `ringmaster job run`
- `ringmaster queue once`
- `ringmaster queue run`
- `ringmaster pr open`
- `ringmaster worktree open`
- `ringmaster cleanup`

Detailed CLI examples live in [`docs/CLI.md`](docs/CLI.md).
