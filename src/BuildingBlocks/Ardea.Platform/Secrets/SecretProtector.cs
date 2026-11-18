using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Ardea.Platform.Secrets;

/// <summary>
/// Encrypts secrets for storage: SMTP password, AI keys, webhook URLs and secrets, VAPID key, GitHub
/// tokens (master plan 9.4). The purpose separates kinds of secrets, so one cannot be decrypted as
/// another. Secrets never appear in API responses or logs.
/// </summary>
public interface ISecretProtector
{
    string Protect(string purpose, string plaintext);

    /// <summary>Returns <c>null</c> when the value cannot be decrypted (wrong master key, tampered value).</summary>
    string? Unprotect(string purpose, string protectedValue);
}

internal sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private const string Root = "Ardea.Secrets.v1";

    public string Protect(string purpose, string plaintext) => provider.CreateProtector(Root, purpose).Protect(plaintext);

    public string? Unprotect(string purpose, string protectedValue)
    {
        try
        {
            return provider.CreateProtector(Root, purpose).Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
