using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Integration.Tests.Infrastructure;

public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient Http { get; } = http;

    public async Task RefreshCsrfAsync() => (await Http.GetAsync("/api/auth/antiforgery")).EnsureSuccessStatusCode();

    public Task<HttpResponseMessage> PostAsync<T>(string url, T body) => Http.PostAsJsonAsync(url, body, Json);

    public Task<HttpResponseMessage> PutAsync<T>(string url, T body) => Http.PutAsJsonAsync(url, body, Json);

    public async Task<T> GetAsync<T>(string url)
    {
        var response = await Http.GetAsync(url);
        await EnsureAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<JsonElement> GetJsonAsync(string url) => await GetAsync<JsonElement>(url);

    public async Task<Guid> CreateAsync<T>(string url, T body)
    {
        var response = await PostAsync(url, body);
        await EnsureAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    public static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)response.StatusCode} {response.RequestMessage?.RequestUri}: {body}");
        }
    }
}
