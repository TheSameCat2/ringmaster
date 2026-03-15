# AGENTS.md

## Workspace Layout
- Root worktree checkout is `master/`.
- All branch worktrees must be created as sibling directories next to `master/`.
- `master` worktree must never be deleted.

## Subrepositories
- Always check for subrepositories (`.gitmodules`) in any worktree before starting implementation.
- When subrepositories are present, run:
  - `git submodule sync --recursive`
  - `git submodule update --init --recursive`
- Attempt to pull subrepository updates when possible:
  - `git submodule foreach --recursive 'git pull --ff-only || true'`
- If a subrepository is detached/pinned and cannot be pulled directly, keep the pinned checkout and report that status.

## Base Branch Rules
- Default base branch is `master`.
- Always branch from `master` and merge back into `master` unless the user explicitly says otherwise.

## Branch Creation
- Create feature/fix branches from GitHub issues.
- Branch names must start with the issue number: `<issue-number>-<issue-slug>`.
- Worktree folder names must match the branch name exactly and be created as sibling directories.
- Create a dedicated sibling worktree for each branch.

## PR and Review Policy
- PR flow is mandatory for all non-trivial work.
- Any change touching multiple files or multiple systems requires PR + review.
- Direct commits to `master` are only for occasional minor fixes.
- Preferred PR merge method is `rebase and merge`.
- Do not create draft PRs; reviewer bots only run on full PR creation.

## Push and History Policy
- Agent may push branches whenever it is useful.
- Never force-push `master`.
- On non-`master` branches, history rewriting is allowed (amend/reset/reorder), including force-push.

## GitHub Actions Policy
- GitHub CLI (`gh`) is installed and authenticated.
- Agent should proactively use `gh` for:
  - issue creation/management
  - branch creation support tied to issues
  - PR reviews, including collecting and surfacing review comments from other agents
  - PR merge operations
- Agent is authorized to take initiative on non-destructive `gh` actions without asking first.
- Destructive GitHub actions require explicit user instruction or prior user confirmation.
- Prompt the user before dangerous actions targeting `master`.
- When posting/editing PR or issue comments via shell commands, avoid unescaped backticks in inline text because shell command substitution can corrupt the message. Prefer plain text bodies or safe quoting.
- For `gh pr create`, `gh pr edit`, and `gh pr comment`, do not pass markdown containing backticks inside double-quoted `--body` strings. Use `--body-file` (or stdin/file-based body input) whenever the text includes markdown code formatting.

## UI and Logging Preferences
- Avoid unrelated visual churn in UI work. Do not change font sizes unless explicitly requested.
- For new logging call sites, default to macro-free `SawmillLib::LoggingPlatform` patterns and do not introduce new legacy `HURDLE_LOG_*` call-site macros.

## Worktree/Branch Cleanup
- Automatic deletion is allowed only for branches/worktrees that are already merged.
- Any deletion of unmerged branches/worktrees requires explicit user request.
- Never delete `master`.
- `gh pr merge --delete-branch` may fail to delete a local branch if that branch is checked out by a worktree; in that case, complete cleanup manually.
- Before removing a worktree that contains initialized submodules, run `git submodule deinit -f --all` in that worktree, then remove the worktree.
- After merge/cleanup, prune and verify refs:
  - `git fetch --prune origin`
  - confirm branch removal locally/remotely and verify `git worktree list`.

## Rebasing and Syncing
- Do not proactively rebase branches.
- Rebase/sync branches only when the user requests it.

## Merge Conflict Handling
- Resolve conflicts collaboratively with user input on a case-by-case basis.
