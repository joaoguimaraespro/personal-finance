using System.Security.Cryptography;
using System.Text;

namespace Ai.Application.Gateway;

/// <summary>Tokens look like <c>pf_1a2b3c4d_&lt;43 chars&gt;</c>: a public lookup prefix and a 256-bit secret.</summary>
public static class AiTokens
{
    public static (string Token, string Prefix, string Hash) Generate()
    {
        var prefix = "pf_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return ($"{prefix}_{secret}", prefix, Hash(secret));
    }

    public static bool TryParse(string? token, out string prefix, out string secret)
    {
        prefix = secret = "";
        if (token is null || token.Length > 100 || !token.StartsWith("pf_", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = token.Split('_');
        if (parts.Length < 3 || parts[1].Length != 8)
        {
            return false;
        }

        prefix = $"pf_{parts[1]}";
        secret = string.Join('_', parts[2..]);
        return secret.Length >= 40;
    }

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool Matches(string secret, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(storedHash));
}
