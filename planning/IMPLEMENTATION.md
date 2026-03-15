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

* [x] **P1.1** Define core enums and records: job states, stage roles, failure categories, retry counters, PR status, blocker info.
* [x] **P1.2** Define canonical file models for `JOB.json`, `STATUS.json`, run metadata, and event records.
* [x] **P1.3** Implement serializer settings and versioned schemas.
* [x] **P1.4** Implement atomic file writing and append-only event logging.
* [x] **P1.5** Implement snapshot rebuild from `events.jsonl`.
* [x] **P1.6** Implement `IJobRepository` and local filesystem repository.
* [x] **P1.7** Implement CLI commands:

  * `job create`
  * `job show`
  * `status`
* [x] **P1.8** Add unit tests for serialization, atomic writes, snapshot rebuild, and folder layout.

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

* [x] **P2.1** Implement the explicit state machine and transition rules.
* [x] **P2.2** Implement `IStateMachine` transition validation.
* [x] **P2.3** Implement `JobEngine` orchestration for one job.
* [x] **P2.4** Add fake stage runners for planner, implementer, verifier, and reviewer.
* [x] **P2.5** Implement per-run folders under `runs/`.
* [x] **P2.6** Implement stage transition events and current-run tracking in `STATUS.json`.
* [x] **P2.7** Implement `job run <jobId>` using fake runners only.
* [x] **P2.8** Add integration tests for happy-path lifecycle and invalid transition rejection.

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

* [x] **P3.1** Implement a generic external process runner with streaming logs and timeouts.
* [x] **P3.2** Implement git CLI adapter and result parsing.
* [x] **P3.3** Define repo config file shape for base branch and verification profiles.
* [x] **P3.4** Implement worktree creation, reuse, discovery, and cleanup helpers.
* [x] **P3.5** Implement branch naming and worktree path conventions.
* [x] **P3.6** Implement diff artifact generation:

  * changed files
  * diffstat
  * patch
* [x] **P3.7** Implement deterministic verifier command execution from repo config.
* [x] **P3.8** Update `PREPARING` to create or recover the worktree.
* [x] **P3.9** Update `VERIFYING` to run configured commands.
* [x] **P3.10** Add temp-repo integration tests for worktrees, branches, verifier execution, and diff artifacts.

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

* [x] **P4.1** Define `ICodexRunner` and `IAgentRunner`.
* [x] **P4.2** Implement `codex exec` process invocation.
* [x] **P4.3** Add support for:

  * `--json`
  * `--cd`
  * `--add-dir`
  * `--output-schema`
  * explicit sandbox and approval flags
* [x] **P4.4** Capture Codex JSONL events and final structured output per run.
* [x] **P4.5** Define prompt templates for planner and implementer.
* [x] **P4.6** Add C# DTOs for schema-constrained outputs.
* [x] **P4.7** Implement prompt-file generation into the run folder.
* [x] **P4.8** Add planner and implementer stage adapters using the real Codex runner.
* [x] **P4.9** Add fake-runner parity tests and opt-in real-Codex smoke tests.
* [x] **P4.10** Persist Codex session IDs when present.

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

* [x] **P5.1** Implement deterministic failure classification.
* [x] **P5.2** Implement normalized failure signatures.
* [x] **P5.3** Implement retry policy objects and bounded retry counters.
* [x] **P5.4** Implement condensed verifier failure artifacts for repair prompts.
* [x] **P5.5** Implement repair-mode implementer prompts.
* [x] **P5.6** Implement reviewer prompts and `REVIEW.md`.
* [x] **P5.7** Implement verdict handling:

  * approved
  * request repair
  * human review required
* [x] **P5.8** Implement `READY_FOR_PR` and `PR.md` generation.
* [x] **P5.9** Add integration tests for:

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

* [x] **P6.1** Implement queue scanning from job directories.
* [x] **P6.2** Implement scheduler prioritization and `nextEligibleAtUtc`.
* [x] **P6.3** Implement job lease acquisition and heartbeats.
* [x] **P6.4** Implement repo mutation locks.
* [x] **P6.5** Implement abandoned-run detection.
* [x] **P6.6** Implement crash recovery and safe resume behavior.
* [x] **P6.7** Implement bounded worker pool and concurrency limits.
* [x] **P6.8** Implement `queue once` and `queue run`.
* [x] **P6.9** Implement minimal notification sinks:

  * console
  * file/jsonl
  * webhook placeholder
* [x] **P6.10** Add failure-injection tests for crash mid-run, stale leases, and lock contention.

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

* [x] **P7.1** Implement GitHub PR provider abstraction.
* [x] **P7.2** Implement `gh`-based PR open/check logic.
* [x] **P7.3** Implement idempotent PR creation.
* [x] **P7.4** Implement `pr open`, `worktree open`, and `cleanup`.
* [x] **P7.5** Implement `doctor` checks for git, codex, gh, auth, and writable paths.
* [x] **P7.6** Implement retention/cleanup policy for finished worktrees and artifacts.
* [x] **P7.7** Add integration tests for provider idempotency and cleanup behavior.
* [x] **P7.8** Add release-note quality CLI docs.

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

* [x] **P8.1** Add CI matrix for Linux, macOS, and Windows build/test smoke.
* [x] **P8.2** Add cross-platform integration tests for path handling and file locking where practical.
* [x] **P8.3** Review all process invocations for shell assumptions.
* [x] **P8.4** Review all temp-file and lock-file behavior for portability.
* [x] **P8.5** Package the CLI as a .NET tool or equivalent release artifact.
* [x] **P8.6** Finalize end-user docs and sample config.
* [x] **P8.7** Add performance smoke tests for multiple queued jobs.
* [x] **P8.8** Add upgrade/migration handling for schema version changes.

### Notes

This phase exists to remove hidden Linux assumptions. The product may be built on Arch, but by this point it must behave like a cross-platform .NET CLI, not like a local Linux-only automation script.

### Definition of done

* CI passes on Linux, macOS, and Windows.
* No runtime feature depends on bash.
* Packaging and installation are documented.
* Upgrade path for schema changes exists.
* Release candidate checklist passes.

---

## Phase 9 — Command completion and operator UX polish

### Goal

Close the remaining product-level CLI gaps that were not covered by the original packet list so the documented operator surface is fully usable.

### Work packets

* [x] **P9.1** Implement `ringmaster init` runtime/config scaffolding.
* [x] **P9.2** Implement `ringmaster job resume` for blocked, abandoned, and ready-for-pr jobs.
* [x] **P9.3** Implement `ringmaster job unblock` with durable human-input storage and resume.
* [x] **P9.4** Implement `ringmaster job cancel` with explicit terminal semantics.
* [x] **P9.5** Implement `ringmaster logs` for run selection and tail/follow behavior.
* [x] **P9.6** Implement explicit operator-facing exit codes and wire them across commands.
* [x] **P9.7** Add integration coverage for the remaining operator commands and exit codes.

### Notes

The product document defines these commands and UX contracts, but the original implementation plan did not packetize them. This phase closes that gap explicitly.

### Definition of done

* The documented top-level CLI commands are implemented or intentionally deferred with explicit rationale.
* Resume, unblock, cancel, and log inspection are proven by automated tests.
* Operator commands use stable exit codes for success, blocked, failed, and tool/config errors.

---

## Post-v1 ideas

Deferred follow-on ideas now live in [`IDEAS.md`](/home/thesamecat/dev/csharp/ringmaster/IDEAS.md).

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

```text
2026-03-15 16:55 UTC
Packet: P1.1
Summary: Defined the first durable job-domain enums and records for lifecycle state, stage roles, failure categories, blocker info, PR status, review status, execution state, and retry counters.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/JobEnums.cs; src/Ringmaster.Core/Jobs/JobStatusSnapshot.cs; src/Ringmaster.Core/Jobs/JobDefinition.cs; src/Ringmaster.Core/Jobs/JobRunRecord.cs; src/Ringmaster.Core/ProductInfo.cs
Follow-ups: P1.2 and P1.3 serialize these records directly into the on-disk contract.
```

```text
2026-03-15 16:55 UTC
Packet: P1.2
Summary: Added canonical file models for JOB.json, STATUS.json, run metadata, and event records, including the fields needed to rebuild queued status from the event log.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/JobDefinition.cs; src/Ringmaster.Core/Jobs/JobStatusSnapshot.cs; src/Ringmaster.Core/Jobs/JobRunRecord.cs; src/Ringmaster.Core/Jobs/JobEventRecord.cs
Follow-ups: Later phases can extend these records without changing the basic file ownership model.
```

```text
2026-03-15 16:55 UTC
Packet: P1.3
Summary: Added the shared JSON serializer settings for schema version 1, camelCase JSON, string enums, pretty-printed snapshots, and compact JSONL event serialization.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Serialization/RingmasterJsonSerializer.cs; tests/Ringmaster.Core.Tests/RingmasterJsonSerializerTests.cs
Follow-ups: Keep future schema additions backward-compatible or bump schema version explicitly.
```

```text
2026-03-15 16:55 UTC
Packet: P1.4
Summary: Implemented atomic UTF-8 file writes and append-only JSONL event logging for durable job persistence.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Infrastructure/Persistence/AtomicFileWriter.cs; src/Ringmaster.Infrastructure/Persistence/JobEventLogStore.cs; tests/Ringmaster.IntegrationTests/LocalFilesystemJobRepositoryTests.cs
Follow-ups: Later phases should reuse these primitives for run metadata, artifacts, and status updates.
```

```text
2026-03-15 16:55 UTC
Packet: P1.5
Summary: Implemented snapshot rebuild from events.jsonl so STATUS.json can be recreated deterministically if the materialized snapshot is missing or damaged.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/JobSnapshotRebuilder.cs; tests/Ringmaster.Core.Tests/JobSnapshotRebuilderTests.cs; tests/Ringmaster.IntegrationTests/LocalFilesystemJobRepositoryTests.cs
Follow-ups: Extend the reducer as later phases introduce more event types.
```

```text
2026-03-15 16:55 UTC
Packet: P1.6
Summary: Added IJobRepository plus the local filesystem repository, deterministic job ID generation, job folder layout creation, placeholder markdown files, and status rebuilding on load.
Tests: dotnet test Ringmaster.sln; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster job create --title "Add retry handling" --description "Implement bounded retries." --json
Files: src/Ringmaster.Abstractions/Jobs/JobRepositoryContracts.cs; src/Ringmaster.Infrastructure/Persistence/DefaultJobIdGenerator.cs; src/Ringmaster.Infrastructure/Persistence/LocalFilesystemJobRepository.cs
Follow-ups: The repository is ready for the explicit state machine in Phase 2.
```

```text
2026-03-15 16:55 UTC
Packet: P1.7
Summary: Replaced the placeholder job/status shell commands with real job creation, job inspection, and queue-status surfaces backed by the filesystem repository.
Tests: dotnet test Ringmaster.sln; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster job show job-20260315-c0119f2e5 --json; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster status --json
Files: src/Ringmaster.App/Program.cs; src/Ringmaster.App/RingmasterApplicationContext.cs; src/Ringmaster.App/CommandLine/RingmasterCli.cs; tests/Ringmaster.IntegrationTests/RingmasterCliCommandTests.cs
Follow-ups: Phase 2 can now drive the same repository through fake stage execution.
```

```text
2026-03-15 16:55 UTC
Packet: P1.8
Summary: Added unit and integration coverage for serialization, snapshot rebuild, atomic writes, repository folder layout, and the Phase 1 CLI command path.
Tests: dotnet test Ringmaster.sln
Files: tests/Ringmaster.Core.Tests/JobSnapshotRebuilderTests.cs; tests/Ringmaster.Core.Tests/RingmasterJsonSerializerTests.cs; tests/Ringmaster.IntegrationTests/LocalFilesystemJobRepositoryTests.cs; tests/Ringmaster.IntegrationTests/RingmasterCliCommandTests.cs; tests/Ringmaster.IntegrationTests/Testing/
Follow-ups: Add fake-runner lifecycle integration tests in Phase 2.
```

```text
2026-03-15 17:06 UTC
Packet: P2.1
Summary: Added the explicit Ringmaster lifecycle map, including the repair loop, blocked and failed exits, and stage-role descriptors for every executable state.
Tests: dotnet build Ringmaster.sln; dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/RingmasterStateMachine.cs; src/Ringmaster.Core/Jobs/JobContracts.cs; tests/Ringmaster.Core.Tests/RingmasterStateMachineTests.cs
Follow-ups: Keep future lifecycle additions centralized in the state machine so the reducer and engine never drift.
```

```text
2026-03-15 17:06 UTC
Packet: P2.2
Summary: Implemented IStateMachine transition validation and added unit coverage for allowed transitions, rejected jumps, executable stage descriptors, and automatic terminal states.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/RingmasterStateMachine.cs; src/Ringmaster.Core/Jobs/JobContracts.cs; tests/Ringmaster.Core.Tests/RingmasterStateMachineTests.cs
Follow-ups: Route all future lifecycle mutations through IStateMachine instead of hand-written state checks.
```

```text
2026-03-15 17:06 UTC
Packet: P2.3
Summary: Added the local JobEngine to drive one queued job through state transitions, invoke stage runners, persist run metadata, and reject invalid lifecycle outcomes before they hit the snapshot.
Tests: dotnet build Ringmaster.sln; dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/JobEngine.cs; src/Ringmaster.Core/Jobs/JobContracts.cs; tests/Ringmaster.IntegrationTests/JobEngineIntegrationTests.cs
Follow-ups: Phase 3 can swap real git-backed preparation and verification under the same engine contract.
```

```text
2026-03-15 17:06 UTC
Packet: P2.4
Summary: Registered deterministic fake runners for planner, implementer, verifier, repair, and reviewer stages so the local vertical slice exercises the same interfaces that later real integrations will use.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.App/Program.cs; src/Ringmaster.Infrastructure/Fakes/FakeStageRunner.cs; tests/Ringmaster.IntegrationTests/Testing/ScriptedStageRunner.cs
Follow-ups: Preserve fake-runner parity as Git, Codex, and reviewer integrations replace individual stages.
```

```text
2026-03-15 17:06 UTC
Packet: P2.5
Summary: Persisted per-run folders under runs/, wrote run.json for each stage execution, and normalized numbered run IDs so retries and later repair loops remain durable and inspectable.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Infrastructure/Persistence/LocalFilesystemJobRepository.cs; src/Ringmaster.Core/Jobs/JobEngine.cs; tests/Ringmaster.IntegrationTests/JobEngineIntegrationTests.cs
Follow-ups: Future phases can attach prompts, command logs, and structured artifacts to the existing per-run directories.
```

```text
2026-03-15 17:06 UTC
Packet: P2.6
Summary: Added stage transition and run events, snapshot reducer support for current-run tracking, attempt counters, blocker clearing, and idle execution after completion, failure, or block.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/JobEventRecord.cs; src/Ringmaster.Core/Jobs/JobSnapshotRebuilder.cs; tests/Ringmaster.Core.Tests/JobSnapshotRebuilderTests.cs
Follow-ups: Extend the reducer with heartbeat, verifier, and PR events as later phases add those transitions.
```

```text
2026-03-15 17:06 UTC
Packet: P2.7
Summary: Implemented job run <jobId> on the CLI using fake runners only, and verified the built binary can create, execute, and reload a local job through READY_FOR_PR.
Tests: dotnet test Ringmaster.sln; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster job create --title "Add retry handling" --description "Implement bounded retries." --json; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster job run job-20260315-468e1d7a9 --json; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster status --job-id job-20260315-468e1d7a9 --json
Files: src/Ringmaster.App/CommandLine/RingmasterCli.cs; src/Ringmaster.App/Program.cs; tests/Ringmaster.IntegrationTests/RingmasterCliCommandTests.cs
Follow-ups: Phase 3 can keep the CLI surface stable while replacing fake preparation and verification with real process-backed execution.
```

```text
2026-03-15 17:06 UTC
Packet: P2.8
Summary: Added automated coverage for the happy path, blocked path, CLI execution path, and invalid transition rejection so Phase 2 exits on executable proof instead of manual inspection.
Tests: dotnet test Ringmaster.sln
Files: tests/Ringmaster.IntegrationTests/JobEngineIntegrationTests.cs; tests/Ringmaster.IntegrationTests/RingmasterCliCommandTests.cs; tests/Ringmaster.Core.Tests/RingmasterStateMachineTests.cs
Follow-ups: Carry the same proof discipline into temp-repo git and verifier integration in Phase 3.
```

```text
2026-03-15 17:18 UTC
Packet: P3.1
Summary: Added a shell-free external process runner with argument-list invocation, timeout handling, live stdout/stderr file streaming, and structured execution results for deterministic command stages.
Tests: dotnet build Ringmaster.sln; dotnet test Ringmaster.sln
Files: src/Ringmaster.Infrastructure/Processes/ExternalProcessTypes.cs; src/Ringmaster.Infrastructure/Processes/ExternalProcessRunner.cs; tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs
Follow-ups: Reuse the same process runner for Codex, git, and verifier commands so timeout and logging behavior stay uniform.
```

```text
2026-03-15 17:18 UTC
Packet: P3.2
Summary: Implemented the git CLI adapter with commit resolution, branch existence checks, porcelain worktree discovery, repair, worktree creation, status capture, and diff output helpers.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Git/GitCli.cs; src/Ringmaster.Git/GitModels.cs; tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs
Follow-ups: Later phases can extend the adapter with push, fetch, and PR-supporting operations without changing the stage contract.
```

```text
2026-03-15 17:18 UTC
Packet: P3.3
Summary: Defined the committed repo config model for base-branch and verification-profile selection, added the filesystem loader, and committed a working ringmaster.json for this repository.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Configuration/RingmasterRepoConfig.cs; src/Ringmaster.Infrastructure/Configuration/RingmasterRepoConfigLoader.cs; ringmaster.json
Follow-ups: Future config additions should stay schema-versioned and deterministic so PREPARING and VERIFYING remain reproducible.
```

```text
2026-03-15 17:18 UTC
Packet: P3.4
Summary: Added worktree management helpers that create, discover, repair, and reuse linked worktrees outside the repo root while preserving one job to one branch/worktree ownership.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Git/GitWorktreeManager.cs; src/Ringmaster.Git/GitCli.cs; tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs
Follow-ups: Cleanup and retention policies can build on the same worktree manager in later phases.
```

```text
2026-03-15 17:18 UTC
Packet: P3.5
Summary: Implemented deterministic branch and worktree naming conventions using short job IDs and slugified titles so git state is reproducible and paths stay compact.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Git/GitWorktreeManager.cs; tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs
Follow-ups: Keep future naming changes backward-compatible with persisted STATUS.json git snapshots.
```

```text
2026-03-15 17:18 UTC
Packet: P3.6
Summary: Added diff artifact generation for changed-files.json, diff.patch, diffstat.txt, and verification-summary.json under each job so later review stages can consume durable artifacts instead of recomputing git state.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Git/VerifyingStageRunner.cs; src/Ringmaster.Core/Jobs/JobEventRecord.cs; src/Ringmaster.Core/Jobs/JobSnapshotRebuilder.cs
Follow-ups: Review and PR stages can now read durable diff artifacts from the job folder.
```

```text
2026-03-15 17:18 UTC
Packet: P3.7
Summary: Replaced fake verification with deterministic command execution from the selected repo profile, including per-command logs, command JSONL records, and structured verification summaries.
Tests: dotnet test Ringmaster.sln; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster job run job-20260315-4423cc06b --json
Files: src/Ringmaster.Git/VerifyingStageRunner.cs; src/Ringmaster.Infrastructure/Processes/ExternalProcessRunner.cs; tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs
Follow-ups: Phase 5 can classify verifier failures from the persisted command records and summaries instead of parsing ad hoc output later.
```

```text
2026-03-15 17:18 UTC
Packet: P3.8
Summary: Updated PREPARING to resolve repo config, validate the verification profile, create or recover the linked worktree, and persist the prepared git snapshot before implementation starts.
Tests: dotnet test Ringmaster.sln
Files: src/Ringmaster.Git/PreparingStageRunner.cs; src/Ringmaster.App/Program.cs; tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs
Follow-ups: Planner integration can now inherit a real prepared worktree without changing the deterministic setup path.
```

```text
2026-03-15 17:18 UTC
Packet: P3.9
Summary: Updated VERIFYING to execute the committed verification profile inside the worktree, refresh the git snapshot, and transition to REVIEWING only after deterministic command success.
Tests: dotnet test Ringmaster.sln; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster status --job-id job-20260315-4423cc06b --json
Files: src/Ringmaster.Git/VerifyingStageRunner.cs; src/Ringmaster.App/Program.cs; tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs
Follow-ups: Failure classification and repair-loop transitions can now attach to real verifier outcomes in Phase 5.
```

```text
2026-03-15 17:18 UTC
Packet: P3.10
Summary: Added temp-repo integration coverage for worktree creation and reuse, verification execution, diff artifact persistence, missing-config blocking, and structured git failure handling.
Tests: dotnet test Ringmaster.sln; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster job create --title "Add retry handling" --description "Implement bounded retries." --json; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster job run job-20260315-4423cc06b --json; /home/thesamecat/dev/csharp/ringmaster/src/Ringmaster.App/bin/Debug/net10.0/ringmaster status --job-id job-20260315-4423cc06b --json
Files: tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs; tests/Ringmaster.IntegrationTests/Testing/TemporaryGitRepository.cs; src/Ringmaster.Git/
Follow-ups: The Phase 4 Codex adapter can now rely on temp-repo proof that git setup and deterministic verification already work.
```

```text
2026-03-15 17:56 UTC
Packet: P4.1-P4.4
Summary: Added the Codex runner abstractions, wired real codex exec process invocation with explicit sandbox and approval flags, streamed JSONL event logs to disk, and captured structured final outputs plus session IDs per run.
Tests: git diff --check; dotnet build Ringmaster.sln; dotnet test Ringmaster.sln; RINGMASTER_RUN_REAL_CODEX_SMOKE=1 dotnet test tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj --filter RealCodexSmokeWritesStructuredOutputWhenEnabled
Files: src/Ringmaster.Codex/CodexContracts.cs; src/Ringmaster.Codex/CodexExecRunner.cs; src/Ringmaster.Codex/CodexAgentRunner.cs; src/Ringmaster.Infrastructure/Processes/ExternalProcessTypes.cs; src/Ringmaster.Infrastructure/Processes/ExternalProcessRunner.cs; tests/Ringmaster.IntegrationTests/CodexExecRunnerTests.cs; tests/Ringmaster.IntegrationTests/Testing/FakeCodexRunner.cs; tests/Ringmaster.IntegrationTests/Testing/FakeExternalProcessRunner.cs
Follow-ups: Keep later repair and reviewer stages on the same runner contract so fake and live Codex paths stay interchangeable.
```

```text
2026-03-15 17:56 UTC
Packet: P4.5-P4.8
Summary: Added schema-constrained planner and implementer prompts, persisted prompt and schema files into each run folder, refactored deterministic repo preparation into a reusable service, and replaced the fake PREPARING and IMPLEMENTING stages with real Codex-backed adapters.
Tests: git diff --check; dotnet build Ringmaster.sln; dotnet test Ringmaster.sln
Files: src/Ringmaster.Codex/CodexPromptBuilder.cs; src/Ringmaster.Codex/PlanningStageRunner.cs; src/Ringmaster.Codex/ImplementingStageRunner.cs; src/Ringmaster.Git/RepositoryPreparationService.cs; src/Ringmaster.Git/PreparingStageRunner.cs; src/Ringmaster.App/Program.cs; tests/Ringmaster.IntegrationTests/PhaseFourIntegrationTests.cs
Follow-ups: Phase 5 should reuse the same prompt builder and durable run artifacts for repair and reviewer packets instead of inventing parallel formats.
```

```text
2026-03-15 17:56 UTC
Packet: P4.9-P4.10
Summary: Added parity coverage proving the real stage runners write PLAN.md, NOTES.md, run artifacts, and persisted Codex session IDs, plus an opt-in live smoke test that exercises the installed Codex CLI end to end.
Tests: git diff --check; dotnet build Ringmaster.sln; dotnet test Ringmaster.sln; RINGMASTER_RUN_REAL_CODEX_SMOKE=1 dotnet test tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj --filter RealCodexSmokeWritesStructuredOutputWhenEnabled
Files: src/Ringmaster.Core/Jobs/JobContracts.cs; src/Ringmaster.Core/Jobs/JobEngine.cs; tests/Ringmaster.IntegrationTests/PhaseFourIntegrationTests.cs; tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj
Follow-ups: Phase 5 can now classify real verifier outcomes and send repair/review prompts while keeping run.json authoritative for exit codes and Codex session tracking.
```

```text
2026-03-15 18:34 UTC
Packet: P5.1-P5.4
Summary: Added deterministic verification failure classification, normalized signatures, durable failure and review events, bounded repair-loop policy evaluation, and condensed repair-summary.json artifacts for repair prompts.
Tests: dotnet build Ringmaster.sln; dotnet test tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj --filter PhaseFiveIntegrationTests; dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/JobContracts.cs; src/Ringmaster.Core/Jobs/JobEnums.cs; src/Ringmaster.Core/Jobs/JobEventRecord.cs; src/Ringmaster.Core/Jobs/JobSnapshotRebuilder.cs; src/Ringmaster.Core/Jobs/DeterministicFailureClassifier.cs; src/Ringmaster.Core/Jobs/RepairLoopPolicyEvaluator.cs; src/Ringmaster.Git/VerifyingStageRunner.cs; src/Ringmaster.Git/GitModels.cs; tests/Ringmaster.Core.Tests/DeterministicFailureClassifierTests.cs; tests/Ringmaster.Core.Tests/JobSnapshotRebuilderTests.cs
Follow-ups: Phase 6 can reuse the same failure and review event history for crash recovery and scheduler decisions.
```

```text
2026-03-15 18:34 UTC
Packet: P5.5-P5.8
Summary: Replaced fake repair and reviewer stages with real Codex-backed runners, added repair-mode and reviewer prompts, persisted REVIEW.md and review-summary.json, and generated durable PR.md drafts when review approves.
Tests: dotnet build Ringmaster.sln; dotnet test tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj --filter PhaseFiveIntegrationTests; dotnet test Ringmaster.sln
Files: src/Ringmaster.Codex/CodexContracts.cs; src/Ringmaster.Codex/CodexPromptBuilder.cs; src/Ringmaster.Codex/RepairingStageRunner.cs; src/Ringmaster.Codex/ReviewingStageRunner.cs; src/Ringmaster.Codex/PullRequestDraftBuilder.cs; src/Ringmaster.App/Program.cs
Follow-ups: Later GitHub integration can open PRs from the generated PR.md file without changing the reviewer contract.
```

```text
2026-03-15 18:34 UTC
Packet: P5.9
Summary: Added end-to-end temp-repo scenarios covering compile failure to repair, test failure to repair, repeated identical failure blocking, and reviewer-requested repair before approval.
Tests: dotnet build Ringmaster.sln; dotnet test tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj --filter PhaseFiveIntegrationTests; dotnet test Ringmaster.sln
Files: tests/Ringmaster.IntegrationTests/PhaseFiveIntegrationTests.cs; tests/Ringmaster.IntegrationTests/PhaseThreeIntegrationTests.cs; tests/Ringmaster.IntegrationTests/PhaseFourIntegrationTests.cs
Follow-ups: The scheduler phase can now treat READY_FOR_PR and BLOCKED states as proven outputs of a real repair/review loop.
```

```text
2026-03-15 19:08 UTC
Packet: P6.1-P6.4
Summary: Added durable queue selection from on-disk job state, priority and nextEligible ordering, file-backed job and repo leases with heartbeats, and scheduler-safe queue processing around the existing per-job engine.
Tests: dotnet build Ringmaster.sln; dotnet test tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj --filter PhaseSixIntegrationTests; dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/QueueContracts.cs; src/Ringmaster.Core/Jobs/QueueProcessor.cs; src/Ringmaster.Infrastructure/Persistence/LocalFilesystemQueueSelector.cs; src/Ringmaster.Infrastructure/Persistence/FileLeaseManager.cs; src/Ringmaster.Infrastructure/Persistence/LocalFilesystemJobRepository.cs; src/Ringmaster.App/Program.cs
Follow-ups: Phase 7 can layer PR publication and cleanup policies on the same queue and lease shell without changing job execution semantics.
```

```text
2026-03-15 19:08 UTC
Packet: P6.5-P6.9
Summary: Added abandoned-run recovery through JobEngine resume behavior, queue once and queue run CLI commands, scheduler lock handling, and minimal console/jsonl/webhook notification sinks for unattended execution.
Tests: dotnet build Ringmaster.sln; dotnet test tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj --filter PhaseSixIntegrationTests; dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/JobEngine.cs; src/Ringmaster.App/CommandLine/RingmasterCli.cs; src/Ringmaster.App/ConsoleNotificationSink.cs; src/Ringmaster.Infrastructure/Persistence/NotificationSinks.cs; tests/Ringmaster.IntegrationTests/RingmasterCliCommandTests.cs; tests/Ringmaster.IntegrationTests/PhaseSixIntegrationTests.cs
Follow-ups: The PR-provider phase can consume the same notifications and scheduler loop instead of inventing a second execution path.
```

```text
2026-03-15 19:08 UTC
Packet: P6.10
Summary: Added failure-injection coverage for abandoned active runs, stale lease recovery, and repo-lock contention so crash and contention behavior is proven instead of assumed.
Tests: dotnet build Ringmaster.sln; dotnet test tests/Ringmaster.FaultInjectionTests/Ringmaster.FaultInjectionTests.csproj --filter QueueRecoveryFaultInjectionTests; dotnet test Ringmaster.sln
Files: tests/Ringmaster.FaultInjectionTests/Ringmaster.FaultInjectionTests.csproj; tests/Ringmaster.FaultInjectionTests/QueueRecoveryFaultInjectionTests.cs
Follow-ups: Phase 8 can extend the same fault-injection harness for cross-platform lock semantics and portability checks.
```

```text
2026-03-15 20:02 UTC
Packet: P7.1-P7.8
Summary: Added a durable PR publication service and GitHub CLI provider, wired queue and job runs through optional auto-open, implemented doctor/pr open/worktree open/cleanup commands, added retention cleanup for finished worktrees and old run logs, and documented the operator CLI.
Tests: dotnet build Ringmaster.sln; dotnet test Ringmaster.sln
Files: src/Ringmaster.Core/Jobs/PullRequestContracts.cs; src/Ringmaster.Core/Jobs/JobEnums.cs; src/Ringmaster.Core/Jobs/JobEventRecord.cs; src/Ringmaster.Core/Jobs/JobSnapshotRebuilder.cs; src/Ringmaster.Core/Jobs/QueueProcessor.cs; src/Ringmaster.Git/GitCli.cs; src/Ringmaster.Git/GitWorktreeManager.cs; src/Ringmaster.Git/CleanupService.cs; src/Ringmaster.GitHub/GitHubPullRequestProvider.cs; src/Ringmaster.GitHub/PullRequestService.cs; src/Ringmaster.App/DoctorService.cs; src/Ringmaster.App/CommandLine/RingmasterCli.cs; src/Ringmaster.App/Program.cs; tests/Ringmaster.Core.Tests/JobSnapshotRebuilderTests.cs; tests/Ringmaster.IntegrationTests/Ringmaster.IntegrationTests.csproj; tests/Ringmaster.IntegrationTests/RingmasterCliCommandTests.cs; tests/Ringmaster.IntegrationTests/PhaseSevenIntegrationTests.cs; docs/CLI.md
Follow-ups: Phase 8 should harden the same command and cleanup paths for cross-platform process, lock, packaging, and schema-upgrade concerns.
```

```text
2026-03-15 20:31 UTC
Packet: P8.1-P8.8
Summary: Added a three-platform GitHub Actions matrix, removed the remaining bash-only integration fixture path, added portability and performance smoke tests, packaged the CLI as a .NET tool, published end-user docs plus a sample config, and introduced schema-version normalization for versioned runtime documents.
Tests: git diff --check; dotnet build Ringmaster.sln; dotnet test Ringmaster.sln; dotnet pack src/Ringmaster.App/Ringmaster.App.csproj -c Release
Files: .github/workflows/ci.yml; README.md; samples/sample-repo/README.md; samples/sample-repo/ringmaster.json; src/Ringmaster.App/Ringmaster.App.csproj; src/Ringmaster.Core/SchemaVersionSupport.cs; src/Ringmaster.Core/Jobs/JobEngine.cs; src/Ringmaster.Infrastructure/Configuration/RingmasterRepoConfigLoader.cs; src/Ringmaster.Infrastructure/Persistence/LocalFilesystemJobRepository.cs; tests/Ringmaster.IntegrationTests/PhaseFiveIntegrationTests.cs; tests/Ringmaster.IntegrationTests/PhaseEightIntegrationTests.cs
Follow-ups: Phase 9 should finish the remaining documented CLI commands, human-intervention flows, and operator exit-code contracts.
```

```text
2026-03-15 21:07 UTC
Packet: P9.1-P9.7
Summary: Completed the remaining operator surface by implementing init, resume, unblock, cancel, logs, explicit exit-code mapping, status watch, and queue-run watch compatibility; added durable NOTES.md operator entries plus integration coverage for the new control paths.
Tests: git diff --check; dotnet build Ringmaster.sln; dotnet test Ringmaster.sln; dotnet pack src/Ringmaster.App/Ringmaster.App.csproj -c Release
Files: README.md; docs/CLI.md; planning/PRODUCT.md; src/Ringmaster.App/CommandLine/RingmasterCli.cs; src/Ringmaster.App/JobOperatorService.cs; src/Ringmaster.App/OperatorExitCodes.cs; src/Ringmaster.App/Program.cs; src/Ringmaster.App/RepositoryInitializationService.cs; src/Ringmaster.App/RunLogService.cs; src/Ringmaster.App/StatusDisplayService.cs; src/Ringmaster.Core/Jobs/JobSnapshotRebuilder.cs; tests/Ringmaster.Core.Tests/JobSnapshotRebuilderTests.cs; tests/Ringmaster.IntegrationTests/PhaseNineIntegrationTests.cs; tests/Ringmaster.IntegrationTests/RingmasterCliCommandTests.cs
Follow-ups: v1 implementation packets are complete; any further work should come from the deferred list or new user-prioritized scope rather than hidden CLI gaps.
```

---

## Immediate next step

All planned v1 implementation packets are complete. Pull from the deferred list or new user-prioritized scope for subsequent work.

[1]: https://developers.openai.com/codex/cli/?utm_source=chatgpt.com "Codex CLI"
[2]: https://developers.openai.com/codex/learn/best-practices/?utm_source=chatgpt.com "Best practices"
[3]: https://developers.openai.com/codex/agent-approvals-security/?utm_source=chatgpt.com "Agent approvals & security"
[4]: https://developers.openai.com/codex/concepts/customization/?utm_source=chatgpt.com "Customization"
[5]: https://developers.openai.com/codex/config-basic/?utm_source=chatgpt.com "Config basics"
[6]: https://developers.openai.com/codex/cli/reference/?utm_source=chatgpt.com "Command line options"
[7]: https://developers.openai.com/codex/noninteractive/?utm_source=chatgpt.com "Non-interactive mode"
[8]: https://developers.openai.com/codex/guides/agents-md/?utm_source=chatgpt.com "Custom instructions with AGENTS.md"

```text
2026-03-15 21:34 UTC
Packet: P9.security.cleanup-path-validation
Summary: Hardened cleanup worktree deletion by rejecting persisted worktree paths outside Ringmaster’s managed worktree root before invoking forced git worktree removal, and added integration coverage for tampered status paths.
Tests: dotnet build Ringmaster.sln (failed: dotnet not installed in container); dotnet test Ringmaster.sln (failed: dotnet not installed in container); ./src/Ringmaster.App/bin/Debug/net10.0/ringmaster --help (failed: binary missing because build could not run); bash -n scripts/dev/*.sh
Files: src/Ringmaster.Git/CleanupService.cs; tests/Ringmaster.IntegrationTests/PhaseSevenIntegrationTests.cs; planning/IMPLEMENTATION.md
Follow-ups: Re-run full build/test/help smoke once .NET SDK is available to validate end-to-end behavior in a provisioned environment.
```
