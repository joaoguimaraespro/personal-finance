# Broker integrations (read-only)

Both integrations are **read-only by construction** ([ADR-0005](adr/0005-read-only-broker-integrations.md)):
the credentials you create cannot trade, and each HTTP client is wrapped in an allow-list that throws on any
method or path other than the read endpoints below — before a request leaves the process.

Credentials are encrypted with ASP.NET Data Protection, used only by the sync service, never returned by the
API, never logged and never available to AI clients.

## Trading 212 — official Public API v0

**Create the key** (Trading 212 app → Settings → API (Beta) → Generate API key):

1. Leave **"Orders – Execute"** and **"Pies – Write"** unticked.
2. Keep account data, history and portfolio read permissions.
3. Restrict the key to your home IP address.
4. Copy the key and the secret (shown once) into *Connections → Trading 212*.

Supported accounts: Invest and Stocks ISA.

**Endpoints used** (and nothing else):

| Method | Path | Documented limit | Used for |
|---|---|---|---|
| GET | `/api/v0/equity/account/summary` | 1 / 5 s | currency, cash, total value |
| GET | `/api/v0/equity/positions` | 1 / 1 s | holdings, average price, current price |
| GET | `/api/v0/equity/metadata/instruments` | 1 / 50 s | ETF vs stock classification, ISIN |
| GET | `/api/v0/equity/history/orders` | 6 / min | executed fills → trades, fees, realised P&L |
| GET | `/api/v0/equity/history/dividends` | 6 / min | dividends |
| GET | `/api/v0/equity/history/transactions` | 6 / min | deposits, withdrawals, fees, interest |
| GET/POST | `/api/v0/equity/history/exports` | 1 / 30 s | (reserved) history CSV report |

Pacing is enforced client-side per endpoint; `429` responses are honoured using `x-ratelimit-reset`.
Pagination follows `nextPagePath` only within the history path that was requested. Syncs run every
4 hours and re-read a 10-day overlap; duplicates are ignored.

**Gaps and the CSV export.** The API has no valuation history (the app records a daily snapshot from the
first sync onward) and reports dividends without withholding tax. Import the history CSV (app → History →
Export) on the connection page to backfill older history and to fill in withholding tax. API and CSV
records share natural keys (ISIN + timestamp + quantity/amount), so importing both never double counts.
Withholding tax is only inferred from the API when the dividend and instrument currencies match, and is
flagged as *derived* until a CSV provides the reported value.

## Interactive Brokers — Flex Web Service v3

The Flex Web Service delivers pre-configured reports over two GET endpoints (`SendRequest`,
`GetStatement`). It has **no trading surface**; Client Portal Gateway and TWS APIs were rejected because
they need a full trading session and daily manual 2FA.

**Create the Activity Flex Query** (Client Portal → Performance & Reports → Flex Queries → Activity):

| Section | Needed for |
|---|---|
| Account Information | base currency |
| Open Positions (Summary) | holdings, mark price, cost basis |
| Trades (Execution) | trades, commissions, realised P&L |
| Cash Transactions (Detail) | deposits/withdrawals, dividends, withholding tax, fees, interest |
| Cash Report | ending cash |
| Change in NAV | ending value |
| Equity Summary in Base (by report date) | **daily NAV history** — true TWR/XIRR from day one |
| Financial Instrument Information | ISIN, asset category |

Format **XML**, date format `yyyyMMdd`, time format `HHmmss`, date/time separator `;`, period
*Last 365 Calendar Days*.

**Enable Flex Web Service**, generate a token with the longest expiry you are comfortable with, restrict it
to your home IP, and enter the token, query id and expiry date in *Connections → Interactive Brokers*.
The app warns before the token expires; error 1012 (expired) marks the connection *Needs attention*.

**Behaviour.** Pacing ≤ 1 request/s and ≤ 10/min per token. "Statement generating" (1019) and other
transient codes are retried with backoff; token/query errors (1012–1015) stop and ask for your attention.
The first sync backfills history in 365-day windows (5 years by default,
`Integrations:Ibkr:BackfillYears`); daily syncs run after 06:00 UTC because Flex data is end-of-day.

## Demo broker

`Integrations:EnableDemo=true` (on by default in Development only) adds a *Demo broker* that generates a
deterministic, fictitious portfolio. Profiles *a* and *b* both hold the same world ETF, which demonstrates the
consolidated view. It is used by the end-to-end tests and README screenshots.
