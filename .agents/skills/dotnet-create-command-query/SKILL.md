---
name: dotnet-create-command-query
description: Create a CQRS command (with handler and validator) or query (with handler and response) in a .NET module, returning Result, named <Verb><Entity>Command/Query, plus the endpoint that sends it. Use when adding a use case, an endpoint or a read model to a module's Application layer.
---

# Create a command or a query

Inputs: `command` or `query`, verb (`Create`, `Update`, `Archive`, `Get`, `List`, `Search`...), entity, module.
First read one existing command and one query in the same module and copy their shape: mediator interfaces
(`ICommand<T>`/`IQuery<T>`/`ISender`, or MediatR's), `Result` type, how handlers reach data (repository + unit of
work, or the module's `DbContext` for reads), how endpoints map a `Result` to HTTP.

Test first: the handler test (success and each `Error`) and, for a new endpoint, an HTTP test through
`WebApplicationFactory`.

## Layout

```
src/Modules/<Module>/<Module>.Application/<Entity>/<Verb>/
  <Verb><Entity>Command.cs           or  <Verb><Entity>Query.cs
  <Verb><Entity>CommandHandler.cs        <Verb><Entity>QueryHandler.cs
  <Verb><Entity>CommandValidator.cs      <Entity>Response.cs (sealed record), if not shared
```

## Command

```csharp
namespace <Module>.Application.<Entity>.Create;

public sealed record Create<Entity>Command(string Name) : ICommand<Guid>;   // ICommand (no value) for Update/Delete

internal sealed class Create<Entity>CommandHandler(
    I<Entity>Repository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<Create<Entity>Command, Guid>
{
    public async Task<Result<Guid>> Handle(Create<Entity>Command command, CancellationToken cancellationToken)
    {
        bool exists = await repository.ExistsByNameAsync(command.Name, cancellationToken);
        if (exists)
        {
            return Result.Failure<Guid>(<Entity>Errors.DuplicateName(command.Name));
        }

        Result<<Entity>> created = <Entity>.Create(command.Name);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        repository.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return created.Value.Id;   // the repo's public id if it has one
    }
}

internal sealed class Create<Entity>CommandValidator : AbstractValidator<Create<Entity>Command>
{
    public Create<Entity>CommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(<Entity>.MaxNameLength);
    }
}
```

Update: load by id (tenant-filtered), `NotFound` error when missing, call entity methods, save. Delete or archive:
the same, with the entity's method. Validators check shape; the domain checks business rules.

## Query

```csharp
namespace <Module>.Application.<Entity>.List;

public sealed record List<Entity>Query(int Page = 1, int PageSize = 20) : IQuery<PagedResult<<Entity>Response>>;

internal sealed class List<Entity>QueryHandler(<Module>DbContext db)
    : IQueryHandler<List<Entity>Query, PagedResult<<Entity>Response>>
{
    public async Task<Result<PagedResult<<Entity>Response>>> Handle(List<Entity>Query query, CancellationToken cancellationToken)
    {
        IQueryable<<Entity>> source = db.<Entity>s.AsNoTracking();   // tenant filter is global
        int total = await source.CountAsync(cancellationToken);
        List<<Entity>Response> items = await source
            .OrderBy(e => e.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(e => new <Entity>Response(e.Id, e.Name, e.Status.ToString()))
            .ToListAsync(cancellationToken);
        return new PagedResult<<Entity>Response>(items, total, query.Page, query.PageSize);
    }
}

public sealed record <Entity>Response(Guid Id, string Name, string Status);
```

Queries: `AsNoTracking`, projection, pagination, sort fields from an allow-list, `NotFound` for a missing single
item. A query never changes state.

## Endpoint

```csharp
[HttpPost]
public async Task<IActionResult> Create(Create<Entity>Command command, CancellationToken cancellationToken)
{
    Result<Guid> result = await sender.Send(command, cancellationToken);
    return result.Match(Ok, CustomResults.Problem);   // the repo's Result-to-HTTP mapping
}
```

Inject `ISender`. Prefer the repo's existing `Match`/Problem mapping; avoid `CreatedAtAction` link generation if
the repo's versioned routes break it (return `Ok` with the id instead).

## Checklist
- [ ] Names `<Verb><Entity>Command|Query` and `...Handler`/`...Validator`
- [ ] Returns `Result`/`Result<T>`; failures are typed `Error`s
- [ ] Handler uses only its own module's data access
- [ ] Tests first; gate passes
