---
name: dotnet-create-entity
description: Create a domain entity in a .NET module with a validating factory, private setters, domain errors and its EF Core configuration (tenant-scoped when the repo is multi-tenant). Use when adding a new aggregate or entity to a module's Domain layer.
---

# Create a domain entity

Inputs: entity name (PascalCase), module. Before writing, read one existing entity, its `<Entity>Errors` and its
configuration in the same module and follow them exactly (base class, id type, public id, tenant marker, `Error`
factory names). The templates below are the default shape only.

Test first: write the domain tests for `Create` and each state method (success and every error) before the code.

## Files

```
src/Modules/<Module>/<Module>.Domain/<Entity>.cs
src/Modules/<Module>/<Module>.Domain/<Entity>Errors.cs
src/Modules/<Module>/<Module>.Infrastructure/Database/Configurations/<Entity>Configuration.cs
tests/<Module>.Tests/Domain/<Entity>Tests.cs
```

## Entity

```csharp
namespace <Module>.Domain;

public sealed class <Entity> : TenantEntity   // the repo's base: BaseEntity<TKey>, AuditableEntity, tenant entity...
{
    private <Entity>() { }                    // for EF Core

    public string Name { get; private set; } = null!;
    public <Entity>Status Status { get; private set; }

    public static Result<<Entity>> Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<<Entity>>(<Entity>Errors.NameRequired);
        }

        if (name.Length > MaxNameLength)
        {
            return Result.Failure<<Entity>>(<Entity>Errors.NameTooLong);
        }

        return new <Entity> { Name = name.Trim(), Status = <Entity>Status.Draft };
    }

    public Result Archive()
    {
        if (Status == <Entity>Status.Archived)
        {
            return Result.Failure(<Entity>Errors.AlreadyArchived);
        }

        Status = <Entity>Status.Archived;
        return Result.Success();
    }

    public const int MaxNameLength = 200;
}

public enum <Entity>Status
{
    Draft,
    Active,
    Archived,
}
```

Rules: private setters; collections exposed as `IReadOnlyList<T>` over a private list; state changes only through
methods that return `Result`; raise domain events inside the entity (`RaiseDomainEvent(...)`) when other modules
must react; the tenant id is set by the infrastructure (or passed to `Create`), never from request input.

## Errors

```csharp
namespace <Module>.Domain;

public static class <Entity>Errors
{
    public static readonly Error NotFound = Error.NotFound("<Entity>.NotFound", "The <entity> was not found.");
    public static readonly Error NameRequired = Error.Validation("<Entity>.NameRequired", "Name is required.");
    public static readonly Error NameTooLong = Error.Validation("<Entity>.NameTooLong", "Name is too long.");
    public static readonly Error AlreadyArchived = Error.Conflict("<Entity>.AlreadyArchived", "The <entity> is already archived.");

    public static Error DuplicateName(string name) =>
        Error.Conflict("<Entity>.DuplicateName", $"A <entity> named '{name}' already exists.");
}
```

Business-rule failures are Validation/Problem, NotFound, Conflict or Forbidden; never `Failure` (500).

## EF Core configuration

```csharp
namespace <Module>.Infrastructure.Database.Configurations;

internal sealed class <Entity>Configuration : IEntityTypeConfiguration<<Entity>>
{
    public void Configure(EntityTypeBuilder<<Entity>> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(<Entity>.MaxNameLength).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
        builder.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();   // tenant column first when tenant-scoped
    }
}
```

The table goes in the module's schema (the context's default schema). Add the `DbSet` to the module context, then
create the migration with the `dotnet-ef-migration` skill.

## Checklist
- [ ] Tests for `Create` and every state method, written first
- [ ] Private constructor, private setters, factory returning `Result<T>`
- [ ] Every failure has a typed `Error` with code `<Entity>.<Reason>`
- [ ] Configuration with lengths, conversions, tenant-first indexes
- [ ] `DbSet` added and migration generated
