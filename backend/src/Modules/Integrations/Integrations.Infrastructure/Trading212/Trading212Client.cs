using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Integrations.Application.Contracts;
using SharedKernel.Http;

namespace Integrations.Infrastructure.Trading212;

/// <summary>
/// Minimal Trading 212 Public API client: read endpoints only. Pacing follows the documented per-endpoint limits;
/// the underlying HttpClient is additionally wrapped in an allow-list that rejects order and pie endpoints.
/// </summary>
internal sealed class Trading212Client(HttpClient http, RateGate gate, TimeProvider clock)
{
    public const string LiveHost = "live.trading212.com";
    public const string DemoHost = "demo.trading212.com";
    private const string Prefix = "/api/v0/equity";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Everything the integration may call. Any other path (orders, pies) is blocked before sending.</summary>
    public static IEnumerable<AllowedRequest> AllowList(string host) =>
    [
        AllowedRequest.Get(host, $"{Prefix}/account/summary"),
        AllowedRequest.Get(host, $"{Prefix}/positions"),
        AllowedRequest.Get(host, $"{Prefix}/metadata/instruments"),
        AllowedRequest.Get(host, $"{Prefix}/history/(orders|dividends|transactions)"),
        AllowedRequest.Get(host, $"{Prefix}/history/exports"),
        AllowedRequest.Post(host, $"{Prefix}/history/exports"),
    ];

    private static readonly Dictionary<string, TimeSpan> Spacing = new()
    {
        ["summary"] = TimeSpan.FromSeconds(5.5),
        ["positions"] = TimeSpan.FromSeconds(1.2),
        ["instruments"] = TimeSpan.FromSeconds(51),
        ["history"] = TimeSpan.FromSeconds(10.5),
    };

    private string _host = LiveHost;
    private AuthenticationHeaderValue? _auth;

    public Trading212Client Configure(string apiKey, string apiSecret, bool demo)
    {
        _host = demo ? DemoHost : LiveHost;
        _auth = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}")));
        return this;
    }

    public Task<T212Summary> SummaryAsync(CancellationToken ct) =>
        GetAsync<T212Summary>($"{Prefix}/account/summary", "summary", ct);

    public Task<List<T212Position>> PositionsAsync(CancellationToken ct) =>
        GetAsync<List<T212Position>>($"{Prefix}/positions", "positions", ct);

    public Task<List<T212InstrumentMeta>> InstrumentsAsync(CancellationToken ct) =>
        GetAsync<List<T212InstrumentMeta>>($"{Prefix}/metadata/instruments", "instruments", ct);

    /// <summary>History is newest-first; stop once items are older than <paramref name="since"/>.</summary>
    public async Task<List<T>> HistoryAsync<T>(string kind, DateTimeOffset? since, Func<T, DateTimeOffset> dateOf,
        CancellationToken ct)
    {
        var items = new List<T>();
        string? path = $"{Prefix}/history/{kind}?limit=50";
        for (var page = 0; path is not null && page < 2_000; page++)
        {
            var result = await GetAsync<T212Page<T>>(path, "history", ct);
            items.AddRange(result.Items);
            if (since is { } s && result.Items.Count > 0 && result.Items.Min(dateOf) < s)
            {
                break;
            }

            // nextPagePath is server-provided: only follow it inside the history path we asked for.
            path = result.NextPagePath is { } next && next.StartsWith($"{Prefix}/history/{kind}", StringComparison.Ordinal)
                ? next
                : null;
        }

        return items;
    }

    private async Task<T> GetAsync<T>(string pathAndQuery, string limitKey, CancellationToken ct)
    {
        if (_auth is null)
        {
            throw new InvalidOperationException("Client not configured.");
        }

        for (var attempt = 0; ; attempt++)
        {
            await gate.WaitAsync($"t212:{_host}:{limitKey}", Spacing[limitKey], ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{_host}{pathAndQuery}"));
            request.Headers.Authorization = _auth;
            using var response = await http.SendAsync(request, ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                    throw new ProviderConfigurationException(
                        "Trading 212 rejected the API key (401). Create a new key and update the connection.");
                case HttpStatusCode.Forbidden:
                    throw new ProviderConfigurationException(
                        "The Trading 212 key lacks a required read permission (403). Enable account data, metadata, portfolio and history.");
                case HttpStatusCode.TooManyRequests when attempt < 3:
                    gate.BlockUntil($"t212:{_host}:{limitKey}", ResetTime(response));
                    continue;
                case HttpStatusCode.TooManyRequests:
                    throw new ProviderUnavailableException("Trading 212 rate limit reached; will retry on the next sync.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderUnavailableException($"Trading 212 returned {(int)response.StatusCode}.");
            }

            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                   ?? throw new ProviderUnavailableException("Empty response from Trading 212.");
        }
    }

    private DateTimeOffset ResetTime(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            var reset = DateTimeOffset.FromUnixTimeSeconds(unix);
            var max = clock.GetUtcNow().AddMinutes(2);
            return reset > max ? max : reset;
        }

        return clock.GetUtcNow().AddSeconds(15);
    }
}
