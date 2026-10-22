using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Application.Interest;
using Finance.Application.Recurring;
using Finance.Application.Transactions;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.BrokerMovements;

/// <summary>A recurring expense waiting to be confirmed that the card payment may be.</summary>
public sealed record RecurringCandidateDto(Guid ExpectedId, string Name, DateOnly DueOn, decimal Amount,
    string Currency, bool Shared);

/// <summary>An expense already recorded (on another account) that the card payment may be.</summary>
/// <param name="Recurring">A confirmed occurrence of a recurring item.</param>
public sealed record ExpenseCandidateDto(Guid TransactionId, DateOnly On, string? Description, string? CategoryKey,
    string? CategoryName, decimal Amount, string Account, bool Recurring, bool Shared);

public sealed record CardPaymentCandidatesDto(IReadOnlyList<RecurringCandidateDto> Recurring,
    IReadOnlyList<ExpenseCandidateDto> Expenses);

/// <summary>
/// What the card payment was, one of: a recurring occurrence to confirm (<see cref="ExpectedId"/>), an expense already
/// recorded on another account (<see cref="TransactionId"/>), or a new expense (<see cref="CategoryId"/>).
/// </summary>
public sealed record CardPaymentRequest(Guid? ExpectedId, Guid? TransactionId, Guid? CategoryId, string? Description);

public static class CardPaymentErrors
{
    public static readonly Error WithdrawalOnly = Error.Validation("CardPayment.Movement",
        "Only money that left the broker can be a card payment.");

    public static readonly Error OneChoice = Error.Validation("CardPayment.Choice",
        "Choose a recurring item, an expense already recorded, or a category for a new expense.");

    public static readonly Error NotAnExpense = Error.Validation("CardPayment.Transaction",
        "Only an expense can be a card payment.");

    public static readonly Error AmountMismatch = Error.Validation("CardPayment.Amount",
        "That bill is too far from what the card paid.");
}

/// <summary>
/// A withdrawal a broker sync reported can be money spent with the broker's card. The expense is then paid from the
/// broker account (the other accounts keep their balance) and linked to the withdrawal; the month counts it as an
/// expense. It spends cash waiting at the broker, and only the part already counted as invested comes off that
/// figure. A recurring bill keeps its own amount and its shares.
/// </summary>
public static class CardPayments
{
    /// <summary>How far from the withdrawal's day a recurring occurrence or an expense is offered.</summary>
    public const int WindowDays = 10;

    public static void MapCardPayments(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/card-payment", async (Guid id, IFinanceDb db, IBrokerDeposits deposits,
                CancellationToken ct) =>
            await deposits.FindAsync(id, ct) is { Amount: < 0 } m
                ? Results.Ok(await CandidatesAsync(db, m, ct))
                : ResultHttp.Problem(BrokerMovementErrors.NotFound));

        group.MapPost("/{id:guid}/card-payment", async (Guid id, CardPaymentRequest req, IFinanceDb db,
                InterestAccrualService interest, IBrokerDeposits deposits, TimeProvider clock, CancellationToken ct) =>
            (await RecordAsync(db, interest, deposits, id, req, clock.GetUtcNow(), ct))
            .ToHttp(t => Results.Ok(new { t.Id })));
    }

    /// <summary>
    /// The closest in amount first, then in date. A recurring bill is offered only in euros and within 20% of what
    /// the card paid, the same band as an expense already recorded.
    /// </summary>
    public static async Task<CardPaymentCandidatesDto> CandidatesAsync(IFinanceDb db, BrokerMovement m,
        CancellationToken ct)
    {
        var amount = Math.Abs(m.BaseAmount);
        var (low, high) = (amount * 0.8m, amount * 1.2m);
        var (first, last) = (m.OccurredOn.AddDays(-WindowDays), m.OccurredOn.AddDays(WindowDays));
        double Distance(decimal a, DateOnly on) =>
            (double)Math.Abs(a - amount) * 100 + Math.Abs(on.DayNumber - m.OccurredOn.DayNumber);

        var due = await (
                from e in db.ExpectedTransactions.AsNoTracking()
                join r in db.RecurringTransactions.AsNoTracking() on e.RecurringTransactionId equals r.Id
                where e.Status == ExpectedStatus.Pending && r.Type == TransactionType.Expense &&
                      e.DueOn >= first && e.DueOn <= last && e.Currency == Currency.Base &&
                      e.Amount >= low && e.Amount <= high
                select new { e.Id, r.Name, e.DueOn, e.Amount, e.Currency, Shared = r.Shares.Any() })
            .ToListAsync(ct);

        var brokers = db.Accounts.Where(a => a.Kind == Domain.Accounts.AccountKind.Broker).Select(a => a.Id);
        var expenses = await (
                from t in db.Transactions.AsNoTracking()
                join a in db.Accounts.AsNoTracking() on t.AccountId equals a.Id
                where t.Type == TransactionType.Expense && t.SharedFromId == null && !t.IsRefund &&
                      !brokers.Contains(t.AccountId) && t.OccurredOn >= first && t.OccurredOn <= last &&
                      t.BaseAmount >= low && t.BaseAmount <= high
                select new
                {
                    t.Id, t.OccurredOn, t.Description, t.CategoryId, t.BaseAmount, Account = a.Name,
                    Recurring = t.ExpectedTransactionId != null,
                    Shared = db.Transactions.Any(s => s.SharedFromId == t.Id),
                })
            .ToListAsync(ct);
        var categoryIds = expenses.Where(e => e.CategoryId != null).Select(e => e.CategoryId!.Value).Distinct().ToList();
        var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.Key, c.Name }, ct);

        return new CardPaymentCandidatesDto(
            due.OrderBy(d => Distance(d.Amount, d.DueOn)).Take(5)
                .Select(d => new RecurringCandidateDto(d.Id, d.Name, d.DueOn, d.Amount, d.Currency, d.Shared)).ToList(),
            expenses.OrderBy(e => Distance(e.BaseAmount, e.OccurredOn)).Take(5)
                .Select(e =>
                {
                    var category = e.CategoryId is { } c ? categories.GetValueOrDefault(c) : null;
                    return new ExpenseCandidateDto(e.Id, e.OccurredOn, e.Description, category?.Key, category?.Name,
                        e.BaseAmount, e.Account, e.Recurring, e.Shared);
                }).ToList());
    }

    public static async Task<Result<Transaction>> RecordAsync(IFinanceDb db, InterestAccrualService interest,
        IBrokerDeposits deposits, Guid id, CardPaymentRequest req, DateTimeOffset now, CancellationToken ct)
    {
        var movement = await deposits.FindAsync(id, ct);
        if (movement is null)
        {
            return BrokerMovementErrors.NotFound;
        }

        if (movement.Amount >= 0)
        {
            return CardPaymentErrors.WithdrawalOnly;
        }

        if (movement.RecordedTransactionId is { } recorded && await db.Transactions.AnyAsync(t => t.Id == recorded, ct))
        {
            return BrokerMovementErrors.AlreadyRecorded;
        }

        if (new object?[] { req.ExpectedId, req.TransactionId, req.CategoryId }.Count(x => x is not null) != 1)
        {
            return CardPaymentErrors.OneChoice;
        }

        var amount = Math.Abs(movement.BaseAmount);
        Result<Transaction> result;
        if (req.ExpectedId is { } expected)
        {
            var bill = await db.ExpectedTransactions.AsNoTracking().Where(e => e.Id == expected)
                .Select(e => new { e.Amount, e.Currency }).FirstOrDefaultAsync(ct);
            if (bill is null)
            {
                return RecurringEndpoints.ExpectedNotFound;
            }

            // Same band as the candidates. The bill keeps its amount; the card only places the payment.
            if (bill.Currency != Currency.Base || bill.Amount < amount * 0.8m || bill.Amount > amount * 1.2m)
            {
                return CardPaymentErrors.AmountMismatch;
            }

            result = await RecurringCommands.ConfirmAsync(db, expected,
                new ConfirmExpectedRequest(null, movement.OccurredOn, movement.AccountId, null, null), now, ct,
                cardPayment: true);
        }
        else if (req.TransactionId is { } existing)
        {
            result = await MoveAsync(db, interest, existing, movement.AccountId, ct);
        }
        else
        {
            result = await TransactionCommands.CreateAsync(db, interest,
                new TransactionDraft(TransactionType.Expense, movement.OccurredOn, amount, Currency.Base,
                    movement.AccountId, req.CategoryId, Description: req.Description?.Trim() is { Length: > 0 } d
                        ? d
                        : null),
                DataSource.Manual, ct, cardPayment: true);
        }

        if (result.IsSuccess)
        {
            await deposits.ResolveAsync(id, result.Value.Id, ct);
        }

        return result;
    }

    /// <summary>An expense first booked on another account was this card payment: it is paid from the broker.</summary>
    private static async Task<Result<Transaction>> MoveAsync(IFinanceDb db, InterestAccrualService interest, Guid id,
        Guid broker, CancellationToken ct)
    {
        var transaction = await db.Transactions.FindAsync([id], ct);
        if (transaction is null)
        {
            return TransactionErrors.NotFound;
        }

        if (transaction.Type != TransactionType.Expense || transaction.IsShare)
        {
            return CardPaymentErrors.NotAnExpense;
        }

        if (TransactionEndpoints.ReadOnlyError(transaction.Source) is { } readOnly)
        {
            return readOnly;
        }

        var before = transaction.AccountId;
        transaction.MoveTo(broker);
        await db.SaveChangesAsync(ct);
        await interest.TryRecalculateAsync([before], ct);
        return transaction;
    }
}
