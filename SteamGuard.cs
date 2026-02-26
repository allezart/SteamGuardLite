using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SteamGuardLite;

public static class MafileReader
{
    public static (bool ok, string? accountName, string sharedSecret, string error)
        TryExtractAccountAndSecret(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (false, null, "", "File is empty.");

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Some maFiles may be wrapped like: { "content": "..." } or { "content": { ... } }
            if (TryGetPropertyCI(root, "content", out var content))
            {
                if (content.ValueKind == JsonValueKind.String)
                {
                    var inner = content.GetString();
                    if (!string.IsNullOrWhiteSpace(inner) && inner.TrimStart().StartsWith("{"))
                    {
                        using var innerDoc = JsonDocument.Parse(inner);
                        return Extract(innerDoc.RootElement);
                    }
                }
                else if (content.ValueKind == JsonValueKind.Object)
                {
                    return Extract(content);
                }
            }

            return Extract(root);
        }
        catch (JsonException ex)
        {
            return (false, null, "", $"Invalid JSON: {ex.Message}");
        }

        static (bool ok, string? accountName, string sharedSecret, string error) Extract(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return (false, null, "", "Expected a JSON object.");

            var secret = GetStringCI(root, "shared_secret", "SharedSecret", "sharedSecret");
            if (string.IsNullOrWhiteSpace(secret))
                return (false, null, "", "Field 'shared_secret' was not found.");

            secret = secret.Trim();
            if (!IsBase64(secret))
                return (false, null, "", "'shared_secret' does not look like base64.");

            var acc = GetStringCI(root, "account_name", "AccountName", "accountName")?.Trim();
            return (true, acc, secret, "");
        }

        static bool IsBase64(string s)
        {
            try { Convert.FromBase64String(s); return true; }
            catch { return false; }
        }
    }

    private static bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in obj.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetStringCI(JsonElement obj, params string[] names)
    {
        foreach (var n in names)
        {
            if (TryGetPropertyCI(obj, n, out var v))
            {
                if (v.ValueKind == JsonValueKind.String)
                    return v.GetString();

                if (v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined)
                    return v.ToString();
            }
        }
        return null;
    }
}

public static class SteamGuardCodeGenerator
{
    // Steam Guard alphabet
    private const string SteamChars = "23456789BCDFGHJKMNPQRTVWXY";

    /// <summary>Generates a 5-char Steam Guard code using shared_secret (base64) and unixTimeSeconds.</summary>
    public static string GenerateCode(string sharedSecretBase64, long unixTimeSeconds)
    {
        // 30-second step
        long timeSlice = unixTimeSeconds / 30;

        byte[] timeBytes = BitConverter.GetBytes(timeSlice);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(timeBytes);

        byte[] secret = Convert.FromBase64String(sharedSecretBase64);

        using var hmac = new HMACSHA1(secret);
        byte[] hash = hmac.ComputeHash(timeBytes);

        int offset = hash[^1] & 0x0F;
        int codePoint =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        var sb = new StringBuilder(5);
        for (int i = 0; i < 5; i++)
        {
            sb.Append(SteamChars[codePoint % SteamChars.Length]);
            codePoint /= SteamChars.Length;
        }

        return sb.ToString();
    }

    /// <summary>Seconds remaining until the next code (1..30).</summary>
    public static int SecondsRemaining(long unixTimeSeconds)
    {
        int sec = (int)(unixTimeSeconds % 30); // 0..29
        return 30 - sec;                       // 30..1
    }
}