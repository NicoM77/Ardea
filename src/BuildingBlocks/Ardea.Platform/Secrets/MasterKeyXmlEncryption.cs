using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Secrets;

/// <summary>
/// The master key from <c>ARDEA_MASTER_KEY</c>: 32 bytes that never enter the database. It encrypts
/// the Data Protection keys at rest, so a database dump alone cannot decrypt stored secrets
/// (master plan 9.4, 12.5).
/// </summary>
public sealed class MasterKey
{
    public MasterKey(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("The master key must be 32 bytes.", nameof(key));
        }

        Key = key;
        // A short fingerprint lets operators see whether two instances share a key, without revealing it.
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(key))[..12];
    }

    internal byte[] Key { get; }

    public string Fingerprint { get; }

    public static MasterKey FromBase64(string value) => new(Convert.FromBase64String(value));
}

/// <summary>Encrypts Data Protection key XML with AES-256-GCM under the master key.</summary>
internal sealed class MasterKeyXmlEncryptor(MasterKey masterKey) : IXmlEncryptor
{
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = System.Text.Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(masterKey.Key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var element = new XElement("encryptedKey",
            new XAttribute("fingerprint", masterKey.Fingerprint),
            new XElement("nonce", Convert.ToBase64String(nonce)),
            new XElement("tag", Convert.ToBase64String(tag)),
            new XElement("value", Convert.ToBase64String(ciphertext)));
        return new EncryptedXmlInfo(element, typeof(MasterKeyXmlDecryptor));
    }
}

/// <summary>Created by Data Protection through its service provider.</summary>
internal sealed class MasterKeyXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var masterKey = services.GetRequiredService<MasterKey>();
        var fingerprint = encryptedElement.Attribute("fingerprint")?.Value;
        if (fingerprint is not null && fingerprint != masterKey.Fingerprint)
        {
            throw new CryptographicException(
                $"Stored secrets were encrypted with another master key (fingerprint {fingerprint}, configured {masterKey.Fingerprint}). Set ARDEA_MASTER_KEY to the key of this database.");
        }

        var nonce = Convert.FromBase64String(encryptedElement.Element("nonce")!.Value);
        var tag = Convert.FromBase64String(encryptedElement.Element("tag")!.Value);
        var ciphertext = Convert.FromBase64String(encryptedElement.Element("value")!.Value);
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(masterKey.Key, MasterKeyXmlEncryptor.TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return XElement.Parse(System.Text.Encoding.UTF8.GetString(plaintext));
    }
}
