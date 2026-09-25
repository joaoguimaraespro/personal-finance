# ADR-0006: AI access only through a policy gateway

- Status: Accepted (implementation in Phases 7–8)
- Date: 2026-09-25

## Context
AI assistants are useful for questions about spending and portfolio, but a general-purpose model with
database access would receive far more than it needs and could be steered by injected text.

## Decision
- The MCP server has no database connection; it calls typed `/api/ai/*` endpoints.
- Each AI client has its own hashed token, explicit scopes (e.g. `expenses.summary.read`), expiry,
  rate limit and revocation. Sensitive scopes (`accounts.identifiers.read`, `raw.transactions.read`,
  `personal.notes.read`) are off by default.
- Tools are purpose-built and return minimal DTOs — asking about restaurant spending returns that total,
  not accounts or portfolio.
- Free text is returned in explicit untrusted fields; tools are read-only; no SQL, shell, file or write
  tools exist.
- Every call (allowed or denied) is audited without storing the returned values.

## Consequences
New questions need new tools rather than ad-hoc queries — deliberate friction that keeps data exposure
reviewable.
