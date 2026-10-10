using System.Net;
using Investments.Application.Abstractions;
using SharedKernel.Http;

namespace Investments.Infrastructure.MarketData;

/// <summary>
/// Company logos from Financial Modeling Prep's public image endpoint (no key). Only the ticker is sent. Logos stay
/// their owners' trademarks and are shown only to identify the holding.
/// </summary>
internal sealed class FmpLogoSource(HttpClient http) : ILogoSource
{
    public const string Host = "financialmodelingprep.com";
    private const int MaxBytes = 256 * 1024;

    public static readonly AllowedRequest[] AllowList = [AllowedRequest.Get(Host, "/image-stock/[A-Z0-9.\\-]{1,12}\\.png")];

    public bool Enabled => true;

    public async Task<(byte[] Content, string ContentType)?> FetchAsync(string ticker, CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri($"https://{Host}/image-stock/{ticker}.png"), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("image/png" or "image/jpeg" or "image/webp"))
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return bytes.Length is > 0 and <= MaxBytes ? (bytes, type) : null;
    }
}
