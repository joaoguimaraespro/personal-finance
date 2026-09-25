# ADR-0005: Broker integrations are read-only by construction

- Status: Accepted
- Date: 2026-09-25

## Context
Trading 212 and Interactive Brokers hold real money. The application must never be able to trade,
transfer or change settings — not by bug, not by prompt injection, not by a compromised dependency.

## Decision
- **Trading 212**: official Public API v0 with an API key created *without* "Orders – Execute" and
  "Pies – Write" permissions, IP-restricted. Only `GET` account/positions/history endpoints and the
  history export request are allow-listed. CSV import as fallback. No scraping.
- **Interactive Brokers**: Flex Web Service v3 (`SendRequest` / `GetStatement`) — a reporting-only
  interface with no trading surface. Client Portal/TWS APIs are rejected: they require full trading
  sessions and daily manual 2FA.
- Every broker `HttpClient` has an allow-list `DelegatingHandler` that throws on any other method/path.
- The domain has no `Buy`/`Sell`/`PlaceOrder`/`Transfer`/`Withdraw` operations; an architecture test
  enforces it. Investment entities are written only by the sync pipeline.

## Consequences
IBKR data is end-of-day and the Flex token must be renewed manually (reminder in the UI). The safety
property does not depend on the application behaving correctly: the credentials themselves cannot trade.
