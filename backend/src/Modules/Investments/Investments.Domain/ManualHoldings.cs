using SharedKernel;

namespace Investments.Domain;

public enum RewardKind
{
    Staking = 0,
    Earn = 1,
    Airdrop = 2,
    Other = 3,
}

/// <summary>
/// A coin the user holds outside any connected broker (an exchange account, a cold wallet), entered by hand:
/// quantity and average buy price in EUR. Prices come from the public market-data provider. The holding is the
/// source of truth; the portfolio records it feeds (position, a single opening lot, rewards) are derived from it
/// and rewritten on every change, so editing the quantity or price simply updates the holding.
/// </summary>
public sealed class ManualHolding : Entity, IAuditable
{
    public const int MaxNotes = 500;

    private readonly List<HoldingReward> _rewards = [];

    private ManualHolding() { }

    /// <summary>The location (finance account of kind Broker, e.g. "Binance" or "Cold wallet").</summary>
    public Guid AccountId { get; private set; }

    public Guid SecurityId { get; private set; }

    /// <summary>Bought quantity, without rewards.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Average buy price per coin, in EUR.</summary>
    public decimal AveragePrice { get; private set; }

    /// <summary>When the coins were (first) bought; the cost is counted as invested on that day.</summary>
    public DateOnly HeldSince { get; private set; }

    public string? Notes { get; private set; }
    public IReadOnlyList<HoldingReward> Rewards => _rewards;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public decimal RewardQuantity => _rewards.Sum(r => r.Quantity);
    public decimal TotalQuantity => Quantity + RewardQuantity;

    /// <summary>What was paid (EUR). Rewards cost nothing.</summary>
    public decimal Cost => decimal.Round(Quantity * AveragePrice, 4);

    /// <summary>Average cost over everything held, rewards included at zero cost.</summary>
    public decimal AverageCostIncludingRewards => TotalQuantity == 0 ? 0 : decimal.Round(Cost / TotalQuantity, 8);

    public static ManualHolding Create(Guid accountId, Guid securityId, decimal quantity, decimal averagePrice,
        DateOnly heldSince, string? notes, DateTimeOffset now) => new()
    {
        AccountId = accountId,
        SecurityId = securityId,
        Quantity = quantity,
        AveragePrice = averagePrice,
        HeldSince = heldSince,
        Notes = Clean(notes),
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
    };

    public void Update(Guid accountId, decimal quantity, decimal averagePrice, DateOnly heldSince, string? notes,
        DateTimeOffset now)
    {
        AccountId = accountId;
        Quantity = quantity;
        AveragePrice = averagePrice;
        HeldSince = heldSince;
        Notes = Clean(notes);
        UpdatedAtUtc = now;
    }

    public HoldingReward AddReward(DateOnly receivedOn, decimal quantity, RewardKind kind, string? note,
        DateTimeOffset now)
    {
        var reward = new HoldingReward(Id, receivedOn, quantity, kind, Clean(note));
        _rewards.Add(reward);
        UpdatedAtUtc = now;
        return reward;
    }

    public bool RemoveReward(Guid rewardId, DateTimeOffset now)
    {
        var reward = _rewards.FirstOrDefault(r => r.Id == rewardId);
        if (reward is null)
        {
            return false;
        }

        _rewards.Remove(reward);
        UpdatedAtUtc = now;
        return true;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = new string(text.Trim().Where(c => !char.IsControl(c) || c == '\n').ToArray());
        return cleaned.Length > MaxNotes ? cleaned[..MaxNotes] : cleaned;
    }
}

/// <summary>
/// Coins received for free (staking, Earn, airdrop): they add to the holding at zero cost and count as income,
/// valued at that day's closing price.
/// </summary>
public sealed class HoldingReward : Entity
{
    private HoldingReward() { }

    internal HoldingReward(Guid holdingId, DateOnly receivedOn, decimal quantity, RewardKind kind, string? note)
    {
        HoldingId = holdingId;
        ReceivedOn = receivedOn;
        Quantity = quantity;
        Kind = kind;
        Note = note;
    }

    public Guid HoldingId { get; private set; }
    public DateOnly ReceivedOn { get; private set; }
    public decimal Quantity { get; private set; }
    public RewardKind Kind { get; private set; }
    public string? Note { get; private set; }
}
