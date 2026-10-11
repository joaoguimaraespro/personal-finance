using System.Globalization;
using Integrations.Application.Contracts;
using Investments.Application.Sync;
using Investments.Domain;

namespace Integrations.Infrastructure.Binance;

/// <summary>
/// Binance (Spot + Simple Earn), read-only. Each coin is one position: spot balance plus flexible and locked Earn.
/// Earn rewards are income, like dividends, valued in EUR at the day's close. Positions and prices are in EUR; the
/// cost of a coin is a moving average over the last <see cref="CostHistoryDays"/> days of everything that brought it in
/// or took it out: fills on EUR and dollar-stablecoin pairs, card purchases, Convert orders, Earn rewards and coins
/// received or sent (valued at the day's close). A coin with no such history keeps the price of its first sync as cost.
/// </summary>
internal sealed class BinanceProvider(BinanceClient client, TimeProvider clock) : IInvestmentProvider
{
    /// <summary>How far back the first sync reads Earn rewards.</summary>
    public const int FirstSyncDays = 180;

    /// <summary>
    /// How far back a coin's cost is rebuilt: 8 windows of 89 days, the longest the coin deposit/withdrawal history
    /// accepts (the withdrawal history weighs 18,000 of Binance's 180,000 per minute).
    /// </summary>
    public const int CostHistoryDays = 8 * CoinWindowDays;

    private const int WindowDays = 30;
    private const int CoinWindowDays = 89;
    private static readonly string[] FlexibleRewardTypes = ["REALTIME", "BONUS", "REWARDS"];

    private sealed record Holdings(Dictionary<string, decimal> Spot, Dictionary<string, decimal> Earn,
        Dictionary<string, decimal> Prices);

    private Holdings? _holdings;
    private List<BinanceFiatPayment>? _payments;
    private List<(string Kind, string Asset, decimal Amount, long Time)>? _rewards;
    private Dictionary<DateOnly, decimal>? _usdPerEur;
    private readonly Dictionary<string, Dictionary<DateOnly, decimal>> _closes = new(StringComparer.Ordinal);

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

        var events = coins.ToDictionary(c => c.Asset, _ => new List<CostEvent>(), StringComparer.Ordinal);
        var latestUsdPerEur = h.Prices.GetValueOrDefault("EURUSDT");
        var usdCloses = await UsdPerEurClosesAsync(h, ct);
        foreach (var c in coins)
        {
            var fills = new List<(string, BinanceFill)>();
            foreach (var quote in BinanceValuation.CostQuotes.Where(q => h.Prices.ContainsKey(c.Asset + q)))
            {
                fills.AddRange((await client.FillsAsync(c.Asset + quote, ct)).Select(f => (quote, f)));
            }

            events[c.Asset].AddRange(BinanceValuation.FillEvents(fills, usdCloses, latestUsdPerEur));
        }

        foreach (var p in await PaymentsAsync(ct))
        {
            if (IsDone(p.Status) && p.ObtainAmount > 0 && p.CryptoCurrency is { } coin && events.TryGetValue(coin, out var list) &&
                await EurValueAsync(p.FiatCurrency, p.SourceAmount, p.CreateTime, h, ct) is { } paid)
            {
                list.Add(CostEvent.In(p.CreateTime, p.ObtainAmount, paid));
            }
        }

        var from = now.AddDays(-CostHistoryDays);
        for (var start = from; start < now; start = start.AddDays(WindowDays))
        {
            var end = start.AddDays(WindowDays) < now ? start.AddDays(WindowDays) : now;
            foreach (var x in await client.ConversionsAsync(start, end, ct))
            {
                if (x.OrderStatus is not "SUCCESS")
                {
                    continue;
                }

                if (events.TryGetValue(x.FromAsset, out var given))
                {
                    given.Add(CostEvent.Out(x.CreateTime, x.FromAmount));
                }

                // What was given is the price paid; a coin Binance cannot value is priced by what was received.
                if (events.TryGetValue(x.ToAsset, out var got) &&
                    (await EurValueAsync(x.FromAsset, x.FromAmount, x.CreateTime, h, ct) ??
                     await EurValueAsync(x.ToAsset, x.ToAmount, x.CreateTime, h, ct)) is { } value)
                {
                    got.Add(CostEvent.In(x.CreateTime, x.ToAmount, value));
                }
            }
        }

        foreach (var r in await RewardsAsync(ct))
        {
            if (r.Amount > 0 && events.TryGetValue(r.Asset, out var list) &&
                await EurValueAsync(r.Asset, r.Amount, r.Time, h, ct) is { } value)
            {
                list.Add(CostEvent.In(r.Time, r.Amount, value)); // the same value is booked as income
            }
        }

        for (var start = from; start < now; start = start.AddDays(CoinWindowDays))
        {
            var end = start.AddDays(CoinWindowDays) < now ? start.AddDays(CoinWindowDays) : now;
            foreach (var d in await client.CoinsReceivedAsync(start, end, ct))
            {
                // Received from another wallet: what it cost there is unknown, so it costs what it was worth that day.
                if (d.Status is 1 or 6 && d.Amount > 0 && events.TryGetValue(d.Coin, out var list) &&
                    await EurValueAsync(d.Coin, d.Amount, d.InsertTime, h, ct) is { } value)
                {
                    list.Add(CostEvent.In(d.InsertTime, d.Amount, value));
                }
            }

            foreach (var w in await client.CoinsSentAsync(start, end, ct))
            {
                if (w.Status == 6 && events.TryGetValue(w.Coin, out var list) &&
                    DateTime.TryParse(w.ApplyTime, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
                {
                    list.Add(CostEvent.Out(new DateTimeOffset(at, TimeSpan.Zero).ToUnixTimeMilliseconds(),
                        w.Amount + w.TransactionFee));
                }
            }
        }

        return coins.Select(c => BinanceValuation.AverageCost(events[c.Asset]) is { } average
                ? new PositionReport(Security(c.Asset), c.Quantity, average, c.EurPrice, now)
                : new PositionReport(Security(c.Asset), c.Quantity, c.EurPrice, c.EurPrice, now, CostKnown: false))
            .ToList();
    }

    /// <summary>EUR value of an amount of any asset on the day of <paramref name="unixMs"/>; null if Binance has no price.</summary>
    private async Task<decimal?> EurValueAsync(string asset, decimal amount, long unixMs, Holdings h, CancellationToken ct)
    {
        var day = BinanceValuation.DayOf(unixMs);
        var usd = await UsdPerEurClosesAsync(h, ct);
        var latest = h.Prices.GetValueOrDefault("EURUSDT");
        if (asset == "EUR" || BinanceValuation.IsDollar(asset))
        {
            return BinanceValuation.EurValueOn(asset, amount, day, usd, latest, new Dictionary<DateOnly, decimal>());
        }

        if (!_closes.TryGetValue(asset, out var closes))
        {
            closes = BinanceValuation.EurPrice(asset, h.Prices) is null
                ? []
                : await EurClosesAsync(asset, From(CostHistoryDays + 7), h.Prices, ct);
            _closes[asset] = closes;
        }

        return BinanceValuation.EurValueOn(asset, amount, day, usd, latest, closes);
    }

    private async Task<Dictionary<DateOnly, decimal>> UsdPerEurClosesAsync(Holdings h, CancellationToken ct) =>
        _usdPerEur ??= h.Prices.GetValueOrDefault("EURUSDT") > 0
            ? await client.DailyClosesAsync("EURUSDT", DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddYears(-3), ct)
            : [];

    private DateOnly From(int daysAgo) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-daysAgo);

    /// <summary>Crypto bought with fiat over the cost history (one read per sync, shared with the cash flows).</summary>
    private async Task<List<BinanceFiatPayment>> PaymentsAsync(CancellationToken ct) =>
        _payments ??= await client.FiatPurchasesAsync(clock.GetUtcNow().AddDays(-CostHistoryDays), clock.GetUtcNow(), ct);

    /// <summary>Earn rewards over the cost history (one read per sync, shared with the income).</summary>
    private async Task<List<(string Kind, string Asset, decimal Amount, long Time)>> RewardsAsync(CancellationToken ct)
    {
        if (_rewards is not null)
        {
            return _rewards;
        }

        var now = clock.GetUtcNow();
        var rewards = new List<(string Kind, string Asset, decimal Amount, long Time)>();
        for (var start = now.AddDays(-CostHistoryDays); start < now; start = start.AddDays(WindowDays))
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

        return _rewards = rewards;
    }

    /// <summary>
    /// Money in and out in fiat: bank/card deposits, crypto bought directly with fiat, and fiat withdrawals. They are the
    /// account's external flows (returns, and deposits that count towards a linked allocation bucket). Fills only set
    /// the cost of each position and are not reported as trades (dollar pairs do not map to the ledger's currencies).
    /// </summary>
    public async Task<IReadOnlyList<InvestmentTransaction>> GetTransactionsAsync(DateTimeOffset? since,
        CancellationToken ct)
    {
        await HoldingsAsync(ct); // the read-only key check runs before anything else is read
        var now = clock.GetUtcNow();
        var movements = new List<CashMovementReport>();
        var from = since ?? now.AddDays(-FirstSyncDays);
        // One read per direction: the fiat history weighs 45,000 of Binance's 180,000 per minute, and takes any range.
        foreach (var o in await client.FiatOrdersAsync(0, from, now, ct))
        {
            if (IsDone(o.Status) && o.IndicatedAmount > 0)
            {
                movements.Add(Movement("deposit", o.OrderNo, CashMovementType.Deposit, o.IndicatedAmount,
                    o.FiatCurrency, o.CreateTime, $"Deposit ({o.Method ?? "fiat"})"));
            }
        }

        foreach (var o in await client.FiatOrdersAsync(1, from, now, ct))
        {
            if (IsDone(o.Status) && o.IndicatedAmount > 0)
            {
                movements.Add(Movement("withdrawal", o.OrderNo, CashMovementType.Withdrawal, -o.IndicatedAmount,
                    o.FiatCurrency, o.CreateTime, $"Withdrawal ({o.Method ?? "fiat"})"));
            }
        }

        foreach (var p in (await PaymentsAsync(ct)).Where(p => p.CreateTime >= from.ToUnixTimeMilliseconds()))
        {
            if (IsDone(p.Status) && p.SourceAmount > 0)
            {
                movements.Add(Movement("purchase", p.OrderNo, CashMovementType.Deposit, p.SourceAmount,
                    p.FiatCurrency, p.CreateTime, $"Bought {p.CryptoCurrency} with {p.FiatCurrency}"));
            }
        }

        return movements.DistinctBy(m => m.ExternalId).Select(InvestmentTransaction.Of).ToList();
    }

    private static bool IsDone(string? status) => status is "Successful" or "Completed";

    private static CashMovementReport Movement(string kind, string orderNo, CashMovementType type, decimal amount,
        string currency, long time, string description) =>
        new($"binance:fiat:{kind}:{orderNo}", type, amount, currency.ToUpperInvariant(),
            DateTimeOffset.FromUnixTimeMilliseconds(time), description);

    public async Task<IReadOnlyList<DividendReport>> GetDividendsAsync(DateTimeOffset? since, CancellationToken ct)
    {
        var h = await HoldingsAsync(ct);
        var now = clock.GetUtcNow();
        var from = since ?? now.AddDays(-FirstSyncDays);
        var rewards = (await RewardsAsync(ct)).Where(r => r.Time >= from.ToUnixTimeMilliseconds()).ToList();

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
