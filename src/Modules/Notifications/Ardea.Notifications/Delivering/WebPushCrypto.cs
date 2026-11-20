using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Ardea.Notifications.Delivering;

internal static class WebPushEncryption
{
    public const int RecordSize = 4096;

    public const int HeaderLength = 16 + 4 + 1 + 65;

    public const int MaxPlaintext = 4096 - HeaderLength - 1 - 16;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] userAgentPublicKey, byte[] authSecret)
    {
        using var server = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, userAgentPublicKey, authSecret, server, RandomNumberGenerator.GetBytes(16));
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] userAgentPublicKey, byte[] authSecret, ECDiffieHellman server, byte[] salt)
    {
        if (plaintext.Length > MaxPlaintext)
        {
            throw new ArgumentException($"A push message holds at most {MaxPlaintext} bytes.", nameof(plaintext));
        }

        var serverPublic = PublicKey(server);
        using var userAgent = P256.ImportPublic(userAgentPublicKey);
        var secret = server.DeriveRawSecretAgreement(userAgent.PublicKey);

        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), userAgentPublicKey, serverPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, authSecret, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var key = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded);
        padded[^1] = 2;

        var body = new byte[HeaderLength + padded.Length + 16];
        salt.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16, 4), RecordSize);
        body[20] = 65;
        serverPublic.CopyTo(body, 21);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, padded, body.AsSpan(HeaderLength, padded.Length), body.AsSpan(HeaderLength + padded.Length, 16));
        return body;
    }

    public static byte[] PublicKey(ECAlgorithm key)
    {
        var parameters = key.ExportParameters(false);
        return Concat([4], parameters.Q.X!, parameters.Q.Y!);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}

internal static class P256
{
    public static ECDiffieHellman ImportPublic(byte[] point)
    {
        var key = ECDiffieHellman.Create();
        key.ImportParameters(Parameters(point, null));
        return key;
    }

    public static ECParameters Parameters(byte[] point, byte[]? privateKey)
    {
        if (point.Length != 65 || point[0] != 4)
        {
            throw new CryptographicException("A P-256 public key is an uncompressed point of 65 bytes.");
        }

        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..] },
            D = privateKey,
        };
    }

    public static bool IsPublicKey(byte[] point)
    {
        try
        {
            using var key = ImportPublic(point);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

internal static class Vapid
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    public static string Authorization(VapidKeys keys, Uri endpoint, DateTimeOffset now) =>
        $"vapid t={Token(keys, endpoint.GetLeftPart(UriPartial.Authority), now)}, k={WebEncoders.Base64UrlEncode(keys.PublicKey)}";

    public static string Token(VapidKeys keys, string audience, DateTimeOffset now)
    {
        var header = WebEncoders.Base64UrlEncode("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["exp"] = (now + Lifetime).ToUnixTimeSeconds(),
            ["sub"] = keys.Subject,
        }));
        var input = Encoding.ASCII.GetBytes($"{header}.{claims}");
        using var signer = ECDsa.Create(P256.Parameters(keys.PublicKey, keys.PrivateKey));
        var signature = signer.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{claims}.{WebEncoders.Base64UrlEncode(signature)}";
    }
}
