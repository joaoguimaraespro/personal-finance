# ADR-0004: One transaction ledger; reports are computed, never stored

- Status: Accepted
- Date: 2026-09-25

## Context
The workbook duplicated its structure per month and stored totals in cells, so the same number existed
in several places and configuration changes rewrote history.

## Decision
All money movements live in a single `transactions` table with provenance and an audit trail. Monthly
and annual figures are computed on request from the ledger by pure, unit-tested calculators. Budgets are
versioned by effective month.

## Consequences
One source of truth and no drift between views. Report queries aggregate in the database and compute in
memory; for one person's data this is milliseconds. Snapshots will be introduced only for data that
cannot be recomputed (portfolio valuations, net-worth history).
