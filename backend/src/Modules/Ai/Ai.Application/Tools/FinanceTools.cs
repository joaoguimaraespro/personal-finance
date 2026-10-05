using Ai.Application.Gateway;
using Ai.Contracts;
using Finance.Application.Abstractions;
using Finance.Application.Accounts;
using Finance.Application.Goals;
using Finance.Domain.Accounts;
using Finance.Domain.Categories;
using Finance.Domain.Interest;
using Finance.Domain.Recurring;
using Finance.Domain.Transactions;
using Investments.Application.Abstractions;
using Investments.Application.Calculations;
using Investments.Application.Fx;
using Investments.Application.NetWorth;
using Investments.Application.Portfolio;
using Microsoft.EntityFrameworkCore;
using Reporting.Application;
using Reporting.Application.Calculations;
using Reporting.Application.Queries;
using SharedKernel;

namespace Ai.Application.Tools;

public sealed record ToolContext(IReadOnlySet<string> Scopes, ToolArgs Args, DateOnly Today)
{
    public bool Has(string scope) => Scopes.Contains(scope);
}

public sealed record ToolOutput(object Data, int RecordCount);

/// <summary>
/// Implementations of the AI tools. Each returns a purpose-built DTO containing only what answers its question:
/// asking about restaurant spending never returns accounts, other categories or the portfolio.
/// All numbers come from the same calculators as the application.
/// </summary>
public sealed class FinanceTools(IFinanceDb finance, IInvestmentsDb investments, LedgerAggregates ledger,
    PortfolioQueries portfolio, NetWorthService netWorth, FxRates fx)
{
    public Task<ToolOutput> RunAsync(string tool, ToolContext ctx, CancellationToken ct) => tool switch
    {
        "get_financial_overview" => OverviewAsync(ctx, ct),
        "get_year_breakdown" => YearBreakdownAsync(ctx, ct),
        "get_monthly_summary" => MonthlyAsync(ctx, ct),
        "get_expense_summary" => ExpenseSummaryAsync(ctx, ct),
        "get_expense_transactions" => ExpenseTransactionsAsync(ctx, ct),
        "get_transactions" => TransactionsAsync(ctx, ct),
        "get_income_summary" => IncomeAsync(ctx, ct),
        "get_net_worth" => NetWorthAsync(ctx, ct),
        "get_net_worth_history" => NetWorthHistoryAsync(ctx, ct),
        "get_accounts" => AccountsAsync(ctx, ct),
        "get_recurring" => RecurringAsync(ctx, ct),
        "get_portfolio_summary" => PortfolioSummaryAsync(ctx, ct),
        "get_allocation" => AllocationAsync(ctx, ct),
        "get_positions" => PositionsAsync(ctx, ct),
        "get_portfolio_performance" => PerformanceAsync(ctx, ct),
        "get_dividend_summary" => DividendsAsync(ctx, ct),
        "get_budget_status" => BudgetAsync(ctx, ct),
        "get_goals" => GoalsAsync(ctx, ct),
        _ => throw new ToolArgumentException("Unknown tool."),
    };

    private async Task<ToolOutput> OverviewAsync(ToolContext ctx, CancellationToken ct)
    {
        var year = ctx.Args.Year(ctx.Today);
        var months = await ledger.MonthlySummariesAsync(new YearMonth(year, 1), new YearMonth(year, 12), ct);
        var t = AnnualCalculator.Compute(year, months).Totals;
        return new(new
        {
            year,
            currency = Currency.Base,
            income = t.Income,
            expenses = t.TotalExpenses,
            fixedExpenses = t.FixedExpenses,
            variableExpenses = t.VariableExpenses,
            invested = t.Invested,
            saved = t.Saved,
            netBalance = t.NetBalance,
            averageMonthlySavingsRate = t.AverageMonthlySavingsRate,
            incomeWeightedSavingsRate = t.WeightedSavingsRate,
            monthsWithActivity = months.Count(m => m.TransactionCount > 0),
        }, 1);
    }

    private async Task<ToolOutput> MonthlyAsync(ToolContext ctx, CancellationToken ct)
    {
        var period = ctx.Args.Period(ctx.Today);
        var r = await ReportEndpoints.MonthlyComparisonAsync(ledger, period, ct);
        object Shape(MonthlySummary m) => new
        {
            period = m.Period.ToString(), income = m.Income, fixedExpenses = m.FixedExpenses,
            variableExpenses = m.VariableExpenses, totalExpenses = m.TotalExpenses, invested = m.Invested, saved = m.Saved,
            netBalance = m.NetBalance, savingsRate = m.SavingsRate, expenseBudget = m.ExpenseBudget,
            expenseBudgetRemaining = m.ExpenseBudgetBalance,
        };
        return new(new { currency = Currency.Base, current = Shape(r.Current), previous = Shape(r.Previous) }, 2);
    }

    private async Task<ToolOutput> ExpenseSummaryAsync(ToolContext ctx, CancellationToken ct)
    {
        var period = ctx.Args.Period(ctx.Today);
        var category = ctx.Args.Category();
        var lines = await ReportEndpoints.CategoryBreakdownAsync(ledger, finance, period, ct);
        if (category is not null)
        {
            var match = await ResolveCategoryAsync(category, ct);
            if (match is null)
            {
                return new(new { period = period.ToString(), category, found = false }, 0);
            }

            var ids = await finance.Categories.Where(c => c.Id == match.Value.Id || c.ParentId == match.Value.Id)
                .Select(c => c.Id).ToListAsync(ct);
            var selected = lines.Where(l => ids.Contains(l.CategoryId)).ToList();
            var count = await finance.Transactions.CountAsync(t => t.Type == TransactionType.Expense &&
                t.CategoryId != null && ids.Contains(t.CategoryId.Value) &&
                t.OccurredOn >= period.FirstDay && t.OccurredOn <= period.LastDay, ct);
            var own = selected.FirstOrDefault(l => l.CategoryId == match.Value.Id);
            return new(new
            {
                period = period.ToString(),
                currency = Currency.Base,
                category = match.Value.Name,
                total = selected.Sum(l => l.Actual),
                transactions = count,
                budget = own?.Budget,
                budgetRemaining = own?.Variance,
                budgetStatus = own?.Status,
                previousMonth = selected.Sum(l => l.PreviousMonth),
                twelveMonthAverage = selected.Sum(l => l.TrailingAverage),
            }, 1);
        }

        var top = lines.Where(l => l.Actual > 0).Take(30).ToList();
        return new(new
        {
            period = period.ToString(),
            currency = Currency.Base,
            totalExpenses = lines.Sum(l => l.Actual),
            byCategory = top.Select(l => new { category = l.Name, nature = l.Nature?.ToString(), total = l.Actual, budget = l.Budget, budgetStatus = l.Status }),
        }, top.Count);
    }

    private async Task<ToolOutput> ExpenseTransactionsAsync(ToolContext ctx, CancellationToken ct)
    {
        var period = ctx.Args.Period(ctx.Today);
        var category = ctx.Args.Category();
        var limit = ctx.Args.Bounded("limit", 20, 1, 50);
        var query = finance.Transactions.AsNoTracking().Where(t => t.Type == TransactionType.Expense &&
            t.OccurredOn >= period.FirstDay && t.OccurredOn <= period.LastDay);
        if (category is not null)
        {
            var match = await ResolveCategoryAsync(category, ct);
            if (match is null)
            {
                return new(new { period = period.ToString(), category, found = false }, 0);
            }

            var ids = await finance.Categories.Where(c => c.Id == match.Value.Id || c.ParentId == match.Value.Id)
                .Select(c => c.Id).ToListAsync(ct);
            query = query.Where(t => t.CategoryId != null && ids.Contains(t.CategoryId.Value));
        }

        var rows = await (from t in query
                join c in finance.Categories on t.CategoryId equals c.Id
                join a in finance.Accounts on t.AccountId equals a.Id
                orderby t.OccurredOn descending, t.CreatedAtUtc descending
                select new { t.Id, t.Type, t.OccurredOn, t.OriginalAmount, t.OriginalCurrency, t.BaseAmount, Category = c.Name, t.Description, t.Notes, Account = a.Institution ?? a.Name, t.Source })
            .Take(limit).ToListAsync(ct);

        var withAccount = ctx.Has(AiScopes.RawTransactions);
        var withNotes = ctx.Has(AiScopes.PersonalNotes);
        var withIds = ctx.Has(AiScopes.TransactionsWrite);
        return new(new
        {
            period = period.ToString(),
            baseCurrency = Currency.Base,
            items = rows.Select(r => new
            {
                // Only clients that may edit transactions see ids, and only of the rows they may edit.
                id = withIds && AiWritePolicy.IsEditable(r.Source, r.Type) ? r.Id : (Guid?)null,
                date = r.OccurredOn,
                amount = r.OriginalAmount,
                currency = r.OriginalCurrency,
                amountEur = r.BaseAmount,
                category = r.Category,
                description = UntrustedText.From(r.Description),
                account = withAccount ? r.Account : null,
                source = withAccount ? r.Source.ToString() : null,
                notes = withNotes ? UntrustedText.From(r.Notes) : null,
            }),
            truncated = rows.Count == limit,
        }, rows.Count);
    }

    private async Task<ToolOutput> IncomeAsync(ToolContext ctx, CancellationToken ct)
    {
        var (from, to, label) = ctx.Args.Has("year") && !ctx.Args.Has("period")
            ? (new YearMonth(ctx.Args.Year(ctx.Today), 1), new YearMonth(ctx.Args.Year(ctx.Today), 12), ctx.Args.Year(ctx.Today).ToString(System.Globalization.CultureInfo.InvariantCulture))
            : (ctx.Args.Period(ctx.Today), ctx.Args.Period(ctx.Today), ctx.Args.Period(ctx.Today).ToString());
        var totals = await ledger.MonthTotalsAsync(from, to, ct);
        var names = await finance.Categories.AsNoTracking().Where(c => c.Type == CategoryType.Income)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var byCategory = totals.SelectMany(t => t.IncomeByCategory)
            .GroupBy(kv => kv.Key).Select(g => new { category = names.GetValueOrDefault(g.Key, "Other"), total = g.Sum(kv => kv.Value) })
            .OrderByDescending(x => x.total).ToList();
        return new(new { period = label, currency = Currency.Base, totalIncome = totals.Sum(t => t.Income), byCategory,
            note = "Broker dividends are reported separately by get_dividend_summary." }, byCategory.Count);
    }

    private async Task<ToolOutput> NetWorthAsync(ToolContext ctx, CancellationToken ct)
    {
        var h = await netWorth.HistoryAsync(ct);
        object? accounts = null;
        if (ctx.Has(AiScopes.AccountIdentifiers))
        {
            var list = await finance.Accounts.AsNoTracking().Where(a => a.ArchivedAtUtc == null).ToListAsync(ct);
            accounts = list.Select(a => new { name = UntrustedText.From(a.Name), kind = a.Kind.ToString(), institution = a.Institution, identifier = a.Identifier });
        }

        return new(new
        {
            asOf = h.Current.Date,
            currency = Currency.Base,
            netWorth = h.Current.NetWorth,
            assets = h.Current.Assets,
            liabilities = h.Current.Liabilities,
            cashAndBank = h.Current.Cash,
            investments = h.Current.Investments,
            otherAssets = h.Current.ManualAssets,
            changeSinceStart = h.ChangeSinceStart,
            changeSinceStartPercent = h.ChangeSinceStartPercent,
            trackingSince = h.StartDate,
            accounts,
            manualAssets = await ManualAssetsAsync(ctx, ct),
        }, 1);
    }

    /// <summary>Manually valued assets and liabilities, listed only for clients that may update their values.</summary>
    private async Task<object?> ManualAssetsAsync(ToolContext ctx, CancellationToken ct)
    {
        if (!ctx.Has(AiScopes.HoldingsWrite))
        {
            return null;
        }

        var assets = await investments.ManualAssets.AsNoTracking().Include(a => a.Valuations)
            .Where(a => a.ArchivedAtUtc == null).OrderBy(a => a.Kind).ThenBy(a => a.Name).Take(50).ToListAsync(ct);
        return assets.Select(a =>
        {
            var latest = a.Valuations.OrderByDescending(v => v.Date).FirstOrDefault();
            return new
            {
                id = a.Id, kind = a.Kind.ToString(), name = UntrustedText.From(a.Name), currency = a.Currency,
                liability = a.IsLiability ? true : (bool?)null, value = latest?.Value, valuedOn = latest?.Date,
            };
        }).ToList();
    }

    private async Task<ToolOutput> PortfolioSummaryAsync(ToolContext ctx, CancellationToken ct)
    {
        var s = await portfolio.SummaryAsync(Scope(ctx), ct);
        return new(new
        {
            currency = Currency.Base,
            totalValue = s.TotalValue, marketValue = s.MarketValue, cash = s.Cash,
            dayChange = s.DayChange, dayChangePercent = s.DayChangePercent, dayChangeBasis = Basis(s.DayChangeBasis),
            netContributions = s.NetContributions, totalReturn = s.TotalReturn,
            totalReturnPercent = s.TotalReturnPercent, since = s.Since, unrealizedPnl = s.UnrealizedPnl,
            realizedPnl = s.RealizedPnl, dividendsNet = s.Dividends, feesAndTaxes = s.Fees, positions = s.Positions,
            brokers = s.Accounts.Select(a => a.Broker.ToString()).Distinct(),
            lastSyncUtc = s.LastSyncUtc,
        }, 1);
    }

    /// <summary>What dayChange compares with: previousClose (shares, ETFs), rolling24h (coins) or mixed (a total of both).</summary>
    private static string Basis(DayChangeBasis basis) => basis switch
    {
        DayChangeBasis.Rolling24Hours => "rolling24h",
        DayChangeBasis.Mixed => "mixed",
        _ => "previousClose",
    };

    private async Task<ToolOutput> PositionsAsync(ToolContext ctx, CancellationToken ct)
    {
        var top = ctx.Args.Bounded("top", 10, 1, 25);
        var positions = (await portfolio.PositionsAsync(Scope(ctx), ct)).Take(top).ToList();
        // Hand-entered holdings' ids only for clients that may update them.
        var holdingIds = ctx.Has(AiScopes.HoldingsWrite)
            ? (await investments.ManualHoldings.AsNoTracking().Select(h => new { h.Id, h.AccountId, h.SecurityId })
                .ToListAsync(ct)).ToDictionary(h => (h.AccountId, h.SecurityId), h => (Guid?)h.Id)
            : null;
        return new(new
        {
            currency = Currency.Base,
            items = positions.Select(p =>
            {
                var manual = p.Holdings.Where(h => h.Broker == DataSource.Manual).ToList();
                return new
                {
                    symbol = p.Symbol, name = UntrustedText.From(p.Name), isin = p.Isin, assetClass = p.AssetClass.ToString(),
                    quantity = p.Quantity, priceCurrency = p.Currency, lastPrice = p.LastPrice, averagePrice = p.AveragePrice,
                    marketValue = p.MarketValueBase, dayChange = p.DayChangeBase, dayChangePercent = p.DayChangePercent,
                    dayChangeBasis = Basis(p.DayChangeBasis),
                    unrealizedPnl = p.UnrealizedPnlBase, unrealizedPnlPercent = p.UnrealizedPnlPercent,
                    portfolioWeight = p.PortfolioWeight,
                    source = manual.Count == p.Holdings.Count ? "manual" : manual.Count == 0 ? "broker" : "mixed",
                    brokers = p.Holdings.Where(h => h.Broker != DataSource.Manual).Select(h => h.Broker.ToString()).Distinct(),
                    // A hand-entered coin's location ("Binance", "Cold wallet") plays the role of an institution.
                    locations = manual.Count == 0
                        ? null
                        : manual.Select(h => new
                        {
                            holdingId = holdingIds?.GetValueOrDefault((h.AccountId, p.SecurityId)),
                            location = UntrustedText.From(h.AccountName),
                            quantity = h.Quantity,
                        }),
                };
            }),
        }, positions.Count);
    }

    private async Task<ToolOutput> PerformanceAsync(ToolContext ctx, CancellationToken ct)
    {
        var from = RangeStart(ctx);
        var p = await portfolio.PerformanceAsync(new PortfolioScope(), from, null, ct);
        var monthEnds = p.Series.GroupBy(s => (s.Date.Year, s.Date.Month)).Select(g => g.Last()).TakeLast(60)
            .Select(s => new { month = $"{s.Date:yyyy-MM}", value = s.Value, netContributions = s.NetContributions }).ToList();
        return new(new
        {
            currency = Currency.Base, from = p.From, to = p.To,
            timeWeightedReturnCumulative = p.TimeWeightedReturn, moneyWeightedReturnAnnualised = p.MoneyWeightedReturn,
            startValue = p.StartValue, endValue = p.EndValue, netContributions = p.NetFlows, gain = p.Gain, monthEnds,
        }, monthEnds.Count);
    }

    private async Task<ToolOutput> DividendsAsync(ToolContext ctx, CancellationToken ct)
    {
        var year = ctx.Args.Has("year") ? ctx.Args.Year(ctx.Today) : (int?)null;
        DateOnly? from = year is { } y ? new DateOnly(y, 1, 1) : null;
        DateOnly? to = year is { } y2 ? new DateOnly(y2, 12, 31) : null;
        var d = await portfolio.DividendsAsync(new PortfolioScope(), from, to, ct);
        // Rewards on hand-entered crypto (staking, earn, airdrops) are booked like dividends of the manual locations.
        var rewards = await portfolio.DividendsAsync(new PortfolioScope(DataSource.Manual), from, to, ct);
        var rewardSymbols = rewards.BySecurity.Select(s => s.Symbol).ToHashSet(StringComparer.Ordinal);
        return new(new
        {
            year, currency = Currency.Base, totalNet = d.TotalNetBase,
            dividendsNet = d.TotalNetBase - rewards.TotalNetBase, cryptoRewardsNet = rewards.TotalNetBase,
            byMonth = d.ByMonth.TakeLast(36).Select(m => new { month = m.Period, net = m.Amount }),
            bySecurity = d.BySecurity.Take(10).Select(s => new
            {
                symbol = s.Symbol, name = UntrustedText.From(s.Name), net = s.Amount,
                kind = rewardSymbols.Contains(s.Symbol) ? "crypto_reward" : "dividend",
            }),
        }, d.ByMonth.Count);
    }

    private async Task<ToolOutput> BudgetAsync(ToolContext ctx, CancellationToken ct)
    {
        var period = ctx.Args.Period(ctx.Today);
        var r = (await ReportEndpoints.MonthlyComparisonAsync(ledger, period, ct)).Current;
        var categories = (await ReportEndpoints.CategoryBreakdownAsync(ledger, finance, period, ct))
            .Where(l => l.Budget is not null).ToList();
        return new(new
        {
            period = period.ToString(),
            currency = Currency.Base,
            expenseBudget = r.ExpenseBudget,
            spent = r.TotalExpenses,
            expenseBudgetRemaining = r.ExpenseBudgetBalance,
            allocation = r.Buckets.Select(b => new { bucket = b.Name, type = b.IsInvestment ? "investment" : "savings", target = b.Target, actual = b.Actual, difference = b.Difference }),
            categoryLimits = categories.Select(c => new { category = c.Name, budget = c.Budget, spent = c.Actual, remaining = c.Variance, status = c.Status }),
        }, r.Buckets.Count + categories.Count);
    }

    private async Task<ToolOutput> GoalsAsync(ToolContext ctx, CancellationToken ct)
    {
        var goals = await GoalEndpoints.ListAsync(finance, ctx.Today, false, ct);
        return new(new
        {
            currency = Currency.Base,
            items = goals.Select(g => new
            {
                id = ctx.Has(AiScopes.PlanningWrite) ? g.Id : (Guid?)null,
                name = UntrustedText.From(g.Name), target = g.TargetAmount, current = g.CurrentAmount, progress = g.Progress,
                remaining = g.Remaining, targetDate = g.TargetDate, neededPerMonth = g.MonthlyNeeded, achieved = g.Achieved,
            }),
        }, goals.Count);
    }

    private async Task<ToolOutput> YearBreakdownAsync(ToolContext ctx, CancellationToken ct)
    {
        var year = ctx.Args.Year(ctx.Today);
        var months = await ledger.MonthlySummariesAsync(new YearMonth(year, 1), new YearMonth(year, 12), ct);
        var annual = AnnualCalculator.Compute(year, months);
        var t = annual.Totals;
        return new(new
        {
            year,
            currency = Currency.Base,
            months = annual.Months.Select(r => new
            {
                month = r.Month.Period.ToString(), income = r.Month.Income, fixedExpenses = r.Month.FixedExpenses,
                variableExpenses = r.Month.VariableExpenses, totalExpenses = r.Month.TotalExpenses,
                invested = r.Month.Invested, saved = r.Month.Saved, netBalance = r.Month.NetBalance,
                savingsRate = r.Month.SavingsRate, transactions = r.Month.TransactionCount,
            }),
            totals = new
            {
                income = t.Income, totalExpenses = t.TotalExpenses, fixedExpenses = t.FixedExpenses,
                variableExpenses = t.VariableExpenses, invested = t.Invested, saved = t.Saved, netBalance = t.NetBalance,
                incomeWeightedSavingsRate = t.WeightedSavingsRate,
            },
        }, 12);
    }

    private async Task<ToolOutput> TransactionsAsync(ToolContext ctx, CancellationToken ct)
    {
        var type = ctx.Args.OneOf("type", AiTools.TransactionTypes);
        var (from, to) = DateRange(ctx);
        var category = ctx.Args.Category();
        var search = ctx.Args.Search();
        var limit = ctx.Args.Bounded("limit", 20, 1, 50);

        var query = finance.Transactions.AsNoTracking().Where(t => t.OccurredOn >= from && t.OccurredOn <= to);
        if (type is not null)
        {
            var types = TypesOf(type);
            query = query.Where(t => types.Contains(t.Type));
        }

        if (category is not null)
        {
            var categoryType = type switch
            {
                "expense" => CategoryType.Expense,
                "income" => CategoryType.Income,
                _ => (CategoryType?)null,
            };
            var match = await ResolveCategoryAsync(category, ct, categoryType);
            if (match is null)
            {
                return new(new { from, to, category, found = false }, 0);
            }

            var ids = await finance.Categories.Where(c => c.Id == match.Value.Id || c.ParentId == match.Value.Id)
                .Select(c => c.Id).ToListAsync(ct);
            query = query.Where(t => t.CategoryId != null && ids.Contains(t.CategoryId.Value));
        }

        if (search is not null)
        {
            // Descriptions and instrument names only: notes are never searched without their scope.
            var term = search.ToLowerInvariant();
            query = query.Where(t => (t.Description != null && t.Description.ToLower().Contains(term)) ||
                                     (t.AssetSymbol != null && t.AssetSymbol.ToLower().Contains(term)) ||
                                     (t.AssetName != null && t.AssetName.ToLower().Contains(term)));
        }

        var totals = await query.GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Count = g.Count(), Total = g.Sum(t => t.BaseAmount) })
            .ToListAsync(ct);
        var rows = await (from t in query
                join c in finance.Categories on t.CategoryId equals c.Id into cs
                from c in cs.DefaultIfEmpty()
                join a in finance.Accounts on t.AccountId equals a.Id
                join ca in finance.Accounts on t.CounterAccountId equals ca.Id into cas
                from ca in cas.DefaultIfEmpty()
                orderby t.OccurredOn descending, t.CreatedAtUtc descending
                select new
                {
                    t.Id, t.Type, t.OccurredOn, t.OriginalAmount, t.OriginalCurrency, t.BaseAmount,
                    Category = c == null ? null : c.Name, t.Nature, t.Description, t.Notes,
                    Account = a.Institution ?? a.Name, Counter = ca == null ? null : ca.Institution ?? ca.Name,
                    t.Source, t.AssetKind, t.AssetSymbol, t.AssetName, t.AssetQuantity,
                })
            .Take(limit).ToListAsync(ct);

        var withAccount = ctx.Has(AiScopes.RawTransactions);
        var withNotes = ctx.Has(AiScopes.PersonalNotes);
        var withIds = ctx.Has(AiScopes.TransactionsWrite);
        var matched = totals.Sum(x => x.Count);
        return new(new
        {
            from,
            to,
            baseCurrency = Currency.Base,
            matched,
            totalsByType = totals.OrderBy(x => x.Type).Select(x => new { type = TypeName(x.Type), count = x.Count, totalEur = x.Total }),
            items = rows.Select(r => new
            {
                // Only clients that may edit transactions see ids, and only of the rows they may edit.
                id = withIds && AiWritePolicy.IsEditable(r.Source, r.Type) ? r.Id : (Guid?)null,
                date = r.OccurredOn,
                type = TypeName(r.Type),
                amount = r.OriginalAmount,
                currency = r.OriginalCurrency,
                amountEur = r.BaseAmount,
                category = r.Category,
                nature = r.Nature?.ToString(),
                description = UntrustedText.From(r.Description),
                asset = r.AssetSymbol is null
                    ? null
                    : new { kind = r.AssetKind?.ToString(), symbol = r.AssetSymbol, name = UntrustedText.From(r.AssetName), quantity = r.AssetQuantity },
                account = withAccount ? r.Account : null,
                counterAccount = withAccount ? r.Counter : null,
                source = withAccount ? r.Source.ToString() : null,
                notes = withNotes ? UntrustedText.From(r.Notes) : null,
            }),
            truncated = matched > rows.Count,
        }, rows.Count);
    }

    private async Task<ToolOutput> NetWorthHistoryAsync(ToolContext ctx, CancellationToken ct)
    {
        var from = RangeStart(ctx);
        var h = await netWorth.HistoryAsync(ct);
        var monthEnds = h.Series.Where(p => from is null || p.Date >= from)
            .GroupBy(p => (p.Date.Year, p.Date.Month))
            .Select(g => g.MaxBy(p => p.Date)!)
            .OrderBy(p => p.Date)
            .TakeLast(120)
            .ToList();
        return new(new
        {
            currency = Currency.Base,
            trackingSince = h.StartDate ?? h.Current.Date,
            current = new { date = h.Current.Date, netWorth = h.Current.NetWorth },
            changeOverRange = monthEnds.Count > 1 ? monthEnds[^1].NetWorth - monthEnds[0].NetWorth : (decimal?)null,
            monthEnds = monthEnds.Select(p => new
            {
                month = $"{p.Date:yyyy-MM}", date = p.Date, netWorth = p.NetWorth, assets = p.Assets,
                liabilities = p.Liabilities,
            }),
        }, monthEnds.Count);
    }

    private async Task<ToolOutput> AccountsAsync(ToolContext ctx, CancellationToken ct)
    {
        AccountKind? kind = ctx.Args.OneOf("kind", AiTools.AccountKinds) is { } k ? Enum.Parse<AccountKind>(k) : null;
        var accounts = await finance.Accounts.AsNoTracking()
            .Where(a => a.ArchivedAtUtc == null && (kind == null || a.Kind == kind))
            .OrderBy(a => a.Kind).ThenBy(a => a.Name)
            .ToListAsync(ct);

        // Ledger accounts: the same balance and interest figures as the Accounts page.
        var ledgerAccounts = accounts.Where(a => a.Kind != AccountKind.Broker).ToList();
        var balances = await AccountBalances.ComputeAsync(finance, ledgerAccounts, null, ct);
        var interest = await AccountEndpoints.InterestSummariesAsync(finance, ledgerAccounts, ctx.Today, ct);
        var factors = await fx.EurPerUnitAsync(ledgerAccounts.Select(a => a.Currency), ctx.Today, ct);
        var current = YearMonth.From(ctx.Today);
        var ids = ledgerAccounts.Where(a => a.SupportsInterest).Select(a => a.Id).ToList();
        var awaiting = await finance.InterestMonths.AsNoTracking()
            .Where(m => ids.Contains(m.AccountId) && m.Status == InterestMonthStatus.Estimated &&
                        (m.Year < current.Year || (m.Year == current.Year && m.Month < current.Month)))
            .GroupBy(m => m.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count, ct);

        // Broker accounts and crypto locations are worth what the portfolio says they hold.
        var portfolioValues = accounts.Any(a => a.Kind == AccountKind.Broker)
            ? (await portfolio.SummaryAsync(new PortfolioScope(), ct)).Accounts.ToDictionary(a => a.AccountId, a => a.MarketValue + a.Cash)
            : new Dictionary<Guid, decimal>();

        var withNames = ctx.Has(AiScopes.AccountIdentifiers);
        // Account ids only for clients that can write entries, and only for accounts that accept them.
        var withIds = ctx.Has(AiScopes.TransactionsWrite) || ctx.Has(AiScopes.RecurringWrite);
        var items = accounts.Select(a =>
        {
            var isBroker = a.Kind == AccountKind.Broker;
            var native = isBroker ? portfolioValues.GetValueOrDefault(a.Id) : balances[a.Id];
            var eur = isBroker || a.Currency == Currency.Base
                ? native
                : decimal.Round(native * factors.GetValueOrDefault(a.Currency), 2);
            var isLocation = a.Institution == Investments.Application.Manual.ManualHoldingService.Institution;
            var i = interest.GetValueOrDefault(a.Id);
            return new
            {
                id = withIds && a.IsManual ? a.Id : (Guid?)null,
                kind = a.Kind.ToString(),
                // A crypto location's name ("Binance", "Cold wallet") is where the coins are: it plays the institution.
                institution = UntrustedText.From(isLocation ? a.Name : a.Institution),
                name = withNames ? UntrustedText.From(a.Name) : null,
                identifier = withNames ? a.Identifier : null,
                currency = isBroker ? Currency.Base : a.Currency,
                balance = native,
                balanceEur = eur,
                liability = a.IsLiability ? true : (bool?)null,
                valuedFrom = isBroker ? (isLocation ? "manual_crypto" : "broker_portfolio") : "ledger",
                interest = i is null || (i.AnnualRatePercent is null && i.YearToDate == 0)
                    ? null
                    : new
                    {
                        annualRatePercent = i.AnnualRatePercent,
                        withholdingPercent = i.WithholdingPercent,
                        rateEffectiveFrom = i.RateEffectiveFrom,
                        payout = i.Payout.ToString(),
                        thisYear = i.YearToDate,
                        thisYearEstimated = i.YearToDateEstimated,
                        thisYearConfirmed = i.YearToDate - i.YearToDateEstimated,
                        estimatedInBalance = i.EstimatedInBalance,
                        monthsAwaitingReconciliation = awaiting.GetValueOrDefault(a.Id),
                    },
            };
        }).ToList();

        return new(new
        {
            baseCurrency = Currency.Base,
            totalEur = items.Sum(x => x.balanceEur),
            items,
            note = "Estimated interest is calculated daily from the rate until the owner confirms the bank's figure for the month.",
        }, items.Count);
    }

    private async Task<ToolOutput> RecurringAsync(ToolContext ctx, CancellationToken ct)
    {
        var days = ctx.Args.Bounded("days", 30, 1, 90);
        var horizon = ctx.Today.AddDays(days);
        var templates = await finance.RecurringTransactions.AsNoTracking()
            .OrderBy(r => r.NextDueOn).Take(100).ToListAsync(ct);
        var categories = await finance.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var pending = await (from e in finance.ExpectedTransactions.AsNoTracking()
                join r in finance.RecurringTransactions on e.RecurringTransactionId equals r.Id
                where e.Status == ExpectedStatus.Pending && e.DueOn <= horizon
                orderby e.DueOn
                select new { ExpectedId = e.Id, RecurringId = r.Id, r.Name, r.Type, e.DueOn, e.Amount, e.Currency })
            .Take(100).ToListAsync(ct);
        var factors = await fx.EurPerUnitAsync(templates.Select(t => t.Currency).Concat(pending.Select(p => p.Currency)),
            ctx.Today, ct);
        decimal Eur(decimal amount, string currency) =>
            currency == Currency.Base ? amount : decimal.Round(amount * factors.GetValueOrDefault(currency), 2);

        var active = templates.Where(r => r.IsActive && (r.EndOn is null || r.EndOn >= ctx.Today)).ToList();
        decimal MonthlyEur(IEnumerable<RecurringTransaction> items) =>
            decimal.Round(items.Sum(r => Eur(PerMonth(r), r.Currency)), 2);

        // Ids only for clients that may confirm, skip or edit recurring items.
        var withIds = ctx.Has(AiScopes.RecurringWrite);
        var items = templates.Take(50).Select(r => new
        {
            id = withIds && r.Type is TransactionType.Expense or TransactionType.Income ? r.Id : (Guid?)null,
            name = UntrustedText.From(r.Name),
            type = TypeName(r.Type),
            amount = r.Amount,
            currency = r.Currency,
            frequency = r.Frequency.ToString(),
            interval = r.Interval,
            dayOfMonth = r.DayOfMonth,
            category = r.CategoryId is { } c ? categories.GetValueOrDefault(c) : null,
            nature = r.Nature?.ToString(),
            monthlyEquivalentEur = decimal.Round(Eur(PerMonth(r), r.Currency), 2),
            nextDueOn = r.NextOccurrenceOnOrAfter(ctx.Today) ?? r.NextDueOn,
            active = r.IsActive && (r.EndOn is null || r.EndOn >= ctx.Today),
        }).ToList();

        // Occurrences not proposed yet. The templates are detached (no tracking), so advancing their schedule here
        // only computes dates; nothing is saved.
        var proposed = pending.Select(p => (p.RecurringId, p.DueOn)).ToHashSet();
        var scheduled = active
            .SelectMany(r => r.TakeDueOccurrences(horizon).Where(d => !proposed.Contains((r.Id, d)))
                .Select(d => new { ExpectedId = (Guid?)null, r.Name, r.Type, DueOn = d, r.Amount, r.Currency, Status = "scheduled" }));
        var upcoming = pending.Select(p => new { ExpectedId = (Guid?)p.ExpectedId, p.Name, p.Type, p.DueOn, p.Amount, p.Currency, Status = "awaiting_confirmation" })
            .Concat(scheduled)
            .OrderBy(u => u.DueOn)
            .Take(60)
            .Select(u => new
            {
                expectedId = withIds ? u.ExpectedId : null,
                dueOn = u.DueOn,
                name = UntrustedText.From(u.Name),
                type = TypeName(u.Type),
                amount = u.Amount,
                currency = u.Currency,
                amountEur = Eur(u.Amount, u.Currency),
                status = u.Status,
                overdue = u.DueOn < ctx.Today ? true : (bool?)null,
            })
            .ToList();

        return new(new
        {
            baseCurrency = Currency.Base,
            monthlyFixedCostsEur = MonthlyEur(active.Where(r => r.Type == TransactionType.Expense)),
            monthlyRecurringIncomeEur = MonthlyEur(active.Where(r => r.Type == TransactionType.Income)),
            monthlyRecurringSavingsAndInvestingEur = MonthlyEur(active.Where(r =>
                r.Type is TransactionType.Savings or TransactionType.InvestmentContribution)),
            items,
            upcomingDays = days,
            upcoming,
        }, items.Count + upcoming.Count);
    }

    private async Task<ToolOutput> AllocationAsync(ToolContext ctx, CancellationToken ct)
    {
        var lines = await portfolio.AllocationAsync(Scope(ctx), ct);
        return new(new
        {
            currency = Currency.Base,
            totalValue = lines.Sum(l => l.Value),
            items = lines.Select(l => new
            {
                assetClass = l.AssetClass.ToString(), value = l.Value, actualShare = l.Actual, targetShare = l.Target,
                difference = l.Difference,
            }),
            note = "Shares are fractions of 1 (0.25 = 25%). Targets are set by the owner; null means no target.",
        }, lines.Count);
    }

    private static decimal PerMonth(RecurringTransaction r) => r.MonthlyEquivalent();

    private static TransactionType[] TypesOf(string type) => type switch
    {
        "expense" => [TransactionType.Expense],
        "income" => [TransactionType.Income],
        "investment" => [TransactionType.InvestmentContribution, TransactionType.InvestmentSale],
        "transfer" => [TransactionType.Transfer],
        _ => [TransactionType.Savings],
    };

    private static string TypeName(TransactionType type) => type switch
    {
        TransactionType.Expense => "expense",
        TransactionType.Income => "income",
        TransactionType.InvestmentContribution => "investment_buy",
        TransactionType.InvestmentSale => "investment_sell",
        TransactionType.Transfer => "transfer",
        _ => "savings",
    };

    /// <summary>A month (period) or an explicit date range of at most 12 months.</summary>
    private static (DateOnly From, DateOnly To) DateRange(ToolContext ctx)
    {
        var from = ctx.Args.Date("from");
        var to = ctx.Args.Date("to");
        if (from is null && to is null)
        {
            var period = ctx.Args.Period(ctx.Today);
            return (period.FirstDay, period.LastDay);
        }

        if (ctx.Args.Has("period"))
        {
            throw new ToolArgumentException("Use either period or from/to, not both.");
        }

        var end = to ?? ctx.Today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (start > end)
        {
            throw new ToolArgumentException("from must be on or before to.");
        }

        return start.AddYears(1) > end ? (start, end) : throw new ToolArgumentException("The range can be at most 12 months.");
    }

    private static PortfolioScope Scope(ToolContext ctx) =>
        new(ctx.Args.OneOf("broker", "Trading212", "InteractiveBrokers", "Manual", "Demo") is { } b ? Enum.Parse<DataSource>(b) : null);

    private static DateOnly? RangeStart(ToolContext ctx) => (ctx.Args.OneOf("range", "1y", "3y", "all") ?? "all") switch
    {
        "1y" => ctx.Today.AddYears(-1),
        "3y" => ctx.Today.AddYears(-3),
        _ => null,
    };

    /// <summary>Matches a category by key or by name (built-in English or Portuguese names, or custom names).</summary>
    private Task<(Guid Id, string Name)?> ResolveCategoryAsync(string input, CancellationToken ct,
        CategoryType? type = CategoryType.Expense) => ResolveCategoryAsync(finance, input, type, false, ct);

    /// <summary>Also used by the write tools, which only accept categories that are not archived.</summary>
    internal static async Task<(Guid Id, string Name)?> ResolveCategoryAsync(IFinanceDb finance, string input,
        CategoryType? type, bool activeOnly, CancellationToken ct)
    {
        var needle = input.Trim().ToLowerInvariant();
        var categories = await finance.Categories.AsNoTracking()
            .Where(c => (type == null || c.Type == type) && (!activeOnly || c.ArchivedAtUtc == null))
            .OrderBy(c => c.Type)
            .Select(c => new { c.Id, c.Key, c.Name }).ToListAsync(ct);
        var hit = categories.FirstOrDefault(c => c.Key == needle.Replace(' ', '-'))
                  ?? categories.FirstOrDefault(c => c.Name.Equals(input.Trim(), StringComparison.OrdinalIgnoreCase))
                  ?? categories.FirstOrDefault(c => PortugueseAliases.TryGetValue(needle, out var key) && c.Key == key);
        return hit is null ? null : (hit.Id, hit.Name);
    }

    private static readonly Dictionary<string, string> PortugueseAliases = new()
    {
        ["restaurantes"] = "restaurants", ["supermercado"] = "groceries", ["habitação"] = "housing", ["habitacao"] = "housing",
        ["transportes"] = "transport", ["combustível"] = "fuel", ["combustivel"] = "fuel", ["viagens"] = "travel",
        ["lazer"] = "entertainment", ["subscrições"] = "subscriptions", ["subscricoes"] = "subscriptions", ["ginásio"] = "gym",
        ["roupa"] = "clothing", ["saúde"] = "health", ["saude"] = "health", ["educação"] = "education", ["educacao"] = "education",
        ["seguros"] = "insurance", ["impostos"] = "taxes", ["prendas"] = "gifts", ["eletricidade"] = "electricity",
        ["alimentação"] = "food", ["alimentacao"] = "food", ["compras"] = "shopping", ["outro"] = "other",
        ["salário"] = "salary", ["salario"] = "salary", ["prémio"] = "bonus", ["premio"] = "bonus", ["juros"] = "interest",
        ["dividendos"] = "dividends",
    };
}
