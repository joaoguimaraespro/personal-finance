using SharedKernel;

namespace Finance.Application.Abstractions;

/// <summary>Money a broker sync reports as deposited into one account in one month, net of withdrawals, in EUR.</summary>
public sealed record BrokerDepositMonth(Guid AccountId, YearMonth Period, decimal NetDeposits);

/// <summary>
/// Deposits and withdrawals that broker syncs recorded (Trading 212, IBKR, Binance), per account and month. Defined
/// here so reporting can count them towards the monthly allocation; implemented by the investments module, which
/// owns the synced data. Hand-entered crypto is not included: it has no real deposits.
/// </summary>
public interface IBrokerDeposits
{
    Task<IReadOnlyList<BrokerDepositMonth>> MonthlyAsync(YearMonth from, YearMonth to, CancellationToken ct);
}
