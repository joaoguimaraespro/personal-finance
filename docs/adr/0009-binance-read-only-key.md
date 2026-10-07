# ADR-0009: Binance through a key the app verifies as read-only

- Status: Accepted
- Date: 2026-10-06
- Extends: [ADR-0005](0005-read-only-broker-integrations.md)

## Context
Coins on Binance were entered by hand, including Simple Earn balances and their daily rewards, which change every
day. The owner asked for them to be synced like Trading 212 and IBKR. Binance's API is a trading API: the same kind of
key can place orders, transfer between wallets and withdraw. ADR-0005 rests on credentials that cannot trade, so
connecting Binance has to keep that property.

## Decision
- Connect Binance with an API key that has only **"Enable Reading"**. Unlike Trading 212 (where the owner is trusted to
  leave the permissions off), the app **checks**: every sync starts with `GET /sapi/v1/account/apiRestrictions` and
  stops with a configuration error if the key can trade (spot, margin, futures, options, portfolio margin, FIX),
  transfer or withdraw. The check runs before any account data is read.
- The `HttpClient` is allow-listed (ADR-0005) to `GET` on: API restrictions, account balances, fill history, Simple
  Earn positions and reward history, public prices and daily candles. No order, convert, subscribe/redeem, transfer
  or withdrawal path is reachable.
- Scope: spot + Simple Earn positions (one position per coin), cost from fills, Earn rewards as income. Trades and
  deposits/withdrawals are not imported (dollar-stablecoin pairs do not map to the ledger's currencies).
- Other exchanges and wallets stay hand-entered.

## Consequences
A leaked database or server still cannot move funds: the stored key is read-only, and a key that is not is never
used. The owner must keep the key IP-restricted (Binance otherwise lets an unrestricted key expire). Cost basis is
unknown for coins that were not bought on Binance with EUR or dollar stablecoins; they show no gain.
