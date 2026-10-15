# Security policy

## Supported versions

Security fixes go into `main` and the [latest release](https://github.com/joaoguimaraespro/personal-finance/releases/latest). Older releases are not patched: update
to the latest one.

## Reporting a vulnerability

Please **do not open a public issue**. Report it privately through
[GitHub security advisories](https://github.com/joaoguimaraespro/personal-finance/security/advisories/new) ("Report a vulnerability" on the Security tab).

Include what you found, how to reproduce it, the install type (desktop app, Docker on a computer, server) and version,
and the impact you expect. You should get an acknowledgement within 7 days. This is a project maintained by one person
in their own time, so fixes are best effort, but security reports come first.

## Scope

In scope: the API, the web app, the MCP server and AI gateway, the deploy and install scripts, the desktop app and its
installers.

Out of scope: installs exposed to the internet against the documentation, findings that need an already compromised
computer or account, and the installers being unsigned (a known limitation; verify downloads with `SHA256SUMS.txt`
on each release, see [docs/desktop-app.md](docs/desktop-app.md#first-run)).

The threat model and the controls in place are in [docs/security.md](docs/security.md).
