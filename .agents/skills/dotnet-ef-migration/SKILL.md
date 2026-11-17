---
name: dotnet-ef-migration
description: Create, review, list, remove or script an EF Core migration for one module of a .NET modular monolith (one DbContext per module). Use after changing an entity or its configuration, or when asked to add a migration.
---

# EF Core migration for a module

Inputs: the migration name (PascalCase, says what changed) and the module. Find the paths from the repo first:
`<Module>.Infrastructure` project, the startup (host) project, and the `<Module>DbContext` class. Read the repo's
`AGENTS.md`/`CLAUDE.md`: if it names another output folder or command, use that.

Never write or edit migration files by hand; the tool generates them.

## Steps

1. Model change complete (entity, `IEntityTypeConfiguration<T>`, `DbSet` if new). `dotnet build` passes.
2. Generate:
   ```bash
   dotnet ef migrations add <Name> \
     --project src/Modules/<Module>/<Module>.Infrastructure/<Module>.Infrastructure.csproj \
     --startup-project src/Web.Api/Web.Api.csproj \
     --context <Module>DbContext \
     --output-dir Database/Migrations
   ```
   If the context name is ambiguous across modules, pass the fully qualified name
   (`<Module>.Infrastructure.Database.<Module>DbContext`).
3. Review the generated `Up()` and `Down()`:
   - only this module's schema is touched;
   - no unintended drops or renames (EF turns a rename into drop + add unless told: use `RenameColumn`/`RenameTable`);
   - indexes and unique constraints include the tenant column when the table is tenant-scoped;
   - data backfills (`migrationBuilder.Sql(...)`) are idempotent and parameter-free.
4. Apply locally: `dotnet ef database update --project ... --startup-project ... --context <Module>DbContext`
   (or the repo's own startup migration step). Run the tests.
5. Commit the model change and the migration together.

## Other commands (same `--project/--startup-project/--context`)

| Need | Command |
|---|---|
| Undo the last, unapplied migration | `dotnet ef migrations remove ...` |
| List | `dotnet ef migrations list ...` |
| Deployment script | `dotnet ef migrations script --idempotent --output <Name>.sql ...` |

## Naming

`Add<X>Table`, `Add<Column>To<Table>`, `Add<X>Index`, `Add<X>Relation`, `Remove<X>`, `Rename<X>To<Y>`,
`Backfill<X>`.

## Troubleshooting

- "No DbContext was found": the context is not registered by the module's `DependencyInjection`, or the startup
  project does not reference the module.
- "More than one DbContext": pass `--context`.
- Pending model changes after generating: something else changed the model; build and diff again.
