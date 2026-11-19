using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ardea.Iam.Security;

/// <summary>
/// RFC 6238 time-based codes as authenticator apps make them (HMAC-SHA1, 30-second steps, six digits),
/// computed the way ASP.NET Core Identity's <c>AuthenticatorTokenProvider</c> does, but returning the time
/// step a code belongs to instead of a yes or no, so the caller can refuse a step that was already used.
/// </summary>
internal static class AuthenticatorCode
{
    /// <summary>Length of a time step in seconds.</summary>
    public const int StepSeconds = 30;

    /// <summary>
    /// Clock drift allowed between the server and the authenticator app, in steps either way: ±90 seconds,
    /// the same window as Identity's own provider.
    /// </summary>
    public const int DriftSteps = 2;

    private const int Digits = 6;
    private const int Modulus = 1_000_000;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>The time step a point in time falls into.</summary>
    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>
    /// The time step within the drift window around <paramref name="now"/> whose code is <paramref name="code"/>;
    /// the latest one if several match. <c>null</c> for a wrong code, a malformed code or an unreadable key.
    /// </summary>
    public static long? MatchingStep(string base32Key, string? code, DateTimeOffset now)
    {
        if (!TryParse(code, out var value) || Base32(base32Key) is not { Length: > 0 } key)
        {
            return null;
        }

        var current = StepAt(now);
        long? matched = null;
        // Every step of the window is computed, so the time taken does not tell which one matched.
        for (var step = current - DriftSteps; step <= current + DriftSteps; step++)
        {
            if (Compute(key, step) == value)
            {
                matched = step;
            }
        }

        CryptographicOperations.ZeroMemory(key);
        return matched;
    }

    /// <summary>The code of one time step (RFC 4226 dynamic truncation).</summary>
    internal static int Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(key, counter, hash);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return binary % Modulus;
    }

    private static bool TryParse(string? code, out int value)
    {
        value = 0;
        if (code is null || code.Length != Digits)
        {
            return false;
        }

        foreach (var c in code)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        return true;
    }

    /// <summary>RFC 4648 base32 without padding, as Identity stores the key; <c>null</c> for any other character.</summary>
    private static byte[]? Base32(string input)
    {
        var output = new byte[input.Length * 5 / 8];
        var bits = 0;
        var buffer = 0;
        var index = 0;
        foreach (var c in input)
        {
            var digit = Base32Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal);
            if (digit < 0)
            {
                return null;
            }

            // At most 12 bits are pending: up to 7 left over plus the 5 of this character.
            buffer = ((buffer << 5) | digit) & 0xfff;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[index++] = (byte)(buffer >> bits);
            }
        }

        return output;
    }
}
