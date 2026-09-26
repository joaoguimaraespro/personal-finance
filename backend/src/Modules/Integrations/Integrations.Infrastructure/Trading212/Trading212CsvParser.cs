using System.Globalization;
using Investments.Application.Sync;
using Investments.Domain;

namespace Integrations.Infrastructure.Trading212;

internal sealed record Trading212CsvResult(
    IReadOnlyList<TradeReport> Trades,
    IReadOnlyList<CashMovementReport> Cash,
    IReadOnlyList<DividendReport> Dividends,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Parses the history CSV exported from the Trading 212 app or the API export endpoint. Columns are matched by
/// header name (the set varies by jurisdiction). Cell text is treated as data and length-capped.
/// </summary>
internal static class Trading212CsvParser
{
    private static readonly string[] TimeFormats =
        ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.fff", "yyyy-MM-dd"];

    public static Trading212CsvResult Parse(TextReader reader, string accountCurrency)
    {
        var rows = Csv.Read(reader).ToList();
        if (rows.Count == 0)
        {
            throw new InvalidDataException("The file is empty.");
        }

        var header = rows[0].Select((h, i) => (Name: h.Trim().TrimStart('﻿').ToLowerInvariant(), Index: i))
            .GroupBy(h => h.Name).ToDictionary(g => g.Key, g => g.First().Index);
        if (!header.ContainsKey("action") || !header.ContainsKey("time"))
        {
            throw new InvalidDataException("Not a Trading 212 history export (missing Action/Time columns).");
        }

        var trades = new List<TradeReport>();
        var cash = new List<CashMovementReport>();
        var dividends = new List<DividendReport>();
        var warnings = new List<string>();

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string? Get(string column) =>
                header.TryGetValue(column, out var i) && i < row.Length && row[i].Trim() is { Length: > 0 } v
                    ? v[..Math.Min(v.Length, 200)]
                    : null;
            decimal? Num(string column) =>
                decimal.TryParse(Get(column), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

            var action = Get("action")?.ToLowerInvariant() ?? "";
            if (!DateTime.TryParseExact(Get("time"), TimeFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                warnings.Add($"Row {r + 1}: unreadable time, skipped.");
                continue;
            }

            var at = new DateTimeOffset(parsed, TimeSpan.Zero);
            var total = Num("total") ?? Num("result") ?? 0m;
            var totalCurrency = Get("currency (total)") ?? accountCurrency;
            var isin = Get("isin");
            var ticker = Get("ticker");
            var security = new SecurityReport(ticker ?? isin ?? "UNKNOWN", isin, ticker ?? isin ?? "UNKNOWN", null,
                Get("name") ?? ticker ?? isin ?? "Unknown", Get("currency (price / share)") ?? accountCurrency,
                AssetClass.Stock);

            if (action.EndsWith(" buy", StringComparison.Ordinal) || action.EndsWith(" sell", StringComparison.Ordinal))
            {
                var shares = Math.Abs(Num("no. of shares") ?? 0);
                if (shares == 0)
                {
                    warnings.Add($"Row {r + 1}: trade without quantity, skipped.");
                    continue;
                }

                var side = action.EndsWith(" buy", StringComparison.Ordinal) ? TradeSide.Buy : TradeSide.Sell;
                var fees = (Num("currency conversion fee") ?? 0) + (Num("transaction fee") ?? 0) + (Num("finra fee") ?? 0);
                var taxes = (Num("stamp duty reserve tax") ?? 0) + (Num("stamp duty") ?? 0) +
                            (Num("french transaction tax") ?? 0) + (Num("ptm levy") ?? 0);
                trades.Add(new TradeReport(Trading212Keys.ForTrade(isin, ticker, at, shares), security, side, shares,
                    Num("price / share") ?? 0, security.Currency, Math.Abs(fees), Math.Abs(taxes), totalCurrency,
                    side == TradeSide.Buy ? -Math.Abs(total) : Math.Abs(total), totalCurrency, Num("result"), at));
            }
            else if (action.StartsWith("dividend", StringComparison.Ordinal))
            {
                var paidOn = DateOnly.FromDateTime(at.UtcDateTime);
                var shares = Num("no. of shares");
                var perShare = Num("price / share");
                var withholding = Num("withholding tax") is { } w ? Math.Abs(w) : (decimal?)null;
                var whtCurrency = Get("currency (withholding tax)") ?? security.Currency;
                decimal? gross = null, whtInAccount = null;
                if (withholding is { } wht)
                {
                    if (whtCurrency == totalCurrency)
                    {
                        whtInAccount = wht;
                    }
                    else if (shares is { } s && perShare is { } p && s * p - wht is var netTicker and > 0)
                    {
                        // Convert at the rate implied by the row itself: net(account) / net(instrument currency).
                        whtInAccount = decimal.Round(wht * (Math.Abs(total) / netTicker), 4);
                    }

                    gross = whtInAccount is { } x ? Math.Abs(total) + x : null;
                }

                dividends.Add(new DividendReport(Trading212Keys.ForDividend(isin, ticker, paidOn, total), security, paidOn,
                    gross, whtInAccount, Math.Abs(total), totalCurrency, null, null));
            }
            else if (action is "deposit" or "withdrawal" or "interest on cash" or "lending interest")
            {
                var type = action switch
                {
                    "deposit" => CashMovementType.Deposit,
                    "withdrawal" => CashMovementType.Withdrawal,
                    _ => CashMovementType.Interest,
                };
                var amount = type == CashMovementType.Withdrawal ? -Math.Abs(total) : Math.Abs(total);
                cash.Add(new CashMovementReport(Trading212Keys.ForCash(type.ToString(), at, amount), type, amount,
                    totalCurrency, at, Get("action")));
            }
            else if (!action.StartsWith("currency conversion", StringComparison.Ordinal))
            {
                warnings.Add($"Row {r + 1}: action \"{Get("action")}\" is not imported.");
            }
        }

        return new Trading212CsvResult(trades, cash, dividends, warnings);
    }
}
