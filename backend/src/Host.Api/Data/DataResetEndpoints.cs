using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Integrations.Application;
using Investments.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Host.Api.Data;

/// <summary>The owner must send this exact phrase: a stray or replayed request cannot wipe the data.</summary>
public sealed record DataResetRequest(string? Confirm);

/// <summary>What a reset removed, so the UI can say it.</summary>
public sealed record DataResetResult(int Accounts, int Transactions);

/// <summary>
/// "Start fresh": deletes everything entered or imported — accounts, transactions, recurring items, budgets, goals,
/// loans, savings interest, coins and assets entered by hand, net worth history — and keeps the broker connections
/// (their accounts and synced data, which the next sync refreshes), categories, allocation buckets, AI access and the
/// login. Irreversible: the UI offers the full JSON export first.
/// </summary>
internal static class DataResetEndpoints
{
    public const string Phrase = "reset-all-data";

    public static IEndpointRouteBuilder MapDataReset(this IEndpointRouteBuilder app)
    {
        app.MapPost("/data/reset", async (DataResetRequest req, IFinanceDb finance, IInvestmentsDb investments,
            IIntegrationsDb integrations, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (req.Confirm != Phrase)
            {
                return Results.Problem($"Send {{ \"confirm\": \"{Phrase}\" }} to delete the data.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await ResetAsync(finance, investments, integrations, ct);
            loggers.CreateLogger("DataReset").LogWarning(
                "Data reset by the owner: {Accounts} accounts and {Transactions} transactions deleted",
                result.Accounts, result.Transactions);
            return Results.Ok(result);
        }).WithTags("Data");
        return app;
    }

    public static async Task<DataResetResult> ResetAsync(IFinanceDb finance, IInvestmentsDb investments,
        IIntegrationsDb integrations, CancellationToken ct)
    {
        var kept = await integrations.Connections.Select(c => c.AccountId).ToListAsync(ct);

        // Finance: children before the accounts they point to.
        await finance.TransactionAudits.ExecuteDeleteAsync(ct);
        var transactions = await finance.Transactions.ExecuteDeleteAsync(ct);
        await finance.ExpectedTransactions.ExecuteDeleteAsync(ct);
        await finance.RecurringTransactions.ExecuteDeleteAsync(ct);
        await finance.AllocationChecks.ExecuteDeleteAsync(ct);
        await finance.Imports.ExecuteDeleteAsync(ct);
        await finance.InterestMonths.ExecuteDeleteAsync(ct);
        await finance.InterestRates.ExecuteDeleteAsync(ct);
        finance.Budgets.RemoveRange(await finance.Budgets.ToListAsync(ct));
        finance.Goals.RemoveRange(await finance.Goals.ToListAsync(ct));
        finance.Loans.RemoveRange(await finance.Loans.Include(l => l.Rates).Include(l => l.Prepayments).ToListAsync(ct));
        await finance.SaveChangesAsync(ct);
        var accounts = await finance.Accounts.Where(a => !kept.Contains(a.Id)).ExecuteDeleteAsync(ct);

        // Investments: whatever lived in the deleted accounts (coins entered by hand), and hand-entered assets.
        investments.ManualHoldings.RemoveRange(await investments.ManualHoldings.ToListAsync(ct));
        await investments.SaveChangesAsync(ct);
        await investments.Positions.Where(p => !kept.Contains(p.AccountId)).ExecuteDeleteAsync(ct);
        await investments.Trades.Where(t => !kept.Contains(t.AccountId)).ExecuteDeleteAsync(ct);
        await investments.Dividends.Where(d => !kept.Contains(d.AccountId)).ExecuteDeleteAsync(ct);
        await investments.CashMovements.Where(c => !kept.Contains(c.AccountId)).ExecuteDeleteAsync(ct);
        await investments.CashBalances.Where(c => !kept.Contains(c.AccountId)).ExecuteDeleteAsync(ct);
        await investments.PortfolioSnapshots.Where(s => !kept.Contains(s.AccountId)).ExecuteDeleteAsync(ct);
        await investments.ManualAssets.ExecuteDeleteAsync(ct);
        await investments.NetWorthSnapshots.ExecuteDeleteAsync(ct);
        return new DataResetResult(accounts, transactions);
    }
}
