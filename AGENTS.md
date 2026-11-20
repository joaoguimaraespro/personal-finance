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
- One icon set per repo, named in its design-system doc. Chat and agent surfaces are the chat library's components
  (CopilotKit) styled with the repo's tokens: read the installed components (`copilotkit` skill) before designing
  them.
<!-- armature:design:end -->

<!-- armature:core:start -->
## Armature (core)

Armature writes defaults into this repo (the sections between `armature:` markers, `.claude/rules/`, skills, agents).
Where this repo's own rules (`.claude/rules/project/`, AGENTS.md outside these sections) differ, the repo wins. Change
an Armature section through `.armature/overrides.json`, never between the markers.

**Fix Lane.** A bug starts with `/fix <issue or description>` (the `fix` skill): diagnose (a read-only Investigate
session when the cause is unclear), `specs/fix-<ID>/` with the root cause and a small `tasks.md`, the Task Loop builds
it test-first, a check for the bug class, a PR with `Fixes <ID>`. More than about two tasks or a design decision: a
Feature.

**Pull requests and review.** The agent writes the PR with the `open-pr` skill: what was implemented (by ids), the
risk tier and rollback, the verification (gate results, the `UI evidence:` line, screenshots) and what is not done.
Screenshots only when the design changed, as the work says (a `[UI]` task, `UI evidence: required` in the spec, an
issue labelled `ui` or `visual-bug`): the agent takes them with Playwright (`visual-check` or the Playwright MCP),
opens them, and puts them where the tracker says (`.armature/config.json` `tracker`): one comment on the Linear
issue with the images embedded (they preview inline), or, for work projects, the local work vault
(`~/Development/work-context`, never uploaded). The PR links them; never committed, never in CI.

**"Review" means "read my comments".** When João says "review", "revê", "check my comments" or "I reviewed it" in a
session that opened a PR, run `address-review` on that PR's comments in the same session: new tasks in `tasks.md`
and the Task Loop again (Spec Lane), or test-first fixes. It is not a fresh code review; that only on "a code
review". A comment that states a general rule (or starts with `rule:`) also becomes a check in that round; a
`review-lessons` sweep (by hand for now, weekly later) catches what was missed (ledger: `.armature/review/lessons-ledger.json`).

**Tools before scripts.** Armature is a flexible base: deterministic checks where a check can decide, and otherwise
the tools the AI providers already ship (the Playwright MCP or `playwright-cli`, the Linear MCP, `gh`) rather than scripts this repo would have to maintain.

**Task Loop.** A feature's `tasks.md`, one task per fresh session, each through the `implement-task` skill (an
`[@dotnet]`-style tag is only a stack hint); a task is done only when the gate (`VERIFY_CMD` in `.agent-loop.env`)
passes. `--dry-run` shows the plan; `tasks_lint.py` checks the task lines first. Consecutive `[P]` tasks (no
dependency on each other, different files) run side by side, two at a time by default, one worktree each;
`--parallel N` or `TASK_PARALLEL` in `.agent-loop.env` changes it (`1` when the gate cannot run twice at once). Each task's
tokens, cost, attempts and time go to a metrics store on the machine (`~/.local/share/armature/metrics`);
`python3 "$ARMATURE_HOME/plugins/core/bin/metrics.py" report` shows them per month and repo, with the interactive
sessions next to them, and `--github <repo>` adds review comments per PR, reverts and follow-up fixes.
`docs/measuring.md` in Armature: the eval with a prompt-only baseline (`make_golden.py`, then `run_evals.py`).

```bash
task-loop specs/<NNN-feature>/tasks.md                                     # core plugin enabled
"$ARMATURE_HOME/plugins/core/bin/task-loop" specs/<NNN-feature>/tasks.md   # normal shell
```

Agents: `security-reviewer` (before a PR touching auth, input, secrets, dependencies or integrations; reports only),
`researcher` (`/radar`). Everything else runs in the main session with skills.

**Checks before prose.** No comment unless it cites a source (ADR, requirement or issue id, URL); comments are
claims, not facts. During the learning period (`review.learning_period` in `.armature/config.json`) a person reviews
every change, and each correction that states a general rule becomes a lint rule, test or type first. The repo's own CI
runs its gates; `quality.sh` and `pr-validate.sh` run Armature's checks locally (gate, test guard, secrets, dead code,
duplication, dependencies, comments, `ui-evidence`: a PR that changes UI states `UI evidence: required` with its
screenshots, or `UI evidence: not needed — <reason>`).

Never skip the git hooks (`--no-verify`, `core.hooksPath`), never merge a pull request or push to `main` or `develop`
(a person merges; Claude Code denies it), never commit `.env` files, secrets or client data, and never add a package
without checking it exists. Commits are Conventional Commits with no AI attribution.
<!-- armature:core:end -->
