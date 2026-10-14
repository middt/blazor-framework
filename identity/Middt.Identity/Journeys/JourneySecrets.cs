using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Middt.Identity.Journeys;

/// <summary>Random secrets, hashes and constant-time comparison for journeys.</summary>
internal static class JourneySecrets
{
    /// <summary>256-bit random value, base64url (cookie safe).</summary>
    public static string NewSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Compares two hex hashes without leaking the position of the first difference.</summary>
    public static bool HashEquals(string? expectedHex, string actualHex) =>
        expectedHex is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedHex), Encoding.ASCII.GetBytes(actualHex));

    /// <summary>6 digit one-time code.</summary>
    public static string NewNumericCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>RFC 6238 TOTP (SHA-1, 6 digits, 30 s) compatible with ASP.NET Core Identity's authenticator keys (Base32).</summary>
internal static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>New 160-bit key in Base32 (the format of <c>UserManager.GetAuthenticatorKeyAsync</c>).</summary>
    public static string NewKey() => ToBase32(RandomNumberGenerator.GetBytes(20));

    public static string Compute(string base32Key, DateTimeOffset time) => Compute(FromBase32(base32Key), time.ToUnixTimeSeconds() / 30);

    /// <summary>Accepts the codes of the previous, current and next 30 s step (clock skew).</summary>
    public static bool Verify(string base32Key, string? code, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        code = code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var key = FromBase32(base32Key);
        var step = now.ToUnixTimeSeconds() / 30;
        var match = false;
        for (var offset = -1; offset <= 1; offset++)
        {
            match |= CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(key, step + offset)), Encoding.ASCII.GetBytes(code));
        }

        return match;
    }

    private static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 6238 authenticator apps use HMAC-SHA1
        HMACSHA1.HashData(key, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static string ToBase32(byte[] data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return builder.ToString();
    }

    public static byte[] FromBase32(string text)
    {
        var clean = text.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Invalid Base32 character.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}

/// <summary>Names of journey data values. Keys starting with <c>_</c> are internal and never returned to the UI.</summary>
internal static class JourneyData
{
    public const string BindingHash = "_bindingHash";
    public const string Consumed = "_consumed";
    public const string CodeHash = "_codeHash";
    public const string CodeSalt = "_codeSalt";
    public const string CodeExpiresAt = "_codeExpiresAt";
    public const string CodeUserId = "_codeUserId";
    public const string TotpKeyProtected = "_totpKeyProtected";

    /// <summary>Validated input of the last submit (merged by the workflow engine; never contains sensitive fields).</summary>
    public const string Form = "form";

    public const string ReturnUrl = "returnUrl";
    public const string ClientId = "clientId";
    public const string ClientName = "clientName";
    public const string Scopes = "scopes";
    public const string ScopeDetails = "scopeDetails";
    public const string UserId = "userId";
    public const string UserName = "userName";
    public const string DisplayName = "displayName";
    public const string Email = "email";
    public const string Error = "error";
    public const string FailedAttempts = "failedAttempts";
    public const string AttemptsExceeded = "attemptsExceeded";
    public const string ConsentOnly = "consentOnly";
    public const string ForceConsent = "forceConsent";
    public const string ConsentDenied = "consentDenied";
    public const string TotpKey = "totpKey";
    public const string TotpUri = "totpUri";

    /// <summary>Values only the service and the tasks may set: never copied from <see cref="AuthJourneyStart.Data"/>.</summary>
    public static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        ReturnUrl, ClientId, ClientName, Scopes, ScopeDetails, UserId, Error, FailedAttempts, AttemptsExceeded, ConsentOnly,
        ForceConsent, ConsentDenied, TotpKey, TotpUri, Form, "authenticated", "mfaRequired", "mfaPassed", "consentRequired",
        "created", "codeValid", "passwordReset", "totpEnabled", "registrationAllowed", "requireConfirmedEmail", "redirect",
        "codeAttempts", "resendCount", "codeSentTo",
    };
}
