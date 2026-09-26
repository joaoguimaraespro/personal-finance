using System.Text.RegularExpressions;

namespace SharedKernel.Http;

/// <summary>
/// Outbound guard for third-party clients (brokers, market data). Only explicitly listed method + host + path
/// combinations may leave the process; anything else — e.g. an order endpoint — throws before any network I/O.
/// This makes "read-only" a property of the HTTP client, not just of the code that happens to call it.
/// </summary>
public sealed class AllowListHttpHandler(IEnumerable<AllowedRequest> allowed) : DelegatingHandler
{
    private readonly AllowedRequest[] _allowed = allowed.ToArray();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new BlockedRequestException("Request without URI.");
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new BlockedRequestException($"Only HTTPS is allowed, got {uri.Scheme}.");
        }

        if (!_allowed.Any(a => a.Matches(request.Method, uri)))
        {
            throw new BlockedRequestException($"{request.Method} {uri.Host}{uri.AbsolutePath} is not on the allow-list.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed record AllowedRequest(HttpMethod Method, string Host, Regex Path)
{
    public static AllowedRequest Get(string host, string pathPattern) =>
        new(HttpMethod.Get, host, new Regex($"^{pathPattern}$", RegexOptions.Compiled | RegexOptions.CultureInvariant));

    public static AllowedRequest Post(string host, string pathPattern) =>
        new(HttpMethod.Post, host, new Regex($"^{pathPattern}$", RegexOptions.Compiled | RegexOptions.CultureInvariant));

    public bool Matches(HttpMethod method, Uri uri) =>
        method == Method && string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase) &&
        Path.IsMatch(uri.AbsolutePath);
}

public sealed class BlockedRequestException(string message) : InvalidOperationException(message);
