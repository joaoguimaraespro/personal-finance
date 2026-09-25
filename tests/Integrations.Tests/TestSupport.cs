using System.Net;
using System.Text;

namespace Integrations.Tests;

/// <summary>Timers fire immediately, so rate-limit spacing and polling backoff cost no wall-clock time.</summary>
internal sealed class InstantTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now = _now.AddMilliseconds(1);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }

        return new NoopTimer();
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Scripted HTTP responses keyed by path (+ query), recording every request.</summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body, Dictionary<string, string>? Headers)>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];

    public ScriptedHandler On(string pathAndQuery, string body, HttpStatusCode status = HttpStatusCode.OK,
        Dictionary<string, string>? headers = null)
    {
        if (!_responses.TryGetValue(pathAndQuery, out var queue))
        {
            _responses[pathAndQuery] = queue = new Queue<(HttpStatusCode, string, Dictionary<string, string>?)>();
        }

        queue.Enqueue((status, body, headers));
        return this;
    }

    /// <summary>Discards earlier scripted responses for this path.</summary>
    public ScriptedHandler Replace(string pathAndQuery, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Remove(pathAndQuery);
        return On(pathAndQuery, body, status);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var key = request.RequestUri!.PathAndQuery;
        var match = _responses.Keys.Where(k => key.StartsWith(k, StringComparison.Ordinal)).OrderByDescending(k => k.Length)
            .FirstOrDefault() ?? throw new InvalidOperationException($"Unexpected request {key}");
        var queue = _responses[match];
        var (status, body, headers) = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers ?? [])
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return Task.FromResult(response);
    }
}
