namespace Integrations.Infrastructure.Instruments;

/// <summary>
/// One shared, lazily loaded copy of a broker catalogue. The load runs independently of the request that triggered
/// it (so a slow first load finishes in the background) and failures are retried at most once a minute, which keeps
/// search-as-you-type from hammering a broker with a bad key.
/// </summary>
internal sealed class CatalogCache<T>(TimeSpan lifetime, TimeProvider clock)
{
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);
    private readonly Lock _gate = new();
    private Task<T>? _task;
    private DateTimeOffset _startedAt;

    public Task<T> GetAsync(Func<Task<T>> load)
    {
        lock (_gate)
        {
            var age = clock.GetUtcNow() - _startedAt;
            var stale = _task switch
            {
                null => true,
                { IsCompletedSuccessfully: true } => age > lifetime,
                { IsCompleted: true } => age > RetryAfterFailure,
                _ => false,
            };
            if (stale)
            {
                _startedAt = clock.GetUtcNow();
                _task = Task.Run(load, CancellationToken.None);
            }

            return _task!;
        }
    }
}
