# ADR-0002: Built-in ASP.NET Identity with mandatory TOTP instead of Keycloak

- Status: Accepted
- Date: 2026-09-25

## Context
The app has exactly one user and is reachable only over the VPN. Keycloak would add a JVM service,
a database and upgrade work, mainly to support features (federation, many users, SSO) that are not needed.

## Decision
ASP.NET Core Identity with cookie authentication for the same-origin SPA, a one-time setup token for the
owner, and **mandatory** TOTP MFA enforced by an authorization policy on the `amr=mfa` claim. The
standard OpenID Connect handler can be enabled by configuration to delegate to Keycloak or similar later.
AI clients will use separate app-issued, hashed, scoped tokens.

## Consequences
Fewer moving parts and a smaller attack surface. Password reset is manual (recovery codes, or a
CLI reset) — acceptable for a single owner.
