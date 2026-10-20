# Desktop app (Windows and macOS)

The simplest way to use Personal Finance on one computer: an installer, no Docker, no server, nothing to keep running.
The app opens in its own window and brings everything it needs: the API, the web app and a private PostgreSQL that
only runs while the window is open, listening on `127.0.0.1` with a random password. Design: [ADR-0010](adr/0010-desktop-app.md).

Downloads are on the [releases page](https://github.com/joaoguimaraespro/personal-finance/releases):

| System | File |
|---|---|
| Windows 10/11 (64-bit) | `PersonalFinance-Setup-<version>-x64.exe` |
| macOS 12+ on Apple Silicon (M1 and later) | `PersonalFinance-<version>-macos-arm64.dmg` |
| macOS 12+ on Intel | `PersonalFinance-<version>-macos-x64.dmg` |

## First run

The installers are not code-signed yet (signing certificates cost money every year, and this is a free project), so
the system warns the first time. The warning means "unknown publisher", not that something harmful was found. To
check that a download is the one built here, compare its SHA-256 with `SHA256SUMS.txt` on the release page
(Windows: `Get-FileHash .\PersonalFinance-Setup-*.exe`; macOS: `shasum -a 256 PersonalFinance-*.dmg`). Every
installer is built from this repository's source by [GitHub Actions](../.github/workflows/desktop.yml).

- **Windows** — SmartScreen shows "Windows protected your PC": choose **More info → Run anyway**. The installer
  needs no administrator rights (it installs for your user only) and can start the app at the end.
- **macOS** — open the `.dmg`, drag **Personal Finance** to **Applications**, then in Applications **right-click → Open
  → Open** (a double-click only offers to move it to the bin the first time). If macOS still refuses, run once in
  Terminal: `xattr -dr com.apple.quarantine "/Applications/Personal Finance.app"`.

Then create your account (no setup token on the desktop app). Two-factor authentication is optional here, since the
app is only reachable from this computer; you can turn it on in *Settings*.

## Updating

Download the new installer from the [latest release](https://github.com/joaoguimaraespro/personal-finance/releases/latest)
and install it over the old one (close the app first). Your data is kept; the database is upgraded on the next start.

## Where your data is

| System | Folder |
|---|---|
| Windows | `%LOCALAPPDATA%\PersonalFinanceApp\data` |
| macOS | `~/Library/Application Support/PersonalFinance` |

It holds the database, the encryption keys for broker credentials and `app.log`. The program itself is in
`%LOCALAPPDATA%\Programs\PersonalFinanceApp` (Windows) or `/Applications` (macOS).

**Removing everything:** on Windows, uninstall from *Settings → Apps* and answer *Yes* when asked whether to delete
your data too (the default keeps it for a reinstall). On macOS, move the app to the Bin and delete
`~/Library/Application Support/PersonalFinance`. To clear the data but keep the app, use *Settings → Start fresh*. Back it up with *Import / Export → Complete JSON archive*, which also moves
your data to a server install or another computer (*Restore from JSON*); see [disaster recovery](disaster-recovery.md#desktop-app). Set `PERSONAL_FINANCE_DATA` to use another folder.

## When it syncs

Everything runs while the app is open. A minute after it opens it catches up on what it missed, then it checks every
few minutes: Trading 212 and Binance every 4 hours, Interactive Brokers once a day (from 06:00 UTC), market prices
every 30 minutes, recurring items and interest every hour. Closed for days? It catches up on the next start.

## AI clients (MCP)

The app runs the MCP server for AI clients on this computer, at `http://127.0.0.1:47821/mcp` (the next free port if
that one is taken; set `PERSONAL_FINANCE_MCP_PORT` to choose another). It works while the app is open, from this
computer only; permissions, audit and revocation are the same as on a server ([docs/ai.md](ai.md)).

**1. Create a client in the app.** *Settings → AI access → New AI client*: give it a name (e.g. "Claude"), tick what it
may read (and, only if you want, write), save. The dialog then shows the token **once**, the `claude mcp add` command
and the Claude Desktop configuration — copy them before closing it.

**2a. Claude Code** (terminal): paste the command it shows, for example

```bash
claude mcp add --transport http personal-finance http://127.0.0.1:47821/mcp --header "Authorization: Bearer pf_…"
```

**2b. Claude Desktop**: it reaches a server on this computer through `mcp-remote`, so install
[Node.js](https://nodejs.org) (LTS) first. Then in Claude Desktop, *Settings → Developer → Edit Config* opens
`claude_desktop_config.json` (Windows: `%APPDATA%\Claude\claude_desktop_config.json`; macOS:
`~/Library/Application Support/Claude/claude_desktop_config.json`). Paste the entry the app showed inside
`"mcpServers"` (merge it with any servers already there):

```json
{
  "mcpServers": {
    "personal-finance": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://127.0.0.1:47821/mcp", "--header", "Authorization: Bearer pf_…"]
    }
  }
}
```

Save, quit Claude Desktop completely and open it again: *personal-finance* appears under the tools (🔌).

**3. Use it** with Personal Finance open, e.g. "How much did I spend on restaurants this month?". If the client says
it cannot connect, open the app first; if it says *unauthorized*, the token was revoked or mistyped — create a new
client. To stop access, revoke the client in *AI access*.

## What is different from the server install

- Background jobs (broker syncs, recurring items, interest, snapshots) run while the app is open and catch up when it
  opens again.
- No phone access: the app (and its MCP server) listens on this computer only. For phone access use the
  [server install](deployment.md) or an [everyday computer with Docker](desktop.md).
- One copy at a time: opening it again brings up the running one.

## Building it

[`.github/workflows/desktop.yml`](../.github/workflows/desktop.yml) builds and smoke-tests every package on its own
platform; a `v*` tag publishes a release. Locally, `dotnet publish backend/src/Host.Desktop -r <rid> --self-contained`
plus the web build in `web/` and PostgreSQL binaries in `pgsql/` next to the executable (see the workflow).
`PersonalFinance --smoke` starts everything, checks the API answers and stops.
