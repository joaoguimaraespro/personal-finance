namespace Investments.Domain;

/// <summary>
/// A security's logo, fetched once by the server and kept here, so browsers only ever load it from this app.
/// <see cref="Content"/> is null when the source has none (re-checked after a while).
/// </summary>
public sealed class SecurityLogo
{
    private SecurityLogo() { }

    public Guid SecurityId { get; private set; }
    public byte[]? Content { get; private set; }
    public string? ContentType { get; private set; }
    public DateTimeOffset FetchedAtUtc { get; private set; }

    public static SecurityLogo Create(Guid securityId) => new() { SecurityId = securityId };

    public void Store(byte[]? content, string? contentType, DateTimeOffset now)
    {
        Content = content;
        ContentType = content is null ? null : contentType;
        FetchedAtUtc = now;
    }

    /// <summary>A logo is kept 90 days; a missing one is asked for again after 30.</summary>
    public bool IsStale(DateTimeOffset now) => now - FetchedAtUtc > TimeSpan.FromDays(Content is null ? 30 : 90);
}
