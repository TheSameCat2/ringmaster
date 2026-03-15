# C# Git-Worktree Codex Ringmaster Design

The right mental model is not “a smart chat that happens to edit code.” It is **a durable workflow engine that uses Codex as a replaceable stage worker**. The orchestrator owns truth, retries, verification, Git side effects, and notifications. Agents read state from disk, edit code, and return structured outputs. If you keep that boundary hard, session crashes and multi-hour jobs stop being scary.

For a greenfield build in March 2026, I would target **.NET 10 LTS** and keep the code compatible with .NET 8 if an organization is pinned there. Microsoft’s current support policy shows .NET 10 LTS supported through November 14, 2028, while .NET 8 LTS ends November 10, 2026. I would build the CLI on the **.NET Generic Host** even though it is a console app, because it is explicitly meant for console applications and gives you lifetime management, configuration, DI, and logging out of the box. ([Microsoft][1])

I would also make four non-negotiable choices up front:

* **Filesystem-backed jobs** with append-only event history and atomic snapshots.
* **Git CLI as the source of truth** for worktree lifecycle and repo mutation.
* **Codex non-interactive `exec` mode** with JSONL events and output schemas.
* **Deterministic verification owned by the orchestrator**, not by the agent.

OpenAI’s current Codex docs support this directly: `codex exec` is the non-interactive mode for scripts and CI; it supports JSONL output with `--json`, working-directory selection with `--cd`, additional writable directories with `--add-dir`, final-message capture with `--output-last-message`, schema-constrained final output with `--output-schema`, and session resume for exec sessions. The same docs also recommend `--ask-for-approval never` for non-interactive usage, and warn that `danger-full-access` should be reserved for isolated environments. ([OpenAI Developers][2])

---

## 1. System Architecture

## 1.1 Architectural thesis

Treat the system as a **local workflow engine** with durable job directories and a bounded worker pool.

The queue is not a separate broker. The queue is **the set of job directories whose `STATUS.json` says they are runnable**. That removes an entire class of corruption and recovery problems.

Agents are stateless workers attached to explicit stages:

* Planner runs during `PREPARING`
* Implementer runs during `IMPLEMENTING`
* Implementer-with-repair-prompt runs during `REPAIRING`
* Verifier agent interprets deterministic verification results during `VERIFYING`
* Reviewer runs during `REVIEWING`

The orchestrator decides transitions. Agents never do.

## 1.2 Recommended stack

* Runtime: **.NET 10 LTS** (fallback compatible with .NET 8 if needed)
* CLI parsing: **System.CommandLine**
* Terminal UX: **Spectre.Console**
* Host/runtime model: **Microsoft.Extensions.Hosting / Generic Host**
* Git integration: **git CLI first**, not LibGit2Sharp for core mutations
* State: **JSON + JSONL on disk**
* PR integration: **GitHub via `gh` first**, behind an abstraction
* Packaging: ship as a **.NET tool** for local/global installation. .NET tools are NuGet-packaged console apps intended for this style of distribution. ([Microsoft Learn][3])

Use System.CommandLine for command parsing and Spectre.Console only for rendering. Spectre’s live display is useful for dashboards, but its docs explicitly say live display is **not thread-safe**, so all live rendering must be serialized on the UI thread. ([Microsoft Learn][4])

## 1.3 Major components

```text
┌──────────────────────────────────────────────────────────┐
│ CLI Shell                                                │
│  - command parsing                                       │
│  - human-facing output                                   │
└───────────────┬──────────────────────────────────────────┘
                │
┌───────────────▼──────────────────────────────────────────┐
│ Ringmaster Host                                          │
│  - dependency injection                                  │
│  - config                                                │
│  - logging                                               │
│  - worker lifetime                                       │
└───────────────┬──────────────────────────────────────────┘
                │
     ┌──────────▼──────────┐
     │ Queue/Scheduler      │
     │ - scan jobs          │
     │ - prioritize         │
     │ - dispatch workers   │
     └──────────┬──────────┘
                │
   ┌────────────▼─────────────┐
   │ Job Engine                │
   │ - state machine           │
   │ - stage orchestration     │
   │ - retry policy            │
   │ - recovery logic          │
   └──────┬─────────┬─────────┘
          │         │
 ┌────────▼───┐ ┌───▼──────────┐
 │ Job Store   │ │ Lease Manager │
 │ - STATUS    │ │ - job lock    │
 │ - events    │ │ - repo lock   │
 │ - runs      │ │ - heartbeat   │
 └────────┬───┘ └───┬──────────┘
          │         │
 ┌────────▼───────────────┐
 │ Git Service             │
 │ - branch/worktree       │
 │ - status/diff           │
 │ - commit/push           │
 │ - cleanup/repair        │
 └────────┬────────────────┘
          │
 ┌────────▼──────────────────────────────────────────┐
 │ Stage Executors                                   │
 │  - Planner Runner                                 │
 │  - Implementer Runner                             │
 │  - Verification Runner (deterministic)            │
 │  - Verifier Agent Runner                          │
 │  - Reviewer Runner                                │
 └────────┬──────────────────────────────────────────┘
          │
 ┌────────▼──────────┐   ┌──────────────────────────┐
 │ Failure Classifier│   │ PR + Notification Layer │
 │ - categorize      │   │ - GitHub provider       │
 │ - signature hash  │   │ - console/webhook/etc   │
 │ - retry decision  │   └──────────────────────────┘
 └───────────────────┘
```

## 1.4 Internal project layout

I would split the code into these assemblies:

```text
src/
  Ringmaster.App              // composition root, System.CommandLine
  Ringmaster.Core             // state machine, policies, domain types
  Ringmaster.Abstractions     // service interfaces
  Ringmaster.Infrastructure   // filesystem, locks, process runner, serialization
  Ringmaster.Git              // git CLI adapter
  Ringmaster.Codex            // codex exec/resume adapter
  Ringmaster.GitHub           // gh-based PR provider
tests/
  Ringmaster.Core.Tests
  Ringmaster.IntegrationTests
  Ringmaster.FaultInjectionTests
```

## 1.5 Core interfaces

At minimum:

* `IJobRepository`
* `IStateMachine`
* `ILeaseManager`
* `IQueueSelector`
* `IGitService`
* `IAgentRunner`
* `IVerificationRunner`
* `IFailureClassifier`
* `IPullRequestProvider`
* `INotificationSink`

That keeps Codex, GitHub, and even git itself replaceable.

## 1.6 Validation model

The product should be buildable and evolvable without requiring a human to inspect every step.

That means Ringmaster needs two complementary proof paths:

* **deterministic verification** against the target repo via configured build/test commands, and
* **deterministic simulation** for pre-integration phases via fake stage runners, temp repos, and a small fixture repo.

Until live Codex, GitHub, or full repo workflows are in scope, the simulation path is the required proof mechanism. It should be cheap to run locally and in CI, and it should exercise the same orchestrator interfaces that real integrations later use.

No lifecycle transition or implementation phase should be considered complete based solely on agent narration or manual inspection.

---

## 2. Data Model

## 2.1 Recommended on-disk layout

Your proposed layout is directionally right, but I would change it.

The biggest change: **add a machine-readable canonical task file and per-run folders**. A single `logs/` folder becomes unmanageable once retries, verification runs, and reviewer runs accumulate.

I recommend this:

```text
<repo>/
  ringmaster.json                    // optional committed repo config
  .ringmaster/                       // ignored runtime state
    jobs/
      job-20260315-7f3c9b2a/
        JOB.json                     // canonical task definition (machine-owned)
        JOB.md                       // human-readable task
        STATUS.json                  // current snapshot (machine-owned)
        PLAN.md                      // planner summary
        NOTES.md                     // compact handoff + operator notes
        REVIEW.md                    // reviewer report
        PR.md                        // generated PR title/body
        events/
          events.jsonl               // append-only history (authoritative log)
        runs/
          0001-preparing-planner/
            run.json
            prompt.md
            output-schema.json
            final-output.json
            codex.events.jsonl
            stderr.log
          0002-implementing-implementer/
            ...
          0003-verifying-system/
            run.json
            commands.jsonl
            build.log
            test.log
            results/
              ...
        artifacts/
          diff.patch
          diffstat.txt
          changed-files.json
          verification-summary.json
          review-summary.json
        locks/
          lease.json
    runtime/
      scheduler.lock
      notifications.jsonl
```

And put actual linked Git worktrees **outside** the main repo working tree, for example:

```text
<repo-parent>/.ringmaster-worktrees/<repo-name>/job-20260315-7f3c9b2a/
```

That avoids nested-checkout weirdness in IDEs, file watchers, recursive globs, and search tools.

## 2.2 Ownership model for files

This matters.

**Machine-owned files**

* `JOB.json`
* `STATUS.json`
* `events/events.jsonl`
* `runs/**/run.json`
* command logs and structured artifacts

**Agent-authored files**

* `PLAN.md`
* `NOTES.md`
* `REVIEW.md`
* `PR.md`
* code changes inside the worktree

**Human-authored content**

* task text at creation time
* optional operator notes inside a reserved section of `NOTES.md`
* unblock decisions via CLI commands

The orchestrator should never ask an agent to edit `STATUS.json`. That file is the system of record.

## 2.3 Why both `JOB.json` and `JOB.md`

Because you need both:

* `JOB.json` is stable for machines and prompts
* `JOB.md` is readable in editors and PRs

A production tool should not force humans to read raw JSON, and it should not force machines to scrape Markdown.

## 2.4 `JOB.json` example

```json
{
  "schemaVersion": 1,
  "jobId": "job-20260315-7f3c9b2a",
  "title": "Add retry handling to payment webhook consumer",
  "description": "Implement bounded retry logic for 429 and 5xx responses in the payment webhook consumer.",
  "acceptanceCriteria": [
    "Retries 429 and transient 5xx responses with exponential backoff",
    "Does not retry 4xx other than 429",
    "Adds or updates tests for retry behavior",
    "No regression in existing payment pipeline tests"
  ],
  "constraints": {
    "allowedPaths": [
      "src/Payments/**",
      "tests/Payments.Tests/**"
    ],
    "forbiddenPaths": [
      "infra/**",
      "docs/architecture/**"
    ],
    "maxFilesChangedSoft": 20
  },
  "repo": {
    "baseBranch": "main",
    "verificationProfile": "default"
  },
  "pr": {
    "autoOpen": true,
    "draftByDefault": true,
    "labels": ["automation", "payments"]
  },
  "priority": 50,
  "createdAtUtc": "2026-03-15T14:02:11Z",
  "createdBy": "alice@example.com"
}
```

## 2.5 `STATUS.json` example

```json
{
  "schemaVersion": 1,
  "jobId": "job-20260315-7f3c9b2a",
  "title": "Add retry handling to payment webhook consumer",
  "state": "VERIFYING",
  "resumeState": "VERIFYING",
  "execution": {
    "status": "Running",
    "runId": "0004-verifying-system",
    "stage": "VERIFYING",
    "role": "SystemVerifier",
    "attempt": 2,
    "startedAtUtc": "2026-03-15T14:29:12Z",
    "heartbeatAtUtc": "2026-03-15T14:29:48Z",
    "processId": 48172,
    "sessionId": null
  },
  "priority": 50,
  "nextEligibleAtUtc": "2026-03-15T14:29:48Z",
  "attempts": {
    "preparing": 1,
    "implementing": 1,
    "verifying": 2,
    "repairing": 1,
    "reviewing": 0
  },
  "git": {
    "repoRoot": "/src/payments",
    "baseBranch": "main",
    "baseCommit": "a18f7c8d1d8c4d9e5b7ac2",
    "jobBranch": "ringmaster/j-7f3c9b2a-webhook-retry",
    "worktreePath": "/worktrees/payments/j-7f3c9b2a",
    "headCommit": "5c99e1244b9a70f819a6b1",
    "hasUncommittedChanges": true,
    "changedFiles": [
      "src/Payments/Webhooks/WebhookConsumer.cs",
      "tests/Payments.Tests/WebhookConsumerTests.cs"
    ]
  },
  "lastFailure": {
    "category": "RepairableCodeFailure",
    "signature": "verify:dotnet-test:WebhookConsumerTests.Should_retry_on_429",
    "summary": "One retry-related unit test still fails",
    "firstSeenAtUtc": "2026-03-15T14:18:21Z",
    "lastSeenAtUtc": "2026-03-15T14:27:02Z",
    "repetitionCount": 1
  },
  "review": {
    "verdict": "Pending",
    "risk": null
  },
  "pr": {
    "status": "NotStarted",
    "url": null,
    "draft": true
  },
  "blocker": null,
  "createdAtUtc": "2026-03-15T14:02:11Z",
  "updatedAtUtc": "2026-03-15T14:29:48Z"
}
```

## 2.6 Per-run `run.json` example

```json
{
  "schemaVersion": 1,
  "runId": "0002-implementing-implementer",
  "jobId": "job-20260315-7f3c9b2a",
  "stage": "IMPLEMENTING",
  "role": "Implementer",
  "attempt": 1,
  "startedAtUtc": "2026-03-15T14:07:41Z",
  "completedAtUtc": "2026-03-15T14:16:09Z",
  "tool": "codex",
  "command": [
    "codex",
    "exec",
    "--json",
    "--cd",
    "/worktrees/payments/j-7f3c9b2a",
    "--add-dir",
    "/src/payments/.ringmaster/jobs/job-20260315-7f3c9b2a",
    "--sandbox",
    "workspace-write",
    "--ask-for-approval",
    "never",
    "--output-schema",
    "output-schema.json",
    "-"
  ],
  "sessionId": "0199a213-81c0-7800-8aa1-bbab2a035a53",
  "exitCode": 0,
  "result": "Completed",
  "artifacts": {
    "prompt": "prompt.md",
    "schema": "output-schema.json",
    "finalOutput": "final-output.json",
    "eventLog": "codex.events.jsonl",
    "stderr": "stderr.log"
  }
}
```

## 2.7 Agent output schemas

Use a separate JSON schema per role and validate it with `--output-schema`.

That turns “agent said something in English” into “agent produced a contract.”

Example implementer output:

```json
{
  "result": "completed",
  "summary": "Implemented retry logic for 429 and 5xx with exponential backoff.",
  "filesModified": [
    "src/Payments/Webhooks/WebhookConsumer.cs",
    "tests/Payments.Tests/WebhookConsumerTests.cs"
  ],
  "testsRunByAgent": [
    {
      "command": "dotnet test tests/Payments.Tests --filter WebhookConsumerTests",
      "outcome": "failed",
      "notes": "One 429 retry assertion still failing"
    }
  ],
  "needsHuman": false,
  "blockers": [],
  "recommendedNextChecks": [
    "Run default verification profile",
    "Inspect 429 retry assertion"
  ]
}
```

Reviewer output:

```json
{
  "verdict": "request_repair",
  "risk": "medium",
  "summary": "Core retry behavior is present, but cancellation propagation is not covered by tests.",
  "findings": [
    {
      "severity": "medium",
      "message": "Cancellation token is not passed through the retry delay path."
    }
  ],
  "requiredRepairs": [
    "Propagate CancellationToken through backoff delay",
    "Add test for cancellation during retry wait"
  ],
  "recommendedPrMode": "draft",
  "needsHuman": false
}
```

## 2.8 Event log shape

`events/events.jsonl` is the authoritative history. `STATUS.json` is a materialized snapshot.

Example lines:

```json
{"seq":1,"ts":"2026-03-15T14:02:11Z","type":"JobCreated","jobId":"job-20260315-7f3c9b2a"}
{"seq":2,"ts":"2026-03-15T14:05:03Z","type":"StateChanged","from":"QUEUED","to":"PREPARING"}
{"seq":3,"ts":"2026-03-15T14:07:41Z","type":"RunStarted","runId":"0002-implementing-implementer","stage":"IMPLEMENTING","role":"Implementer"}
{"seq":4,"ts":"2026-03-15T14:16:09Z","type":"RunCompleted","runId":"0002-implementing-implementer","exitCode":0}
{"seq":5,"ts":"2026-03-15T14:16:12Z","type":"StateChanged","from":"IMPLEMENTING","to":"VERIFYING"}
```

Because the event log is append-only, you can rebuild `STATUS.json` if a snapshot is damaged.

---

## 3. Job Lifecycle

## 3.1 Public state machine

I would keep your states and add only one optional internal terminal distinction.

```text
QUEUED
  -> PREPARING
  -> IMPLEMENTING
  -> VERIFYING
  -> REVIEWING
  -> READY_FOR_PR
  -> DONE

VERIFYING
  -> REPAIRING
REPAIRING
  -> VERIFYING

Any active state
  -> BLOCKED
  -> FAILED
```

`READY_FOR_PR` is intentionally separate from `DONE`. It is the state where the code is good enough for a PR even if PR creation is disabled, auth is unavailable, or a team wants a final human review before publishing.

## 3.2 Detailed lifecycle

### 1. `QUEUED`

Job exists on disk. No worktree is required yet.

Stored:

* task definition
* repo target
* base branch
* priority
* verification profile
* PR policy

### 2. `PREPARING`

The orchestrator does all deterministic setup:

* acquire job lease
* resolve repo config
* choose branch name
* create or recover worktree
* capture base commit
* write initial status snapshot
* run planner agent in read-only mode

Planner can return:

* clear scope → continue
* ambiguous or architectural decision required → `BLOCKED`
* obviously invalid task or unsupported repo → `FAILED`

### 3. `IMPLEMENTING`

The implementer agent gets:

* `JOB.md`
* `JOB.json`
* `STATUS.json`
* `PLAN.md`
* compact `NOTES.md`
* relevant prior failure summary if this is a resumed job

The implementer edits code and returns a structured summary. The orchestrator does **not** trust agent claims about success. It only trusts the filesystem and later verification.

Transition rules:

* if the agent produced no useful change and reported blocker → `BLOCKED`
* if the agent crashed or timed out → retry or recover
* if the agent made changes or reasonably attempted work → `VERIFYING`

### 4. `VERIFYING`

This is mostly deterministic, not agent-driven.

The orchestrator runs the repo’s configured verification profile:

* restore/build
* targeted tests or smoke checks
* lint/format validation if configured

Then the verifier agent may interpret failures and suggest the next repair focus.

Transition rules:

* all required checks pass → `REVIEWING`
* code failure that looks repairable → `REPAIRING`
* transient infra/tool failure → bounded retry, else `BLOCKED` or `FAILED`
* ambiguous failures or repeated nondeterminism → `BLOCKED`

### 5. `REPAIRING`

This reuses the implementer role with a failure-focused prompt.

Inputs are narrowed:

* failure summary
* failing command(s)
* condensed log excerpts
* changed-file set
* last review findings if any

Transition:

* successful repair attempt → back to `VERIFYING`
* repeated same failure or no progress → `BLOCKED` or `FAILED`

### 6. `REVIEWING`

Reviewer is read-only.

Inputs:

* `JOB.md`
* `PLAN.md`
* `NOTES.md`
* `diff.patch` / changed files
* verification summary
* failure history

Reviewer returns:

* `approve`
* `request_repair`
* `human_review_required`

Transitions:

* approve → `READY_FOR_PR`
* request_repair → `REPAIRING`
* human review required → `BLOCKED`

### 7. `READY_FOR_PR`

The branch is viable.

At this point the orchestrator:

* prepares PR title/body
* optionally pushes the branch
* optionally opens a PR
* records resulting PR URL

Transitions:

* PR opened successfully → `DONE`
* PR creation disabled or deferred → remain `READY_FOR_PR`
* auth/provider failure → `BLOCKED`

### 8. `DONE`

Terminal success.

The job retains:

* branch name
* head commit
* PR URL if any
* full run history
* review artifacts

Cleanup of the worktree is **not** mandatory on state transition. It should be policy-based and deferred.

### 9. `BLOCKED`

Not a failure. It means the tool needs a human action that should not be guessed.

Examples:

* scope ambiguity
* architectural choice
* required credential/auth
* repeated same-failure signature
* repo verification profile missing
* branch merge conflict beyond policy

`STATUS.json` should include a structured blocker:

```json
{
  "reasonCode": "ArchitectureDecision",
  "summary": "Need a decision on whether retries should survive process restarts",
  "questions": [
    "Persist retry schedule in durable storage?",
    "Or keep retry in-memory and document best-effort semantics?"
  ],
  "resumeState": "PREPARING"
}
```

### 10. `FAILED`

Terminal unrecoverable state.

Use only when:

* max repair budget exceeded
* unsupported repository/tooling shape
* corrupted state that cannot be rebuilt
* operator explicitly marks failed

Do **not** use `FAILED` when human input could recover the job. That should be `BLOCKED`.

## 3.3 State-transition invariants

* Only the orchestrator may mutate `state`
* Every transition appends an event before writing a new snapshot
* Every active state has at most one current run
* A run cannot exist without a stage
* `DONE`, `FAILED`, and `READY_FOR_PR` are terminal for automatic execution unless explicitly resumed

---

## 4. Agent Execution Model

Codex’s non-interactive mode is the right integration point here. It is built for scripts/CI, emits JSONL events with `--json`, supports prompt input from stdin, supports writable extra directories with `--add-dir`, supports schema-constrained final output, and can resume non-interactive sessions. Also, OpenAI’s prompting guidance explicitly emphasizes autonomy/persistence and warns against chatty preambles or status updates during rollout because they can interrupt completion. ([OpenAI Developers][2])

## 4.1 Session policy

**Primary rule:** do not rely on session memory for correctness.

Use session resume only as an optimization.

That means:

* every run begins from job files on disk
* every prompt tells the agent what to read first
* every stage has explicit writable paths
* every stage returns structured output
* every important side effect is independently re-checkable by the orchestrator

## 4.2 Prompt construction

Generate prompts from templates.

Each prompt should contain:

1. **Role contract**

   * Planner / Implementer / Verifier / Reviewer

2. **Workspace paths**

   * worktree root
   * job directory
   * writable files/directories
   * forbidden files/directories

3. **Read order**

   * `JOB.md`
   * `STATUS.json`
   * `PLAN.md` or `NOTES.md`
   * latest failure/review summary if relevant

4. **Behavior rules**

   * do not edit `STATUS.json`
   * do not commit
   * do not open PRs
   * if blocked, return blocker info in schema

5. **Completion checklist**

   * what “done for this stage” means

6. **Output contract**

   * schema path
   * required JSON keys

## 4.3 Role-specific run modes

### Planner

* sandbox: read-only
* approval mode: never
* outputs: `PLAN.md` + structured plan JSON

### Implementer

* sandbox: workspace-write
* approval mode: never
* writable paths: worktree + job dir for notes/artifacts
* outputs: code changes + structured result

### Verifier agent

* sandbox: read-only
* approval mode: never
* inputs: deterministic verification artifacts
* outputs: failure interpretation, not lifecycle control

### Reviewer

* sandbox: read-only
* approval mode: never
* inputs: diff + verification summary + plan
* outputs: verdict + PR summary

## 4.4 Suggested Codex invocation

I would standardize on a single runner shape like this:

```bash
codex exec \
  --json \
  --model gpt-5.4 \
  --cd <worktreePath> \
  --add-dir <jobDir> \
  --sandbox workspace-write \
  --ask-for-approval never \
  --output-schema <runDir>/output-schema.json \
  --output-last-message <runDir>/final-output.json \
  - < <runDir>/prompt.md
```

For read-only roles, switch sandbox to `read-only`.

This avoids `--full-auto`, which OpenAI currently documents as `workspace-write` plus `on-request` approvals. That is fine for interactive low-friction local use, but not for unattended workers because it can still pause for approval. ([OpenAI Developers][5])

Do **not** use `--ephemeral`. Persisted session rollout files are useful for optional resume, even though correctness must not depend on them. `codex exec` also supports resume of exec sessions by explicit session ID or by `--last`. ([OpenAI Developers][2])

## 4.5 Capturing progress and session identity

When `--json` is enabled, Codex emits JSONL events including `thread.started`, `turn.started`, `turn.completed`, `turn.failed`, `item.*`, and `error`. Capture the `thread_id` from `thread.started` and persist it into `run.json` / `STATUS.json`. ([OpenAI Developers][2])

That gives you:

* live progress display
* postmortem traceability
* resume capability
* cleaner diagnostics than scraping terminal text

## 4.6 Resume strategy

Support three policies:

* `Disabled`
* `Opportunistic` **(recommended default)**
* `Preferred`

Recommended behavior:

1. If a Codex run died unexpectedly and there is a persisted session ID:

   * if the event stream ended mid-turn and resume budget is not exhausted, attempt one `codex exec resume <sessionId>`
2. Otherwise:

   * start a fresh run with the same stage and a complete disk-backed prompt

That gives you the best of both worlds:

* resume when it is cheap
* correctness even when session resume is unavailable

## 4.7 Handoff compaction

Do not make each new agent read 20 old logs.

After each completed stage, the orchestrator should regenerate `NOTES.md` as a **compact handoff** containing:

* current objective
* accepted plan
* changed files so far
* last verification outcome
* current blocker/failure summary
* next expected action

Raw run logs remain in `runs/`. `NOTES.md` is the compaction layer.

## 4.8 AGENTS.md interaction

Codex automatically discovers `AGENTS.md` files up the project tree from the working directory. That is useful for repo-level coding rules, but the orchestrator should not depend on dynamic `AGENTS.md` files for job state. Dynamic state belongs in explicit job files and prompt templates. ([OpenAI Developers][6])

---

## 5. Failure Handling Strategy

## 5.1 Classification model

Use a two-layer classifier:

1. **Deterministic classifier first**

   * exit code
   * timeout
   * signal
   * stderr pattern
   * missing binary/auth/config
   * git/provider errors
   * verification artifacts

2. **Agent-assisted interpretation second**

   * only for code/test/log diagnosis
   * never for lifecycle authority

Core categories:

* `TransientError`
* `RepairableCodeFailure`
* `ToolFailure`
* `HumanEscalationRequired`

Internally, I would refine them further:

* `TransientInfrastructure`
* `RepairableCompileFailure`
* `RepairableTestFailure`
* `AgentProtocolFailure`
* `EnvironmentFailure`
* `ProviderFailure`
* `HumanDecisionRequired`
* `MaxAttemptsExceeded`

## 5.2 Failure signature

Every failure gets a normalized signature.

Examples:

* `verify:dotnet-build:CS0103:WebhookConsumer.cs`
* `verify:dotnet-test:WebhookConsumerTests.Should_retry_on_429`
* `git:push:auth-failed`
* `codex:exec:timeout`
* `review:scope-drift:52-files`

Use that signature to detect thrashing.

Track:

* first seen
* last seen
* repetition count
* last diff hash associated with the failure

## 5.3 Retry policy

Suggested defaults:

* transient command retry: **2**
* Codex process crash retry in same stage: **1 fresh + 1 optional resume**
* implementing attempts before block/fail: **3**
* repair cycles before block/fail: **4**
* identical failure signature with no meaningful diff change: **2**
* identical diff hash across implement/repair attempts: **2**

If the system sees:

* same failure signature
* same or near-identical diff
* no new files touched
* no new passing checks

then the job is thrashing and should move to `BLOCKED`, not keep burning tokens.

## 5.4 Specific handling

### Compile failures

Usually `RepairableCodeFailure`.

Flow:

* capture compiler diagnostics
* normalize signature
* create condensed failure artifact
* transition to `REPAIRING`

If compiler failure is environment-related, such as missing SDK or restore auth, classify as `ToolFailure` or `HumanEscalationRequired`.

### Test failures

Split into three cases:

1. deterministic and code-related → `RepairableCodeFailure`
2. flaky/transient → rerun once, maybe twice
3. environment/integration dependency broken → `ToolFailure` or `BLOCKED`

Do not send a 10,000-line test log back to the repair prompt. Generate a compact summary:

* failing test names
* top stack frames
* first assertion diff
* relevant changed files

### Tool crashes

Examples:

* codex executable missing
* auth expired
* git not found
* `gh` not authenticated
* process OOM or segfault
* corrupted worktree admin files

Handling:

* one retry if plausibly transient
* attempt deterministic repair (`git worktree repair`) if applicable
* otherwise `BLOCKED` or `FAILED`

Git’s worktree docs explicitly provide `repair` for outdated or corrupted worktree admin files. ([Git][7])

### Agent hallucinations

These show up as protocol mismatches:

* agent claims success but verification fails
* agent claims tests passed but no verifier run exists
* agent says it changed files but git shows none
* agent edits forbidden paths
* agent returns invalid output shape

Handling:

* classify as `AgentProtocolFailure`
* rerun once with a stricter prompt and narrower scope
* on repeat, `BLOCKED`

Never let “agent said so” override deterministic checks.

### Infinite loops / runaway autonomy

Guardrails:

* wall-clock budget per run
* max shell commands per run
* max token budget per run if available
* repeated failure signature detection
* repeated diff hash detection
* no-progress timeout

Also shape prompts to avoid chatty preambles and repeated self-narration; OpenAI’s prompting guidance explicitly warns that prompting for extra preambles/status updates can interfere with long rollouts. ([OpenAI Developers][8])

## 5.5 Verification profiles

Use multiple verification tiers:

* `smoke`: fast compile + targeted tests
* `default`: normal CI-like validation
* `full`: expensive integration/regression suite

Policy:

* after each implement/repair loop, run `smoke`
* before leaving `VERIFYING` for `REVIEWING`, run `default`
* before PR open in high-risk repos, optionally run `full`

That reduces cycle time without weakening final quality gates.

---

## 6. Git Workflow Integration

Git worktrees are the right primitive here. Git documents them as linked working trees that let one repository have multiple checked-out branches at once; `--lock` prevents pruning/move/delete races; `list --porcelain` is stable for scripts; `remove`, `prune`, and `repair` are the right cleanup and recovery operations. ([Git][7])

## 6.1 Branch and worktree policy

* One job → one branch
* One job → one linked worktree
* Branch name format: `ringmaster/j-<shortId>-<slug>`
* Worktree path format: short and deterministic

Example:

```text
branch: ringmaster/j-7f3c9b2a-webhook-retry
worktree: /worktrees/payments/j-7f3c9b2a
```

Keep names short for Windows compatibility.

## 6.2 Worktree creation

During `PREPARING`:

1. fetch base branch if policy allows
2. compute target branch name
3. if branch/worktree already exists, validate and reuse
4. else create with explicit base ref and lock it

Use a command equivalent to:

```bash
git worktree add --lock --reason "ringmaster job job-20260315-7f3c9b2a" \
  -b ringmaster/j-7f3c9b2a-webhook-retry \
  /worktrees/payments/j-7f3c9b2a \
  origin/main
```

The orchestrator should always provide the explicit branch name and base ref. Reproducibility matters more than convenience.

## 6.3 Worktree discovery and recovery

Use:

```bash
git worktree list --porcelain -z
```

Git documents that porcelain output as the stable script-facing format. ([Git][7])

If the worktree metadata is broken because someone moved directories manually or the admin files drifted, attempt:

```bash
git worktree repair <path>
```

before failing the job. Git explicitly documents that repair path. ([Git][7])

## 6.4 Status and diff detection

For machine parsing, use:

```bash
git --no-optional-locks status --porcelain=v2 -z --branch
```

Git’s porcelain formats are intended for scripts, and Git explicitly recommends `--no-optional-locks` for background `status` execution to avoid lock conflicts with concurrent processes. ([Git][9])

Generate artifacts after each important stage:

* `changed-files.json`
* `diffstat.txt`
* `diff.patch`
* `verification-summary.json`

Recommended commands:

```bash
git diff --name-status <baseCommit>...HEAD
git diff --binary <baseCommit>...HEAD > diff.patch
git diff --stat <baseCommit>...HEAD > diffstat.txt
```

## 6.5 Commit policy

Do not let agents commit.

The orchestrator owns commits.

Recommended policy:

* allow agents to modify the worktree
* after verification passes for a meaningful checkpoint, Ringmaster creates a commit
* commit message format is deterministic

Example:

```text
ringmaster(job-20260315-7f3c9b2a): implement retry handling
```

This gives you:

* resumability
* auditability
* less risk of agents rewriting history badly

I would not auto-squash by default. Let the PR be squashed on merge if the team wants a single commit.

## 6.6 Verification commands

Verification commands must come from repo configuration, not agent invention.

Put them in `ringmaster.json`, for example:

```json
{
  "schemaVersion": 1,
  "baseBranch": "main",
  "verificationProfiles": {
    "smoke": {
      "commands": [
        { "name": "build", "fileName": "dotnet", "arguments": ["build", "--no-restore"], "timeoutSeconds": 900 }
      ]
    },
    "default": {
      "commands": [
        { "name": "build", "fileName": "dotnet", "arguments": ["build", "--no-restore"], "timeoutSeconds": 900 },
        { "name": "test", "fileName": "dotnet", "arguments": ["test", "--no-build"], "timeoutSeconds": 1800 }
      ]
    }
  }
}
```

That keeps lifecycle decisions deterministic and testable.

## 6.7 Pull request opening

Use a provider abstraction, but implement GitHub first via `gh`.

Important rule: the agent may draft PR metadata, but the orchestrator performs the side effects.

GitHub CLI’s docs say `gh pr create` will prompt for push/fork behavior unless you use `--head`, and it will also prompt for title/body unless you pass `--title` and `--body`/`--body-file`. It also supports `--draft`. In automation, always pass these flags explicitly. ([GitHub CLI][10])

Flow:

1. ensure branch is pushed

   ```bash
   git push -u origin <jobBranch>
   ```
2. check whether a PR already exists for `<jobBranch>`
3. if not, create:

   ```bash
   gh pr create \
     --head <jobBranch> \
     --base <baseBranch> \
     --title "<title>" \
     --body-file <jobDir>/PR.md \
     --draft
   ```
4. persist returned URL into `STATUS.json`

PR creation must be idempotent. On restart after a partial failure, always check for an existing PR by head branch before creating another.

## 6.8 Cleanup

Finished jobs should not immediately destroy their worktrees.

Recommended policy:

* retain worktrees for N days after `DONE` or `FAILED`
* cleanup only when branch/push/PR state is safely recorded
* then unlock and remove

Git documents that `git worktree remove` only removes clean worktrees by default, with `--force` required for dirty ones and double-force for locked ones. Use that conservatively. ([Git][7])

---

## 7. CLI Command Design

The CLI should support both **operator mode** and **worker mode**.

## 7.1 Top-level commands

```text
ringmaster init
ringmaster doctor

ringmaster job create
ringmaster job show
ringmaster job run
ringmaster job resume
ringmaster job unblock
ringmaster job cancel

ringmaster queue run
ringmaster queue once

ringmaster status
ringmaster logs

ringmaster pr open
ringmaster worktree open
ringmaster cleanup
```

## 7.2 Command behaviors

### `ringmaster init`

Creates local runtime layout and optional repo config scaffold.

Example:

```bash
ringmaster init --base-branch main --pr-provider github
```

### `ringmaster doctor`

Checks:

* git availability
* codex availability/auth
* `gh` availability/auth
* repo config validity
* worktree root writable
* runtime folders writable

### `ringmaster job create`

Creates a durable queued job.

Example:

```bash
ringmaster job create \
  --title "Add retry handling to payment webhook consumer" \
  --task-file task.md \
  --verify-profile default \
  --priority 50
```

### `ringmaster job run <jobId>`

Runs one job synchronously in the foreground until terminal state or block.

### `ringmaster job resume <jobId>`

Resumes from current state:

* blocked job after human input
* abandoned active-stage job after crash
* ready-for-pr job for PR opening

### `ringmaster job unblock <jobId>`

Stores a human answer and restarts from `resumeState`.

Example:

```bash
ringmaster job unblock job-20260315-7f3c9b2a \
  --message "Use in-memory retry only; no durable scheduler needed."
```

### `ringmaster queue run`

Starts the long-running worker loop.

Example:

```bash
ringmaster queue run --max-parallel 3 --watch
```

### `ringmaster queue once`

Runs a single scheduling pass. Useful in cron/CI.

### `ringmaster status`

Shows one job or all jobs.
Support:

* human view
* `--json`
* `--watch`

### `ringmaster logs`

Opens or tails logs.

Examples:

```bash
ringmaster logs job-20260315-7f3c9b2a --run 0004-verifying-system
ringmaster logs job-20260315-7f3c9b2a --follow
```

### `ringmaster pr open <jobId>`

Opens the PR for a `READY_FOR_PR` job if auto-open was disabled.

### `ringmaster worktree open <jobId>`

Prints or opens the worktree path for manual inspection.

### `ringmaster cleanup`

Prunes expired job worktrees and old artifacts.

## 7.3 UX notes

* human-readable default output
* `--json` for scripting
* `--watch` for live dashboard
* explicit exit codes:

  * `0` success
  * `10` blocked
  * `20` failed
  * `30` tool/config error

That makes shell automation easier.

---

## 8. Concurrency Model

## 8.1 Execution scope

For v1, support **multiple jobs on one machine** using the local filesystem.

Do not pretend this is a distributed scheduler yet. Network shares and multi-host lease arbitration are a different product.

## 8.2 Worker model

Use a bounded worker pool with separate semaphores for different resource classes:

* `maxParallelJobs`
* `maxConcurrentCodexRuns`
* `maxConcurrentVerificationRuns`
* `maxConcurrentPrOperations`

This prevents five heavy `dotnet test` runs from starving the machine while Codex processes are also active.

## 8.3 Locking model

### Job lease

One active executor per job.

Implementation:

* open `job.lock` with exclusive access and keep the handle alive
* update `lease.json` heartbeat every few seconds

The open handle gives real exclusion. The heartbeat gives observability.

### Repo mutation lock

Serialize:

* worktree add/remove/repair
* branch creation
* fetch/prune
* maybe push if you want stricter coordination

This lock is per repo, not global.

### Scheduler lock

Optional but useful. Prevents two long-running `queue run` workers from both thinking they are the primary scheduler for the same repo.

## 8.4 Queue selection

Suggested ordering:

1. runnable state only
2. `nextEligibleAtUtc <= now`
3. higher priority first
4. older jobs age upward to prevent starvation

Represent delay/backoff through `nextEligibleAtUtc`, not sleep state hidden in memory.

## 8.5 No concurrent stages within a job

Even if a job has multiple independent subproblems, do not run multiple agent stages concurrently inside the same job in v1.

Reasons:

* shared worktree
* shared notes
* shared branch
* shared retry counters
* much harder recovery

Parallelism belongs across jobs first, not inside them.

## 8.6 Background status scanning

Use `git --no-optional-locks status` for background scans, exactly because Git warns that background `status` writes can contend with other simultaneous processes. ([Git][9])

---

## 9. Logging and Observability

## 9.1 Logging goals

Logs must answer these questions quickly:

* what state is the job in now
* what happened last
* what exact command or agent run failed
* what files changed
* why did the orchestrator choose this next state
* what does a human need to do, if anything

## 9.2 Logging layers

### 1. Snapshot

`STATUS.json`

* fast read
* current truth for operators

### 2. Event history

`events/events.jsonl`

* append-only
* authoritative audit trail
* supports reconstruction

### 3. Per-run artifacts

`runs/<runId>/...`

* prompt
* schema
* agent event stream
* final structured output
* stderr/stdout
* command logs

### 4. Derived artifacts

`artifacts/`

* diff patch
* changed files
* verification summary
* review summary
* PR body

## 9.3 Structured command logging

For every deterministic command, write a JSONL record with:

* timestamp
* runId
* jobId
* working directory
* executable
* arguments
* env var names used
* timeout
* exit code
* duration
* stdout/stderr log file paths

Do not keep command output in memory. Stream it to files.

## 9.4 Secret handling

Redact:

* configured secret values
* known token patterns
* provider auth headers
* environment variable values unless explicitly allowlisted

Also prefer env vars over command-line args for secrets, because args are easier to leak into logs and process listings.

## 9.5 Live dashboard

`status --watch` should show:

* state
* current stage
* active run id
* elapsed time
* last failure summary
* retry count
* PR URL when present

Implement rendering through a single UI thread. Spectre’s live components are not thread-safe, so background workers should emit progress events to a channel, and the UI loop should be the only place that updates the display. ([Spectre.Console][11])

## 9.6 Future observability hook

Add an abstraction so phase 3 can export:

* OpenTelemetry traces
* metrics
* external web dashboard feed

But keep the filesystem logs authoritative in v1.

---

## 10. MVP Implementation Plan

## Phase 1 — Minimal Ringmaster

Goal: one production-credible job runner with durable disk state.

Build:

* `init`, `doctor`, `job create`, `job run`, `job resume`, `status`, `logs`
* job folder creation
* `JOB.json`, `STATUS.json`, `events.jsonl`
* deterministic state machine
* worktree creation/reuse
* planner + implementer runs
* deterministic verification runner
* basic failure classification
* reviewer run
* manual PR generation into `PR.md`
* single-job foreground execution
* integration tests with temp repos and fake agent runner

Do not add:

* multi-job parallel queue
* notifications
* auto-PR open
* aggressive retry heuristics

## Phase 2 — Reliability hardening

Goal: safe unattended daily use.

Add:

* long-running `queue run`
* job/repo leases with heartbeats
* crash recovery and abandoned-run recovery
* Codex session capture + opportunistic resume
* bounded repair loop
* failure signatures and thrash detection
* auto PR open via GitHub provider
* notifications for blocked/failed/ready
* cleanup policies
* verification profiles (`smoke` / `default`)
* richer `doctor` checks
* rebuild-status-from-events tool
* fault-injection tests

## Phase 3 — Advanced features

Goal: scale and polish.

Add:

* multi-job worker pool with resource semaphores
* flaky test classifier
* scope-drift detection
* stale-branch detection / optional base refresh
* mergeability checks before PR
* remote checkpoint pushes
* external telemetry
* GitLab/Azure DevOps PR providers
* optional containerized verification
* repo summary caching
* richer operator dashboard

---

## 11. Risks and Failure Modes

## 11.1 Agent thrashing

**Risk:** agent keeps making small edits but repeats the same failure.

**Mitigation:**

* failure signature hashing
* diff-hash comparison
* capped repair cycles
* move to `BLOCKED`, not infinite retry

## 11.2 Context drift

**Risk:** each new run rereads too much history and loses the real issue.

**Mitigation:**

* compact `NOTES.md`
* structured run outputs
* narrow failure artifacts
* reviewer sees diff + summaries, not raw history dumps

## 11.3 Test misinterpretation

**Risk:** agent “fixes” the wrong thing because logs are noisy.

**Mitigation:**

* deterministic verification first
* condensed failure summaries
* failing-test rerun policy
* verifier agent interprets after raw command execution, not instead of it

## 11.4 Merge conflicts / stale branches

**Risk:** long-running job drifts from base branch.

**Mitigation:**

* capture base commit at creation
* surface divergence in status
* check mergeability before PR
* optional future auto-refresh/rebase stage, but not in v1

## 11.5 Toolchain drift

**Risk:** missing SDK, broken restore auth, expired Codex or GitHub credentials.

**Mitigation:**

* `doctor` command
* environment preflight in `PREPARING`
* classify as tool/human issue, not code failure
* notify only when action is actually needed

## 11.6 Job-state corruption

**Risk:** crash while writing status.

**Mitigation:**

* append event first
* atomic temp-write + rename for snapshots
* rebuild snapshot from `events.jsonl`

## 11.7 PR duplication

**Risk:** process dies after PR creation but before status update.

**Mitigation:**

* provider lookup by head branch before create
* idempotent PR open flow
* store PR URL immediately after create

## 11.8 Unsafe agent side effects

**Risk:** agent edits status files, commits unexpectedly, or touches forbidden paths.

**Mitigation:**

* explicit prompt rules
* forbidden path policy
* deterministic post-run checks
* Ringmaster-owned commit/PR steps only
* classify violations as protocol failure

## 11.9 Large repositories and slow Windows paths

**Risk:** huge repos or Windows-mounted paths make worktrees and scans slow.

**Mitigation:**

* short worktree paths
* smoke/default profiles
* background status with `--no-optional-locks`
* on Windows, validate WSL mode first for Codex-heavy automation; OpenAI’s current Windows guidance specifically recommends keeping repos under the WSL Linux home for better performance and fewer symlink/permission issues. ([OpenAI Developers][12])

---

## Final recommendation

Build this as a **filesystem-first, deterministic orchestrator** with Codex as a **replaceable stage executor**.

The most important design decisions are:

1. **Per-job durable folders** with append-only events and atomic status snapshots.
2. **Explicit state machine** that only the orchestrator may mutate.
3. **Git CLI ownership of branch/worktree lifecycle**.
4. **`codex exec` + JSONL + output schemas**, not interactive chats.
5. **Deterministic verification** before any lifecycle transition.
6. **Bounded repair loops** with failure signatures and thrash detection.
7. **PR creation as orchestrator-owned side effect**, never an agent decision.

If you implement those correctly, the tool will survive session loss, process crashes, long-running repair loops, and human pauses without losing control of the job or the repository.

[1]: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core?utm_source=chatgpt.com "NET and .NET Core official support policy"
[2]: https://developers.openai.com/codex/noninteractive/ "Non-interactive mode"
[3]: https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools "https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools"
[4]: https://learn.microsoft.com/en-us/dotnet/standard/commandline/syntax "Command-line syntax overview for System.CommandLine - .NET | Microsoft Learn"
[5]: https://developers.openai.com/codex/cli/reference/ "https://developers.openai.com/codex/cli/reference/"
[6]: https://developers.openai.com/codex/config-advanced/ "Advanced Configuration"
[7]: https://git-scm.com/docs/git-worktree "Git - git-worktree Documentation"
[8]: https://developers.openai.com/cookbook/examples/gpt-5/codex_prompting_guide/ "Codex Prompting Guide"
[9]: https://git-scm.com/docs/git-status "https://git-scm.com/docs/git-status"
[10]: https://cli.github.com/manual/gh_pr_create "GitHub CLI | Take GitHub to the command line"
[11]: https://spectreconsole.net/console/live/live-display "Spectre.Console Documentation - Live Display"
[12]: https://developers.openai.com/codex/windows/ "Windows"
