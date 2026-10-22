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

## Workflow

| Size | Process |
|---|---|
| Bug fix, small change | A task with acceptance criteria → test-first → pull request |
| Feature | `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → human review of spec and tasks → implement |

Features live in `specs/<NNN-feature>/` (`spec.md`, `plan.md`, `tasks.md`). Implement tasks with
`~/Development/nexus/scripts/agent-loop.sh specs/<NNN-feature>/tasks.md`: one fresh agent session per task, and a task
counts as done only when the verification gate in [.agent-loop.env](.agent-loop.env) passes.

## Commands

```bash
cd backend && dotnet build                                   # .NET 10 (see backend/global.json)
cd backend && dotnet test --project tests/Finance.Tests      # one test project (Microsoft.Testing.Platform)
cd web && npm test && npm run lint:format                    # Vitest + Prettier
```

`Integration.Tests` need Docker; CI runs them on every pull request.

## Subagents (Claude Code)

`.claude/agents/`: `tdd-guide` (implements one task test-first), `csharp-reviewer`, `typescript-reviewer`,
`security-reviewer` (report only), `build-error-resolver` (minimal fixes to a broken build).

## Never

Merge pull requests, push to `main`, edit merged ADRs, weaken or skip tests, or commit secrets.
