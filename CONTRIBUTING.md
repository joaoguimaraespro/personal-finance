# Contributing

Thanks for your interest. Personal Finance is maintained by one person and shaped around a specific way of managing
money, so the best way to help is:

- **Bugs** — open an issue with the bug report template. Never paste real financial data, account numbers, API keys or
  tokens; reproduce with the demo broker (*Connections → Demo broker*) when you can.
- **Features** — open an issue first and describe the problem you want solved. Pull requests for features that were
  not discussed may be closed, however good the code.
- **Security issues** — never in a public issue: see [SECURITY.md](SECURITY.md).

## Development setup

Requirements: .NET SDK 10 (see `backend/global.json`), Node.js 22, Docker (for PostgreSQL and integration tests).

```bash
docker compose -f deploy/compose.dev.yml up -d          # PostgreSQL on 127.0.0.1:55432
dotnet run --project backend/src/Host.Api                  # API on http://localhost:5080 (migrates on start)
cd web && npm ci && npm start                              # UI on http://localhost:4200
```

Create the owner with the dev setup token from `backend/src/Host.Api/appsettings.Development.json`.

## Tests

| Suite | What it covers |
|---|---|
| `backend/tests/Reporting.Tests` | Every workbook formula reproduced with concrete numbers |
| `backend/tests/Finance.Tests`, `Investments.Tests`, … | Domain rules and calculations |
| `backend/tests/Architecture.Tests` | Layering; no trading or money-movement operations; no raw SQL; reporting never writes |
| `backend/tests/Integration.Tests` | Real PostgreSQL via Testcontainers: auth, MFA, CSRF, CRUD, audit, reports, imports, AI gateway |
| `web` (Vitest) | Components and services |
| `web/e2e` (Playwright) | First run to dashboards, run against the production images in CI |

```bash
cd backend && dotnet test         # needs Docker for the integration tests
cd web && npm test && npx prettier --check src
```

All of them run in CI on every pull request, together with CodeQL, secret scanning and the desktop app build.

## Conventions

- **Read-only brokers.** No code may place orders, move money or withdraw — an architecture test fails the build.
  New providers go through `IInvestmentProvider` and an allow-list of read endpoints
  ([ADR-0005](docs/adr/0005-read-only-broker-integrations.md)).
- **Money is `decimal`**, never floating point, and the backend calculates; the UI only displays.
- **Module boundaries** follow [docs/architecture.md](docs/architecture.md); architecture tests enforce them.
- **Translations:** every UI string goes into both `web/public/i18n/en.json` and `web/public/i18n/pt-PT.json`.
- **Broker guides** in [docs/broker-integrations.md](docs/broker-integrations.md) stay in sync with the in-app guide.
- **Design decisions** that change architecture, security or data get an ADR in `docs/adr/`. ADRs are not edited after
  they are merged: a new ADR supersedes or amends an old one.
- **Database changes** come with an EF Core migration (`dotnet ef migrations add …`).
- **Formatting:** `.editorconfig` for C#, Prettier for the web app.
- **No personal data** in code, fixtures, tests or screenshots: use synthetic data and the demo broker.

## Pull requests

- One change per pull request, with a [Conventional Commits](https://www.conventionalcommits.org) title
  (`feat: …`, `fix(web): …`, `docs: …`); pull requests are squash-merged.
- Fill in the pull request template, add tests for behaviour changes and screenshots for UI changes.
- By contributing you agree that your contribution is licensed under the [MIT License](LICENSE).

Releases are cut by pushing a `v*` tag; see [docs/releasing.md](docs/releasing.md).
