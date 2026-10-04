using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharedKernel;

namespace Ai.Application.Tools;

public sealed class ToolArgumentException(string message) : Exception(message);

/// <summary>
/// Strict argument reader: only declared parameters are read, each is validated, and only validated values are
/// recorded in the audit log. Unknown arguments are rejected rather than ignored.
/// </summary>
public sealed partial class ToolArgs
{
    private readonly JsonElement _args;
    private readonly Dictionary<string, string> _used = [];

    public ToolArgs(JsonElement args, IEnumerable<string> declared)
    {
        _args = args.ValueKind == JsonValueKind.Object ? args : default;
        if (_args.ValueKind == JsonValueKind.Object)
        {
            var allowed = declared.ToHashSet(StringComparer.Ordinal);
            var unknown = _args.EnumerateObject().Select(p => p.Name).Where(n => !allowed.Contains(n)).ToList();
            if (unknown.Count > 0)
            {
                throw new ToolArgumentException($"Unknown argument(s): {string.Join(", ", unknown.Take(5).Select(u => u.Length > 30 ? u[..30] : u))}.");
            }
        }
    }

    /// <summary>Validated arguments, safe to log.</summary>
    public IReadOnlyDictionary<string, string> Used => _used;

    public YearMonth Period(DateOnly today)
    {
        var raw = String("period");
        if (raw is null)
        {
            return YearMonth.From(today);
        }

        return YearMonth.TryParse(raw, out var ym) && ym.Year is >= 1970 and <= 2100
            ? ym
            : throw Invalid("period", "period must be yyyy-MM.");
    }

    public int Year(DateOnly today)
    {
        var y = Int("year") ?? today.Year;
        return y is >= 1970 and <= 2100 ? y : throw new ToolArgumentException("year out of range.");
    }

    public int Bounded(string name, int fallback, int min, int max)
    {
        var v = Int(name) ?? fallback;
        return v >= min && v <= max ? v : throw new ToolArgumentException($"{name} must be between {min} and {max}.");
    }

    public string? Category()
    {
        var c = String("category");
        return c is null || CategoryPattern().IsMatch(c) ? c : throw Invalid("category", "category must be a short name.");
    }

    /// <summary>An optional calendar date, yyyy-MM-dd.</summary>
    public DateOnly? Date(string name)
    {
        var raw = String(name);
        if (raw is null)
        {
            return null;
        }

        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) &&
               d.Year is >= 1970 and <= 2100
            ? d
            : throw Invalid(name, $"{name} must be a date as yyyy-MM-dd.");
    }

    /// <summary>A short search phrase: letters, digits, spaces and a little punctuation, 2-40 characters.</summary>
    public string? Search()
    {
        var s = String("search")?.Trim();
        return s is null || SearchPattern().IsMatch(s) ? s : throw Invalid("search", "search must be 2-40 letters, digits or spaces.");
    }

    public string? OneOf(string name, IEnumerable<string> values) => OneOf(name, values.ToArray());

    public string? OneOf(string name, params string[] values)
    {
        var v = String(name);
        return v is null || values.Contains(v, StringComparer.OrdinalIgnoreCase)
            ? values.FirstOrDefault(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase))
            : throw Invalid(name, $"{name} must be one of: {string.Join(", ", values)}.");
    }

    public bool Has(string name) => _args.ValueKind == JsonValueKind.Object && _args.TryGetProperty(name, out var v) &&
                                    v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    /// <summary>A rejected value is not audited: only validated values are recorded.</summary>
    private ToolArgumentException Invalid(string name, string message)
    {
        _used.Remove(name);
        return new ToolArgumentException(message);
    }

    private string? String(string name)
    {
        if (!Has(name))
        {
            return null;
        }

        var v = _args.GetProperty(name);
        var text = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
        if (text is null || text.Length > 40)
        {
            throw new ToolArgumentException($"{name} is too long.");
        }

        _used[name] = text;
        return text;
    }

    private int? Int(string name)
    {
        if (!Has(name))
        {
            return null;
        }

        var v = _args.GetProperty(name);
        var ok = v.ValueKind == JsonValueKind.Number ? v.TryGetInt32(out var n)
            : int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
        if (!ok)
        {
            throw new ToolArgumentException($"{name} must be an integer.");
        }

        _used[name] = n.ToString(CultureInfo.InvariantCulture);
        return n;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} &/\-]{1,40}$")]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"^[\p{L}\p{N} &/\-.,'+]{2,40}$")]
    private static partial Regex SearchPattern();
}
