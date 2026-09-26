using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Host.Mcp;

public sealed record CallerInfo(Guid ClientId, string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Tools);

public sealed class GatewayException(string message) : Exception(message);

/// <summary>Forwards MCP tool calls to the API's AI Policy Gateway using the caller's own token.</summary>
public sealed class GatewayClient(HttpClient http)
{
    public async Task<CallerInfo?> WhoAmIAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ai/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        return response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<CallerInfo>(JsonSerializerOptions.Web, ct)
            : null;
    }

    public async Task<string> CallAsync(string token, string tool, Dictionary<string, object?> args, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ai/tools/{Uri.EscapeDataString(tool)}")
        {
            Content = JsonContent.Create(args.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            return body;
        }

        // Surface the gateway's reason (denied, rate limited, invalid argument) without anything else.
        var reason = "The request was refused.";
        try
        {
            reason = JsonDocument.Parse(body).RootElement.GetProperty("error").GetString() ?? reason;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Keep the generic reason.
        }

        throw new GatewayException(reason);
    }
}
