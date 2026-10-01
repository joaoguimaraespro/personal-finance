using System.Threading.Channels;

namespace Investments.Application.Portfolio;

/// <summary>
/// Accounts whose reconstructed history should be rebuilt (after a sync or a CSV import). Processed in the
/// background so price downloads never delay or fail a sync.
/// </summary>
public sealed class HistoryRebuildQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Enqueue(Guid accountId) => _channel.Writer.TryWrite(accountId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
