# ADR-0003: VPN-only exposure through tailscale serve

- Status: Accepted
- Date: 2026-09-25

## Context
The server already runs public services behind a reverse proxy. Financial data should not share that
attack surface. The app runs under rootless Docker, which cannot bind port 443.

## Decision
The web container publishes only `127.0.0.1:8080`. `tailscale serve` terminates HTTPS with automatically
renewed tailnet certificates and forwards to it. The API trusts forwarded headers only from the Caddy
network.

## Consequences
Unreachable from the internet and the LAN; no certificate management. Access requires the Tailscale
client on each device. ChatGPT-style cloud connectors cannot reach the MCP endpoint unless explicitly
exposed later (a separate decision).
