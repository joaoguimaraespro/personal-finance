using System.Collections.Concurrent;

namespace Integrations.Infrastructure;

/// <summary>
/// Minimum spacing between calls per key (endpoint), shared across the process. Brokers enforce per-account
/// limits, so the gate is deliberately conservative rather than relying on 429 responses.
/// </summary>
internal sealed class RateGate(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _next = new();

    public async Task WaitAsync(string key, TimeSpan spacing, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var now = clock.GetUtcNow();
            if (_next.TryGetValue(key, out var next) && next > now)
            {
                await Task.Delay(next - now, clock, ct);
            }

            _next[key] = clock.GetUtcNow() + spacing;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Server said to back off until a given time (e.g. x-ratelimit-reset).</summary>
    public void BlockUntil(string key, DateTimeOffset until) =>
        _next.AddOrUpdate(key, until, (_, current) => current > until ? current : until);
}
