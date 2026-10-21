using Finance.Application.Abstractions;
using SharedKernel.Notifications;

namespace Finance.Application.BrokerMovements;

/// <summary>
/// Recent broker deposits and withdrawals that wait for the owner to say which account the money came from (or went
/// to), for brokers that count towards an allocation bucket. Read-only: recording or ignoring one resolves it.
/// </summary>
public sealed class BrokerMovementNotificationSource(IFinanceDb db, IBrokerDeposits? deposits = null)
    : INotificationSource
{
    public async Task<IReadOnlyList<NotificationItem>> GetAsync(DateOnly today, CancellationToken ct)
    {
        if (deposits is null)
        {
            return [];
        }

        var movements = await BrokerMovementEndpoints.ListAsync(db, deposits,
            today.AddDays(-BrokerMovementEndpoints.RecentDays), today, ct);
        return movements.Where(m => m.State == BrokerMovementState.Pending && m.BucketId is not null)
            .Take(Notifications.FinanceNotificationSource.MaxPerKind)
            .Select(m => new NotificationItem(
                $"broker-movement:{m.Id}",
                NotificationKind.BrokerMovement,
                NotificationSeverity.Info,
                m.On,
                "/portfolio",
                m.Id,
                new Dictionary<string, object?>
                {
                    ["broker"] = m.Broker, ["brokerAccountId"] = m.AccountId, ["bucketId"] = m.BucketId,
                    ["deposit"] = m.Amount > 0, ["amount"] = Math.Abs(m.Amount), ["currency"] = m.Currency,
                    ["baseAmount"] = Math.Abs(m.BaseAmount), ["on"] = m.On.ToString("yyyy-MM-dd"),
                },
                // Money out of a broker went to one of the owner's accounts, or was spent with its card.
                m.Amount > 0 ? ["record", "skip"] : ["record", "card", "skip"]))
            .ToList();
    }
}
