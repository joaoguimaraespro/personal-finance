using System.Threading.Channels;
using Integrations.Application.Connections;

namespace Integrations.Application.Sync;

/// <summary>Manual "sync now" requests, processed by the background sync service one at a time.</summary>
public sealed class SyncQueue
{
    private readonly Channel<(Guid ConnectionId, SyncTrigger Trigger)> _channel =
        Channel.CreateBounded<(Guid, SyncTrigger)>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    public bool Enqueue(Guid connectionId) => _channel.Writer.TryWrite((connectionId, SyncTrigger.Manual));

    public IAsyncEnumerable<(Guid ConnectionId, SyncTrigger Trigger)> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Parses a broker CSV export into normalised reports (implemented per broker in infrastructure).</summary>
public interface ICsvHistoryParser
{
    Contracts.BrokerKind Kind { get; }

    Task<CsvImportData> ParseAsync(Stream csv, string accountCurrency, CancellationToken ct);
}

public sealed record CsvImportData(
    IReadOnlyList<Investments.Application.Sync.TradeReport> Trades,
    IReadOnlyList<Investments.Application.Sync.CashMovementReport> Cash,
    IReadOnlyList<Investments.Application.Sync.DividendReport> Dividends,
    IReadOnlyList<string> Warnings);
