# ADR-0010: A desktop app for people without a server

- Status: Accepted
- Date: 2026-10-15

## Context
The app runs on a home server (Docker, Caddy, Tailscale). People who want to use it on their own Windows or macOS
computer, without a server or Docker, need an installer that just works, and the app still has to keep financial
data on that computer only.

## Decision
- **One codebase, a desktop mode.** `Desktop:Enabled` makes the API:
  - start its **own PostgreSQL**: portable binaries shipped with the installer (zonky `embedded-postgres-binaries`,
    PostgreSQL licence), a cluster in the user's profile, `127.0.0.1` only, a free port, a generated SCRAM password
    kept next to the data. It is stopped with the app; nothing is installed system-wide;
  - serve the Angular build itself, with the same security headers Caddy sets on a server (no reverse proxy);
  - use plain HTTP on `127.0.0.1` (the traffic never leaves the computer), so cookies are not `Secure`;
  - need **no setup token** (only someone at the computer can reach it) and make **two-factor authentication
    optional** (it can be turned on in Settings). A server keeps both mandatory.
- **A launcher with its own window** (system WebView: WebView2 on Windows, WKWebView on macOS) starts the API and
  closes it with the window.
- **Installers** built by CI for Windows (per-user, no admin) and macOS (Apple Silicon and Intel), **unsigned** for
  now: the first launch needs "More info → Run anyway" (Windows) or right-click → Open (macOS). Signing can be added
  later without changing the app.
- SQLite was rejected: it would mean rewriting the data layer and two database dialects to test.

## Consequences
Same features, migrations and tests on a server and on a desktop. The installer is larger (a .NET runtime and
PostgreSQL, ~120 MB). The embedded binaries have no `pg_dump`: desktop backups use the app's JSON archive. Unsigned
installers show a one-time warning.
