using System.Xml.Linq;
using Integrations.Application.Contracts;
using SharedKernel.Http;

namespace Integrations.Infrastructure.Ibkr;

/// <summary>
/// Interactive Brokers Flex Web Service v3: request a pre-configured report, then download it. A reporting-only
/// interface — the token cannot trade — additionally restricted by the allow-list to these two GET endpoints.
/// </summary>
internal sealed class FlexClient(HttpClient http, RateGate gate, TimeProvider clock)
{
    public const string Host = "ndcdyn.interactivebrokers.com";
    private const string BasePath = "/AccountManagement/FlexWebService";

    public static IEnumerable<AllowedRequest> AllowList() =>
        [AllowedRequest.Get(Host, $"{BasePath}/(SendRequest|GetStatement)")];

    private static readonly int[] Retryable = [1001, 1004, 1005, 1006, 1007, 1008, 1009, 1019, 1021];
    private static readonly int[] Fatal = [1010, 1011, 1012, 1013, 1014, 1015, 1016, 1017, 1020];

    public async Task<XDocument> FetchAsync(string token, string queryId, DateOnly? from, DateOnly? to,
        CancellationToken ct)
    {
        var range = from is { } f && to is { } t ? $"&fd={f:yyyyMMdd}&td={t:yyyyMMdd}" : "";
        var send = await GetAsync($"{BasePath}/SendRequest?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(queryId)}&v=3{range}", token, ct);
        var status = FlexStatementParser.Status(send);
        if (status is null || status.Status != "Success" || status.ReferenceCode is null)
        {
            throw Error(status);
        }

        // The statement is generated asynchronously; poll with backoff while IBKR reports "in progress".
        var delay = TimeSpan.FromSeconds(5);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(delay, clock, ct);
            var doc = await GetAsync($"{BasePath}/GetStatement?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(status.ReferenceCode)}&v=3", token, ct);
            var pending = FlexStatementParser.Status(doc);
            if (pending is null)
            {
                return doc;
            }

            if (pending.ErrorCode is not (1019 or 1001 or 1004 or 1005 or 1006 or 1007 or 1008 or 1009 or 1021))
            {
                throw Error(pending);
            }

            delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 1.6));
        }

        throw new ProviderUnavailableException("IBKR did not finish generating the statement in time.");
    }

    private async Task<XDocument> GetAsync(string pathAndQuery, string token, CancellationToken ct)
    {
        // Documented limit: 1 request/second and 10/minute per token → one request every 6.1 s.
        await gate.WaitAsync($"ibkr:{token.GetHashCode(StringComparison.Ordinal)}", TimeSpan.FromSeconds(6.1), ct);
        using var response = await http.GetAsync(new Uri($"https://{Host}{pathAndQuery}"), ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderUnavailableException($"IBKR Flex returned {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return FlexStatementParser.Load(stream);
    }

    private static Exception Error(FlexStatus? status)
    {
        var code = status?.ErrorCode;
        var message = status?.ErrorMessage ?? "Unexpected Flex response.";
        return code switch
        {
            // "Statement is not available": usually a date range before the account existed. The provider narrows
            // the range; only when nothing at all is available does it become a configuration problem.
            1003 => new FlexStatementUnavailableException(message),
            1012 => new ProviderConfigurationException("The IBKR Flex token has expired. Generate a new one in Client Portal."),
            1013 => new ProviderConfigurationException("The IBKR Flex token is restricted to another IP address."),
            1014 => new ProviderConfigurationException("The IBKR Flex query id is invalid."),
            1015 => new ProviderConfigurationException("The IBKR Flex token is invalid."),
            { } c when Fatal.Contains(c) => new ProviderConfigurationException($"IBKR Flex error {c}: {message}"),
            { } c when Retryable.Contains(c) || c == 1018 => new ProviderUnavailableException($"IBKR Flex busy ({c}): {message}"),
            _ => new ProviderUnavailableException($"IBKR Flex error {code}: {message}"),
        };
    }
}

/// <summary>IBKR error 1003: no statement for the requested period.</summary>
internal sealed class FlexStatementUnavailableException(string message) : Exception(message);
