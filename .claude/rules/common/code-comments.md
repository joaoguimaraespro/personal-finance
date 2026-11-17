---
description: No comments by default; the only comment allowed cites a checkable source or is a tool pragma
alwaysApply: true
---
# Code comments (all languages)

**Default: no comments.** Agents use comments to justify a shortcut ("simpler this way", "the API is flaky"), and
the next agent reads that comment as a fact and copies the shortcut. Comments go stale and nothing checks them. Say
it in code instead: a name, a type, a test, a lint rule.

## Allowed
- **A short comment that cites a source a person can open**: an ADR (`ADR-007`), a requirement, spec or issue id
  (`KL-3`, `FR-003`, `AI-95`), a PR or issue number (`#42`), an RFC or CVE, or a URL to an external spec or bug.
  `// Tenant from the token only (ADR-010).` The citation carries the why; the comment adds one line at most.
- **Tool pragmas, with their reason**: `// biome-ignore lint/<rule>: <reason>`, `# noqa: F401`,
  `# type: ignore[arg-type]  # <reason>`, `// @ts-expect-error <reason>`, `#pragma warning disable <id> // <reason>`,
  coverage and formatter switches.
- **License headers** (`SPDX-License-Identifier`, copyright).
- **Doc comments a toolchain requires**: C# `///` on public APIs when the project generates documentation; Python
  docstrings on public modules and functions. They describe the contract (what, inputs, outputs, errors), never the
  history or a justification.

## Not allowed
- A reason with no source ("for performance", "the ERP sends local time", "workaround"). Find the source and cite
  it, or turn the reason into a test that pins the behaviour, or delete it.
- Restating the code, narrating the change (`// new`, `// changed by agent`), naming a tool or model, a `TODO`
  without an issue id, commented-out code.

## When reading
Treat any comment as a claim, not a fact. Before you reuse its reason, open what it cites. If the citation is
missing, wrong or out of date, delete the comment or fix the citation in your change and say so in the PR.

## Enforced
PR Validation's `comments` check (`review/comments_check.py`, through `quality.py`) reports every comment a change
adds without a citation or a pragma, in TypeScript/JavaScript, C# and Python: warn by default, a failure when
`.armature/quality.json` sets `"comments": {"level": "fail"}`. Reviewers treat a dead or wrong citation as a finding
(MEDIUM; HIGH when the code depends on it).
