---
description: .NET solution layout: modular monolith projects and folders, naming, build props, analyzers, architecture tests
paths:
  - "**/*.cs"
  - "**/*.csproj"
  - "**/*.slnx"
  - "**/Directory.Build.props"
  - "**/Directory.Packages.props"
  - "**/global.json"
---
# .NET project structure

The owner's target layout for every .NET API (decided 2026-11-17). New repos start from it
(`plugins/stack-dotnet/templates/`); existing repos move toward it in small changes. A repo's ADR wins where it
records a different choice.

## Layout

```text
<Product>.slnx
global.json                       SDK pinned: rollForward latestPatch, allowPrerelease false
Directory.Build.props             net11.0, nullable, warnings as errors, analyzers, code style in build
Directory.Packages.props          every package version, once (central package management)
BannedSymbols.txt                 APIs agents misuse, each with the fix (BannedApiAnalyzers, src projects only)
.editorconfig                     Armature template (templates/.editorconfig)
src/
  Web.Api/                        Program.cs, composition root only: maps each module, no business code
  BuildingBlocks/
    SharedKernel/                 Entity, AggregateRoot, Result, Error, ErrorType, IDomainEvent (no ASP.NET)
    Application/                  ICommand, IQuery, handlers, pipeline behaviours, PagedResult, IUnitOfWork
    Mediator/                     ISender, IPublisher (in-house, no MediatR licence)
    Infrastructure/               ModuleDbContext base, interceptors, tenant filter, outbox
    Web/                          ProblemDetails mapping, MapApiV1, auth, tenancy, request ids, health
  Modules/<Module>/
    <Module>.Domain/              <Entity>.cs, <Entity>Errors.cs, value objects, domain events
    <Module>.Application/
      Abstractions/               I<Module>UnitOfWork, I<Entity>Repository (only when a repository earns its keep)
      <Entity>/<Verb>/            <Verb><Entity>Command|Query, …Handler, …Validator, …Response
    <Module>.Infrastructure/
      Database/                   <Module>DbContext, Schema.cs, Configurations/, Migrations/
      DependencyInjection.cs      Add<Module>Infrastructure()
    <Module>.Api/
      <Entity>/<Verb><Entity>Endpoint.cs   one minimal-API endpoint per use case
      <Module>Endpoints.cs        Map<Module>Endpoints(this IEndpointRouteBuilder)
      I<Module>Api.cs             the module's public API for other modules (the only cross-module door)
tests/
  Architecture.Tests/             module boundaries, naming, no sync-over-async, no tenant-filter bypass
  <Module>.Tests/                 handler and domain tests (fakes, no I/O)
  Integration.Tests/              WebApplicationFactory + Testcontainers (PostgreSQL, Keycloak)
```

## Naming
- Assemblies and root namespaces `<Company>.<Product>.<Project>` (modules: `<Company>.<Product>.Modules.<Module>.<Layer>`),
  set once in `Directory.Build.props`, never per project.
- Requests `<Verb><Entity>Command` / `<Verb><Entity>Query`; handlers `<Request>Handler`; validators
  `<Request>Validator`; responses `<Entity>Response` or `<Entity>DetailsResponse`. The architecture tests check the
  suffixes.
- Types are `internal sealed` unless another project needs them; handlers, validators, configurations and endpoints
  are always `internal sealed`.

## Build rules (Directory.Build.props)
`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`,
`GenerateDocumentationFile` (so IDE0005 runs; `CS1591` off), `Deterministic`, `NuGetAudit` with `NuGetAuditMode=all`.
Analyzers, all free: Roslynator.Analyzers (Apache-2.0), Meziantou.Analyzer (MIT) and
Microsoft.CodeAnalysis.BannedApiAnalyzers (MIT). A rule is turned off only in `.editorconfig`, with a comment that
cites why (an ADR or an issue). No `#pragma warning disable` without a comment and an issue.

## Banned APIs (BannedSymbols.txt)
The APIs agents reach for to patch a problem instead of fixing it are build errors (RS0030) in `src/` projects, and
each error says what to use instead: `DateTime.Now`/`UtcNow` and `DateTimeOffset.Now`/`UtcNow` (inject
`TimeProvider`), `Task.Result`, `.Wait()` and `GetAwaiter().GetResult()` (await), `Thread.Sleep` (`Task.Delay` with
`TimeProvider`), `Console` (`ILogger<T>`), `new HttpClient()` (`IHttpClientFactory` or a typed client). Test projects
are exempt. When a review keeps correcting the same call, add it here with its fix (the `review-lessons` skill);
never remove an entry to make a build pass.

## Gate
`dotnet format --verify-no-changes`, `dotnet build -warnaserror`, `dotnet test` (unit, architecture, integration).
The integration tests need Docker; the gate fails early when it is missing.

## Legacy (ordo-api today)
Controllers, the `x-organization-id` header for the tenant, anemic entities and missing analyzers are the old
shape. New code follows this rule; moving an existing module is its own change.
