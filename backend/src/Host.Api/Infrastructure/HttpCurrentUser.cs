using Finance.Application.Abstractions;

namespace Host.Api.Infrastructure;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor, ActorScope scope) : ICurrentUser
{
    public string Actor => scope.Current ?? (accessor.HttpContext?.User.Identity?.Name is { Length: > 0 } name
        ? "user:" + name
        : "system");
}
