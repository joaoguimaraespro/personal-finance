using System.Security.Cryptography;

namespace Integration.Tests.Infrastructure;

/// <summary>RFC 6238 TOTP (SHA-1, 30 s, 6 digits) — what an authenticator app computes from the shared key.</summary>
public static class Totp
{
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var key = Base32Decode(base32Key.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant());
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var bytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        var hash = HMACSHA1.HashData(key, bytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
