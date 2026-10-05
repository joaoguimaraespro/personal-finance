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

    // ---- Write-tool arguments. Every value is checked here before any service sees it; ids and text are data.

    public static T Required<T>(string name, T? value) where T : struct =>
        value ?? throw new ToolArgumentException($"{name} is required.");

    /// <summary>An id of an existing record (a UUID). Whether it exists is checked by the tool.</summary>
    public Guid? Id(string name)
    {
        var raw = String(name);
        if (raw is null)
        {
            return null;
        }

        return Guid.TryParseExact(raw, "D", out var id) && id != Guid.Empty
            ? id
            : throw Invalid(name, $"{name} must be an id as returned by a read tool.");
    }

    /// <summary>A decimal with at most <paramref name="maxDecimals"/> decimal places, within bounds.</summary>
    public decimal? Decimal(string name, decimal min, decimal max, int maxDecimals)
    {
        if (!Has(name))
        {
            return null;
        }

        var v = _args.GetProperty(name);
        var d = 0m;
        var ok = v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDecimal(out d),
            JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out d),
            _ => false,
        };
        if (!ok)
        {
            throw new ToolArgumentException($"{name} must be a number.");
        }

        if (d < min || d > max)
        {
            throw new ToolArgumentException(
                $"{name} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (decimal.Round(d, maxDecimals) != d)
        {
            throw new ToolArgumentException($"{name} can have at most {maxDecimals} decimal places.");
        }

        _used[name] = d.ToString(CultureInfo.InvariantCulture);
        return d;
    }

    /// <summary>
    /// Free text from the model (a description, a name). Control, zero-width and bidi characters are refused rather
    /// than silently dropped, and the length is capped. It is stored as data and returned as untrusted_text.
    /// </summary>
    public string? Text(string name, int maxLength)
    {
        if (!Has(name))
        {
            return null;
        }

        var v = _args.GetProperty(name);
        if (v.ValueKind != JsonValueKind.String)
        {
            throw new ToolArgumentException($"{name} must be text.");
        }

        var text = v.GetString()!.Trim();
        if (text.Length == 0 || text.Length > maxLength)
        {
            throw new ToolArgumentException($"{name} must be 1-{maxLength} characters.");
        }

        if (text.Any(c => char.IsControl(c) || c is '​' or '‌' or '‍' or '⁠' or '﻿' ||
                          c is >= '‪' and <= '‮' || c is >= '⁦' and <= '⁩'))
        {
            throw new ToolArgumentException($"{name} contains control or invisible characters.");
        }

        _used[name] = text;
        return text;
    }

    /// <summary>An optional month, yyyy-MM.</summary>
    public YearMonth? Month(string name)
    {
        var raw = String(name);
        if (raw is null)
        {
            return null;
        }

        return YearMonth.TryParse(raw, out var ym) && ym.Year is >= 1970 and <= 2100
            ? ym
            : throw Invalid(name, $"{name} must be yyyy-MM.");
    }

    public int? Integer(string name, int min, int max)
    {
        var v = Int(name);
        return v is null || (v >= min && v <= max)
            ? v
            : throw Invalid(name, $"{name} must be between {min} and {max}.");
    }

    /// <summary>Optional client-chosen key that makes a retried write a no-op.</summary>
    public string? IdempotencyKey()
    {
        var key = String("idempotency_key", 64);
        return key is null || KeyPattern().IsMatch(key)
            ? key
            : throw Invalid("idempotency_key", "idempotency_key must be 8-64 letters, digits, - or _.");
    }

    /// <summary>Validated arguments without the idempotency key: what an idempotency receipt's hash covers.</summary>
    public IReadOnlyDictionary<string, string> UsedWithoutKey =>
        _used.Where(kv => kv.Key != "idempotency_key").OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>A rejected value is not audited: only validated values are recorded.</summary>
    private ToolArgumentException Invalid(string name, string message)
    {
        _used.Remove(name);
        return new ToolArgumentException(message);
    }

    private string? String(string name, int maxLength = 40)
    {
        if (!Has(name))
        {
            return null;
        }

        var v = _args.GetProperty(name);
        var text = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
        if (text is null || text.Length > maxLength)
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

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex KeyPattern();
}
