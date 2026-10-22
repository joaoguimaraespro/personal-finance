using SharedKernel;

namespace Finance.Application.Abstractions;

/// <summary>
/// Money a broker sync reports as deposited into one account in one month, net of withdrawals, in EUR. Only movements
/// counted as they are (history from before the owner reviews them); reviewed ones are <see cref="BrokerMovement"/>s.
/// </summary>
public sealed record BrokerDepositMonth(Guid AccountId, YearMonth Period, decimal NetDeposits);

public enum BrokerMovementState
{
    /// <summary>Waits for the owner to say which account the money came from (or went to).</summary>
    Pending = 0,

    /// <summary>Recorded in the ledger as <see cref="BrokerMovement.RecordedTransactionId"/>.</summary>
    Recorded = 1,

    /// <summary>Needs no entry (money moved between brokers, or already recorded by hand).</summary>
    Ignored = 2,
}

/// <summary>A synced deposit (positive) or withdrawal (negative) on a broker account that the owner reviews.</summary>
/// <param name="Kind">What the broker called it (e.g. Trading 212's <c>DEPOSIT</c> or <c>TRANSFER</c>).</param>
public sealed record BrokerMovement(Guid Id, Guid AccountId, decimal Amount, string Currency, decimal BaseAmount,
    DateOnly OccurredOn, string? Kind, BrokerMovementState State, Guid? RecordedTransactionId);

/// <summary>What one broker account's synced trades took out of its cash on one day, in EUR: buys net of sells.</summary>
public sealed record BrokerTradeDay(Guid AccountId, DateOnly On, decimal NetBought);

/// <summary>
/// Deposits and withdrawals that broker syncs recorded (Trading 212, IBKR, Binance). Defined here so the ledger can
/// count them towards the monthly allocation and ask where recent ones came from; implemented by the investments
/// module, which owns the synced data. Hand-entered crypto is not included: it has no real deposits.
/// </summary>
public interface IBrokerDeposits
{
    Task<IReadOnlyList<BrokerDepositMonth>> MonthlyAsync(YearMonth from, YearMonth to, CancellationToken ct);

    /// <summary>Movements the owner reviews (pending, recorded or ignored) between the two days, oldest first.</summary>
    Task<IReadOnlyList<BrokerMovement>> ReviewableAsync(DateOnly from, DateOnly to, CancellationToken ct);

    Task<BrokerMovement?> FindAsync(Guid id, CancellationToken ct);

    /// <summary>Links the movement to the transaction that records it, or marks it as needing none (null).</summary>
    Task<bool> ResolveAsync(Guid id, Guid? transactionId, CancellationToken ct);

    /// <summary>Puts a recorded or ignored movement back to pending (its transaction, if any, stays).</summary>
    Task<bool> ReopenAsync(Guid id, CancellationToken ct);

    /// <summary>Synced buys net of sells per broker account and day, between the two days.</summary>
    Task<IReadOnlyList<BrokerTradeDay>> NetBuysAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
