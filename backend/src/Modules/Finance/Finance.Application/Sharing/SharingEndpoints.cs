using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Http;
using Finance.Application.Interest;
using Finance.Application.Transactions;
using Finance.Domain.Accounts;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.Sharing;

/// <summary>A person's part of an expense, and how much of it is still to be paid.</summary>
public sealed record OpenShareDto(Guid ShareId, Guid ExpenseId, DateOnly On, string? Description, string? CategoryName,
    string PaidFrom, decimal ExpenseAmount, decimal Amount, decimal Outstanding);

/// <summary>A movement in a person's account, signed as it moves their balance (+ they owe more).</summary>
public sealed record PersonMovementDto(Guid TransactionId, DateOnly On, string Kind, decimal Amount, string? Description,
    string? OtherAccount);

/// <summary>Balance: what the person owes the owner (positive) or the owner owes them (negative).</summary>
public sealed record PersonDto(Guid Id, string Name, decimal Balance, DateOnly? OwedSince,
    IReadOnlyList<OpenShareDto> Open, IReadOnlyList<PersonMovementDto> Recent);

public sealed record SharingOverviewDto(decimal OwedToYou, decimal YouOwe, IReadOnlyList<PersonDto> People);

/// <summary>Being paid back (received) or paying a person back (paid), into or from one of the owner's accounts.</summary>
public sealed record SettleRequest(string Direction, Guid AccountId, decimal Amount, DateOnly On, string? Description);

public static class SharingErrors
{
    public static readonly Error NotAPerson = Error.NotFound("Sharing.Person", "Person not found.");

    public static readonly Error InvalidSettlement = Error.Validation("Sharing.Settle",
        "Choose received or paid, one of your own accounts and a positive amount.");
}

/// <summary>People the owner shares costs with (ADR-0011): who owes what, and being paid back.</summary>
public static class SharingEndpoints
{
    /// <summary>Most recent movements shown per person.</summary>
    private const int RecentCount = 10;

    public static void MapSharing(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sharing").WithTags("Sharing");

        group.MapGet("/", async (IFinanceDb db, CancellationToken ct) => Results.Ok(await OverviewAsync(db, ct)));

        group.MapPost("/{personId:guid}/settle", async (Guid personId, SettleRequest req, IFinanceDb db,
            InterestAccrualService interest, CancellationToken ct) =>
        {
            var person = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == personId && a.Kind == AccountKind.Person, ct);
            if (person is null)
            {
                return ResultHttp.Problem(SharingErrors.NotAPerson);
            }

            var own = await db.Accounts.AsNoTracking().AnyAsync(a => a.Id == req.AccountId &&
                a.Kind != AccountKind.Person && a.Kind != AccountKind.Broker && a.ArchivedAtUtc == null, ct);
            if (!own || req.Direction is not ("received" or "paid") || req.Amount <= 0)
            {
                return ResultHttp.Problem(SharingErrors.InvalidSettlement);
            }

            var received = req.Direction == "received";
            var draft = new TransactionDraft(TransactionType.Transfer, req.On, req.Amount, person.Currency,
                received ? person.Id : req.AccountId, CounterAccountId: received ? req.AccountId : person.Id,
                Description: string.IsNullOrWhiteSpace(req.Description) ? person.Name : req.Description);
            return (await TransactionCommands.CreateAsync(db, interest, draft, DataSource.Manual, ct))
                .ToHttp(t => Results.Created($"/api/transactions/{t.Id}", new { t.Id }));
        });
    }

    public static async Task<SharingOverviewDto> OverviewAsync(IFinanceDb db, CancellationToken ct)
    {
        var people = await db.Accounts.AsNoTracking()
            .Where(a => a.Kind == AccountKind.Person && a.ArchivedAtUtc == null)
            .OrderBy(a => a.Name).ToListAsync(ct);
        if (people.Count == 0)
        {
            return new SharingOverviewDto(0, 0, []);
        }

        var ids = people.Select(p => p.Id).ToList();
        var balances = await AccountBalances.ComputeAsync(db, people, null, ct);
        var rows = await (from t in db.Transactions.AsNoTracking()
                where ids.Contains(t.AccountId) || (t.CounterAccountId != null && ids.Contains(t.CounterAccountId.Value))
                join e in db.Transactions.AsNoTracking() on t.SharedFromId equals e.Id into es
                from e in es.DefaultIfEmpty()
                join ea in db.Accounts on e.AccountId equals ea.Id into eas
                from ea in eas.DefaultIfEmpty()
                join c in db.Categories on t.CategoryId equals c.Id into cs
                from c in cs.DefaultIfEmpty()
                join oa in db.Accounts on t.CounterAccountId equals oa.Id into oas
                from oa in oas.DefaultIfEmpty()
                join sa in db.Accounts on t.AccountId equals sa.Id
                orderby t.OccurredOn, t.CreatedAtUtc
                select new
                {
                    t.Id, t.AccountId, t.CounterAccountId, t.Type, t.IsRefund, t.SharedFromId, t.OccurredOn,
                    t.BaseAmount, t.Description, Category = c == null ? null : c.Name,
                    PaidFrom = ea == null ? null : ea.Name, ExpenseAmount = e == null ? (decimal?)null : e.BaseAmount,
                    Counter = oa == null ? null : oa.Name, Source = sa.Name,
                })
            .ToListAsync(ct);

        var result = new List<PersonDto>();
        foreach (var person in people)
        {
            var mine = rows.Where(r => r.AccountId == person.Id || r.CounterAccountId == person.Id).ToList();
            var movements = mine.Select(r =>
            {
                var (kind, signed, other) = r switch
                {
                    { SharedFromId: not null } => ("share", r.BaseAmount, r.PaidFrom),
                    { Type: TransactionType.Transfer } when r.AccountId == person.Id => ("paidYou", -r.BaseAmount, r.Counter),
                    { Type: TransactionType.Transfer } => ("youPaid", r.BaseAmount, r.Source),
                    { Type: TransactionType.Expense, IsRefund: false } => ("theyPaid", -r.BaseAmount, null),
                    _ => ("other", r.AccountId == person.Id ? r.BaseAmount : -r.BaseAmount, null),
                };
                return (Row: r, Movement: new PersonMovementDto(r.Id, r.OccurredOn, kind, signed, r.Description, other));
            }).ToList();

            // What is still owed: payments cover the oldest debts first (an opening balance, then shares and money
            // lent, by date), so only the newest stay open.
            var balance = balances[person.Id];
            var opening = Math.Max(0, person.OpeningBalance);
            var owedItems = movements.Where(m => m.Movement.Amount > 0).ToList();
            var covered = Math.Max(0, opening + owedItems.Sum(m => m.Movement.Amount) - balance);
            covered -= Math.Min(covered, opening);
            var open = new List<OpenShareDto>();
            DateOnly? since = opening > 0 && covered == 0 && balance > 0 ? person.OpeningBalanceOn : null;
            foreach (var (row, movement) in owedItems)
            {
                var paid = Math.Min(covered, movement.Amount);
                covered -= paid;
                var outstanding = movement.Amount - paid;
                if (outstanding <= 0)
                {
                    continue;
                }

                since ??= row.OccurredOn;
                if (row.SharedFromId is { } expenseId)
                {
                    open.Add(new OpenShareDto(row.Id, expenseId, row.OccurredOn, row.Description, row.Category,
                        row.PaidFrom ?? "", row.ExpenseAmount ?? movement.Amount, movement.Amount, outstanding));
                }
            }

            result.Add(new PersonDto(person.Id, person.Name, balance, balance > 0 ? since : null,
                open.OrderByDescending(o => o.On).ToList(),
                movements.Select(m => m.Movement).OrderByDescending(m => m.On).Take(RecentCount).ToList()));
        }

        return new SharingOverviewDto(result.Where(p => p.Balance > 0).Sum(p => p.Balance),
            -result.Where(p => p.Balance < 0).Sum(p => p.Balance), result);
    }
}
