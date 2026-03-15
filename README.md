# 🎪 Ringmaster

![Ringmaster](docs/images/ringmaster.png)


*Step right up, darling. Let me orchestrate your chaos.*

**Tired of babysitting AI coding agents?** They hallucinate, drift off-task, break builds, and leave you cleaning up messes at 2am.

**Ringmaster keeps them on a leash.**

```bash
# One command. AI does the work. Tests pass. PR opens. You sleep.
ringmaster job create --title "Add rate limiting" --task-file task.md --auto-open-pr
ringmaster queue run --watch
```

That's it. Ringmaster hands your task to Codex in an isolated git worktree, verifies the build passes, runs your tests, and opens a PR — *automatically*. If something breaks, it repairs. If the agent gets stuck, it asks you. If tests fail, it loops until they don't.

**The problem:** AI coding agents are powerful but unreliable. They need structure, verification, and retry logic.

**The solution:** Ringmaster is a filesystem-first workflow engine that owns state, verification, retries, and git side effects. Codex is just the performer. **Ringmaster owns the circus.**

---

## 🖤 What Ringmaster Controls

- **Queued job state** under `.ringmaster/jobs` — *your sins, filed neatly*
- **Linked git worktrees** for isolated implementation — *every act gets its own stage*
- **Verification artifacts** — *nothing leaves without inspection*
- **Repair-loop and retry decisions** — *we don't accept failure, we rewrite it*
- **Pull request publication** through GitHub CLI — *the final bow*

Codex is replaceable. **Ringmaster is the infrastructure** — state, retries, verification, and all git side effects answer to it.

---

## 🔮 Installation

Build and pack the local .NET tool:

```bash
dotnet pack src/Ringmaster.App/Ringmaster.App.csproj -c Release
```

Summon it to your machine:

```bash
dotnet tool install --global Ringmaster.Tool --add-source artifacts/packages
```

Refresh an existing installation when the show evolves:

```bash
dotnet tool update --global Ringmaster.Tool --add-source artifacts/packages
```

---

## ⚡ Quick Start

1. Ensure `git`, `codex`, and `gh` are installed and authenticated — *no loose ends*
2. Run `ringmaster init --base-branch master`
3. Review or edit the generated `ringmaster.json` — *know your rules*
4. Run `ringmaster doctor` — *check your vitals*
5. Create work with `ringmaster job create` — *birth a task*
6. Execute with `ringmaster job run`, `ringmaster job resume`, or `ringmaster queue run --watch`

Detailed end-to-end examples await below for both `.NET` and `CMake`/`gcc` repositories.

---

## 🖤 Minimal Config

See [`samples/sample-repo/ringmaster.json`](samples/sample-repo/ringmaster.json) for a concrete example.

The smallest useful config — elegant, deadly:

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

---

## 🎭 Detailed Usage Examples

### New .NET Project

This example creates a small class library plus tests, initializes Ringmaster, and runs one job through the normal operator flow. *Watch closely.*

#### 1. Create the Repository

```bash
mkdir retry-demo
cd retry-demo
git init --initial-branch=main

dotnet new sln --name RetryDemo
dotnet new classlib --name RetryDemo
dotnet new xunit --name RetryDemo.Tests

dotnet sln RetryDemo.sln add RetryDemo/RetryDemo.csproj
dotnet sln RetryDemo.sln add RetryDemo.Tests/RetryDemo.Tests.csproj
dotnet add RetryDemo.Tests/RetryDemo.Tests.csproj reference RetryDemo/RetryDemo.csproj

dotnet build RetryDemo.sln
dotnet test RetryDemo.sln

git add .
git commit -m "Initial .NET project"
```

#### 2. Initialize Ringmaster

```bash
ringmaster init --base-branch main
ringmaster doctor
```

Because the repo root contains a single solution file, `ringmaster init` will scaffold `ringmaster.json` with `dotnet build` and `dotnet test` commands for that solution. The generated file will look roughly like this:

```json
{
  "schemaVersion": 1,
  "baseBranch": "main",
  "verificationProfiles": {
    "default": {
      "commands": [
        {
          "name": "build",
          "fileName": "dotnet",
          "arguments": ["build", "RetryDemo.sln"],
          "timeoutSeconds": 900
        },
        {
          "name": "test",
          "fileName": "dotnet",
          "arguments": ["test", "RetryDemo.sln", "--no-build"],
          "timeoutSeconds": 1200
        }
      ]
    }
  }
}
```

If your solution needs different verification, edit `ringmaster.json` before creating jobs. *Your rules, your circus.*

#### 3. Create a Job

Write a task file — *describe your desire:*

```markdown
# task.md

Add exponential backoff to the retry policy.

Acceptance criteria:
- Retry delay doubles on each retry
- Delay is capped at 30 seconds
- Tests cover the cap behavior
```

Create the job:

```bash
ringmaster job create \
  --title "Add exponential backoff to RetryPolicy" \
  --task-file task.md \
  --base-branch main \
  --verify-profile default \
  --auto-open-pr \
  --label automation
```

That command prints the generated job id. *Cherish it.* Use that id in the remaining commands.

#### 4. Run and Inspect the Job

Run one job in the foreground:

```bash
ringmaster job run <jobId>
```

Watch overall state — *the show unfolds:*

```bash
ringmaster status --job-id <jobId>
ringmaster status --watch
```

Inspect logs and artifacts:

```bash
ringmaster logs <jobId>
ringmaster logs <jobId> --follow
ringmaster worktree open <jobId>
```

If the job blocks for human input — *sometimes they need guidance:*

```bash
ringmaster job unblock <jobId> \
  --message "Use TimeProvider instead of DateTime.UtcNow."
```

If you want to continue a previously blocked or interrupted job later:

```bash
ringmaster job resume <jobId>
```

If auto-open was disabled and the job finishes in `READY_FOR_PR`:

```bash
ringmaster pr open <jobId>
```

---

### New CMake Project with gcc

This example shows a minimal C++ project verified with `cmake`, `gcc`/`g++`, and `ctest`. *Different stage, same show.*

#### 1. Create the Repository

```bash
mkdir hello-cmake
cd hello-cmake
git init --initial-branch=main
mkdir -p include src tests
```

Create `CMakeLists.txt`:

```cmake
cmake_minimum_required(VERSION 3.20)
project(hello_cmake LANGUAGES CXX)

enable_testing()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(greeter src/greeter.cpp)
target_include_directories(greeter PUBLIC include)

add_executable(hello_app src/main.cpp)
target_link_libraries(hello_app PRIVATE greeter)

add_executable(greeter_tests tests/greeter_tests.cpp)
target_link_libraries(greeter_tests PRIVATE greeter)

add_test(NAME greeter_tests COMMAND greeter_tests)
```

Create `include/greeter.h`:

```cpp
#pragma once

#include <string>

std::string greeting();
```

Create `src/greeter.cpp`:

```cpp
#include "greeter.h"

std::string greeting()
{
  return "hello";
}
```

Create `src/main.cpp`:

```cpp
#include <iostream>

#include "greeter.h"

int main()
{
  std::cout << greeting() << '\n';
  return 0;
}
```

Create `tests/greeter_tests.cpp`:

```cpp
#include <cassert>

#include "greeter.h"

int main()
{
  assert(greeting() == "hello");
  return 0;
}
```

Build and test the baseline manually:

```bash
cmake -S . -B build -G "Unix Makefiles" \
  -DCMAKE_C_COMPILER=gcc \
  -DCMAKE_CXX_COMPILER=g++ \
  -DCMAKE_BUILD_TYPE=Debug

cmake --build build --parallel
ctest --test-dir build --output-on-failure

git add .
git commit -m "Initial CMake project"
```

#### 2. Initialize Ringmaster

```bash
ringmaster init --base-branch main
```

For a CMake repo, you will usually replace the generated `ringmaster.json` with an explicit verification profile:

```json
{
  "schemaVersion": 1,
  "baseBranch": "main",
  "verificationProfiles": {
    "default": {
      "commands": [
        {
          "name": "configure",
          "fileName": "cmake",
          "arguments": [
            "-S",
            ".",
            "-B",
            "build",
            "-G",
            "Unix Makefiles",
            "-DCMAKE_C_COMPILER=gcc",
            "-DCMAKE_CXX_COMPILER=g++",
            "-DCMAKE_BUILD_TYPE=Debug"
          ],
          "timeoutSeconds": 600
        },
        {
          "name": "build",
          "fileName": "cmake",
          "arguments": ["--build", "build", "--parallel"],
          "timeoutSeconds": 900
        },
        {
          "name": "test",
          "fileName": "ctest",
          "arguments": ["--test-dir", "build", "--output-on-failure"],
          "timeoutSeconds": 900
        }
      ]
    }
  }
}
```

Then verify Ringmaster prerequisites and repo health:

```bash
ringmaster doctor
```

#### 3. Create and Run a Job

Create a task file:

```markdown
# task.md

Add a `Greeter` class that accepts a configurable greeting prefix.

Acceptance criteria:
- `hello_app` still prints a greeting
- tests cover the configurable prefix
- `ctest` passes
```

Create and run the job:

```bash
ringmaster job create \
  --title "Add configurable greeting prefix" \
  --task-file task.md \
  --base-branch main \
  --verify-profile default

ringmaster job run <jobId>
```

Inspect progress and outputs:

```bash
ringmaster status --job-id <jobId>
ringmaster logs <jobId>
ringmaster worktree open <jobId>
```

If you prefer unattended operation after the first setup — *let it run while you sleep:*

```bash
ringmaster queue run --max-parallel 1 --watch
```

The important part is that `ringmaster.json` must describe exactly how your repo is configured, built, and tested. Once that profile is correct, the operator workflow is the same for `.NET`, `CMake`, or any other build system. *Different beasts, same whip.*

---

## 🎯 Operator Commands

Your arsenal, darling:

- `ringmaster init` — *birth the config*
- `ringmaster doctor` — *check the vitals*
- `ringmaster job create` — *summon a task*
- `ringmaster job show` — *reveal its secrets*
- `ringmaster job run` — *let it dance*
- `ringmaster job resume` — *wake the sleeping*
- `ringmaster job unblock` — *guide the lost*
- `ringmaster job cancel` — *end it*
- `ringmaster status` — *survey the circus*
- `ringmaster logs` — *read the tea leaves*
- `ringmaster queue once` — *one pass*
- `ringmaster queue run` — *keep the show going*
- `ringmaster pr open` — *take the final bow*
- `ringmaster worktree open` — *step into the arena*
- `ringmaster cleanup` — *sweep the stage*

Detailed CLI examples live in [`docs/CLI.md`](docs/CLI.md).

---

## 🤖 Agentic Use

This repository includes a root [`SKILL.md`](SKILL.md) for agent frameworks such as OpenClaw. Use that file when you want an agent to operate the repo through Ringmaster's intended CLI workflow instead of inventing its own orchestration.

---

<p align="center"><i>Made by Synthia with 💜</i></p>
