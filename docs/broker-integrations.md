# Broker integrations (read-only)

Both integrations are **read-only by construction** ([ADR-0005](adr/0005-read-only-broker-integrations.md)):
the credentials you create cannot trade, and each HTTP client is wrapped in an allow-list that throws on any
method or path other than the read endpoints below — before a request leaves the process.

Credentials are encrypted with ASP.NET Data Protection, used only by the sync service, never returned by the
API, never logged and never available to AI clients.

The same step-by-step guide is built into the app (*Connections → How to connect*, and *Where do I find
these?* in the add dialog), in English and Portuguese. Keep both in sync when the brokers change their UI.

**Why no "Log in with …" button?** Neither broker offers delegated sign-in to a self-hosted app:

- Trading 212 has no OAuth. Access is only through API keys that the account holder generates in the app;
  third-party apps ask users to paste them ([Help Centre](https://helpcentre.trading212.com/hc/en-us/articles/14584770928157-Trading-212-API-key),
  [API authentication](https://docs.trading212.com/api/section/authentication)).
- IBKR's Web API OAuth 1.0a is only for third-party vendors that pass IBKR's onboarding and compliance approval
  (weeks, company-level); OAuth 2.0 is not available to individual accounts, and retail Web API access otherwise
  needs the Client Portal Gateway with daily 2FA ([Web API authentication](https://www.interactivebrokers.com/docs/web-api/authentication/introduction)).
  The Flex Web Service token is the only unattended, reporting-only option.

## Trading 212 — official Public API v0

**Create the key** (about 3 minutes, app or web):

1. Switch to the account to track — **Invest** or **Stocks ISA** (CFD and SIPP are not supported; one key per account).
2. **Menu (☰) → Settings → API (Beta)**, accept the risk warning, tap **Generate API key** and name it
   (e.g. `personal-finance`).
3. Permissions — switch **on** only what the app reads:

   | Permission | Needed for |
   |---|---|
   | **Account data** | `account/summary` — currency, cash, total value |
   | **Metadata** | `metadata/instruments` — names, ISIN, ETF vs stock |
   | **Portfolio** | `positions` — open positions |
   | **History** (orders, dividends, transactions) | trades, dividends, cash movements |

   Leave **off**: **Orders – Execute** and **Pies – Write** (Orders/Pies read are not needed either). The
   allow-list would block those calls anyway, but a leaked key with trade permissions is still dangerous.
4. IP access: **Restrict access to trusted IPs** with your server's public IP (recommended); *Unrestricted* only
   if that IP changes often.
5. **Generate**, then copy the **API key** and the **API secret** — the secret is shown **only once**. Both are
   required: requests use HTTP Basic auth, `Authorization: Basic base64(<key>:<secret>)`.
6. In the app: *Connections → + Trading 212*, paste both, keep account type **Live** (Demo only for a practice
   account, which uses `demo.trading212.com`; live uses `live.trading212.com`) and save. The first sync starts
   immediately.

**Errors you may see.** `401` → the key/secret pair is wrong or deleted: generate a new key and use *Update
credentials*. `403` → a read permission is missing (usually Metadata or History): generate a new key with the
four permissions above. The connection card explains both and links back to the guide.

**Revoke** anytime: Settings → API (Beta) → select the key → Delete (immediate and permanent).

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
first sync onward and [reconstructs the days before it](#reconstructed-history)) and reports dividends without
withholding tax. Import the history CSV (app → History →
Export) on the connection page to backfill older history and to fill in withholding tax. API and CSV
records share natural keys (ISIN + timestamp + quantity/amount), so importing both never double counts.
Withholding tax is only inferred from the API when the dividend and instrument currencies match, and is
flagged as *derived* until a CSV provides the reported value.

## Interactive Brokers — Flex Web Service v3

The Flex Web Service delivers pre-configured reports over two GET endpoints (`SendRequest`,
`GetStatement`). It has **no trading surface**; Client Portal Gateway and TWS APIs were rejected because
they need a full trading session and daily manual 2FA.

**1. Create the Activity Flex Query** — Client Portal → **Performance & Reports → Flex Queries** → next to
*Activity Flex Query* click **+** and name it (e.g. `personal-finance`). Tick these sections; in each, **Select
All** fields is simplest (the parser reads the attributes listed):

| Section (option) | XML element | Needed for | Attributes read |
|---|---|---|---|
| Account Information | `AccountInformation` | base currency | `currency` |
| Open Positions (**Summary**) | `OpenPosition` | holdings, mark price, cost basis | `symbol`, `conid`, `isin`, `description`, `assetCategory`, `subCategory`, `listingExchange`, `currency`, `position`, `markPrice`, `costBasisPrice` |
| Trades (**Execution**) | `Trade` | trades, commissions, realised P&L | `tradeID`, `dateTime`/`tradeDate`, `buySell`, `quantity`, `tradePrice`, `ibCommission`, `ibCommissionCurrency`, `taxes`, `netCash`, `fifoPnlRealized`, `currency`, `fxRateToBase` + instrument fields |
| Cash Transactions (**Detail**) | `CashTransaction` | deposits/withdrawals, dividends, withholding tax, fees, interest | `type`, `amount`, `currency`, `fxRateToBase`, `dateTime`, `reportDate`, `transactionID`, `actionID`, `description` + instrument fields |
| Cash Report | `CashReport` | ending cash | `endingCash` (BASE_SUMMARY row) |
| Change in NAV | `ChangeInNAV` | ending value | `endingValue` |
| Net Asset Value (NAV) in Base | `EquitySummaryInBase` | **daily NAV history** — true TWR/XIRR from day one | `reportDate`, `cash`, `total` |

Financial Instrument Information is optional: ISIN and asset category are read from the position and trade rows.
Section labels have changed across Client Portal versions — match on the XML element if in doubt.

**2. Delivery configuration**: format **XML**, period *Last 365 Calendar Days*, date format `yyyyMMdd`, time
format `HHmmss`, date/time separator `;` (semicolon). Save. The **Query ID** is the number shown for the query
in the list (info icon).

**3. Enable Flex Web Service** — on the same page, **Flex Web Service Configuration** (gear icon) → enable →
**Generate A New Token**. Choose the expiry (6 hours to 1 year) and optionally restrict it to your server's IP
([IBKR guide](https://www.ibkrguides.com/clientportal/performanceandstatements/flex-web-service.htm)). Generating
a new token invalidates the previous one — that is also how you **revoke** it (or switch the service off).

**4. In the app**: *Connections → + Interactive Brokers*, paste the token and the numeric query ID and enter
the expiry date. The app warns 30 days before expiry; error 1012 (expired) marks the connection *Needs
attention*, as do 1013 (IP restriction), 1014 (invalid query) and 1015 (invalid token), each with a translated
explanation on the card.

Requests go to `https://ndcdyn.interactivebrokers.com/AccountManagement/FlexWebService/{SendRequest,GetStatement}`
with `v=3` and a `User-Agent` header, which IBKR requires for programmatic access.

**Behaviour.** Pacing ≤ 1 request/s and ≤ 10/min per token. "Statement generating" (1019) and other
transient codes are retried with backoff; token/query errors (1012–1015) stop and ask for your attention.
Later syncs request the query's own period (no `fd`/`td`): IBKR answers error 1003 for short ranges that end on
a day it hasn't closed yet, and re-reading the year is harmless because records are keyed by IBKR ids.
The first sync backfills history in 365-day windows (5 years by default,
`Integrations:Ibkr:BackfillYears`); daily syncs run after 06:00 UTC because Flex data is end-of-day.

## Reconstructed history

Trading 212 reports only current state, so on its own the *Performance* chart would start on the first sync.
After every sync and CSV import (and every 6 hours as a catch-up) the app rebuilds the daily history of each
account **without broker-reported history** from its own ledger:

- holdings per day = today's positions minus the fills after that day; cash per day = the first real snapshot's
  cash minus later deposits, withdrawals, fees, interest, dividends and fills. Rolling back from the real values
  means the rebuilt series ends exactly where the recorded one starts, even if old records are missing;
- each holding is valued at that day's public closing price (weekends and holidays carry the last close),
  converted with the ECB reference rate of the day;
- when no close is available within a week, the latest trade price is used and the day is counted as
  *estimated* — the chart then says the history is partly estimated.

Rebuilt days are stored as snapshots with origin `reconstructed`. They are regenerated from scratch on every
run, never overwrite `broker` (IBKR NAV) or `computed` (daily) snapshots, and are replaced by real ones as they
arrive. Accounts whose broker reports history (IBKR, demo profiles *a*/*b*) are left alone. Deposits and
withdrawals stay external flows, so TWR and XIRR treat the rebuilt period like any other.

**Prices.** `MarketData:Provider` (`MARKET_DATA_PROVIDER` in `deploy/.env`) selects the source:

| Value | Behaviour |
|---|---|
| `yahoo` (default) | Yahoo Finance's public chart/search endpoints (no key) |
| `none` | No requests at all; history starts on the first sync |

The listing is found by ISIN and preferably on the exchange the broker reports (Trading 212 `VWCEd_EQ` →
`VWCE.DE` on Xetra; IBKR `listingExchange`), in the security's currency. The chosen symbol and the range already
downloaded are cached (`investments.price_listings`), and closes are stored in `investments.market_prices` with
source `MarketData` — each day is fetched once; broker prices are never overwritten. Requests are spaced
≥ 0.75 s apart, time out, are retried with backoff and go through an allow-list (`/v1/finance/search`,
`/v8/finance/chart/{symbol}`); a failure is logged and the rebuild falls back to trade prices — a sync never
fails because of it. **Only ISINs, listing symbols and date ranges leave the server** — never quantities,
values or account data. The Yahoo endpoints are unofficial and may change or be rate limited; set
`none` if you prefer no third-party requests.

## Binance — Spot and Simple Earn (read-only key)

Binance is the one exchange that can be connected, because its API keys can be made read-only and the app can check
that they are. Everything else (other exchanges, hardware wallets) is [entered by hand](#crypto-entered-by-hand).

1. Binance → *Account → API Management → Create API → System generated*. Leave **only "Enable Reading"** ticked
   (no spot/margin trading, futures, options, transfers or withdrawals) and restrict access to your home IP.
2. *Connections → Add connection → Binance*: paste the API Key and Secret Key.

- **Read-only by construction** ([ADR-0009](adr/0009-binance-read-only-key.md)). Before reading anything, each sync
  asks Binance what the key may do (`GET /sapi/v1/account/apiRestrictions`) and refuses a key that can trade,
  transfer or withdraw. The HTTP client is allow-listed to `GET` requests on the account, Simple Earn positions and
  rewards, fill history, prices and daily candles; an order, convert, redeem, transfer or withdrawal request is
  blocked before it leaves the process. Requests are signed (HMAC-SHA256) and the secret is stored encrypted like
  the other brokers' credentials.
- **Positions.** One position per coin: spot balance + flexible Earn + locked Earn, valued in EUR at Binance's
  price (the EUR pair, else the USDT/USDC/FDUSD pair converted with EURUSDT). EUR balances are the account's cash.
  Coins Binance cannot price in EUR or dollars are skipped. A coin held both on Binance and by hand is one position
  line (coins have no ISIN, so the symbol identifies them).
- **Cost.** The average buy price comes from the account's fills on EUR and dollar-stablecoin pairs (moving average;
  dollar fills converted at that day's EURUSDT close). Coins that were never bought there (deposited, converted or
  earned) have no known cost: they are valued from the first sync on, without a gain.
- **Earn rewards** (flexible real-time APR, bonus tiers, locked rewards) are income, like dividends: one entry per
  reward, valued at the coin's EUR close of that day, under *Dividends & rewards*. The first sync reads the last
  180 days; later syncs read from the previous one, and the external id
  (`binance:earn:<product>:<coin>:<time>:<amount>`) keeps re-syncs from counting a reward twice.
- **Not imported.** Individual trades and deposits/withdrawals (dollar-quoted pairs do not map to the ledger's
  currencies) and other Earn products (dual investment, launchpool). Sync every 4 hours, like Trading 212.

## Crypto entered by hand

Coins held elsewhere — another exchange or a wallet — are **not connected**: *Portfolio → Add crypto* records the coin,
quantity, average buy price (EUR), where it is held (free text, e.g. *Binance*, *Ledger*), an optional "held since"
date and notes. The app prices it itself.

- **Model.** Each location is a read-only finance account of kind *Broker* (institution `Manual (crypto)`), so the
  portfolio, the Accounts page, net worth and dashboards include it like a broker account. The holding
  (`investments.manual_holdings`) is the source of truth; its position (`DataSource.Manual`), one opening lot
  (a buy plus the money put in, on the "held since" day) and its rewards are rewritten from it on every change.
  The same coin in two locations is one position line with two holdings. A location left empty is archived.
- **Coins and prices.** The coin list comes from Yahoo search (crypto results only); only the typed text is sent.
  The listing is the coin's EUR pair (`BTC-EUR`), else its USD pair (`KAS-USD`; many smaller coins have no EUR
  pair), cached in `price_listings`. USD closes are stored in EUR at the ECB rate of the day. Daily closes go to
  `market_prices`; the last days are re-downloaded when the portfolio is opened (at most every 15 minutes) and by
  the 6-hourly snapshot job, so today's bar is the live price and yesterday's close is final.
- **24-hour change.** Coins trade around the clock, so their change is shown as **24h** (the price now against
  24 hours ago, as exchanges such as Binance show it) rather than *Today*. Each refresh also downloads the coin's last
  two days of 15-minute bars (`/v8/finance/chart/{symbol}?interval=15m&range=2d`, the same allow-listed endpoint) and
  stores the price 24 hours before the latest quote on its listing (`price_listings.reference24h_*`, converted at the
  same day's ECB rate as the live price for USD-quoted coins). The change is quantity × (price now − price 24 h ago).
  If that request fails, the coin falls back to the change since yesterday's close and is labelled *Today*. Shares and
  ETFs keep *Today* (since the previous close; at weekends, Friday's move); the portfolio's total adds both.
- **History.** Editing a holding drops the location's snapshots and rebuilds them from the daily closes since the
  "held since" date, so the performance chart never jumps on an edit (it shows the holding as if held at the
  current quantity since that date).
- **Rewards** (staking, Earn, airdrops) add coins at zero cost on the day received and are reported as income,
  valued at that day's close, under *Dividends & rewards* (portfolio income only — the monthly/annual reports
  cover the ledger and do not include dividends either).
- **Privacy.** Only the coin's ticker/listing symbol and date ranges leave the server, never quantities or values.
  With `MARKET_DATA_PROVIDER=none` coins cannot be added.

## Demo broker

`Integrations:EnableDemo=true` (on by default in Development only) adds a *Demo broker* that generates a
deterministic, fictitious portfolio. Profiles *a* and *b* both hold the same world ETF, which demonstrates the
consolidated view. Profile *c* reports no valuation history, like Trading 212, to demonstrate the
reconstructed history. It is used by the end-to-end tests and README screenshots.

## Autofill

The add-transaction form (*Investments → Buy / Sell*) offers search-as-you-type for **stocks, ETFs, funds and
bonds**. **Crypto is never looked up** in that form: symbol, name, quantity and price are always typed in by hand, and the
server records crypto entries as manually priced whatever the client sends. (To track coins you hold, use
*Portfolio → Add crypto* — see [Crypto entered by hand](#crypto-entered-by-hand).)

Keys come from environment variables only (never from the database, never committed): `T212_API_KEY`,
`T212_API_SECRET`, `T212_ENVIRONMENT`, `IBKR_FLEX_TOKEN`, `IBKR_FLEX_QUERY_ID` in `deploy/.env`, mapped to
`MarketData__*`. Use read-only keys, as for connections. Without them the form still works: suggestions come from
securities already synced through *Connections*, and everything can be entered manually.

| Source | Search | Price on the transaction date |
|---|---|---|
| Trading 212 | Full tradable-instrument list (`/equity/metadata/instruments`, cached 12 h, searched locally — the API has no search endpoint) | Only the live price of an instrument you **hold**, and only for **today** — the API has no historical prices |
| IBKR | Only instruments in your last 365 days of statements (positions + trades) — Flex has no instrument search | Your own fill price on that day, else the latest end-of-day mark price (statement date or later) |
| Synced holdings | Securities already synced from any connection | Your fill that day, else the last recorded close (≤ 7 days earlier) |

When the chosen provider has no price, the server falls back to synced holdings by ISIN. Anything missing is left
empty with a note, never guessed. The first IBKR search after start-up waits for a Flex statement (can take tens of
seconds); the form says "still loading" and the next keystroke uses the cache.
