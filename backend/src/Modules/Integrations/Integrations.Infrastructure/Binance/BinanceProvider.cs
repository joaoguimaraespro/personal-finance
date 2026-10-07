using System.Globalization;
using Integrations.Application.Contracts;
using Investments.Application.Sync;
using Investments.Domain;

namespace Integrations.Infrastructure.Binance;

/// <summary>
/// Binance (Spot + Simple Earn), read-only. Each coin is one position: spot balance plus flexible and locked Earn.
/// Earn rewards are income, like dividends, valued in EUR at the day's close. Positions and prices are in EUR; the
/// cost of a coin comes from its fills on EUR and dollar-stablecoin pairs (unknown for coins deposited, converted or
/// earned: those are valued from the first sync on, with no gain).
/// </summary>
internal sealed class BinanceProvider(BinanceClient client, TimeProvider clock) : IInvestmentProvider
{
    /// <summary>How far back the first sync reads Earn rewards.</summary>
    public const int FirstSyncDays = 180;

    private const int WindowDays = 30;
    private static readonly string[] FlexibleRewardTypes = ["REALTIME", "BONUS", "REWARDS"];

    private sealed record Holdings(Dictionary<string, decimal> Spot, Dictionary<string, decimal> Earn,
        Dictionary<string, decimal> Prices);

    private Holdings? _holdings;

    public BrokerKind Kind => BrokerKind.Binance;

    public async Task<AccountSnapshot> GetAccountSnapshotAsync(CancellationToken ct)
    {
        var h = await HoldingsAsync(ct);
        var cash = h.Spot.GetValueOrDefault("EUR") + h.Earn.GetValueOrDefault("EUR");
        var coins = Coins(h).Sum(c => c.Quantity * c.EurPrice);
        // Binance exposes no valuation history here; the snapshotter records it daily from now on.
        return new AccountSnapshot("EUR", cash, decimal.Round(cash + coins, 2), []);
    }

    public async Task<IReadOnlyList<PositionReport>> GetPositionsAsync(CancellationToken ct)
    {
        var h = await HoldingsAsync(ct);
        var now = clock.GetUtcNow();
        var coins = Coins(h).ToList();
        if (coins.Count == 0)
        {
            return [];
        }

        var usdPerEur = h.Prices.GetValueOrDefault("EURUSDT");
        var usdCloses = usdPerEur > 0 && coins.Any(c => HasDollarPair(c.Asset, h.Prices))
            ? await client.DailyClosesAsync("EURUSDT", DateOnly.FromDateTime(now.UtcDateTime).AddYears(-3), ct)
            : [];
        var positions = new List<PositionReport>();
        foreach (var c in coins)
        {
            var fills = new List<(string, BinanceFill)>();
            foreach (var quote in BinanceValuation.CostQuotes.Where(q => h.Prices.ContainsKey(c.Asset + q)))
            {
                fills.AddRange((await client.FillsAsync(c.Asset + quote, ct)).Select(f => (quote, f)));
            }

            var average = BinanceValuation.AverageCost(fills, usdCloses, usdPerEur) ?? c.EurPrice;
            positions.Add(new PositionReport(Security(c.Asset), c.Quantity, average, c.EurPrice, now));
        }

        return positions;
    }

    /// <summary>Fills only set the cost of each position; they are not reported as trades (dollar pairs).</summary>
    public Task<IReadOnlyList<InvestmentTransaction>> GetTransactionsAsync(DateTimeOffset? since, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InvestmentTransaction>>([]);

    public async Task<IReadOnlyList<DividendReport>> GetDividendsAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var h = await HoldingsAsync(ct);
        var now = clock.GetUtcNow();
        var from = since ?? now.AddDays(-FirstSyncDays);
        var rewards = new List<(string Kind, string Asset, decimal Amount, long Time)>();
        for (var start = from; start < now; start = start.AddDays(WindowDays))
        {
            var end = start.AddDays(WindowDays) < now ? start.AddDays(WindowDays) : now;
            foreach (var type in FlexibleRewardTypes)
            {
                rewards.AddRange((await client.FlexibleRewardsAsync(type, start, end, ct))
                    .Select(r => ("flexible-" + type.ToLowerInvariant(), r.Asset, r.Rewards, r.Time)));
            }

            rewards.AddRange((await client.LockedRewardsAsync(start, end, ct))
                .Select(r => ("locked", r.Asset, r.Amount, r.Time)));
        }

        var dividends = new List<DividendReport>();
        foreach (var group in rewards.Where(r => r.Amount > 0).GroupBy(r => r.Asset))
        {
            var asset = group.Key;
            if (BinanceValuation.EurPrice(asset, h.Prices) is not { } price)
            {
                continue; // not quoted on Binance in EUR or dollars: cannot be valued
            }

            var first = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(group.Min(r => r.Time)).UtcDateTime);
            var closes = await EurClosesAsync(asset, first.AddDays(-7), h.Prices, ct);
            foreach (var r in group)
            {
                var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(r.Time).UtcDateTime);
                var value = BinanceValuation.RewardValue(r.Amount, day, closes, price);
                var id = $"binance:earn:{r.Kind}:{asset}:{r.Time}:{r.Amount.ToString(CultureInfo.InvariantCulture)}";
                dividends.Add(new DividendReport(id, Security(asset), day, null, null, value, "EUR", null, r.Amount));
            }
        }

        return dividends;
    }

    private async Task<Holdings> HoldingsAsync(CancellationToken ct)
    {
        if (_holdings is not null)
        {
            return _holdings;
        }

        await client.EnsureReadOnlyKeyAsync(ct);
        var earn = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var p in await client.FlexiblePositionsAsync(ct))
        {
            earn[p.Asset] = earn.GetValueOrDefault(p.Asset) + p.TotalAmount;
        }

        foreach (var p in await client.LockedPositionsAsync(ct))
        {
            earn[p.Asset] = earn.GetValueOrDefault(p.Asset) + p.Amount;
        }

        var earnAssets = earn.Keys.ToHashSet(StringComparer.Ordinal);
        var spot = (await client.AccountAsync(ct)).Balances
            .Where(b => b.Free + b.Locked > 0 && !BinanceValuation.IsEarnShadow(b.Asset, earnAssets))
            .GroupBy(b => b.Asset)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.Free + b.Locked), StringComparer.Ordinal);
        return _holdings = new Holdings(spot, earn, await client.PricesAsync(ct));
    }

    /// <summary>Every coin held (spot + Earn) that Binance can price in EUR; EUR itself is cash.</summary>
    private static IEnumerable<(string Asset, decimal Quantity, decimal EurPrice)> Coins(Holdings h) =>
        h.Spot.Keys.Union(h.Earn.Keys)
            .Where(a => a != "EUR")
            .Select(a => (Asset: a, Quantity: h.Spot.GetValueOrDefault(a) + h.Earn.GetValueOrDefault(a),
                Price: BinanceValuation.EurPrice(a, h.Prices)))
            .Where(c => c.Quantity > 0 && c.Price is not null)
            .Select(c => (c.Asset, c.Quantity, c.Price!.Value))
            .OrderBy(c => c.Asset, StringComparer.Ordinal);

    private static bool HasDollarPair(string asset, Dictionary<string, decimal> prices) =>
        BinanceValuation.CostQuotes.Skip(1).Any(q => prices.ContainsKey(asset + q));

    /// <summary>Daily EUR closes: the EUR pair, else a dollar pair divided by EURUSDT on the same day.</summary>
    private async Task<Dictionary<DateOnly, decimal>> EurClosesAsync(string asset, DateOnly from,
        Dictionary<string, decimal> prices, CancellationToken ct)
    {
        if (prices.ContainsKey(asset + "EUR"))
        {
            return await client.DailyClosesAsync(asset + "EUR", from, ct);
        }

        var usdPerEur = await client.DailyClosesAsync("EURUSDT", from, ct);
        if (asset is "USDT" or "USDC" or "FDUSD")
        {
            return usdPerEur.Where(x => x.Value > 0).ToDictionary(x => x.Key, x => decimal.Round(1m / x.Value, 8));
        }

        var pair = BinanceValuation.CostQuotes.Skip(1).Select(q => asset + q).FirstOrDefault(prices.ContainsKey);
        if (pair is null)
        {
            return [];
        }

        var usd = await client.DailyClosesAsync(pair, from, ct);
        return usd.Where(x => usdPerEur.GetValueOrDefault(x.Key) > 0)
            .ToDictionary(x => x.Key, x => decimal.Round(x.Value / usdPerEur[x.Key], 8));
    }

    private static SecurityReport Security(string asset) =>
        new(asset, null, asset, null, asset, "EUR", AssetClass.Crypto);
}
