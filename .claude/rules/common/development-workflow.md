---
description: How a change moves from an issue to a pull request: Spec Lane or Fix Lane, research first, gates, git conventions
alwaysApply: true
---
# Development workflow

The repo's `AGENTS.md` is the source for its workflow; this rule is the default.

## Pick the lane
- **Fix Lane** (a fix, debt, one change): acceptance criteria in the issue, a failing test, the smallest fix, the
  gate, a pull request.
- **Spec Lane** (a feature, more than one change): spec, plan and tasks with Spec Kit in `specs/<NNN-slug>/`, citing
  requirement IDs. Open a draft pull request with the spec and stop until a person approves it. Then the Task Loop
  runs one fresh session per task, test first, until each task's gate passes.

## Before writing new code
- Search the codebase for an existing implementation or pattern and follow it.
- Read the current docs of the library at the version the repo pins (official docs first).
- Prefer a maintained library over hand-rolled utility code; check its licence.

## Git
- Short-lived `<type>/<slug>` branch, Conventional Commits (`type(scope): summary`, at most 72 characters), one
  logical change per commit, small pull requests with `Fixes <ISSUE-ID>` in the body.
- No `Co-Authored-By` trailers, agent names or AI tool references in commits or pull requests.
- Never push to `main`, force-push, skip hooks or merge. A person merges.
- Details: the `git-workflow` skill.

## Decisions
- A decision with non-obvious trade-offs gets an ADR in `docs/adr/` before or with the change.

<!-- Adapted from affaan-m/ECC rules/common/development-workflow.md and git-workflow.md (MIT) at d2ef268; merged with Armature's Spec Lane/Fix Lane and the owner's git rules (no attribution, a person merges). -->
