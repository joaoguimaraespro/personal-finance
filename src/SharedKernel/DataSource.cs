namespace SharedKernel;

/// <summary>
/// Provenance of every financial record, so the user can always audit where a number came from.
/// </summary>
public enum DataSource
{
    Manual = 0,
    Recurring = 1,
    Xlsx = 2,
    Csv = 3,
    Json = 4,
    Trading212 = 10,
    InteractiveBrokers = 11,
}
