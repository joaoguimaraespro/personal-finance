# ADR-0012: The MCP server inside the desktop app

- Status: Accepted
- Date: 2026-10-18
- Extends: ADR-0006 (AI policy gateway), ADR-0010 (desktop app)

## Context
On a server, AI clients (Claude Code, Claude Desktop, ChatGPT, local models) reach the app through the MCP server, a
separate process that only talks to the API's AI gateway. The desktop app had no MCP server, so its owner could not
connect an AI client.

## Decision
- The desktop launcher runs the **same MCP server** in its process, next to the API, on `127.0.0.1` at a **stable
  port** (47821, or the next free one; `PERSONAL_FINANCE_MCP_PORT` overrides it) so a client's configuration keeps
  working between runs. It calls the API's gateway with each client's token, exactly as on a server: the same
  scopes, rate limits, write budget, audit and revocation.
- The API reports the address (`/api/auth/me` → `mcpUrl`) so *AI access* shows the right `claude mcp add` command,
  and a `claude_desktop_config.json` entry for Claude Desktop through `mcp-remote`.
- It listens on loopback only and only while the app is open; nothing is exposed to the network.

## Consequences
AI access works the same on a desktop and on a server. The desktop package grows by the MCP SDK. Claude Desktop needs
Node.js for `mcp-remote` (it does not connect to plain-HTTP local servers by itself). The desktop smoke test checks
the MCP server answers on every platform.
