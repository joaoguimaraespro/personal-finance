using System.Text.Json;
using System.Text.Json.Serialization;
using Finance.Application.Abstractions;
using Finance.Application.Interest;
using Finance.Domain.Accounts;
using Finance.Domain.Allocation;
using Finance.Domain.Budgets;
using Finance.Domain.Categories;
using Finance.Domain.Goals;
using Finance.Domain.Interest;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using Investments.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Exports.Application;

/// <summary>
/// Complete, versioned JSON export — every record with its provenance — so the data can always leave the app.
/// Finance data can be re-imported into another instance; broker data is included for reference and is
/// re-created by syncing the brokers again.
/// </summary>
public sealed class JsonArchive(IFinanceDb finance, IInvestmentsDb investments, TimeProvider clock,
    InterestAccrualService interest)
{
    public const int SchemaVersion = 1;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        var archive = new
        {
            schemaVersion = SchemaVersion,
            application = "personal-finance",
            exportedAtUtc = clock.GetUtcNow(),
            baseCurrency = Currency.Base,
            finance = new
            {
                accounts = await finance.Accounts.AsNoTracking().Select(a => new
                {
                    a.Id, a.Name, a.Kind, a.Currency, a.OpeningBalance, a.OpeningBalanceOn, a.Institution,
                    a.Identifier, archived = a.ArchivedAtUtc != null, a.InterestPayout, a.AllocationBucketId,
                }).ToListAsync(ct),
                interestRates = await finance.InterestRates.AsNoTracking().OrderBy(r => r.EffectiveFrom)
                    .Select(r => new { r.AccountId, r.EffectiveFrom, r.AnnualRatePercent, r.WithholdingPercent })
                    .ToListAsync(ct),
                categories = await finance.Categories.AsNoTracking().Select(c => new
                {
                    c.Id, c.Key, c.Name, c.Type, c.DefaultNature, c.ParentId, c.IsSystem, c.Color, c.Icon,
                    archived = c.ArchivedAtUtc != null,
                }).ToListAsync(ct),
                buckets = await finance.Buckets.AsNoTracking().Select(b => new { b.Id, b.Key, b.Name, b.Group, b.IsSystem }).ToListAsync(ct),
                // Estimated interest is derived from the rates above and recalculated after import.
                transactions = (await finance.Transactions.AsNoTracking()
                    .Where(t => t.Source != DataSource.InterestEstimate)
                    .OrderBy(t => t.OccurredOn).ToListAsync(ct)).Select(t => new
                {
                    t.Id, t.Type, t.OccurredOn, t.OccurredAtUtc, t.TimeZone, t.AccountId, t.CounterAccountId, t.CategoryId,
                    t.Nature, t.BucketId, t.GoalId, t.OriginalAmount, t.OriginalCurrency, t.FxRate, t.BaseAmount,
                    t.BaseCurrency, t.Description, t.Notes, t.Source, t.ExternalId, t.CreatedAtUtc, t.UpdatedAtUtc,
                    t.AssetKind, t.AssetSymbol, t.AssetName, t.AssetIsin, t.AssetQuantity, t.AssetUnitPrice,
                    t.AssetPriceSource,
                    // Category lines of a split transaction (absent otherwise). EUR amounts are re-derived on import.
                    splits = t.IsSplit
                        ? t.Splits.OrderBy(s => s.Position).Select(s => new
                        {
                            s.CategoryId, amount = s.OriginalAmount, s.BaseAmount, s.Nature, s.Note,
                        }).ToList()
                        : null,
                }).ToList(),
                recurring = (await finance.RecurringTransactions.AsNoTracking().ToListAsync(ct)).Select(r => new
                {
                    r.Id, r.Name, r.Type, r.Amount, r.Currency, r.AccountId, r.CounterAccountId, r.CategoryId, r.Nature,
                    r.BucketId, r.Description, r.Frequency, r.Interval, r.DayOfMonth, r.StartOn, r.EndOn, r.NextDueOn,
                    r.IsActive, r.CreatedAtUtc, r.UpdatedAtUtc,
                    splits = r.Splits.Count == 0
                        ? null
                        : r.Splits.OrderBy(s => s.Position).Select(s => new { s.CategoryId, s.Amount, s.Note }).ToList(),
                }).ToList(),
                budgets = (await finance.Budgets.AsNoTracking().Include(b => b.Items).ToListAsync(ct)).Select(b => new
                {
                    effectiveFrom = YearMonth.From(b.EffectiveFrom).ToString(), b.Note,
                    items = b.Items.Select(i => new { i.Target, i.Mode, i.Value, i.BucketId, i.CategoryId }),
                }),
                goals = await finance.Goals.AsNoTracking().ToListAsync(ct),
                allocationChecks = await finance.AllocationChecks.AsNoTracking()
                    .Select(c => new { c.Year, c.Month, c.BucketId, c.Status }).ToListAsync(ct),
            },
            investments = new
            {
                securities = await investments.Securities.AsNoTracking().ToListAsync(ct),
                positions = await investments.Positions.AsNoTracking().ToListAsync(ct),
                trades = await investments.Trades.AsNoTracking().ToListAsync(ct),
                dividends = await investments.Dividends.AsNoTracking().ToListAsync(ct),
                cashMovements = await investments.CashMovements.AsNoTracking().ToListAsync(ct),
                snapshots = await investments.PortfolioSnapshots.AsNoTracking().ToListAsync(ct),
                targetAllocation = await investments.TargetAllocations.AsNoTracking().ToListAsync(ct),
                manualAssets = await investments.ManualAssets.AsNoTracking().Include(a => a.Valuations).ToListAsync(ct),
                manualHoldings = await investments.ManualHoldings.AsNoTracking().Include(h => h.Rewards).ToListAsync(ct),
                netWorth = await investments.NetWorthSnapshots.AsNoTracking().ToListAsync(ct),
            },
        };
        return JsonSerializer.SerializeToUtf8Bytes(archive, Json);
    }

    public sealed record ImportResult(int Accounts, int Categories, int Buckets, int Goals, int Budgets, int Recurring,
        int Transactions, int Skipped);

    /// <summary>
    /// Re-imports the finance section of an archive. Existing records (same id / key / external id) are kept;
    /// imported transactions are tagged Source = Json with the original id as external id, so it is idempotent.
    /// </summary>
    public async Task<Result<ImportResult>> ImportAsync(Stream stream, CancellationToken ct)
    {
        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 16 }, ct);
        }
        catch (JsonException)
        {
            return Error.Validation("Json.Invalid", "Not a valid JSON export.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != SchemaVersion ||
                !root.TryGetProperty("finance", out var f))
            {
                return Error.Validation("Json.Schema", $"Expected a personal-finance export with schemaVersion {SchemaVersion}.");
            }

            var map = new Dictionary<Guid, Guid>();
            int accounts = 0, categories = 0, buckets = 0, goals = 0, budgets = 0, recurring = 0, transactions = 0, skipped = 0;

            // Accounts: keep ids that already exist, otherwise create and remember the mapping.
            var existingAccounts = await finance.Accounts.Select(a => a.Id).ToListAsync(ct);
            var accountBuckets = new List<(Account Account, Guid BucketId)>();
            foreach (var a in Items(f, "accounts"))
            {
                var id = a.GetProperty("id").GetGuid();
                var kind = Enum.Parse<AccountKind>(a.GetProperty("kind").GetString()!);
                if (existingAccounts.Contains(id))
                {
                    map[id] = id;
                    continue;
                }

                var account = Account.Create(Str(a, "name") ?? "Imported", kind, Str(a, "currency") ?? Currency.Base,
                    Dec(a, "openingBalance") ?? 0, DateOnly.Parse(Str(a, "openingBalanceOn") ?? "2000-01-01", System.Globalization.CultureInfo.InvariantCulture),
                    Str(a, "institution"), Str(a, "identifier"));
                if (Str(a, "interestPayout") is { } payout && Enum.TryParse<InterestPayout>(payout, out var p) &&
                    account.SupportsInterest)
                {
                    account.SetInterestPayout(p);
                }

                if (a.TryGetProperty("allocationBucketId", out var bucketId) && bucketId.ValueKind == JsonValueKind.String)
                {
                    accountBuckets.Add((account, bucketId.GetGuid()));
                }

                finance.Accounts.Add(account);
                map[id] = account.Id;
                accounts++;
            }

            var categoryByKey = await finance.Categories.ToDictionaryAsync(c => c.Key, c => c.Id, ct);
            foreach (var c in Items(f, "categories").OrderBy(c => c.TryGetProperty("parentId", out var p) && p.ValueKind != JsonValueKind.Null))
            {
                var id = c.GetProperty("id").GetGuid();
                var key = Str(c, "key")!;
                if (categoryByKey.TryGetValue(key, out var existing))
                {
                    map[id] = existing;
                    continue;
                }

                Guid? parent = c.TryGetProperty("parentId", out var pid) && pid.ValueKind == JsonValueKind.String
                    ? map.GetValueOrDefault(pid.GetGuid()) : null;
                var category = Category.CreateCustom(Str(c, "name") ?? key, Enum.Parse<CategoryType>(Str(c, "type")!),
                    Str(c, "defaultNature") is { } n ? Enum.Parse<ExpenseNature>(n) : null, parent, Str(c, "color"), Str(c, "icon"));
                finance.Categories.Add(category);
                map[id] = category.Id;
                categories++;
            }

            var bucketByKey = await finance.Buckets.ToDictionaryAsync(b => b.Key, b => b.Id, ct);
            foreach (var b in Items(f, "buckets"))
            {
                var id = b.GetProperty("id").GetGuid();
                if (bucketByKey.TryGetValue(Str(b, "key")!, out var existing))
                {
                    map[id] = existing;
                    continue;
                }

                var bucket = AllocationBucket.CreateCustom(Str(b, "name") ?? "Imported", Enum.Parse<BucketGroup>(Str(b, "group")!));
                finance.Buckets.Add(bucket);
                map[id] = bucket.Id;
                buckets++;
            }

            // Broker accounts keep counting their deposits towards the same (re-mapped) bucket.
            foreach (var (account, bucketId) in accountBuckets.Where(x => map.ContainsKey(x.BucketId)))
            {
                account.SetAllocationBucket(map[bucketId]);
            }

            var goalNames = await finance.Goals.ToDictionaryAsync(g => g.Name, g => g.Id, ct);
            foreach (var g in Items(f, "goals"))
            {
                var id = g.GetProperty("id").GetGuid();
                var name = Str(g, "name") ?? "Goal";
                if (goalNames.TryGetValue(name, out var existing))
                {
                    map[id] = existing;
                    continue;
                }

                var goal = FinancialGoal.Create(name, Dec(g, "targetAmount") ?? 0,
                    Str(g, "targetDate") is { } td ? DateOnly.Parse(td, System.Globalization.CultureInfo.InvariantCulture) : null,
                    Dec(g, "startingAmount") ?? 0, Dec(g, "manualCurrentAmount"), Str(g, "icon"));
                finance.Goals.Add(goal);
                map[id] = goal.Id;
                goals++;
            }

            await finance.SaveChangesAsync(ct);

            var existingPeriods = await finance.Budgets.Select(b => b.EffectiveFrom).ToListAsync(ct);
            foreach (var b in Items(f, "budgets"))
            {
                if (!YearMonth.TryParse(Str(b, "effectiveFrom"), out var period) || existingPeriods.Contains(period.FirstDay))
                {
                    continue;
                }

                var specs = Items(b, "items").Select(i => new BudgetItemSpec(Enum.Parse<BudgetTarget>(Str(i, "target")!),
                    Enum.Parse<BudgetMode>(Str(i, "mode")!), Dec(i, "value") ?? 0, Mapped(i, "bucketId", map), Mapped(i, "categoryId", map)));
                var budget = Budget.Create(period, specs, Str(b, "note"));
                if (budget.IsSuccess)
                {
                    finance.Budgets.Add(budget.Value);
                    budgets++;
                }
            }

            var importedIds = Items(f, "transactions").Select(t => $"json:{t.GetProperty("id").GetGuid()}").ToList();
            var alreadyImported = (await finance.Transactions.IgnoreQueryFilters()
                .Where(t => t.Source == DataSource.Json && importedIds.Contains(t.ExternalId!))
                .Select(t => t.ExternalId!).ToListAsync(ct)).ToHashSet();
            var existingTransactions = (await finance.Transactions.IgnoreQueryFilters().Select(t => t.Id).ToListAsync(ct)).ToHashSet();
            foreach (var t in Items(f, "transactions"))
            {
                var originalId = t.GetProperty("id").GetGuid();
                // Estimated interest is recalculated from the restored rates; importing it would count it as real.
                if (existingTransactions.Contains(originalId) || alreadyImported.Contains($"json:{originalId}") ||
                    IsInterestEstimate(t) || Mapped(t, "accountId", map) is not { } accountId)
                {
                    skipped++;
                    continue;
                }

                var splits = Splits(t, map);
                if (splits is { Count: 0 })
                {
                    skipped++; // a split line's category is missing from the archive
                    continue;
                }

                var draft = new TransactionDraft(Enum.Parse<TransactionType>(Str(t, "type")!),
                    DateOnly.Parse(Str(t, "occurredOn")!, System.Globalization.CultureInfo.InvariantCulture), Dec(t, "originalAmount") ?? 0,
                    Str(t, "originalCurrency") ?? Currency.Base, accountId, Mapped(t, "categoryId", map),
                    Str(t, "nature") is { } n ? Enum.Parse<ExpenseNature>(n) : null, Mapped(t, "counterAccountId", map),
                    Mapped(t, "bucketId", map), Mapped(t, "goalId", map), Dec(t, "fxRate"), Str(t, "description"), Str(t, "notes"),
                    Asset: Str(t, "assetKind") is { } kind && Str(t, "assetSymbol") is { } symbol
                        ? new InvestmentAsset(Enum.Parse<InvestmentAssetKind>(kind), symbol, Str(t, "assetName"),
                            Str(t, "assetIsin"), Dec(t, "assetQuantity"), Dec(t, "assetUnitPrice"), Str(t, "assetPriceSource"))
                        : null,
                    Splits: splits);
                var created = Transaction.Create(draft, DataSource.Json, externalId: $"json:{originalId}");
                if (created.IsSuccess)
                {
                    finance.Transactions.Add(created.Value);
                    transactions++;
                }
                else
                {
                    skipped++;
                }
            }

            var recurringNames = await finance.RecurringTransactions.Select(r => r.Name).ToListAsync(ct);
            foreach (var r in Items(f, "recurring"))
            {
                var recurringSplits = Splits(r, map);
                if (recurringNames.Contains(Str(r, "name") ?? "") || Mapped(r, "accountId", map) is not { } accountId ||
                    recurringSplits is { Count: 0 } ||
                    (recurringSplits is not null && SplitRules.Validate(Dec(r, "amount") ?? 0, recurringSplits) is not null))
                {
                    continue;
                }

                finance.RecurringTransactions.Add(RecurringTransaction.Create(new RecurringDefinition(Str(r, "name")!,
                    Enum.Parse<TransactionType>(Str(r, "type")!), Dec(r, "amount") ?? 0, Str(r, "currency") ?? Currency.Base, accountId,
                    Enum.Parse<RecurrenceFrequency>(Str(r, "frequency")!),
                    DateOnly.Parse(Str(r, "startOn")!, System.Globalization.CultureInfo.InvariantCulture),
                    r.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 1,
                    r.TryGetProperty("dayOfMonth", out var dm) && dm.ValueKind == JsonValueKind.Number ? dm.GetInt32() : null,
                    Str(r, "endOn") is { } e ? DateOnly.Parse(e, System.Globalization.CultureInfo.InvariantCulture) : null,
                    Mapped(r, "categoryId", map), Str(r, "nature") is { } n ? Enum.Parse<ExpenseNature>(n) : null,
                    Mapped(r, "counterAccountId", map), Mapped(r, "bucketId", map), Str(r, "description"),
                    recurringSplits)));
                recurring++;
            }

            // Rate history (append-only periods): only periods the target account does not have yet.
            // Accounts were saved above, so every mapped account can be read back.
            var accountsById = await finance.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);

            var knownPeriods = (await finance.InterestRates.Select(r => new { r.AccountId, r.EffectiveFrom })
                .ToListAsync(ct)).Select(r => (r.AccountId, r.EffectiveFrom)).ToHashSet();
            foreach (var r in Items(f, "interestRates"))
            {
                if (Mapped(r, "accountId", map) is not { } accountId || !accountsById.TryGetValue(accountId, out var owner) ||
                    Str(r, "effectiveFrom") is not { } from)
                {
                    continue;
                }

                var effectiveFrom = DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture);
                if (!knownPeriods.Add((accountId, effectiveFrom)))
                {
                    continue;
                }

                var rate = AccountInterestRate.Create(owner, effectiveFrom, Dec(r, "annualRatePercent") ?? 0,
                    Dec(r, "withholdingPercent"), clock.GetUtcNow());
                if (rate.IsSuccess)
                {
                    finance.InterestRates.Add(rate.Value);
                }
            }

            await finance.SaveChangesAsync(ct);
            await interest.TryRecalculateAsync(map.Values.Distinct().Select(id => (Guid?)id), ct);
            return new ImportResult(accounts, categories, buckets, goals, budgets, recurring, transactions, skipped);
        }
    }

    /// <summary>
    /// The "splits" of an archived transaction or recurring item, with categories mapped to this instance: null when
    /// not split, empty when a line's category cannot be mapped (the record is then skipped).
    /// </summary>
    private static List<SplitLine>? Splits(JsonElement e, Dictionary<Guid, Guid> map)
    {
        var items = Items(e, "splits");
        if (items.Count == 0)
        {
            return null;
        }

        var lines = new List<SplitLine>();
        foreach (var s in items)
        {
            if (Mapped(s, "categoryId", map) is not { } categoryId || Dec(s, "amount") is not { } amount)
            {
                return [];
            }

            lines.Add(new SplitLine(categoryId, amount, Str(s, "note"),
                Str(s, "nature") is { } n && Enum.TryParse<ExpenseNature>(n, out var nature) ? nature : null));
        }

        return lines;
    }

    private static List<JsonElement> Items(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool IsInterestEstimate(JsonElement t) =>
        t.TryGetProperty("source", out var v) && v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() == nameof(DataSource.InterestEstimate),
            JsonValueKind.Number => v.GetInt32() == (int)DataSource.InterestEstimate,
            _ => false,
        };

    private static decimal? Dec(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

    private static Guid? Mapped(JsonElement e, string name, Dictionary<Guid, Guid> map) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && map.TryGetValue(v.GetGuid(), out var id) ? id : null;
}
