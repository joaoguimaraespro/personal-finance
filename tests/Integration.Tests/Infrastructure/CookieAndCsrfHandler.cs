using System.Net;

namespace Integration.Tests.Infrastructure;

/// <summary>Behaves like the Angular client: keeps cookies and echoes XSRF-TOKEN in the X-XSRF-TOKEN header.</summary>
public sealed class CookieAndCsrfHandler : DelegatingHandler
{
    public CookieContainer Cookies { get; } = new();

    public bool SendCsrfHeader { get; set; } = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var header = Cookies.GetCookieHeader(uri);
        if (!string.IsNullOrEmpty(header))
        {
            request.Headers.Add("Cookie", header);
        }

        var xsrf = Cookies.GetCookies(uri)["XSRF-TOKEN"]?.Value;
        if (SendCsrfHeader && xsrf is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", xsrf);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var cookie in setCookies)
            {
                Cookies.SetCookies(uri, cookie);
            }
        }

        return response;
    }
}
