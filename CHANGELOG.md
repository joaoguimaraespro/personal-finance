# Changelog

Notable changes for people who install the app. Every release, with its installers, is on the
[releases page](https://github.com/joaoguimaraespro/personal-finance/releases); the full history is in the [commits](https://github.com/joaoguimaraespro/personal-finance/commits/main).

## [Unreleased]

### Added

- Recurring expenses paid by someone else (a flatmate pays the rent): choose *Someone else (I owe them)* and each
  confirmed occurrence is owed to that person, until you pay them back in Sharing.

## [1.3.0] - 2026-10-20

### Changed

- **Monthly:** spending by category shows the month's total and the six biggest categories (the rest as Other) in a
  ranked list; the allocation shows each bucket as a progress bar towards its target, and the expense budget as spent
  of budget.
- The month you started in, when it began after the 1st, is marked as your first month and left out of the 12-month
  averages, with how to complete it.
- Forms in dialogs check their fields and explain what is missing under each one; what the server refuses shows in
  the dialog.

### Fixed

- Error messages were hidden behind open dialogs.

## [1.2.0] - 2026-10-19

### Added

- **Dividend calendar** on the Portfolio page: each position's dividends by month over the last 12 months, the ones
  that pay nothing included, with yield, the monthly average and the thin months.
- **Sharing:** received / paid with no account (cash or outside the app), "Paid for me" on a person's card, and what
  was owed before you started (a person's starting balance, when adding or editing them).
- **Desktop app:** the MCP server for AI clients (Claude Code, Claude Desktop) runs inside the app; uninstalling on
  Windows can delete the data too.
- Accounts, people, custom categories, goals and manually valued assets can be **deleted for good**, after showing
  what goes with them (a category in use moves its movements to another one first).

### Changed

- People no longer appear in account lists; they live on the Sharing page. Account lists show each account's bank.
- Monthly: the allocation status follows what was set aside (no "Automatic" option), with "Not this month" and undo;
  the spending-by-category chart is centred.

## [1.1.0] - 2026-10-17

First public release.

### Added

- **Desktop app** for Windows and macOS (Apple Silicon and Intel): an installer, no Docker, its own embedded database.
- **Shared expenses**: split a cost with friends or flatmates (a streaming plan, the flat's bills, a supermarket run).
  The account pays it all, your spending counts only your part, and each person owes theirs until they pay you back.
  Shared recurring items follow what was actually paid; reminders show who still owes what.
- Budget and everyday money: accounts grouped by institution, restricted-use balances, interest-earning accounts,
  split transactions, refunds, recurring items (every N days, monthly, yearly on a date, varying bills at their
  recent average; the amount can be corrected right from a notification), monthly allocation and savings goals.
- Investments: read-only Trading 212, Interactive Brokers and Binance sync, crypto wallets entered by hand, real
  cost basis in euros, portfolio by broker and wallet, returns over 1M / YTD / 1Y / all time.
- Net worth history reconstructed before the first snapshot; loans with an amortisation plan, charges, interest on
  real days (Actual/360, as banks count it), rate revisions, early repayments and a simulator.
- Import / export: .xlsx, CSV and the app's JSON archive; start fresh while keeping connections and categories.
- Optional AI access over MCP and an in-app assistant behind a policy gateway, with opt-in write tools.
- English and Portuguese (Portugal), light and dark themes, phone layout.

[1.3.0]: https://github.com/joaoguimaraespro/personal-finance/releases/tag/v1.3.0
[1.2.0]: https://github.com/joaoguimaraespro/personal-finance/releases/tag/v1.2.0
[1.1.0]: https://github.com/joaoguimaraespro/personal-finance/releases/tag/v1.1.0
