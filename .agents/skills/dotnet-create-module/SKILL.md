---
name: dotnet-create-module
description: Scaffold a new module in a .NET modular monolith (Domain, Application, Infrastructure, Api projects, its own DbContext and schema, DI registration, test project). Use when a new bounded context needs its own module.
---

# Create a module

Inputs: module name (PascalCase, plural noun or capability: `Invoices`, `Notifications`), its first entity.
First read the repo's architecture doc/ADRs and one existing module end to end. Copy its project references,
package versions (`Directory.Packages.props` if central), base classes and registration style. The structure below
is the default only.

## Structure

```
src/Modules/<Module>/
  <Module>.Domain/             entities, value objects, <Entity>Errors, domain events   -> SharedKernel
  <Module>.Application/        commands, queries, validators, responses, abstractions    -> Domain, mediator
    DependencyInjection.cs
  <Module>.Infrastructure/     <Module>DbContext, Schema, configurations, repositories   -> Application, base Infrastructure
    Database/<Module>DbContext.cs, Database/Schema.cs, Database/Configurations/, Database/Migrations/
    DependencyInjection.cs
  <Module>.Api/                controllers or minimal-API endpoints                      -> Application
tests/<Module>.Tests/          domain and handler tests (+ integration tests where the repo keeps them)
```

Dependencies point inwards only: Api -> Application -> Domain; Infrastructure -> Application. No project references
another module's projects except its public API contract project, if the repo has one.

## DbContext and schema

```csharp
namespace <Module>.Infrastructure.Database;

public static class Schema
{
    public const string Name = "<module_snake_case>";
}

public sealed class <Module>DbContext(DbContextOptions<<Module>DbContext> options /*, tenant provider if any */)
    : BaseDbContext(options), I<Module>UnitOfWork
{
    public DbSet<<Entity>> <Entity>s => Set<<Entity>>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema.Name);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(<Module>DbContext).Assembly);
    }
}
```

Use the repo's provider and naming convention (e.g. `UseNpgsql(...).UseSnakeCaseNamingConvention()`), the shared
connection string, and the migrations history table in the module's schema
(`npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schema.Name)`).

## Registration

```csharp
public static IServiceCollection Add<Module>Module(this IServiceCollection services, IConfiguration configuration)
{
    services.Add<Module>Application();          // validators, handlers (assembly scan)
    services.Add<Module>Infrastructure(configuration);   // DbContext, repositories, unit of work
    return services;
}
```

Register in the host's `Program.cs` next to the other modules; add the Api assembly to controller discovery or map
its endpoints; include the module in startup migration application if the repo does that.

## Then
1. First entity with `dotnet-create-entity`, first use cases with `dotnet-create-command-query`.
2. Initial migration with `dotnet-ef-migration` (`Initial<Module>Schema`).
3. Add the projects to the solution (`dotnet sln add`), add the test project, extend architecture tests to the new
   module.

## Checklist
- [ ] Four projects + test project, references inward only, added to the solution
- [ ] Own DbContext, schema and migrations folder
- [ ] Registered in the host; endpoints authorised
- [ ] Architecture tests cover it; gate passes
