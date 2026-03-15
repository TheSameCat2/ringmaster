# IMPLEMENTATION.md

## Purpose

This document is the execution plan for building the product defined in `planning/PRODUCT.md`.

It is written to serve two audiences:

1. humans coordinating the build, and
2. Codex CLI sessions executing implementation work.

The primary implementation environment is **Arch Linux**, but the product itself must remain **cross-platform** across Linux, macOS, and Windows. The implementation workflow should use **Codex CLI** as the primary agentic coding tool. For scripted and repeatable work, the concrete non-interactive command should be `codex exec`, not the earlier abstract `codex run`; current Codex docs describe `codex exec` as the stable non-interactive entry point and document support for `--json`, `--cd`, `--add-dir`, `--output-schema`, and session resume. Codex docs also support project-scoped `.codex/config.toml` files and `AGENTS.md` guidance that is discovered from the project root toward the current working directory. ([OpenAI Developers][1])

This file is intended to be **updated during implementation**. Each completed work packet should be checked off and briefly summarized in the session log at the end of this document. OpenAI’s Codex guidance recommends using a plan document for longer multi-step work, which is exactly how this file should be used. ([OpenAI Developers][2])

---

## How Codex should use this file

At the start of every implementation session:

1. Read `planning/PRODUCT.md`.
2. Read `planning/IMPLEMENTATION.md`.
3. Read the nearest applicable `AGENTS.md`.
4. Implement **exactly one work packet** unless the packet explicitly says it may be split.
5. Run relevant tests.
6. Update the checklist and session log.
7. Stop and report blockers instead of silently changing scope.

For interactive work, prefer starting Codex from the repo root so root-level `AGENTS.md` and project config are in scope. For scripted work, prefer `codex exec` with explicit working directory and explicit sandbox/approval settings. For code-writing packets, prefer `workspace-write`; for review-only or planning-only packets, prefer `read-only`. Codex docs also note that `--full-auto` is a convenience alias for `workspace-write` with `on-request` approvals, so it is not the right default for unattended scripted packets. ([OpenAI Developers][3])

---

## Repository bootstrap target

The repository should reach this shape early:

```text
/
  planning/
    PRODUCT.md
    IMPLEMENTATION.md
  AGENTS.md
  .codex/
    config.toml
  global.json
  Directory.Build.props
  .editorconfig
  .gitattributes
  src/
    Ringmaster.App/
    Ringmaster.Core/
    Ringmaster.Abstractions/
    Ringmaster.Infrastructure/
    Ringmaster.Git/
    Ringmaster.Codex/
    Ringmaster.GitHub/
  tests/
    Ringmaster.Core.Tests/
    Ringmaster.IntegrationTests/
    Ringmaster.FaultInjectionTests/
  scripts/
    dev/
      bootstrap-arch.sh
      run-phase.sh
      smoke.sh
  samples/
    sample-repo/
```

The root `AGENTS.md` should stay short and high-signal: build/test commands, architecture guardrails, file ownership rules, and a requirement to read the planning docs first. Codex docs explicitly recommend keeping `AGENTS.md` small and using it for durable repo-level rules such as build/test expectations and conventions. A project-scoped `.codex/config.toml` should also be added so Codex can pick up repo-local defaults once the project is trusted. ([OpenAI Developers][4])

---

## Non-negotiable implementation rules

### 1. Runtime behavior lives in C#, not shell scripts

Arch-specific bash scripts are allowed for developer convenience only. The actual product runtime must not depend on bash, GNU-only tools, or Linux-only semantics.

### 2. All external tools are invoked without a shell

Use `ProcessStartInfo` with argument lists, not shell command strings. This is required for portability, quoting correctness, and testability.

### 3. Git integration is via git CLI, not LibGit2Sharp

The product design already assumes git CLI semantics and worktree lifecycle. Keep that choice.

### 4. Codex integration is adapter-based

All Codex behavior must live behind interfaces so the system can be tested with fake runners and deterministic fixtures.

### 5. Deterministic verification owns correctness

Codex may propose changes and interpret failures, but the orchestrator decides success based on file state, git state, and verifier results.

### 6. No hidden in-memory truth

If a crash would lose it, it is not real state. Persist state to disk.

### 7. Every phase must end with tests

No phase is complete without automated validation.

---

## Cross-platform guardrails

These rules apply from the first commit, not at the end.

* Use `Path.Combine`, `Path.GetRelativePath`, and `IFileSystem` abstractions instead of hardcoded `/`.
* Normalize line endings with `.gitattributes`.
* Use UTF-8 consistently for JSON, Markdown, and logs.
* Keep worktree paths short and deterministic to reduce Windows path risk.
* Keep OS-specific behavior isolated in Infrastructure.
* Do not assume symlink support.
* Do not assume case-sensitive filesystems.
* Keep file locking behind an interface with integration tests.
* Use temp directories and temp git repos in tests.
* Treat `git`, `codex`, and `gh` as external executables discovered at runtime.
* Add Windows and macOS build/test smoke coverage before calling the product ready.

For Codex-specific setup, use project-local config where helpful, but keep critical automation behavior explicit on the command line because CLI flags and `--config` overrides take precedence over project config. ([OpenAI Developers][5])

---

## Autonomous validation contract

These rules exist so implementation can proceed without human spot-checking.

* Every completed packet must end with a repeatable validation step: a build, test command, integration scenario, fault-injection run, or deterministic fixture-driven simulation.
* Manual inspection, agent claims, and “looks correct” are never sufficient proof for packet or phase completion.
* When live dependencies are not yet in scope, prove behavior with fake runners, temp directories, temp git repos, and the planned `samples/sample-repo/` fixture instead of skipping validation.
* If a packet cannot yet be validated automatically, treat that as a blocker or create an explicit follow-up packet before marking broader work complete.
* The session log must record the exact validation command or test suite that was actually run.

Validation should climb the cheapest correct ladder:

1. unit tests for pure logic and serialization,
2. integration tests with temp repos/filesystems and fake stage runners,
3. fault-injection tests for crash/recovery behavior,
4. opt-in real-tool smoke tests for `git`, `codex`, and `gh`.

---

## Phase 0 — Bootstrap and Codex operating contract

### Goal

Create a clean repository skeleton that Codex can work in safely and repeatedly.

### Work packets

* [x] **P0.1** Create the .NET solution and project layout.
* [x] **P0.2** Add `global.json`, `Directory.Build.props`, `.editorconfig`, and `.gitattributes`.
* [x] **P0.3** Add initial package references and project references.
* [x] **P0.4** Add root `AGENTS.md` with explicit instruction to read `planning/PRODUCT.md` and `planning/IMPLEMENTATION.md` first.
* [x] **P0.5** Add `.codex/config.toml` with minimal project defaults.
* [x] **P0.6** Add `scripts/dev/bootstrap-arch.sh`, `scripts/dev/smoke.sh`, and `scripts/dev/run-phase.sh` for local convenience only.
* [x] **P0.7** Add placeholder test projects and verify solution build/test.
* [x] **P0.8** Add a minimal `ringmaster --help` command shell.

### Notes

Use Arch Linux as the day-to-day implementation host, but keep every production behavior inside .NET code. The bash scripts in this phase are wrappers only.

The repo-local Codex setup should be intentionally small. Codex docs support project config in `.codex/config.toml` and repo guidance via `AGENTS.md`; keep both concise and focused. ([OpenAI Developers][5])

### Definition of done

* `dotnet build` passes.
* `dotnet test` passes.
* `ringmaster --help` works.
* Root `AGENTS.md` exists.
* Repo-local `.codex/config.toml` exists.
* A Codex session can start at repo root and identify the two planning docs.

---

## Phase 1 — Core domain, durable storage, and snapshots

### Goal

Implement the durable job model and filesystem persistence before integrating Git or Codex.

### Work packets

* [ ] **P1.1** Define core enums and records: job states, stage roles, failure categories, retry counters, PR status, blocker info.
* [ ] **P1.2** Define canonical file models for `JOB.json`, `STATUS.json`, run metadata, and event records.
* [ ] **P1.3** Implement serializer settings and versioned schemas.
* [ ] **P1.4** Implement atomic file writing and append-only event logging.
* [ ] **P1.5** Implement snapshot rebuild from `events.jsonl`.
* [ ] **P1.6** Implement `IJobRepository` and local filesystem repository.
* [ ] **P1.7** Implement CLI commands:

  * `job create`
  * `job show`
  * `status`
* [ ] **P1.8** Add unit tests for serialization, atomic writes, snapshot rebuild, and folder layout.

### Notes

Do not add Git or Codex yet. This phase exists to lock down the persistence contract first.

The outcome of this phase should be a job directory that looks correct on disk even with fake execution.

### Definition of done

* A job can be created from a task description.
* `STATUS.json` and `events/events.jsonl` are created correctly.
* Snapshot rebuild from events works.
* All persistence tests pass.
* The on-disk format matches `PRODUCT.md`.

---

## Phase 2 — State machine and local vertical slice

### Goal

Implement the orchestrator state machine and a fully local end-to-end path using fake stage runners.

### Work packets

* [ ] **P2.1** Implement the explicit state machine and transition rules.
* [ ] **P2.2** Implement `IStateMachine` transition validation.
* [ ] **P2.3** Implement `JobEngine` orchestration for one job.
* [ ] **P2.4** Add fake stage runners for planner, implementer, verifier, and reviewer.
* [ ] **P2.5** Implement per-run folders under `runs/`.
* [ ] **P2.6** Implement stage transition events and current-run tracking in `STATUS.json`.
* [ ] **P2.7** Implement `job run <jobId>` using fake runners only.
* [ ] **P2.8** Add integration tests for happy-path lifecycle and invalid transition rejection.

### Notes

This phase proves the orchestration model independent of external tools. It should be possible to run a job from `QUEUED` to a terminal state without Git or Codex.

Do not skip fake runners. They are the fastest way to stabilize the engine before external integrations make debugging harder.

### Definition of done

* A fake job can transition through the lifecycle.
* Failed transitions are rejected.
* Stage history is durable.
* Re-running a finished fake job is blocked or explicitly handled.
* Integration tests cover at least one success path and one blocked path.

---

## Phase 3 — Git process runner, repo config, and worktree lifecycle

### Goal

Add deterministic git-backed execution and verifier command execution.

### Work packets

* [ ] **P3.1** Implement a generic external process runner with streaming logs and timeouts.
* [ ] **P3.2** Implement git CLI adapter and result parsing.
* [ ] **P3.3** Define repo config file shape for base branch and verification profiles.
* [ ] **P3.4** Implement worktree creation, reuse, discovery, and cleanup helpers.
* [ ] **P3.5** Implement branch naming and worktree path conventions.
* [ ] **P3.6** Implement diff artifact generation:

  * changed files
  * diffstat
  * patch
* [ ] **P3.7** Implement deterministic verifier command execution from repo config.
* [ ] **P3.8** Update `PREPARING` to create or recover the worktree.
* [ ] **P3.9** Update `VERIFYING` to run configured commands.
* [ ] **P3.10** Add temp-repo integration tests for worktrees, branches, verifier execution, and diff artifacts.

### Notes

At the end of this phase, the system still uses fake planner/implementer/reviewer runners, but it uses real Git and real verifier commands.

Keep all command invocation shell-free. Do not build strings like `"git worktree add ..."` and pass them to a shell.

### Definition of done

* A temp git repo can be prepared into a dedicated worktree.
* The orchestrator can run verifier commands from config.
* Diff artifacts are written under the job.
* Git failures surface as structured failures.
* Integration tests pass on Linux.

---

## Phase 4 — Real Codex runner integration

### Goal

Replace fake agent runners with a real Codex CLI adapter.

### Work packets

* [ ] **P4.1** Define `ICodexRunner` and `IAgentRunner`.
* [ ] **P4.2** Implement `codex exec` process invocation.
* [ ] **P4.3** Add support for:

  * `--json`
  * `--cd`
  * `--add-dir`
  * `--output-schema`
  * explicit sandbox and approval flags
* [ ] **P4.4** Capture Codex JSONL events and final structured output per run.
* [ ] **P4.5** Define prompt templates for planner and implementer.
* [ ] **P4.6** Add C# DTOs for schema-constrained outputs.
* [ ] **P4.7** Implement prompt-file generation into the run folder.
* [ ] **P4.8** Add planner and implementer stage adapters using the real Codex runner.
* [ ] **P4.9** Add fake-runner parity tests and opt-in real-Codex smoke tests.
* [ ] **P4.10** Persist Codex session IDs when present.

### Notes

The product document’s abstract Codex invocation should be concretized here as `codex exec`. Codex docs currently document `codex exec` as the non-interactive command, along with resume support and structured output options. Use those capabilities directly instead of inventing a custom chat-style integration. ([OpenAI Developers][6])

Use `workspace-write` plus `--ask-for-approval never` for unattended code-writing packets, and `read-only` plus `--ask-for-approval never` for planner/reviewer packets. That aligns with current Codex approval and sandbox guidance. ([OpenAI Developers][3])

Because Codex can be run from a chosen working directory and can be granted extra writable directories with `--add-dir`, this phase should also lock down the pattern the product will later use for worktree root plus job folder access. ([OpenAI Developers][6])

### Definition of done

* Planner and implementer stages can run via real Codex.
* Each run writes prompt, schema, structured output, and Codex event logs.
* A sample repo can be modified through the real runner.
* Fake and real runners share the same orchestrator interfaces.
* Opt-in smoke tests exist for real Codex integration.

---

## Phase 5 — Failure classification, repair loop, and reviewer

### Goal

Implement the durable repair cycle and review gate.

### Work packets

* [ ] **P5.1** Implement deterministic failure classification.
* [ ] **P5.2** Implement normalized failure signatures.
* [ ] **P5.3** Implement retry policy objects and bounded retry counters.
* [ ] **P5.4** Implement condensed verifier failure artifacts for repair prompts.
* [ ] **P5.5** Implement repair-mode implementer prompts.
* [ ] **P5.6** Implement reviewer prompts and `REVIEW.md`.
* [ ] **P5.7** Implement verdict handling:

  * approved
  * request repair
  * human review required
* [ ] **P5.8** Implement `READY_FOR_PR` and `PR.md` generation.
* [ ] **P5.9** Add integration tests for:

  * compile failure -> repair
  * test failure -> repair
  * repeated same failure -> blocked
  * reviewer requests repair

### Notes

This is the first phase where the system starts to feel autonomous. Keep the repair loop tight and bounded.

Do not allow the reviewer to mutate lifecycle state directly. The reviewer only returns structured findings; the orchestrator decides the transition.

### Definition of done

* The system can classify build/test failures.
* The system can re-run implementer in repair mode.
* Repeated identical failures are detected.
* Reviewer output is durable and affects state transitions.
* `PR.md` is generated when a job reaches `READY_FOR_PR`.

---

## Phase 6 — Scheduler, leases, recovery, and concurrency

### Goal

Move from single-job foreground execution to unattended multi-job execution with crash recovery.

### Work packets

* [ ] **P6.1** Implement queue scanning from job directories.
* [ ] **P6.2** Implement scheduler prioritization and `nextEligibleAtUtc`.
* [ ] **P6.3** Implement job lease acquisition and heartbeats.
* [ ] **P6.4** Implement repo mutation locks.
* [ ] **P6.5** Implement abandoned-run detection.
* [ ] **P6.6** Implement crash recovery and safe resume behavior.
* [ ] **P6.7** Implement bounded worker pool and concurrency limits.
* [ ] **P6.8** Implement `queue once` and `queue run`.
* [ ] **P6.9** Implement minimal notification sinks:

  * console
  * file/jsonl
  * webhook placeholder
* [ ] **P6.10** Add failure-injection tests for crash mid-run, stale leases, and lock contention.

### Notes

Codex docs currently support resuming non-interactive sessions, but the product must treat session resume as an optimization, not as a correctness dependency. Persist the session ID if available, and use resume opportunistically only after disk-backed state has already been written. ([OpenAI Developers][7])

This phase should also prove that the orchestrator can be killed and restarted without losing job truth.

### Definition of done

* Multiple jobs can be scheduled safely.
* Only one worker can own a job at a time.
* Repo mutations are serialized per repo.
* Crash recovery is deterministic.
* Restarting the queue does not corrupt state.
* Fault-injection tests pass.

---

## Phase 7 — PR provider, operator commands, and cleanup

### Goal

Complete the operator-facing toolchain and make results publishable.

### Work packets

* [ ] **P7.1** Implement GitHub PR provider abstraction.
* [ ] **P7.2** Implement `gh`-based PR open/check logic.
* [ ] **P7.3** Implement idempotent PR creation.
* [ ] **P7.4** Implement `pr open`, `worktree open`, and `cleanup`.
* [ ] **P7.5** Implement `doctor` checks for git, codex, gh, auth, and writable paths.
* [ ] **P7.6** Implement retention/cleanup policy for finished worktrees and artifacts.
* [ ] **P7.7** Add integration tests for provider idempotency and cleanup behavior.
* [ ] **P7.8** Add release-note quality CLI docs.

### Notes

Codex should not create PRs directly. The orchestrator owns all external side effects such as pushing branches and opening pull requests.

The operator commands in this phase are what make the system usable day to day by developers.

### Definition of done

* A `READY_FOR_PR` job can open a PR idempotently.
* `doctor` identifies missing runtime prerequisites.
* Cleanup can prune expired worktrees without damaging active jobs.
* Operator commands are documented and tested.

---

## Phase 8 — Cross-platform hardening, packaging, and release readiness

### Goal

Make the product ready for daily professional use beyond the Arch Linux development environment.

### Work packets

* [ ] **P8.1** Add CI matrix for Linux, macOS, and Windows build/test smoke.
* [ ] **P8.2** Add cross-platform integration tests for path handling and file locking where practical.
* [ ] **P8.3** Review all process invocations for shell assumptions.
* [ ] **P8.4** Review all temp-file and lock-file behavior for portability.
* [ ] **P8.5** Package the CLI as a .NET tool or equivalent release artifact.
* [ ] **P8.6** Finalize end-user docs and sample config.
* [ ] **P8.7** Add performance smoke tests for multiple queued jobs.
* [ ] **P8.8** Add upgrade/migration handling for schema version changes.

### Notes

This phase exists to remove hidden Linux assumptions. The product may be built on Arch, but by this point it must behave like a cross-platform .NET CLI, not like a local Linux-only automation script.

### Definition of done

* CI passes on Linux, macOS, and Windows.
* No runtime feature depends on bash.
* Packaging and installation are documented.
* Upgrade path for schema changes exists.
* Release candidate checklist passes.

---

## Deferred until after v1

Do not start these until the previous phases are complete.

* Distributed scheduling across multiple machines
* Database-backed job store
* Web dashboard
* Containerized verifier sandboxes
* GitLab/Azure DevOps PR providers
* Auto-rebase / auto-merge
* Nested or hierarchical jobs
* Sub-agent orchestration inside one job
* Cloud execution backends

---

## Test strategy by layer

### Unit tests

Cover:

* enums and DTOs
* transition validation
* retry policy
* failure signature normalization
* serializer round trips
* path generation
* prompt rendering

### Integration tests

Cover:

* temp repo setup
* worktree lifecycle
* job folder creation
* verifier commands
* diff artifact generation
* queue scanning
* lease contention
* cleanup

### Fault-injection tests

Cover:

* crash during status write
* crash during event append
* crash during active stage
* stale heartbeat
* corrupted worktree metadata
* repeated identical failure loop
* missing external tool

### Real-tool smoke tests

These should be opt-in, not required for every test run:

* real `git`
* real `codex`
* real `gh`

### Simulation harness

Use a deterministic local harness for unattended development before real external integrations are required.

Cover:

* fake planner, implementer, verifier, and reviewer runners
* a compact fixture repo under `samples/sample-repo/`
* temp git repo builders for worktree and branch lifecycle tests
* deterministic failing-build, failing-test, and tool-error fixtures
* recovery scenarios that can be replayed without live Codex or GitHub access

---

## Codex working agreement for implementation

Use this pattern for packet-level work:

1. implement one packet,
2. add or update tests,
3. run the narrowest correct test set,
4. update this document,
5. summarize what changed and what remains.

For longer work packets, it is acceptable to split into sub-packets, but the session must update this file accordingly.

Codex docs support repo-local `AGENTS.md`, project-scoped `.codex/config.toml`, and non-interactive scripted runs via `codex exec`; this repository should use those features directly rather than relying on ad hoc session memory. ([OpenAI Developers][8])

### Recommended interactive prompt template

Use this in a normal `codex` session:

```text
Read planning/PRODUCT.md and planning/IMPLEMENTATION.md first.
Then read AGENTS.md.

Implement packet <PACKET_ID> only.
Do not start later packets.
Keep changes minimal and production-quality.
Add or update tests.
Run the relevant tests.
Update planning/IMPLEMENTATION.md by checking off the packet if complete and append a short session log entry.
If blocked, stop and explain the blocker clearly.
```

### Recommended scripted command pattern

Use this for repeatable packet execution:

```bash
codex exec \
  --json \
  --cd . \
  --sandbox workspace-write \
  --ask-for-approval never \
  "Read planning/PRODUCT.md and planning/IMPLEMENTATION.md first. Implement packet P1.4 only. Add tests, run them, and update planning/IMPLEMENTATION.md."
```

For review-only or planning-only packets, switch `workspace-write` to `read-only`. The current Codex CLI docs describe these flags and approval/sandbox modes directly. ([OpenAI Developers][6])

---

## Phase exit gates

A phase is not complete unless all of these are true:

* all packets in the phase are checked off,
* relevant tests pass,
* at least one repeatable validation path proves the phase’s main success path,
* the session log records the validation commands or suites actually run,
* docs are updated,
* no known blocker is hidden,
* no result is accepted based only on manual inspection or agent claims,
* the next phase can start without relying on chat memory.

---

## Session log

Append entries here as implementation progresses.

```text
YYYY-MM-DD HH:MM UTC
Packet: Px.y
Summary:
Tests:
Files:
Follow-ups:
```

```text
2026-03-15 16:27 UTC
Packet: P0.1
Summary: Initialized the repository, added a baseline .gitignore, created Ringmaster.sln, and scaffolded the initial src/ project layout for App, Core, Abstractions, Infrastructure, Git, Codex, and GitHub.
Tests: dotnet build Ringmaster.sln
Files: .gitignore; Ringmaster.sln; src/Ringmaster.App/; src/Ringmaster.Core/; src/Ringmaster.Abstractions/; src/Ringmaster.Infrastructure/; src/Ringmaster.Git/; src/Ringmaster.Codex/; src/Ringmaster.GitHub/
Follow-ups: Implement P0.2 next to add global SDK/build/editor settings before introducing project references or CLI behavior.
```

```text
2026-03-15 16:33 UTC
Packet: P0.1
Summary: Renamed the product scaffold from Orchestrator to Ringmaster and updated the planning docs so future phases use Ringmaster for concrete project, CLI, config, runtime-path, branch, and commit examples.
Tests: dotnet build Ringmaster.sln
Files: .gitignore; Ringmaster.sln; planning/PRODUCT.md; planning/IMPLEMENTATION.md; src/Ringmaster.App/; src/Ringmaster.Core/; src/Ringmaster.Abstractions/; src/Ringmaster.Infrastructure/; src/Ringmaster.Git/; src/Ringmaster.Codex/; src/Ringmaster.GitHub/
Follow-ups: Continue with P0.2 using the Ringmaster naming baseline.
```

```text
2026-03-15 16:37 UTC
Packet: Planning
Summary: Tightened the implementation contract so unattended work must be gated by repeatable validation, and made the fake-runner plus sample-repo path an explicit simulation harness before live integrations are required.
Tests: not run (planning-doc update only)
Files: planning/IMPLEMENTATION.md; planning/PRODUCT.md
Follow-ups: Apply the validation contract to every future packet, starting with P0.2.
```

```text
2026-03-15 16:45 UTC
Packet: P0.2
Summary: Added global SDK, build, editor, and line-ending configuration at the repo root to keep the solution deterministic across machines.
Tests: dotnet build Ringmaster.sln; dotnet test Ringmaster.sln
Files: global.json; Directory.Build.props; .editorconfig; .gitattributes
Follow-ups: P0.3 wires the initial package graph and project references on top of these shared defaults.
```

```text
2026-03-15 16:45 UTC
Packet: P0.3
Summary: Added the initial package references for the CLI host and wired the first project-reference graph between App, Core, Abstractions, Infrastructure, Git, Codex, GitHub, and the test projects.
Tests: dotnet build Ringmaster.sln; dotnet test Ringmaster.sln
Files: src/Ringmaster.App/Ringmaster.App.csproj; src/Ringmaster.Abstractions/Ringmaster.Abstractions.csproj; src/Ringmaster.Codex/Ringmaster.Codex.csproj; src/Ringmaster.Core/Ringmaster.Core.csproj; src/Ringmaster.Git/Ringmaster.Git.csproj; src/Ringmaster.GitHub/Ringmaster.GitHub.csproj; src/Ringmaster.Infrastructure/Ringmaster.Infrastructure.csproj; tests/Ringmaster.Core.Tests/Ringmaster.Core.Tests.csproj; tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj; tests/Ringmaster.FaultInjectionTests/Ringmaster.FaultInjectionTests.csproj; Ringmaster.sln
Follow-ups: P0.8 uses the App package set to expose the first real command shell.
```

```text
2026-03-15 16:45 UTC
Packet: P0.4
Summary: Added a short repo-specific startup section to AGENTS.md that points every session at the planning docs and records the canonical build, test, help-smoke, and script-syntax commands.
Tests: dotnet build Ringmaster.sln; dotnet test Ringmaster.sln
Files: AGENTS.md
Follow-ups: Keep the root guidance concise as later phases add command coverage.
```

```text
2026-03-15 16:45 UTC
Packet: P0.5
Summary: Added a minimal project-scoped Codex config with repo-local defaults for model, sandbox mode, reasoning effort, and multi-agent support.
Tests: codex --help >/dev/null; codex exec --help >/dev/null
Files: .codex/config.toml
Follow-ups: Expand only if a later phase needs additional repo-local Codex defaults.
```

```text
2026-03-15 16:45 UTC
Packet: P0.6
Summary: Added Arch bootstrap, local smoke, and packet-runner shell wrappers for repeatable development workflows without moving product behavior out of .NET.
Tests: bash -n scripts/dev/bootstrap-arch.sh; bash -n scripts/dev/smoke.sh; bash -n scripts/dev/run-phase.sh; scripts/dev/bootstrap-arch.sh; scripts/dev/smoke.sh
Files: scripts/dev/bootstrap-arch.sh; scripts/dev/smoke.sh; scripts/dev/run-phase.sh; samples/sample-repo/README.md
Follow-ups: Later phases can extend these wrappers, but they must remain developer conveniences only.
```

```text
2026-03-15 16:45 UTC
Packet: P0.7
Summary: Added the three planned test projects, connected them to the solution, and introduced the first core and integration tests plus a placeholder fault-injection test to keep the suite green from the start.
Tests: dotnet test Ringmaster.sln; scripts/dev/smoke.sh
Files: tests/Ringmaster.Core.Tests/; tests/Ringmaster.IntegrationTests/; tests/Ringmaster.FaultInjectionTests/; Ringmaster.sln
Follow-ups: Replace placeholder fault-injection coverage with real crash/recovery tests in later phases.
```

```text
2026-03-15 16:45 UTC
Packet: P0.8
Summary: Replaced the template app with a minimal Generic Host plus System.CommandLine shell that exposes the planned top-level command structure and a working ringmaster help experience.
Tests: dotnet build Ringmaster.sln; dotnet test Ringmaster.sln; ./src/Ringmaster.App/bin/Debug/net10.0/ringmaster --help; scripts/dev/smoke.sh
Files: src/Ringmaster.App/Program.cs; src/Ringmaster.App/CommandLine/RingmasterCli.cs; src/Ringmaster.Core/ProductInfo.cs
Follow-ups: Start Phase 1 with the durable job-state and persistence models that will back these CLI surfaces.
```

---

## Immediate next step

Start **Phase 1, Packet P1.1** and define the core job/state enums and records that the persistence layer will serialize.

[1]: https://developers.openai.com/codex/cli/?utm_source=chatgpt.com "Codex CLI"
[2]: https://developers.openai.com/codex/learn/best-practices/?utm_source=chatgpt.com "Best practices"
[3]: https://developers.openai.com/codex/agent-approvals-security/?utm_source=chatgpt.com "Agent approvals & security"
[4]: https://developers.openai.com/codex/concepts/customization/?utm_source=chatgpt.com "Customization"
[5]: https://developers.openai.com/codex/config-basic/?utm_source=chatgpt.com "Config basics"
[6]: https://developers.openai.com/codex/cli/reference/?utm_source=chatgpt.com "Command line options"
[7]: https://developers.openai.com/codex/noninteractive/?utm_source=chatgpt.com "Non-interactive mode"
[8]: https://developers.openai.com/codex/guides/agents-md/?utm_source=chatgpt.com "Custom instructions with AGENTS.md"
