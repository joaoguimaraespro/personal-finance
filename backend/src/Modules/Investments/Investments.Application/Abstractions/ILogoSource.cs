namespace Investments.Application.Abstractions;

/// <summary>Public company logos by ticker. Only the ticker leaves the server; disabled with market data off.</summary>
public interface ILogoSource
{
    bool Enabled { get; }

    /// <summary>The logo of <paramref name="ticker"/>, or null when the source has none.</summary>
    Task<(byte[] Content, string ContentType)?> FetchAsync(string ticker, CancellationToken ct);
}

public sealed class DisabledLogoSource : ILogoSource
{
    public bool Enabled => false;

    public Task<(byte[] Content, string ContentType)?> FetchAsync(string ticker, CancellationToken ct) =>
        Task.FromResult<(byte[], string)?>(null);
}
