using Finance.Application.Abstractions;

namespace Host.Api.Infrastructure;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Actor => accessor.HttpContext?.User.Identity?.Name is { Length: > 0 } name
        ? "user:" + name
        : "system";
}
