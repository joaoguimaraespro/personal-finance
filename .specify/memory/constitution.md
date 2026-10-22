# Personal Finance Constitution

Derived from [CONTRIBUTING.md](../../CONTRIBUTING.md), [docs/architecture.md](../../docs/architecture.md) and the
ADRs in [docs/adr/](../../docs/adr/). When this file and those disagree, the ADRs win and this file is amended.

## Core Principles

### I. Read-Only Brokers (NON-NEGOTIABLE)
No code may place orders, move money or withdraw. New providers go through `IInvestmentProvider` and an allow-list of
read endpoints ([ADR-0005](../../docs/adr/0005-read-only-broker-integrations.md)). `Architecture.Tests` fails the build
otherwise. A spec that needs a write operation against a broker is rejected, not planned.

### II. Money Is Exact, and the Backend Calculates
Money is `decimal`, never floating point. The backend computes every figure; the UI only displays. Reporting never
writes. Every workbook formula has a test with concrete numbers in `Reporting.Tests`.

### III. Test-First (NON-NEGOTIABLE)
Each task starts from its acceptance criteria: write the failing test, then the code that makes it pass, then refactor.
A task is done only when the project's verification commands pass, never on an agent's own claim. Behaviour changes
without a test are not merged.

### IV. Module Boundaries
Modules follow [docs/architecture.md](../../docs/architecture.md); architecture tests enforce layering, no raw SQL, and
no trading or money-movement operations. Database changes ship with an EF Core migration.

### V. No Personal Data
No real financial data, account numbers, keys or tokens in code, fixtures, tests, specs or screenshots. Use synthetic
data and the demo broker.

## Product Constraints

- Stack: .NET 10 backend, Angular web app, PostgreSQL. Single owner, no Keycloak, reachable only over VPN.
- Every UI string goes into both `web/public/i18n/en.json` and `web/public/i18n/pt-PT.json`.
- Broker guides in `docs/broker-integrations.md` stay in sync with the in-app guide.
- Formatting: `.editorconfig` for C#, Prettier for the web app.

## Development Workflow

- Size decides the process: a bug fix or small change is a task with acceptance criteria; a feature goes through
  `/speckit-specify` → `/speckit-plan` → `/speckit-tasks`, and a human reviews the spec and the task list before any code.
- Tasks are small enough for one agent session and each lists its acceptance criteria.
- Verification gate (local): `dotnet test` on every test project except `Integration.Tests` (needs Docker), then
  `npm test` and `npm run lint:format` in `web`. CI runs everything, including integration and e2e tests.
- One change per pull request, Conventional Commits title, squash-merged. Agents never merge.
- Design decisions that change architecture, security or data get a new ADR in `docs/adr/`; merged ADRs are never edited.

## Governance

This constitution overrides habits and agent defaults. Plans must state how they comply with each principle; a plan
that breaks one must justify it explicitly, and Principles I, III and V cannot be waived. Amendments are made in a pull
request that bumps the version below.

**Version**: 1.0.0 | **Ratified**: 2026-10-22 | **Last Amended**: 2026-10-22
