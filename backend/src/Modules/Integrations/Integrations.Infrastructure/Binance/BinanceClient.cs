using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Integrations.Application.Contracts;
using SharedKernel.Http;

namespace Integrations.Infrastructure.Binance;

/// <summary>
/// Minimal Binance Spot API client: signed GET requests to account, Simple Earn and fill history, plus public prices.
/// Nothing else can be sent: the HttpClient is wrapped in an allow-list of these GET paths (no order, convert,
/// transfer, subscription or withdrawal endpoint), and <see cref="EnsureReadOnlyKeyAsync"/> refuses any key that
/// Binance reports as able to trade or move funds.
/// </summary>
internal sealed class BinanceClient(HttpClient http, RateGate gate, TimeProvider clock)
{
    public const string Host = "api.binance.com";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Everything the integration may call; any other request is blocked before it leaves the process.</summary>
    public static IEnumerable<AllowedRequest> AllowList() =>
    [
        AllowedRequest.Get(Host, "/sapi/v1/account/apiRestrictions"),
        AllowedRequest.Get(Host, "/api/v3/account"),
        AllowedRequest.Get(Host, "/api/v3/myTrades"),
        AllowedRequest.Get(Host, "/sapi/v1/fiat/(orders|payments)"),
        AllowedRequest.Get(Host, "/sapi/v1/simple-earn/(flexible|locked)/position"),
        AllowedRequest.Get(Host, "/sapi/v1/simple-earn/(flexible|locked)/history/rewardsRecord"),
        AllowedRequest.Get(Host, "/api/v3/ticker/price"),
        AllowedRequest.Get(Host, "/api/v3/klines"),
    ];

    /// <summary>Request weight is generous (6,000/min); spacing keeps a sync far below it.</summary>
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(250);

    private string? _apiKey;
    private byte[]? _secret;

    public BinanceClient Configure(string apiKey, string apiSecret)
    {
        _apiKey = apiKey.Trim();
        _secret = Encoding.UTF8.GetBytes(apiSecret.Trim());
        return this;
    }

    /// <summary>
    /// The key must be read-only: Binance reports what it may do, and any trading, margin, futures, options, transfer
    /// or withdrawal permission is refused before anything is read.
    /// </summary>
    public async Task EnsureReadOnlyKeyAsync(CancellationToken ct)
    {
        var r = await SignedAsync<BinanceApiRestrictions>("/sapi/v1/account/apiRestrictions", "", ct);
        var extra = new List<string>();
        if (r.EnableSpotAndMarginTrading) extra.Add("spot & margin trading");
        if (r.EnableMargin) extra.Add("margin");
        if (r.EnableFutures) extra.Add("futures");
        if (r.EnableVanillaOptions) extra.Add("options");
        if (r.EnablePortfolioMarginTrading) extra.Add("portfolio margin");
        if (r.EnableFixApiTrade) extra.Add("FIX trading");
        if (r.EnableWithdrawals) extra.Add("withdrawals");
        if (r.EnableInternalTransfer || r.PermitsUniversalTransfer) extra.Add("transfers");
        if (extra.Count > 0)
        {
            throw new ProviderConfigurationException(
                $"This Binance API key can do more than read ({string.Join(", ", extra)}). Create a key with only \"Enable Reading\" and update the connection.");
        }

        if (!r.EnableReading)
        {
            throw new ProviderConfigurationException("This Binance API key cannot read the account. Enable \"Enable Reading\".");
        }
    }

    public Task<BinanceAccount> AccountAsync(CancellationToken ct) =>
        SignedAsync<BinanceAccount>("/api/v3/account", "omitZeroBalances=true", ct);

    public Task<List<BinanceFlexiblePosition>> FlexiblePositionsAsync(CancellationToken ct) =>
        PagedAsync<BinanceFlexiblePosition>("/sapi/v1/simple-earn/flexible/position", "", ct);

    public Task<List<BinanceLockedPosition>> LockedPositionsAsync(CancellationToken ct) =>
        PagedAsync<BinanceLockedPosition>("/sapi/v1/simple-earn/locked/position", "", ct);

    /// <summary>Flexible rewards of one kind (REALTIME, BONUS or REWARDS) between two instants.</summary>
    public Task<List<BinanceFlexibleReward>> FlexibleRewardsAsync(string type, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct) =>
        PagedAsync<BinanceFlexibleReward>("/sapi/v1/simple-earn/flexible/history/rewardsRecord",
            $"type={type}&startTime={from.ToUnixTimeMilliseconds()}&endTime={to.ToUnixTimeMilliseconds()}", ct);

    public Task<List<BinanceLockedReward>> LockedRewardsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct) =>
        PagedAsync<BinanceLockedReward>("/sapi/v1/simple-earn/locked/history/rewardsRecord",
            $"startTime={from.ToUnixTimeMilliseconds()}&endTime={to.ToUnixTimeMilliseconds()}", ct);

    /// <summary>Fiat deposits (<paramref name="direction"/> 0) or withdrawals (1) between two instants.</summary>
    public Task<List<BinanceFiatOrder>> FiatOrdersAsync(int direction, DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct) =>
        FiatPagedAsync<BinanceFiatOrder>("/sapi/v1/fiat/orders", direction, from, to, ct);

    /// <summary>Crypto bought with fiat (card or bank) between two instants.</summary>
    public Task<List<BinanceFiatPayment>> FiatPurchasesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct) =>
        FiatPagedAsync<BinanceFiatPayment>("/sapi/v1/fiat/payments", 0, from, to, ct);

    private async Task<List<T>> FiatPagedAsync<T>(string path, int transactionType, DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        var rows = new List<T>();
        for (var page = 1; page <= 20; page++)
        {
            var result = await SignedAsync<BinanceFiatPage<T>>(path,
                $"transactionType={transactionType}&beginTime={from.ToUnixTimeMilliseconds()}" +
                $"&endTime={to.ToUnixTimeMilliseconds()}&page={page}&rows=500", ct);
            rows.AddRange(result.Data ?? []);
            if (result.Data is null || result.Data.Count < 500 || rows.Count >= result.Total)
            {
                break;
            }
        }

        return rows;
    }

    /// <summary>Every fill of the account on <paramref name="symbol"/>, oldest first (paged by fill id).</summary>
    public async Task<List<BinanceFill>> FillsAsync(string symbol, CancellationToken ct)
    {
        var fills = new List<BinanceFill>();
        long fromId = 0;
        for (var page = 0; page < 200; page++)
        {
            var batch = await SignedAsync<List<BinanceFill>>("/api/v3/myTrades",
                $"symbol={symbol}&fromId={fromId}&limit=1000", ct);
            fills.AddRange(batch);
            if (batch.Count < 1000)
            {
                break;
            }

            fromId = batch[^1].Id + 1;
        }

        return fills;
    }

    /// <summary>Latest price of every symbol (public).</summary>
    public async Task<Dictionary<string, decimal>> PricesAsync(CancellationToken ct) =>
        (await GetAsync<List<BinanceTicker>>("/api/v3/ticker/price", ct))
        .GroupBy(t => t.Symbol).ToDictionary(g => g.Key, g => g.First().Price, StringComparer.Ordinal);

    /// <summary>Daily closes of <paramref name="symbol"/> from <paramref name="from"/> (UTC days, public).</summary>
    public async Task<Dictionary<DateOnly, decimal>> DailyClosesAsync(string symbol, DateOnly from, CancellationToken ct)
    {
        var closes = new Dictionary<DateOnly, decimal>();
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        for (var page = 0; page < 20 && DateOnly.FromDateTime(start.UtcDateTime) <= today; page++)
        {
            var rows = await GetAsync<List<JsonElement[]>>(
                $"/api/v3/klines?symbol={symbol}&interval=1d&startTime={start.ToUnixTimeMilliseconds()}&limit=1000", ct);
            foreach (var row in rows)
            {
                var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64()).UtcDateTime);
                closes[day] = decimal.Parse(row[4].GetString()!, CultureInfo.InvariantCulture);
            }

            if (rows.Count < 1000)
            {
                break;
            }

            start = DateTimeOffset.FromUnixTimeMilliseconds(rows[^1][0].GetInt64()).AddDays(1);
        }

        return closes;
    }

    private async Task<List<T>> PagedAsync<T>(string path, string query, CancellationToken ct)
    {
        var rows = new List<T>();
        for (var current = 1; current <= 50; current++)
        {
            var q = (query.Length > 0 ? query + "&" : "") + $"current={current}&size=100";
            var page = await SignedAsync<BinancePage<T>>(path, q, ct);
            rows.AddRange(page.Rows ?? []);
            if (page.Rows is null || page.Rows.Count < 100 || rows.Count >= page.Total)
            {
                break;
            }
        }

        return rows;
    }

    private Task<T> SignedAsync<T>(string path, string query, CancellationToken ct)
    {
        if (_apiKey is null || _secret is null)
        {
            throw new InvalidOperationException("Client not configured.");
        }

        var payload = (query.Length > 0 ? query + "&" : "") +
                      $"recvWindow=10000&timestamp={clock.GetUtcNow().ToUnixTimeMilliseconds()}";
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(payload)));
        return SendAsync<T>($"{path}?{payload}&signature={signature}", _apiKey, ct);
    }

    private Task<T> GetAsync<T>(string pathAndQuery, CancellationToken ct) => SendAsync<T>(pathAndQuery, null, ct);

    private async Task<T> SendAsync<T>(string pathAndQuery, string? apiKey, CancellationToken ct)
    {
        await gate.WaitAsync($"binance:{Host}", Spacing, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{Host}{pathAndQuery}"));
        if (apiKey is not null)
        {
            request.Headers.Add("X-MBX-APIKEY", apiKey);
        }

        using var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                   ?? throw new ProviderUnavailableException("Empty response from Binance.");
        }

        BinanceError? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<BinanceError>(Json, ct);
        }
        catch (JsonException)
        {
            // Not a Binance error body (e.g. a proxy page): the status code decides.
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests or (HttpStatusCode)418)
        {
            gate.BlockUntil($"binance:{Host}", clock.GetUtcNow() + RetryAfter(response));
            throw new ProviderUnavailableException("Binance rate limit reached; will retry on the next sync.");
        }

        // -2014/-2015: bad key, IP not allowed or permission missing; -1022: wrong secret; 401: same.
        if (response.StatusCode == HttpStatusCode.Unauthorized || error?.Code is -2014 or -2015 or -1022)
        {
            throw new ProviderConfigurationException(
                "Binance rejected the API key (invalid key or secret, IP not allowed, or reading not enabled). Check the key and update the connection.");
        }

        // -1021: this server's clock is too far from Binance's.
        if (error?.Code == -1021)
        {
            throw new ProviderUnavailableException("Binance rejected the request time; check this server's clock (NTP).");
        }

        throw new ProviderUnavailableException(
            $"Binance returned {(int)response.StatusCode}{(error is null ? "" : $" ({error.Code}: {error.Msg})")}.");
    }

    private static TimeSpan RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta && delta < TimeSpan.FromMinutes(5)
            ? delta
            : TimeSpan.FromMinutes(1);
}
