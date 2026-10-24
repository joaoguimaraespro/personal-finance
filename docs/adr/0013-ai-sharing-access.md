# ADR-0013: Shared costs for AI clients

- Status: Accepted
- Date: 2026-10-24
- Extends: [ADR-0008](0008-ai-write-access.md) (AI write access), [ADR-0011](0011-shared-expenses.md) (shared expenses)

## Context
Shared expenses (ADR-0011) are part of everyday upkeep: "I paid 36 € of internet, Ana and Rui owe 12 € each",
"Ana paid me back 12 €", "who still owes me?". AI clients could only see people as accounts of kind `Person` in
`get_accounts`. Nothing told them who shares an expense, what is still unpaid or how to record a payback. A payback
settled outside the app's accounts, such as cash, could not be recorded at all.

People are third parties. Their names, and who owes the owner what, are more personal than a category total.

## Decision
- **`sharing.read`**, a read scope that is not sensitive, like `accounts.balances.read`. It gives `get_sharing`: each person's balance
  (+ they owe the owner, − the owner owes them), since when, what was owed before the app, open shares with what is
  still unpaid, recent movements and the totals owed both ways. It also adds who shares each expense to
  `get_transactions`, and who shares each recurring item to `get_recurring`. **People are accounts**, so their names
  (and the owner's accounts in these answers) appear only with `accounts.names.read` or `accounts.identifiers.read`.
  Without either, a person is a balance with no name. Descriptions and names are `untrusted_text`.
- **`sharing.write`**, an opt-in sensitive write scope, added to the allow-list of ADR-0008, with three typed
  single-record tools:
  - `set_expense_shares(transaction_id, shares)` sets the people's parts of one hand-entered expense, or `none`.
  - `set_recurring_shares(recurring_id, shares)` sets the percentages of one recurring expense, or `none`. Nothing
    else about the item changes.
  - `create_settlement(person_id, direction, amount, date?, account_id?, description?)` records money received from a
    person or paid to them. With an account it is a transfer. Without one, it was settled outside the app.
- **Shares travel as one string** (`"person_id: value; …"`, 1–20 lines, each person once), like split lines. Every
  tool argument stays a scalar. People are named **by id only**, never by name, because names are user data.
- **The app's own rules.** The tools call `TransactionCommands.UpdateAsync` with shares, `RecurringCommands.UpdateAsync`
  and the settle command shared with the Sharing page (`SharingEndpoints.SettleAsync`). Validation, the ledger audit
  actor `ai:<client>` and interest recalculation are therefore the same as in the app. The ADR-0008 refusals still
  apply:
  - broker-sourced rows and broker accounts;
  - archived accounts and people;
  - shares themselves, refunds and expenses split by category (ADR-0011: sharing and splitting are not combined).
- **Ids only for writers.** Person ids and the ids of open expenses go only to `sharing.write` clients. The same goes
  for the ids of shareable expenses in `get_transactions` and of recurring items in `get_recurring`.
- An expense a person paid for the owner needs no new tool. It is `create_transaction` with the person's id as
  `account_id`, under `transactions.write`, and the tool description says so.

## Consequences
- Reversibility. Removing a share means the owner takes that part on. Putting it back is another
  `set_expense_shares`. A settlement is an ordinary transaction, so `delete_transaction` can move it to the recycle
  bin. None of these tools deletes anything outright.
- Creating, renaming or archiving people, and editing what was owed before the app, stay in the app. The MCP has no
  account-management tools.
- The pinned catalogue and the write-scope allow-list in `AiBoundaryTests` grow by one read tool, three write tools
  and one write scope.
