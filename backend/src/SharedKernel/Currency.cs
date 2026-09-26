namespace SharedKernel;

public static class Currency
{
    public const string Base = "EUR";

    /// <summary>Currencies accepted for manual entry. Brokers may report others; those are stored as reported.</summary>
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        "EUR", "USD", "GBP", "CHF", "JPY", "CAD", "AUD", "SEK", "NOK", "DKK", "PLN", "CZK", "BRL",
    };

    public static bool IsValid(string? code) => code is { Length: 3 } && code.All(char.IsAsciiLetterUpper);

    /// <summary>Money is rounded half-away-from-zero to cents only at presentation/aggregation boundaries.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
