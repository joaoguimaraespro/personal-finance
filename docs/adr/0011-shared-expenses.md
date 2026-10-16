# ADR-0011: Shared expenses with people

- Status: Accepted
- Date: 2026-10-16

## Context
The owner often pays a cost for several people (a streaming subscription, the shared flat's internet and water, a
supermarket run) and is paid back later into another account, or a friend pays something for the owner. Booked as
one expense, spending is overstated; booking the money that comes back as income inflates income and the savings
rate; and nobody keeps track of who still owes what.

## Decision
- **People are accounts** of a new kind, `Person`. A person's balance is what they owe the owner (positive) or what
  the owner owes them (negative). Net worth counts it as money receivable or owed, so it stays right while a debt is
  open.
- **A shared expense stays one expense** in the account that paid it (the full amount leaves that account). Each
  person's part is a **share**: a refund of the same category booked in the person's account and linked to the
  expense (`SharedFromId`). The category's spending — budgets, reports, the month — counts only the owner's part, on
  the day of the expense, whenever the others pay.
- **Being paid back is a transfer** from the person to the account the money arrived in (neither income nor
  spending). Paying a person back is the opposite transfer. A friend who paid for the owner is an expense in that
  person's account.
- **Shares follow their expense**: editing the expense updates them, deleting it deletes them. Deleting one share
  means the owner takes that part on (it becomes their spending). Shares are not edited on their own.
- What is still owed per expense is derived (payments cover the oldest shares first); nothing is stored twice.
- Splitting across categories and sharing are not combined: a shared expense has one category.

## Consequences
Reports, budgets, account balances and net worth need no special cases: they already handle refunds, transfers and
accounts. People appear in account answers to AI clients like any account (kind `Person`). Recurring items can carry
shares too, so a shared bill proposes the owner's expense and the others' parts together.
