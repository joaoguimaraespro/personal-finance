using Finance.Application.Abstractions;
using Finance.Domain.Accounts;
using Finance.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Finance.Domain.Loans;
using SharedKernel;

namespace Finance.Application.Accounts;

public static class AccountBalances
{
    /// <summary>
    /// Balance = opening balance + inflows − outflows, in the account's currency.
    /// EUR accounts use the EUR base amount; foreign-currency accounts only accept same-currency entries.
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> ComputeAsync(IFinanceDb db, IReadOnlyCollection<Account> accounts,
        DateOnly? asOf, CancellationToken ct)
    {
        var query = db.Transactions.AsNoTracking();
        if (asOf is { } date)
        {
            query = query.Where(t => t.OccurredOn <= date);
        }

        var sums = await query
            .GroupBy(t => new { t.AccountId, t.CounterAccountId, t.Type })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.CounterAccountId,
                g.Key.Type,
                Base = g.Sum(t => t.BaseAmount),
                Original = g.Sum(t => t.OriginalAmount),
            })
            .ToListAsync(ct);

        var balances = new Dictionary<Guid, decimal>();
        foreach (var account in accounts)
        {
            decimal Amount(decimal @base, decimal original) =>
                account.Currency == Currency.Base ? @base : original;

            var balance = account.OpeningBalance;
            foreach (var s in sums)
            {
                // Income and sale proceeds flow into the account; for a sale the counter account is the source.
                var inflow = s.Type is TransactionType.Income or TransactionType.InvestmentSale;
                if (s.AccountId == account.Id)
                {
                    balance += inflow ? Amount(s.Base, s.Original) : -Amount(s.Base, s.Original);
                }
                else if (s.CounterAccountId == account.Id)
                {
                    balance += s.Type == TransactionType.InvestmentSale
                        ? -Amount(s.Base, s.Original)
                        : Amount(s.Base, s.Original);
                }
            }

            balances[account.Id] = balance;
        }

        // A loan with terms owes what its amortisation plan says on that day (instalments need not be booked).
        var loanIds = accounts.Where(a => a.Kind == AccountKind.Loan).Select(a => a.Id).ToList();
        if (loanIds.Count > 0)
        {
            var day = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var loans = await db.Loans.AsNoTracking().Include(l => l.Rates).Include(l => l.Prepayments)
                .Where(l => loanIds.Contains(l.AccountId)).ToListAsync(ct);
            foreach (var loan in loans.Where(l => l.Rates.Count > 0))
            {
                balances[loan.AccountId] = -LoanSchedule.StatusOn(loan.Plan(), loan.Principal, day).Outstanding;
            }
        }

        return balances;
    }

    /// <summary>Balances at the end of each of <paramref name="days"/>, with one query for all of them.</summary>
    public static async Task<Dictionary<DateOnly, Dictionary<Guid, decimal>>> ComputeOnAsync(IFinanceDb db,
        IReadOnlyCollection<Account> accounts, IReadOnlyList<DateOnly> days, CancellationToken ct)
    {
        var last = days.Max();
        var rows = await db.Transactions.AsNoTracking().Where(t => t.OccurredOn <= last)
            .GroupBy(t => new { t.AccountId, t.CounterAccountId, t.Type, t.OccurredOn })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.CounterAccountId,
                g.Key.Type,
                g.Key.OccurredOn,
                Base = g.Sum(t => t.BaseAmount),
                Original = g.Sum(t => t.OriginalAmount),
            })
            .ToListAsync(ct);
        var loans = await db.Loans.AsNoTracking().Include(l => l.Rates).Include(l => l.Prepayments)
            .Where(l => l.Rates.Count > 0).ToListAsync(ct);

        var result = new Dictionary<DateOnly, Dictionary<Guid, decimal>>();
        foreach (var day in days)
        {
            var balances = new Dictionary<Guid, decimal>();
            foreach (var account in accounts)
            {
                var balance = account.OpeningBalance;
                foreach (var s in rows.Where(r => r.OccurredOn <= day &&
                                                  (r.AccountId == account.Id || r.CounterAccountId == account.Id)))
                {
                    var amount = account.Currency == Currency.Base ? s.Base : s.Original;
                    var inflow = s.Type is TransactionType.Income or TransactionType.InvestmentSale;
                    balance += s.AccountId == account.Id
                        ? inflow ? amount : -amount
                        : s.Type == TransactionType.InvestmentSale ? -amount : amount;
                }

                balances[account.Id] = balance;
            }

            foreach (var loan in loans.Where(l => balances.ContainsKey(l.AccountId)))
            {
                balances[loan.AccountId] = -LoanSchedule.StatusOn(loan.Plan(), loan.Principal, day).Outstanding;
            }

            result[day] = balances;
        }

        return result;
    }
}
