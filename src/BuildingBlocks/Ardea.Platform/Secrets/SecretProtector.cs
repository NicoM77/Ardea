using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Ardea.Platform.Secrets;

public interface ISecretProtector
{
    string Protect(string purpose, string plaintext);

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
