using System.Globalization;
using System.Net;
using System.Text.Json;
using Investments.Application.Abstractions;
using Investments.Application.Portfolio;
using Microsoft.Extensions.Logging;
using SharedKernel.Http;

namespace Investments.Infrastructure.MarketData;

/// <summary>
/// Yahoo Finance's public chart and search endpoints (unofficial, no key; they may change without notice).
/// Only an ISIN or a listing symbol and a date range are sent — never quantities, values or account data.
/// Requests are spaced out process-wide, retried with backoff by the resilience handler, and any failure
/// returns null so the caller falls back to trade prices.
/// </summary>
internal sealed class YahooPriceHistorySource(HttpClient http, ILogger<YahooPriceHistorySource> logger)
    : IPriceHistorySource
{
    public const string Host = "query2.finance.yahoo.com";

    /// <summary>The only requests this client may make (enforced by <see cref="AllowListHttpHandler"/>).</summary>
    public static readonly AllowedRequest[] AllowList =
    [
        AllowedRequest.Get(Host, "/v1/finance/search"),
        AllowedRequest.Get(Host, "/v8/finance/chart/[A-Za-z0-9.%^=_-]{1,40}"),
    ];

    /// <summary>At most a few listings are probed per security.</summary>
    private const int MaxCandidates = 5;

    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(750);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public string Name => "yahoo";

    public bool Enabled => true;

    public async Task<ListingMatch?> ResolveAsync(ListingQuery query, CancellationToken ct)
    {
        var searched = query.Isin is { Length: 12 } isin
            ? YahooParser.SearchSymbols(await GetAsync(
                $"/v1/finance/search?q={Uri.EscapeDataString(isin)}&quotesCount=10&newsCount=0&listsCount=0", ct))
            : [];
        var candidates = YahooParser.Candidates(query, searched).Take(MaxCandidates).ToList();
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        ListingMatch? fallback = null;
        foreach (var symbol in candidates)
        {
            var series = await GetDailyClosesAsync(symbol, to.AddDays(-31), to, ct);
            if (series is null || series.Closes.Count == 0)
            {
                continue; // Unknown symbol, or a listing without history (e.g. Stuttgart fund quotes).
            }

            var match = new ListingMatch(symbol, series.Currency);
            if (series.Currency == CurrencyUnits.Normalize(query.Currency).Currency)
            {
                return match;
            }

            // Same ISIN in another currency is the same share class: usable after FX conversion.
            fallback ??= match;
        }

        return fallback;
    }

    public async Task<IReadOnlyList<CoinMatch>> SearchCoinsAsync(string query, CancellationToken ct)
    {
        var text = query.Trim();
        if (text.Length is 0 or > 40)
        {
            return [];
        }

        return YahooParser.Coins(await GetAsync(
            $"/v1/finance/search?q={Uri.EscapeDataString(text)}&quotesCount=20&newsCount=0&listsCount=0", ct));
    }

    public async Task<PriceSeries?> GetDailyClosesAsync(string symbol, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        var period1 = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var json = await GetAsync(string.Create(CultureInfo.InvariantCulture,
            $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d&events=split"),
            ct);
        return json is null ? null : YahooParser.Chart(json);
    }

    /// <summary>
    /// Two days of 15-minute bars (Yahoo's <c>range=2d</c> starts at midnight UTC yesterday, so it always reaches
    /// 24 hours back).
    /// </summary>
    public async Task<Rolling24h?> GetRolling24hAsync(string symbol, CancellationToken ct)
    {
        var json = await GetAsync($"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=15m&range=2d", ct);
        return json is null ? null : YahooParser.Rolling24h(json);
    }

    /// <summary>The body, or null on "not found" and on any failure (logged without the query string).</summary>
    private async Task<string?> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var wait = _lastRequest + MinInterval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            using var response = await http.GetAsync(new Uri($"https://{Host}{pathAndQuery}"), ct);
            _lastRequest = DateTimeOffset.UtcNow;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Market data request failed with {Status} for {Path}", (int)response.StatusCode,
                    pathAndQuery.Split('?')[0].Split('/')[3]);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Market data request failed: {Error}", ex.GetType().Name);
            return null;
        }
        finally
        {
            _lastRequest = DateTimeOffset.UtcNow;
            Gate.Release();
        }
    }
}

/// <summary>Parsing and listing selection, separated from HTTP so it can be tested against recorded responses.</summary>
internal static partial class YahooParser
{
    private static readonly Dictionary<string, string> Suffixes = new()
    {
        ["XETR"] = ".DE",
        ["XLON"] = ".L",
        ["XAMS"] = ".AS",
        ["XPAR"] = ".PA",
        ["XMIL"] = ".MI",
        ["XSWX"] = ".SW",
        ["XMAD"] = ".MC",
        [Investments.Application.Prices.ListingHints.UnitedStates] = "",
    };

    private static readonly HashSet<string> PriceableTypes = ["ETF", "EQUITY", "MUTUALFUND"];

    /// <summary>Symbols from a search response, in Yahoo's relevance order, priceable types only.</summary>
    public static IReadOnlyList<string> SearchSymbols(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return quotes.EnumerateArray()
            .Where(q => q.TryGetProperty("symbol", out var s) && s.ValueKind == JsonValueKind.String &&
                        (!q.TryGetProperty("quoteType", out var t) || PriceableTypes.Contains(t.GetString() ?? "")))
            .Select(q => q.GetProperty("symbol").GetString()!)
            .ToList();
    }

    /// <summary>Coins in a search response (Yahoo lists each coin per quote currency: BTC-USD, BTC-EUR, …).</summary>
    public static IReadOnlyList<CoinMatch> Coins(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var coins = new List<CoinMatch>();
        foreach (var q in quotes.EnumerateArray())
        {
            if (!q.TryGetProperty("quoteType", out var type) || type.GetString() != "CRYPTOCURRENCY" ||
                !q.TryGetProperty("symbol", out var s) || s.GetString() is not { } symbol ||
                CoinPair().Match(symbol) is not { Success: true } pair)
            {
                continue;
            }

            var id = pair.Groups["id"].Value;
            if (coins.Any(c => c.Id == id))
            {
                continue;
            }

            var quote = pair.Groups["quote"].Value;
            var name = (q.TryGetProperty("shortname", out var n) ? n.GetString() : null) ?? id;
            if (name.EndsWith(" " + quote, StringComparison.Ordinal))
            {
                name = name[..^(quote.Length + 1)];
            }

            // Yahoo disambiguates clashing tickers with a numeric suffix (PEPE24478): show the plain ticker.
            var ticker = TickerSuffix().Replace(id, "");
            coins.Add(new CoinMatch(id, ticker.Length > 0 ? ticker : id, name.Trim()));
        }

        return coins;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^(?<id>[A-Z0-9]{1,30})-(?<quote>[A-Z]{3})$")]
    private static partial System.Text.RegularExpressions.Regex CoinPair();

    [System.Text.RegularExpressions.GeneratedRegex("(?<=[A-Z])[0-9]{4,}$")]
    private static partial System.Text.RegularExpressions.Regex TickerSuffix();

    /// <summary>
    /// Listings to probe, best first: the exchange the broker reports (VWCEd_EQ → VWCE.DE), then search results
    /// on that exchange, then the remaining search results. Coins try the EUR pair, then the USD pair (many
    /// smaller coins are only quoted in USD).
    /// </summary>
    public static IEnumerable<string> Candidates(ListingQuery query, IReadOnlyList<string> searched)
    {
        var coins = query.ExchangeHints
            .Where(h => h.Exchange == Investments.Application.Prices.ListingHints.Crypto)
            .SelectMany(h => new[] { h.Root + "-EUR", h.Root + "-USD" });
        var hinted = coins.Concat(query.ExchangeHints
            .Where(h => Suffixes.ContainsKey(h.Exchange))
            .Select(h => h.Root + Suffixes[h.Exchange]))
            .ToList();
        var suffixes = query.ExchangeHints.Where(h => Suffixes.ContainsKey(h.Exchange))
            .Select(h => Suffixes[h.Exchange]).Where(s => s.Length > 0).ToHashSet();
        return hinted
            .Concat(searched.Where(s => suffixes.Any(x => s.EndsWith(x, StringComparison.Ordinal))))
            .Concat(searched)
            .Where(s => s.Length is > 0 and <= 32)
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// The latest quote of an intraday chart response and the price 24 hours before it: the open of the bar that
    /// was trading then (bars are 15 minutes, so within 15 minutes of the exact time). Null when the chart does not
    /// reach that far back.
    /// </summary>
    public static Rolling24h? Rolling24h(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var result = results[0];
        if (!result.TryGetProperty("meta", out var meta) ||
            !meta.TryGetProperty("currency", out var c) || c.ValueKind != JsonValueKind.String ||
            !meta.TryGetProperty("regularMarketPrice", out var p) || p.ValueKind != JsonValueKind.Number ||
            !meta.TryGetProperty("regularMarketTime", out var t) || t.ValueKind != JsonValueKind.Number ||
            !result.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) || quotes.GetArrayLength() == 0)
        {
            return null;
        }

        var latest = (decimal)p.GetDouble();
        if (latest <= 0)
        {
            return null;
        }

        var (currency, factor) = CurrencyUnits.Normalize(c.GetString()!);
        var target = t.GetInt64() - 24 * 3600;
        var quote = quotes[0];
        var opens = quote.TryGetProperty("open", out var o) ? o : default;
        var closes = quote.TryGetProperty("close", out var cl) ? cl : default;

        static decimal? PriceAt(JsonElement series, int i) =>
            series.ValueKind == JsonValueKind.Array && i >= 0 && i < series.GetArrayLength() &&
            series[i].ValueKind == JsonValueKind.Number && series[i].GetDouble() > 0
                ? (decimal)series[i].GetDouble()
                : null;

        // The bar trading at the target time: the last one that started at or before it.
        for (var i = timestamps.GetArrayLength() - 1; i >= 0; i--)
        {
            if (timestamps[i].ValueKind != JsonValueKind.Number || timestamps[i].GetInt64() > target)
            {
                continue;
            }

            if ((PriceAt(opens, i) ?? PriceAt(closes, i - 1) ?? PriceAt(closes, i)) is not { } reference)
            {
                return null;
            }

            return new Rolling24h(currency, DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()), Quote(latest) * factor,
                DateTimeOffset.FromUnixTimeSeconds(target), Quote(reference) * factor);
        }

        return null;
    }

    /// <summary>Quotes arrive as float32 noise: 4 decimals are exact enough, except for coins worth fractions of a cent.</summary>
    private static decimal Quote(decimal value) => decimal.Round(value, value >= 1 ? 4 : 10);

    /// <summary>Daily closes from a chart response; days are the exchange's local dates.</summary>
    public static PriceSeries? Chart(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
            !chart.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var result = results[0];
        var meta = result.GetProperty("meta");
        var rawCurrency = meta.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()!
            : null;
        if (rawCurrency is null)
        {
            return null;
        }

        var (currency, factor) = CurrencyUnits.Normalize(rawCurrency);
        var offset = meta.TryGetProperty("gmtoffset", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetInt64() : 0;
        if (!result.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array ||
            !result.TryGetProperty("indicators", out var indicators) ||
            !indicators.TryGetProperty("quote", out var quotes) || quotes.GetArrayLength() == 0 ||
            !quotes[0].TryGetProperty("close", out var closes) || closes.ValueKind != JsonValueKind.Array)
        {
            return new PriceSeries(currency, []);
        }

        var days = new List<DailyClose>();
        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());
        for (var i = 0; i < count; i++)
        {
            var close = closes[i];
            if (close.ValueKind != JsonValueKind.Number || timestamps[i].ValueKind != JsonValueKind.Number)
            {
                continue; // Yahoo returns null for days without trading.
            }

            var value = (decimal)close.GetDouble();
            if (value <= 0)
            {
                continue;
            }

            var local = DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64() + offset).UtcDateTime;
            // Closes arrive as float32 noise (135.8800048828125): 4 decimals in the quote unit are exact enough,
            // except for coins worth fractions of a cent.
            days.Add(new DailyClose(DateOnly.FromDateTime(local), decimal.Round(value, value >= 1 ? 4 : 10) * factor));
        }

        return new PriceSeries(currency, days.DistinctBy(d => d.Date).ToList());
    }
}
