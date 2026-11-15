---
description: C# coding style: nullable, explicit types, file-scoped namespaces, records, async, formatting
paths:
  - "**/*.cs"
  - "**/*.csx"
---
# C# coding style

Extends [common/coding-style.md](../common/coding-style.md). `.editorconfig` enforces most of this; run
`dotnet format` before committing.

## Language
- Nullable reference types on (`<Nullable>enable</Nullable>`); treat nullable warnings as errors in new projects.
- File-scoped namespaces (`namespace X.Y;`). `using` directives outside the namespace, `System` first, unused removed.
- Braces on every control statement, even one-liners.
- Explicit access modifiers on every non-interface member. `sealed` by default for classes not designed for
  inheritance. `readonly` fields where possible.
- No `dynamic` in application code.

## `var`
The repo's `.editorconfig` (`csharp_style_var_*`) decides (some repos use `var` throughout). When the
repo sets nothing, the default is:
- No `var` for built-in types: `int count = 10;`, `string name = "x";`, `Guid id = Guid.NewGuid();`.
- No `var` when the right side does not make the type obvious (repository calls, LINQ pipelines ending in
  `ToListAsync`, method results): `List<Order> orders = await ...;`, `Order? order = await repo.GetAsync(id, ct);`.
- `var` is fine when the type is apparent: `new Order(...)`, `TimeSpan.FromMinutes(5)`, `Result.Success(x)`.

## Types
- `record` / `record struct` for immutable value-like data (DTOs, requests, responses, value objects); `sealed record`
  for responses. `class` for entities with identity and lifecycle. Interfaces at service boundaries.
- Prefer `init` and constructor parameters; produce changed copies with `with`. Never mutate an input model.
- Primary constructors are fine for dependency injection in services and handlers; keep domain entities with private
  constructors and static factories.
- Money and exact quantities are `decimal`, never `double`/`float`.

## Async
- `async`/`await` all the way; never `.Result`, `.Wait()` or `.GetAwaiter().GetResult()` in request paths.
- Every public async method takes a `CancellationToken` and passes it on. Name async methods `...Async`.
- No `async void` outside event handlers.

## Errors and logging
- Expected failures (not found, validation, conflict, forbidden) are `Result`/`Error` values, not exceptions (see
  [patterns.md](patterns.md)). Exceptions are for the unexpected.
- Structured logging with message templates (`logger.LogError(ex, "Failed to load order {OrderId}", id)`), never
  string interpolation in the template; never log tokens, secrets or personal data.

## Formatting
- Expression-bodied members for properties, accessors, operators and lambdas; block bodies for methods and
  constructors unless a one-line body stays clearly readable.
- LF line endings, 4-space indent, UTF-8, final newline.

<!-- Adapted from affaan-m/ECC rules/csharp/coding-style.md (MIT) at d2ef268 (identical to the Ordo frozen copy); merged with the generic ordo-api CLAUDE.md/.editorconfig conventions (var, braces, file-scoped namespaces, modifiers), personal-finance-sdd (decimal money, primary constructors) and NEXUS knowledge/dotnet. -->
