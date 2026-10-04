using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Investments.Application.Abstractions;
using Investments.Application.Calculations;
using Investments.Application.Portfolio;
using Investments.Application.Prices;
using Investments.Application.Sync;
using Investments.Domain;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Investments.Application.Manual;

public sealed record CoinDto(string Id, string Symbol, string Name);

public sealed record CreateManualHoldingRequest(string CoinId, string Symbol, string Name, decimal Quantity,
    decimal AveragePrice, string? Location, string? Notes, DateOnly? HeldSince);

public sealed record UpdateManualHoldingRequest(decimal Quantity, decimal AveragePrice, string? Location,
    string? Notes, DateOnly? HeldSince);

public sealed record RewardRequest(decimal Quantity, DateOnly? ReceivedOn, RewardKind Kind, string? Note);

public sealed record RewardDto(Guid Id, DateOnly ReceivedOn, decimal Quantity, RewardKind Kind, string? Note,
    decimal? ValueBase);

public sealed record ManualHoldingDto(Guid Id, Guid AccountId, string Location, Guid SecurityId, string CoinId,
    string Symbol, string Name, decimal Quantity, decimal AveragePrice, DateOnly HeldSince, string? Notes,
    decimal RewardQuantity, IReadOnlyList<RewardDto> Rewards);

/// <param name="PricesEnabled">False when <c>MarketData:Provider</c> is <c>none</c>: coins cannot be priced.</param>
public sealed record ManualHoldingsView(IReadOnlyList<ManualHoldingDto> Holdings, IReadOnlyList<string> Locations,
    bool PricesEnabled);

/// <summary>
/// Coins held outside connected brokers, entered by hand. Each location ("Binance", "Cold wallet") is a read-only
/// finance account of kind Broker, like a broker connection, so the portfolio, net worth and dashboards include it
/// without special cases. The holding is the source of truth; its position, its opening lot (one buy and the money
/// put in, on the day it is held since) and its rewards are rewritten from it on every change. A location's history
/// is rebuilt from public daily closes, so an edit never leaves a jump in the performance chart.
/// </summary>
public sealed class ManualHoldingService(
    IInvestmentsDb db,
    IFinanceDb finance,
    IPriceHistorySource source,
    PriceHistoryService prices,
    PortfolioSnapshotter snapshotter,
    HistoryRebuildQueue history,
    TimeProvider clock)
{
    /// <summary>Institution stored on location accounts; marks them as manual (no connection behind them).</summary>
    public const string Institution = "Manual (crypto)";

    public const string DefaultLocation = "Crypto";

    /// <summary>Prices are refreshed at most this often.</summary>
    public static readonly TimeSpan PriceMaxAge = TimeSpan.FromMinutes(15);

    /// <summary>Days re-downloaded on each refresh (yesterday's close becomes final the day after).</summary>
    private const int RefreshDays = 10;

    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    private DateTimeOffset Now => clock.GetUtcNow();
    private DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    public async Task<IReadOnlyList<CoinDto>> SearchAsync(string? query, CancellationToken ct)
    {
        var text = query?.Trim() ?? "";
        if (text.Length < 2 || !source.Enabled)
        {
            return [];
        }

        return (await source.SearchCoinsAsync(text, ct)).Take(10).Select(c => new CoinDto(c.Id, c.Symbol, c.Name))
            .ToList();
    }

    public async Task<ManualHoldingsView> ListAsync(CancellationToken ct)
    {
        var holdings = await db.ManualHoldings.AsNoTracking().Include(h => h.Rewards).ToListAsync(ct);
        var accounts = await LocationAccountsAsync(ct);
        var securityIds = holdings.Select(h => h.SecurityId).Distinct().ToList();
        var securities = await db.Securities.AsNoTracking().Where(s => securityIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var coinIds = await db.BrokerSymbols.AsNoTracking()
            .Where(b => b.Source == DataSource.Manual && securityIds.Contains(b.SecurityId))
            .ToDictionaryAsync(b => b.SecurityId, b => b.Symbol, ct);
        var rewardValues = (await db.Dividends.AsNoTracking()
                .Where(d => d.Source == DataSource.Manual && d.ExternalId.StartsWith("manual:"))
                .Select(d => new { d.ExternalId, d.NetBaseAmount }).ToListAsync(ct))
            .ToDictionary(d => d.ExternalId, d => d.NetBaseAmount);

        var list = holdings
            .Select(h =>
            {
                var security = securities[h.SecurityId];
                return new ManualHoldingDto(h.Id, h.AccountId,
                    accounts.GetValueOrDefault(h.AccountId)?.Name ?? DefaultLocation, h.SecurityId,
                    coinIds.GetValueOrDefault(h.SecurityId, security.Symbol), security.Symbol, security.Name,
                    h.Quantity, h.AveragePrice, h.HeldSince, h.Notes, h.RewardQuantity,
                    h.Rewards.OrderByDescending(r => r.ReceivedOn).Select(r => new RewardDto(r.Id, r.ReceivedOn,
                        r.Quantity, r.Kind, r.Note, rewardValues.TryGetValue(RewardExternalId(h.Id, r.Id), out var v)
                            ? v
                            : null)).ToList());
            })
            .OrderBy(h => h.Symbol).ThenBy(h => h.Location)
            .ToList();
        var locations = accounts.Values.Where(a => a.ArchivedAtUtc == null).Select(a => a.Name)
            .Concat(list.Select(h => h.Location)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        return new ManualHoldingsView(list, locations, source.Enabled);
    }

    public async Task<Result<Guid>> CreateAsync(CreateManualHoldingRequest req, CancellationToken ct)
    {
        if (!source.Enabled)
        {
            return PricesOff;
        }

        var coinId = req.CoinId.Trim().ToUpperInvariant();
        var securityId = await CoinSecurityAsync(coinId, req.Symbol, req.Name, ct);
        if (!await prices.HasListingAsync(securityId, ct))
        {
            return Error.Validation("ManualHolding.NoPrice",
                "No price was found for this coin, so it cannot be valued. Pick another one from the list.");
        }

        var account = await LocationAsync(req.Location, ct);
        if (await db.ManualHoldings.AnyAsync(h => h.AccountId == account.Id && h.SecurityId == securityId, ct))
        {
            return Error.Conflict("ManualHolding.Duplicate",
                $"This coin is already in {account.Name}. Edit that holding instead.");
        }

        var holding = ManualHolding.Create(account.Id, securityId, req.Quantity, req.AveragePrice,
            req.HeldSince ?? Today, req.Notes, Now);
        db.ManualHoldings.Add(holding);
        await db.SaveChangesAsync(ct);
        await prices.EnsureAsync(new Dictionary<Guid, DateOnly> { [securityId] = holding.HeldSince.AddDays(-7) },
            Today, ct);
        await prices.RefreshRecentAsync(securityId, RefreshDays, ct);
        await ProjectAsync(holding, null, ct);
        await RebuildAsync([account.Id], ct);
        return holding.Id;
    }

    public async Task<Result> UpdateAsync(Guid id, UpdateManualHoldingRequest req, CancellationToken ct)
    {
        var holding = await db.ManualHoldings.Include(h => h.Rewards).FirstOrDefaultAsync(h => h.Id == id, ct);
        if (holding is null)
        {
            return NotFound;
        }

        var account = await LocationAsync(req.Location, ct);
        if (account.Id != holding.AccountId && await db.ManualHoldings.AnyAsync(
                h => h.AccountId == account.Id && h.SecurityId == holding.SecurityId, ct))
        {
            return Error.Conflict("ManualHolding.Duplicate",
                $"This coin is already in {account.Name}. Edit that holding instead.");
        }

        var previousAccount = holding.AccountId;
        var heldSince = req.HeldSince ?? holding.HeldSince;
        holding.Update(account.Id, req.Quantity, req.AveragePrice, heldSince, req.Notes, Now);
        await db.SaveChangesAsync(ct);
        await prices.EnsureAsync(new Dictionary<Guid, DateOnly> { [holding.SecurityId] = heldSince.AddDays(-7) },
            Today, ct);

        await ProjectAsync(holding, previousAccount, ct);
        await RebuildAsync(previousAccount == account.Id ? [account.Id] : [account.Id, previousAccount], ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct)
    {
        var holding = await db.ManualHoldings.Include(h => h.Rewards).FirstOrDefaultAsync(h => h.Id == id, ct);
        if (holding is null)
        {
            return NotFound;
        }

        await RemoveRecordsAsync(holding, holding.AccountId, ct);
        db.ManualHoldings.Remove(holding);
        await db.SaveChangesAsync(ct);
        await RebuildAsync([holding.AccountId], ct);
        return Result.Success();
    }

    public async Task<Result<Guid>> AddRewardAsync(Guid id, RewardRequest req, CancellationToken ct)
    {
        var holding = await db.ManualHoldings.Include(h => h.Rewards).FirstOrDefaultAsync(h => h.Id == id, ct);
        if (holding is null)
        {
            return NotFound;
        }

        var receivedOn = req.ReceivedOn ?? Today;
        var reward = holding.AddReward(receivedOn, req.Quantity, req.Kind, req.Note, Now);
        await db.SaveChangesAsync(ct);
        await prices.EnsureAsync(new Dictionary<Guid, DateOnly> { [holding.SecurityId] = receivedOn.AddDays(-7) },
            Today, ct);
        await ProjectAsync(holding, null, ct);
        await RebuildAsync([holding.AccountId], ct);
        return reward.Id;
    }

    public async Task<Result> RemoveRewardAsync(Guid id, Guid rewardId, CancellationToken ct)
    {
        var holding = await db.ManualHoldings.Include(h => h.Rewards).FirstOrDefaultAsync(h => h.Id == id, ct);
        if (holding is null || !holding.RemoveReward(rewardId, Now))
        {
            return NotFound;
        }

        await db.SaveChangesAsync(ct);
        await ProjectAsync(holding, null, ct);
        await RebuildAsync([holding.AccountId], ct);
        return Result.Success();
    }

    /// <summary>
    /// Fetches current prices for coins whose price is older than <paramref name="maxAge"/> (one request per coin;
    /// only the coin's listing symbol is sent). Returns whether any price changed.
    /// </summary>
    public async Task<bool> RefreshPricesAsync(TimeSpan maxAge, CancellationToken ct)
    {
        if (!source.Enabled || !await RefreshGate.WaitAsync(0, ct))
        {
            return false; // Disabled, or another refresh is already running.
        }

        try
        {
            var securityIds = await db.ManualHoldings.AsNoTracking().Select(h => h.SecurityId).Distinct()
                .ToListAsync(ct);
            var cutoff = Now - maxAge;
            var positions = await db.Positions
                .Where(p => p.Source == DataSource.Manual && securityIds.Contains(p.SecurityId)).ToListAsync(ct);
            var changed = false;
            foreach (var securityId in securityIds)
            {
                var mine = positions.Where(p => p.SecurityId == securityId).ToList();
                if (mine.Count == 0 || mine.All(p => p.SyncedAtUtc > cutoff))
                {
                    continue;
                }

                var latest = await prices.RefreshRecentAsync(securityId, RefreshDays, ct);
                foreach (var p in mine)
                {
                    if (latest is { } close)
                    {
                        changed |= p.LastPrice != close.Close;
                        p.Update(p.Quantity, p.AveragePrice, close.Close, ManualCrypto.PriceAsOf(close.Date, Now),
                            Now);
                    }
                    else
                    {
                        // Provider unavailable: keep the last price, try again after maxAge.
                        p.Update(p.Quantity, p.AveragePrice, p.LastPrice, p.PriceAsOfUtc, Now);
                    }
                }

                await db.SaveChangesAsync(ct);
            }

            return changed;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    /// <summary>Rewrites everything the portfolio reads for this holding.</summary>
    private async Task ProjectAsync(ManualHolding holding, Guid? previousAccount, CancellationToken ct)
    {
        await RemoveRecordsAsync(holding, previousAccount ?? holding.AccountId, ct);
        if (previousAccount is { } old && old != holding.AccountId)
        {
            await RemoveRecordsAsync(holding, holding.AccountId, ct);
        }

        var prefix = ExternalPrefix(holding.Id);
        var closes = (await db.MarketPrices.AsNoTracking()
                .Where(p => p.SecurityId == holding.SecurityId && p.Currency == Currency.Base)
                .Select(p => new { p.Date, p.Close }).ToListAsync(ct))
            .Select(p => (p.Date, p.Close)).ToList();
        var latest = closes.Where(c => c.Close > 0).MaxBy(c => c.Date);
        var lastPrice = latest.Close > 0 ? latest.Close : holding.AveragePrice;
        var priceAsOf = latest.Close > 0 ? ManualCrypto.PriceAsOf(latest.Date, Now) : Now;

        if (holding.Quantity > 0)
        {
            var at = Noon(holding.HeldSince);
            db.Trades.Add(Trade.Record(holding.AccountId, holding.SecurityId, TradeSide.Buy, holding.Quantity,
                holding.AveragePrice, Currency.Base, 0, 0, 0, -holding.Cost, null, at, DataSource.Manual,
                prefix + ":lot"));
            db.CashMovements.Add(CashMovement.Record(holding.AccountId, CashMovementType.Deposit, holding.Cost,
                Currency.Base, holding.Cost, at, null, DataSource.Manual, prefix + ":lot"));
        }

        foreach (var reward in holding.Rewards)
        {
            var externalId = RewardExternalId(holding.Id, reward.Id);
            var value = ManualCrypto.RewardValue(reward.Quantity, reward.ReceivedOn, closes, lastPrice);
            // A free fill: more coins, no money in or out.
            db.Trades.Add(Trade.Record(holding.AccountId, holding.SecurityId, TradeSide.Buy, reward.Quantity, 0,
                Currency.Base, 0, 0, 0, 0, null, Noon(reward.ReceivedOn), DataSource.Manual, externalId));
            db.Dividends.Add(Dividend.Record(holding.AccountId, holding.SecurityId, reward.ReceivedOn, value, 0, value,
                Currency.Base, value, false, DataSource.Manual, externalId));
        }

        db.Positions.Add(Position.Report(holding.AccountId, holding.SecurityId, holding.TotalQuantity,
            holding.AverageCostIncludingRewards, lastPrice, priceAsOf, DataSource.Manual, Now));
        await db.SaveChangesAsync(ct);
    }

    private async Task RemoveRecordsAsync(ManualHolding holding, Guid accountId, CancellationToken ct)
    {
        var prefix = ExternalPrefix(holding.Id);
        await db.Trades.Where(t => t.Source == DataSource.Manual && t.ExternalId.StartsWith(prefix))
            .ExecuteDeleteAsync(ct);
        await db.CashMovements.Where(c => c.Source == DataSource.Manual && c.ExternalId.StartsWith(prefix))
            .ExecuteDeleteAsync(ct);
        await db.Dividends.Where(d => d.Source == DataSource.Manual && d.ExternalId.StartsWith(prefix))
            .ExecuteDeleteAsync(ct);
        await db.Positions.Where(p => p.AccountId == accountId && p.SecurityId == holding.SecurityId &&
                                      p.Source == DataSource.Manual)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// A location's whole history derives from its holdings and public closes: drop it, value today, and let the
    /// background job rebuild the past. Locations left empty are archived (and hidden).
    /// </summary>
    private async Task RebuildAsync(IReadOnlyList<Guid> accountIds, CancellationToken ct)
    {
        foreach (var accountId in accountIds)
        {
            await db.PortfolioSnapshots.Where(s => s.AccountId == accountId).ExecuteDeleteAsync(ct);
            if (!await db.ManualHoldings.AnyAsync(h => h.AccountId == accountId, ct))
            {
                var account = await finance.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
                if (account is { ArchivedAtUtc: null })
                {
                    account.Archive(Now);
                    await finance.SaveChangesAsync(ct);
                }
            }
        }

        await snapshotter.SnapshotTodayAsync(ct);
        foreach (var accountId in accountIds)
        {
            history.Enqueue(accountId);
        }
    }

    private async Task<Guid> CoinSecurityAsync(string coinId, string symbol, string name, CancellationToken ct)
    {
        var existing = await db.BrokerSymbols.AsNoTracking()
            .Where(b => b.Source == DataSource.Manual && b.Symbol == coinId)
            .Select(b => (Guid?)b.SecurityId).FirstOrDefaultAsync(ct);
        if (existing is { } id)
        {
            return id;
        }

        var security = Security.Create(null, Clip(symbol, 40).ToUpperInvariant(), null, Clip(name, 200),
            Currency.Base, AssetClass.Crypto);
        db.Securities.Add(security);
        db.BrokerSymbols.Add(BrokerSymbol.Create(DataSource.Manual, coinId, security.Id));
        await db.SaveChangesAsync(ct);
        return security.Id;
    }

    /// <summary>The location's account, matched by name (case-insensitive); created or restored when needed.</summary>
    private async Task<Account> LocationAsync(string? name, CancellationToken ct)
    {
        var wanted = string.IsNullOrWhiteSpace(name) ? DefaultLocation : Clip(name.Trim(), 80);
        var lower = wanted.ToLowerInvariant();
        var candidates = await finance.Accounts
            .Where(a => a.Kind == AccountKind.Broker && a.Institution == Institution && a.Name.ToLower() == lower)
            .ToListAsync(ct);
        var account = candidates.FirstOrDefault(a => a.ArchivedAtUtc == null) ?? candidates.FirstOrDefault();
        if (account is null)
        {
            account = Account.Create(wanted, AccountKind.Broker, Currency.Base, 0, Today, Institution);
            finance.Accounts.Add(account);
            await finance.SaveChangesAsync(ct);
        }
        else if (account.ArchivedAtUtc is not null)
        {
            account.Restore();
            await finance.SaveChangesAsync(ct);
        }

        return account;
    }

    private async Task<Dictionary<Guid, Account>> LocationAccountsAsync(CancellationToken ct) =>
        await finance.Accounts.AsNoTracking()
            .Where(a => a.Kind == AccountKind.Broker && a.Institution == Institution)
            .ToDictionaryAsync(a => a.Id, ct);

    public static string ExternalPrefix(Guid holdingId) => $"manual:{holdingId}";

    private static string RewardExternalId(Guid holdingId, Guid rewardId) =>
        $"{ExternalPrefix(holdingId)}:reward:{rewardId}";

    private static DateTimeOffset Noon(DateOnly date) =>
        new(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    private static string Clip(string text, int max) => text.Length > max ? text[..max] : text;

    private static readonly Error NotFound = Error.NotFound("ManualHolding.NotFound", "Holding not found.");

    private static readonly Error PricesOff = Error.Validation("ManualHolding.PricesOff",
        "Automatic prices are turned off on this server (MARKET_DATA_PROVIDER=none), so coins cannot be valued.");
}
