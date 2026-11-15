---
description: Security checks before every commit, secret handling, data handling and what to do when an issue is found
alwaysApply: true
---
# Security (all languages)

## Before every commit
- [ ] No hardcoded secrets (keys, passwords, tokens, connection strings); no `.env` committed
- [ ] All external input validated; queries parameterised (no string-built SQL)
- [ ] Output encoded for its context (HTML, shell, SQL, logs)
- [ ] Authentication and authorisation checked on the server, per request; tenant taken from the token, not from
      a header, query or route
- [ ] Error messages and logs leak no secrets, tokens or personal data
- [ ] New dependencies are maintained, pinned, and from the official registry

## Secrets
- Environment variables or a secret manager only. Check required secrets at startup and fail fast.
- Local development values are clearly fake (e.g. prefixed `local-only-`). Rotate anything that may have leaked.

## Data
- Synthetic data only in the repo, tests, specs, issues and commits. No client data, no real organisation names.
- Least privilege for every service account, token and database user.

## When you find an issue
1. Stop the current change.
2. Use the `security-reviewer` agent on the affected code.
3. Fix CRITICAL issues before anything else; rotate exposed secrets.
4. Look for the same pattern elsewhere in the codebase.

<!-- Adapted from affaan-m/ECC rules/common/security.md (MIT) at d2ef268; merged with the owner's no-client-data and tenant-from-token rules. -->
