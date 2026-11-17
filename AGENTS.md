# AGENTS.md

Instructions for AI coding agents (Claude Code, Codex, Cursor) working in this repository. Humans: see
[CONTRIBUTING.md](CONTRIBUTING.md).

## Rules

The binding rules are in [.specify/memory/constitution.md](.specify/memory/constitution.md). The short version:

- **Read-only brokers.** Never write code that places orders, moves money or withdraws.
- **Money is `decimal`**; the backend calculates, the UI only displays.
- **Test-first.** Every behaviour change starts with a failing test.
- **No personal data** anywhere: synthetic data and the demo broker only.
- Architecture, security or data decisions get a new ADR in `docs/adr/`; merged ADRs are never edited.

Project rules that add to or override the generic stack rules below live in `.claude/rules/project/` (Cursor:
`.cursor/rules/project-*.mdc`): `backend.md`, `web.md`, `testing.md`, `security.md`. Where they differ from a pack
rule, the project rule wins.

## Workflow

| Size | Process |
|---|---|
| Bug fix, small change | A task with acceptance criteria → test-first → pull request |
| Feature | `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → human review of spec and tasks → implement |

Features live in `specs/<NNN-feature>/` (`spec.md`, `plan.md`, `tasks.md`). Implement tasks with
`task-loop specs/<NNN-feature>/tasks.md`: one fresh agent session per task, and a task
counts as done only when the verification gate in [.agent-loop.env](.agent-loop.env) passes.

## Commands

```bash
cd backend && dotnet build                                   # .NET 10 (see backend/global.json)
cd backend && dotnet format --verify-no-changes --include <files>   # C# format check on the files you touch
cd backend && dotnet test --project tests/Finance.Tests      # one test project (Microsoft.Testing.Platform)
cd web && npm run build                                      # Angular build and type check
cd web && npm test && npm run lint:format                    # Vitest + Prettier
```

`Integration.Tests` need Docker; CI runs them on every pull request.
The whole backend is not `dotnet format` clean yet, so check only the files you change.

## Subagents (Claude Code)

One version of each generic agent, from Armature; the project context they need is in the rules above, not in
copies of the agents.

- Armature `core` plugin (user level): the `implement-task` skill (one task, test-first), `/fix` (bugs),
  `security-reviewer` and `researcher` (report only).
- Written into `.claude/agents/` by the Armature packs (managed: `armature sync`, never edited here):
  `csharp-reviewer`, `typescript-reviewer` and `design-system-guard` (report only).

## Never

Merge pull requests, push to `main`, edit merged ADRs, weaken or skip tests, or commit secrets.

<!-- armature:stack-dotnet:start -->
## .NET (stack-dotnet)

Armature defaults: where this repo's own rules (`.claude/rules/project/`, AGENTS.md outside these sections) differ, the repo wins. Rules: `.claude/rules/csharp/`; read `project-structure.md` before adding a project or folder and
`api-endpoints.md` before an endpoint. Skills `dotnet-create-*`, `dotnet-ef-migration`; templates in
`plugins/stack-dotnet/templates/`; reviewer `csharp-reviewer`. Tasks tagged `[@dotnet]` run through `implement-task`.

- Modular monolith (`src/Web.Api`, `BuildingBlocks`, `Modules`), minimal APIs, `<Verb><Entity>Command`/`Query`,
  RFC 9457 ProblemDetails; style from the repo's `.editorconfig`.
- Never reach into another module's `DbContext`; banned APIs (`BannedSymbols.txt`) are build errors, not suggestions.
<!-- armature:stack-dotnet:end -->

<!-- armature:stack-typescript:start -->
## TypeScript

Armature defaults: where this repo's own rules (`.claude/rules/project/`, AGENTS.md outside these sections) differ, the repo wins. Rules: `.claude/rules/typescript/`; banned APIs in `templates/biome.banned.json`; reviewer
`typescript-reviewer` (reports only).

- Strict: no `any`, no hiding casts, no `!` without a guard; a schema at every trust boundary.
- One formatter and linter, the one the repo already uses.
<!-- armature:stack-typescript:end -->

<!-- armature:stack-angular:start -->
## Angular

Armature defaults: where this repo's own rules (`.claude/rules/project/`, AGENTS.md outside these sections) differ, the repo wins. Rules: `.claude/rules/angular/` plus the TypeScript rules; skill `angular-guide`; reviewer
`typescript-reviewer`. Tasks tagged `[@angular]` run through the core `implement-task` skill.

- HTTP only through the repo's API service, so the interceptors apply. The backend computes figures; the UI displays them.
<!-- armature:stack-angular:end -->

<!-- armature:security:start -->
## Security review tools

Armature defaults: where this repo's own rules (`.claude/rules/project/`, AGENTS.md outside these sections) differ, the repo wins. The upstream security plugins (Trail of Bits, `security-guidance`) are installed at user scope, on demand,
from their own marketplaces; the pack README has the commands.

- Never vendor them (CC-BY-SA-4.0). `security-reviewer` (core) stays the first pass; semgrep runs in PR Validation.
- Never paste secrets, tokens or client data into a prompt or a finding.
<!-- armature:security:end -->

<!-- armature:design:start -->
## Design work

Armature defaults: where this repo's own rules (`.claude/rules/project/`, AGENTS.md outside these sections) differ, the repo wins. Rule: `.claude/rules/design/design-system.md` (tokens, states, accessibility, which design skill when); the
repo's design-system doc and `DESIGN.md` outrank any skill.

- Tokens only, never hard-coded colours or sizes; every state is designed; WCAG 2.2 AA.
- A visual change is done only when a person has seen it (light and dark, phone and desktop).
<!-- armature:design:end -->

<!-- armature:core:start -->
## Armature (core)

Armature writes defaults into this repo (the sections between `armature:` markers, `.claude/rules/`, skills, agents).
Where this repo's own rules (`.claude/rules/project/`, AGENTS.md outside these sections) differ, the repo wins. Change
an Armature section through `.armature/overrides.json`, never between the markers.

**Fix Lane.** A bug starts with `/fix <issue or description>` (the `fix` skill): a red test, the root cause in one
line, the smallest fix, a check for the bug class, a PR with `Fixes <ID>`.

**Task Loop.** A feature's `tasks.md`, one task per fresh session, each through the `implement-task` skill (an
`[@dotnet]`-style tag is only a stack hint); a task is done only when the gate (`VERIFY_CMD` in `.agent-loop.env`)
passes. `--dry-run` shows the plan; `tasks_lint.py` checks the task lines first. `--parallel N` runs up to N
consecutive `[P]` tasks at once, one worktree each (N sessions billed at once; the gate must tolerate it).

```bash
task-loop specs/<NNN-feature>/tasks.md                                     # core plugin enabled
"$ARMATURE_HOME/plugins/core/bin/task-loop" specs/<NNN-feature>/tasks.md   # normal shell
```

Agents: `security-reviewer` (before a PR touching auth, input, secrets, dependencies or integrations; reports only),
`researcher` (`/radar`). Everything else runs in the main session with skills.

**Checks before prose.** No comment unless it cites a source (ADR, requirement or issue id, URL); comments are
claims, not facts. During the learning period (`review.learning_period` in `.armature/config.json`) a person reviews
every change, and `review-lessons` turns repeated remarks into lint rules, tests or types first. PR Validation (the
required CI check) and `quality.sh` run the gate, the test guard, secrets, dead code, duplication, dependencies,
comments and `ui-evidence` (UI PRs carry screenshots in `## Verification`).

Never skip the git hooks (`--no-verify`, `core.hooksPath`), never commit `.env` files, secrets or client data, and
never add a package without checking it exists. Commits are Conventional Commits with no AI attribution.
<!-- armature:core:end -->
