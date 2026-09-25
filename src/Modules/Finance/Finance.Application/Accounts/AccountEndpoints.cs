using FluentValidation;
using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Domain.Accounts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Accounts;

public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountKind Kind,
    string Currency,
    string? Institution,
    string? IdentifierMasked,
    decimal OpeningBalance,
    DateOnly OpeningBalanceOn,
    decimal Balance,
    bool IsManual,
    bool IsLiability,
    bool Archived);

public sealed record CreateAccountRequest(
    string Name,
    AccountKind Kind,
    string Currency,
    decimal OpeningBalance,
    DateOnly? OpeningBalanceOn,
    string? Institution,
    string? Identifier);

public sealed record UpdateAccountRequest(
    string Name,
    decimal OpeningBalance,
    DateOnly OpeningBalanceOn,
    string? Institution,
    string? Identifier);

public sealed class CreateAccountValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Kind).IsInEnum()
            .NotEqual(AccountKind.Broker).WithMessage("Broker accounts are created by broker connections.");
        RuleFor(x => x.Currency).Must(Currency.IsValid).WithMessage("Use an ISO 4217 currency code.");
        RuleFor(x => x.Institution).MaximumLength(80);
        RuleFor(x => x.Identifier).MaximumLength(64);
    }
}

public sealed class UpdateAccountValidator : AbstractValidator<UpdateAccountRequest>
{
    public UpdateAccountValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Institution).MaximumLength(80);
        RuleFor(x => x.Identifier).MaximumLength(64);
    }
}

public static class AccountEndpoints
{
    public static readonly Error NotFound = Error.NotFound("Account.NotFound", "Account not found.");

    public static readonly Error ReadOnly =
        Error.Forbidden("Account.ReadOnly", "Broker accounts are read-only and managed by their integration.");

    public static void MapAccounts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Accounts");

        group.MapGet("/", async (IFinanceDb db, bool? includeArchived, CancellationToken ct) =>
        {
            var accounts = await db.Accounts.AsNoTracking()
                .Where(a => includeArchived == true || a.ArchivedAtUtc == null)
                .OrderBy(a => a.Kind).ThenBy(a => a.Name)
                .ToListAsync(ct);
            var balances = await AccountBalances.ComputeAsync(db, accounts, null, ct);
            return Results.Ok(accounts.Select(a => ToDto(a, balances[a.Id])));
        });

        group.MapPost("/", async (CreateAccountRequest req, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var account = Account.Create(req.Name, req.Kind, req.Currency, req.OpeningBalance,
                req.OpeningBalanceOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), req.Institution,
                req.Identifier);
            db.Accounts.Add(account);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/accounts/{account.Id}", ToDto(account, account.OpeningBalance));
        }).Validate<CreateAccountRequest>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateAccountRequest req, IFinanceDb db, CancellationToken ct) =>
        {
            var account = await db.Accounts.FindAsync([id], ct);
            if (account is null)
            {
                return ResultHttp.Problem(NotFound);
            }

            if (!account.IsManual)
            {
                return ResultHttp.Problem(ReadOnly);
            }

            // An empty identifier in the request keeps the stored one; "-" clears it.
            var identifier = req.Identifier switch
            {
                null or "" => account.Identifier,
                "-" => null,
                _ => req.Identifier,
            };
            account.Update(req.Name, req.OpeningBalance, req.OpeningBalanceOn, req.Institution, identifier);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Validate<UpdateAccountRequest>();

        group.MapPost("/{id:guid}/archive", (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
            SetArchived(id, archived: true, db, clock, ct));

        group.MapPost("/{id:guid}/restore", (Guid id, IFinanceDb db, TimeProvider clock, CancellationToken ct) =>
            SetArchived(id, archived: false, db, clock, ct));
    }

    private static async Task<IResult> SetArchived(Guid id, bool archived, IFinanceDb db, TimeProvider clock,
        CancellationToken ct)
    {
        var account = await db.Accounts.FindAsync([id], ct);
        if (account is null)
        {
            return ResultHttp.Problem(NotFound);
        }

        if (archived)
        {
            account.Archive(clock.GetUtcNow());
        }
        else
        {
            account.Restore();
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static string? Mask(string? identifier) => identifier switch
    {
        null or "" => null,
        { Length: <= 4 } => "••••",
        _ => "••••" + identifier[^4..],
    };

    private static AccountDto ToDto(Account a, decimal balance) => new(a.Id, a.Name, a.Kind, a.Currency,
        a.Institution, Mask(a.Identifier), a.OpeningBalance, a.OpeningBalanceOn, balance, a.IsManual, a.IsLiability,
        a.ArchivedAtUtc is not null);
}
