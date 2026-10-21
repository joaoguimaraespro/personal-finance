using Finance.Application.Abstractions;
using Finance.Application.Http;
using Finance.Application.Interest;
using Finance.Application.Transactions;
using Finance.Domain.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Finance.Application.BrokerMovements;

/// <summary>A synced deposit or withdrawal as the owner reviews it: positive amounts went into the broker.</summary>
/// <param name="State">
/// Pending also when it was recorded but that transaction has since been deleted: the question comes back.
/// </param>
/// <param name="CardPayment">Recorded as an expense paid with the broker's card, not money moved to an account.</param>
public sealed record BrokerMovementDto(Guid Id, Guid AccountId, string Broker, Guid? BucketId, DateOnly On,
    decimal Amount, string Currency, decimal BaseAmount, string? Kind, BrokerMovementState State,
    Guid? TransactionId, bool CardPayment = false);

public static class BrokerMovementErrors
{
    public static readonly Error NotFound = Error.NotFound("BrokerMovement.NotFound", "Deposit or withdrawal not found.");

    public static readonly Error AlreadyRecorded = Error.Conflict("BrokerMovement.Recorded",
        "This deposit or withdrawal is already recorded.");

    public static readonly Error InvestmentOnly = Error.Validation("BrokerMovement.Type",
        "A broker deposit is recorded as an investment (a purchase), a withdrawal as a sale.");
}

/// <summary>
/// Synced broker deposits and withdrawals wait for the owner to say which of their accounts the money came from (or
/// went to): recording one creates that investment entry, with the broker as its counter account, so the source
/// account's balance and the month's allocation both reflect it. Until then the month shows it as pending.
/// </summary>
public static class BrokerMovementEndpoints
{
    /// <summary>How far back the list (and the notification centre) looks.</summary>
    public const int RecentDays = 120;

    public static void MapBrokerMovements(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/broker-movements").WithTags("Broker movements");

        group.MapGet("/", async (IFinanceDb db, IBrokerDeposits deposits, TimeProvider clock, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            return Results.Ok((await ListAsync(db, deposits, today.AddDays(-RecentDays), today, ct)).Reverse());
        });

        group.MapPost("/{id:guid}/record", async (Guid id, TransactionRequest req, IFinanceDb db,
                InterestAccrualService interest, IBrokerDeposits deposits, CancellationToken ct) =>
            (await RecordAsync(db, interest, deposits, id, req, ct))
            .ToHttp(t => Results.Created($"/api/transactions/{t.Id}", new { t.Id })))
            .Validate<TransactionRequest>();

        group.MapCardPayments();

        group.MapPost("/{id:guid}/ignore", async (Guid id, IBrokerDeposits deposits, CancellationToken ct) =>
            await deposits.ResolveAsync(id, null, ct)
                ? Results.NoContent()
                : ResultHttp.Problem(BrokerMovementErrors.NotFound));
    }

    /// <summary>The reviewed movements between the two days, oldest first, with the state they really have.</summary>
    public static async Task<IReadOnlyList<BrokerMovementDto>> ListAsync(IFinanceDb db, IBrokerDeposits deposits,
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var movements = await deposits.ReviewableAsync(from, to, ct);
        if (movements.Count == 0)
        {
            return [];
        }

        var accountIds = movements.Select(m => m.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
        var txIds = movements.Where(m => m.RecordedTransactionId is not null)
            .Select(m => m.RecordedTransactionId!.Value).ToList();
        var existing = txIds.Count == 0
            ? []
            : await db.Transactions.AsNoTracking().Where(t => txIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Type, ct);

        return movements.Where(m => accounts.ContainsKey(m.AccountId)).Select(m =>
        {
            var account = accounts[m.AccountId];
            var state = m.State == BrokerMovementState.Recorded && !existing.ContainsKey(m.RecordedTransactionId!.Value)
                ? BrokerMovementState.Pending
                : m.State;
            var recorded = state == BrokerMovementState.Recorded;
            return new BrokerMovementDto(m.Id, m.AccountId, account.Name, account.AllocationBucketId, m.OccurredOn,
                m.Amount, m.Currency, m.BaseAmount, m.Kind, state, recorded ? m.RecordedTransactionId : null,
                recorded && existing[m.RecordedTransactionId!.Value] == TransactionType.Expense);
        }).ToList();
    }

    public static async Task<Result<Transaction>> RecordAsync(IFinanceDb db, InterestAccrualService interest,
        IBrokerDeposits deposits, Guid id, TransactionRequest req, CancellationToken ct)
    {
        var movement = await deposits.FindAsync(id, ct);
        if (movement is null)
        {
            return BrokerMovementErrors.NotFound;
        }

        if (movement.RecordedTransactionId is { } recorded &&
            await db.Transactions.AnyAsync(t => t.Id == recorded, ct))
        {
            return BrokerMovementErrors.AlreadyRecorded;
        }

        if (!TransactionTypes.IsInvestment(req.Type))
        {
            return BrokerMovementErrors.InvestmentOnly;
        }

        // The broker is always the other side; the bucket defaults to the one the broker counts towards.
        var bucket = req.BucketId ?? await db.Accounts.AsNoTracking().Where(a => a.Id == movement.AccountId)
            .Select(a => a.AllocationBucketId).FirstOrDefaultAsync(ct);
        var draft = req.ToDraft() with { CounterAccountId = movement.AccountId, BucketId = bucket };
        var created = await TransactionCommands.CreateAsync(db, interest, draft, DataSource.Manual, ct);
        if (created.IsSuccess)
        {
            await deposits.ResolveAsync(id, created.Value.Id, ct);
        }

        return created;
    }
}
