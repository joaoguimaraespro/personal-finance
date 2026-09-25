# ADR-0001: Modular monolith

- Status: Accepted
- Date: 2026-09-25

## Context
Single user, one home server, a handful of bounded contexts (finance, investments, integrations,
reporting, AI). Microservices would add network failure modes, distributed transactions and operational
load without any scaling need.

## Decision
One ASP.NET Core process with modules (`Domain` / `Application` / `Infrastructure`) sharing one
PostgreSQL database in separate schemas. Boundaries are enforced by project references and
architecture tests. Background jobs run as hosted services; a separate worker process can be split out
later without changing module code.

## Consequences
Simple deployment and consistent transactions (an import commits atomically). Discipline is required to
keep modules decoupled — hence the architecture tests.
