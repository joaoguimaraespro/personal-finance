using System.Text;
using System.Text.Json.Serialization;

namespace Ai.Application.Gateway;

/// <summary>
/// Free text that came from a person or an import (descriptions, merchants, security names, notes). It is sent to
/// AI clients inside an explicit wrapper so the model can tell data from instructions, and it is sanitised and
/// length-capped. It never widens what a tool returns.
/// </summary>
public sealed record UntrustedText([property: JsonPropertyName("untrusted_text")] string Text)
{
    public const int MaxLength = 160;

    public static UntrustedText? From(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var sb = new StringBuilder(Math.Min(text.Length, MaxLength));
        var lastWasSpace = false;
        foreach (var c in text)
        {
            if (sb.Length >= MaxLength)
            {
                break;
            }

            // Control characters, zero-width and bidi overrides can hide or reorder text shown to a model.
            if (char.IsControl(c) || c is '​' or '‌' or '‍' or '⁠' or '﻿' || c is >= '‪' and <= '‮' || c is >= '⁦' and <= '⁩')
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            sb.Append(c);
        }

        var clean = sb.ToString().Trim();
        return clean.Length == 0 ? null : new UntrustedText(clean);
    }
}
