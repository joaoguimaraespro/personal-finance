using System.Text.Json;

namespace Integrations.Infrastructure.Binance;

/// <summary>
/// The settled movements of every coin read so far (card purchases, Convert, Earn rewards, coins received and sent),
/// up to <see cref="Through"/> (Unix ms). Kept between syncs so the account's whole history is read only once; Earn
/// rewards, paid every day, are kept as one entry per coin and month.
/// </summary>
internal sealed class CostHistory
{
    private sealed record Row(string A, string K, long T, decimal Q, decimal V, decimal? P = null);

    private sealed record Stored(int V, long Through, List<Row> Events);

    /// <summary>Raised when what is kept changes meaning (3: coins in at their market price): read again.</summary>
    private const int Version = 3;

    private CostHistory(long through, List<(string Asset, CostEvent Event)> events)
    {
        Through = through;
        Events = events;
    }

    public long Through { get; }
    public IReadOnlyList<(string Asset, CostEvent Event)> Events { get; }

    /// <summary>The kept history, or an empty one from the account's start (also when it was kept by another version).</summary>
    public static CostHistory Parse(string? json)
    {
        var empty = new CostHistory(BinanceProvider.HistoryStart.ToUnixTimeMilliseconds(), []);
        if (string.IsNullOrWhiteSpace(json))
        {
            return empty;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(json);
            return stored is { V: Version }
                ? new CostHistory(stored.Through,
                    stored.Events.Select(r => (r.A, new CostEvent(r.T, r.Q, r.V, r.K, r.P))).ToList())
                : empty;
        }
        catch (JsonException)
        {
            return empty;
        }
    }

    public CostHistory Append(IEnumerable<(string Asset, CostEvent Event)> events, long through)
    {
        var all = Events.Concat(events).ToList();
        var rewards = all.Where(e => e.Event.Kind == "reward")
            .GroupBy(e => (e.Asset, Month: BinanceValuation.DayOf(e.Event.Time) is var d ? d.Year * 100 + d.Month : 0))
            .Select(g => (Asset: g.Key.Asset, Event: new CostEvent(g.Max(e => e.Event.Time),
                g.Sum(e => e.Event.Quantity), g.Sum(e => e.Event.EurValue), "reward")));
        return new CostHistory(through,
            all.Where(e => e.Event.Kind != "reward").Concat(rewards).OrderBy(e => e.Event.Time).ToList());
    }

    public string ToJson() => JsonSerializer.Serialize(new Stored(Version, Through,
        Events.Select(e => new Row(e.Asset, e.Event.Kind, e.Event.Time, e.Event.Quantity, e.Event.EurValue,
            e.Event.Paid)).ToList()));
}
