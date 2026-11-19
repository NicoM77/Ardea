using System.Security.Cryptography;
using System.Text.Json;
using Ardea.Platform.Configuration;
using Ardea.Platform.Persistence;
using Ardea.Platform.Secrets;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ardea.Notifications.Delivering;

/// <summary>The instance's VAPID key pair (RFC 8292) and the contact the push services see.</summary>
/// <param name="PublicKey">The uncompressed P-256 point, 65 bytes; browsers subscribe with it.</param>
/// <param name="PrivateKey">The private scalar, 32 bytes.</param>
/// <param name="Subject">A <c>mailto:</c> or <c>https:</c> contact for the push services.</param>
internal sealed record VapidKeys(byte[] PublicKey, byte[] PrivateKey, string Subject);

/// <summary>Configuration of Web Push (master plan 8.5, 12.4); checked when API and worker start.</summary>
internal static class PushConfiguration
{
    /// <summary><c>ARDEA_VAPID__PUBLIC_KEY</c>: base64url of the uncompressed point (65 bytes).</summary>
    public const string PublicKeyKey = "VAPID:PUBLIC_KEY";

    /// <summary><c>ARDEA_VAPID__PRIVATE_KEY</c>: base64url of the private scalar (32 bytes).</summary>
    public const string PrivateKeyKey = "VAPID:PRIVATE_KEY";

    /// <summary><c>ARDEA_VAPID__SUBJECT</c>: <c>mailto:</c> or <c>https:</c>; the public address by default.</summary>
    public const string SubjectKey = "VAPID:SUBJECT";

    /// <summary><c>ARDEA_PUSH_SERVICE_HOSTS</c>: further push services subscriptions may name, e.g. a self-hosted one.</summary>
    public const string ServiceHostsKey = "PUSH_SERVICE_HOSTS";

    /// <summary>What is wrong with the VAPID variables: both keys or neither, of the right length, and one pair.</summary>
    public static IReadOnlyList<string> Errors(IConfiguration configuration)
    {
        var errors = new List<string>();
        var subject = configuration[SubjectKey];
        if (!string.IsNullOrWhiteSpace(subject) && !IsSubject(subject))
        {
            errors.Add($"{ArdeaConfiguration.VariableName(SubjectKey)} must start with mailto: or https:, got '{subject}'.");
        }

        var publicKey = configuration[PublicKeyKey];
        var privateKey = configuration[PrivateKeyKey];
        if (string.IsNullOrWhiteSpace(publicKey) && string.IsNullOrWhiteSpace(privateKey))
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            errors.Add($"{ArdeaConfiguration.VariableName(PublicKeyKey)} and {ArdeaConfiguration.VariableName(PrivateKeyKey)} are set together or not at all; without them the instance generates its own pair.");
            return errors;
        }

        if (Decode(publicKey) is not { Length: 65 } point || point[0] != 4 || !P256.IsPublicKey(point))
        {
            errors.Add($"{ArdeaConfiguration.VariableName(PublicKeyKey)} must be an uncompressed P-256 point (65 bytes) encoded as base64url.");
            return errors;
        }

        if (Decode(privateKey) is not { Length: 32 } scalar || !Matches(point, scalar))
        {
            errors.Add($"{ArdeaConfiguration.VariableName(PrivateKeyKey)} must be the 32-byte private key, base64url, that belongs to {ArdeaConfiguration.VariableName(PublicKeyKey)}.");
        }

        return errors;
    }

    /// <summary>The configured pair, or <c>null</c> when the instance keeps its own in the database.</summary>
    internal static VapidKeys? FromConfiguration(IConfiguration configuration) =>
        Decode(configuration[PublicKeyKey]) is { Length: 65 } point && Decode(configuration[PrivateKeyKey]) is { Length: 32 } scalar
            ? new VapidKeys(point, scalar, Subject(configuration))
            : null;

    internal static string Subject(IConfiguration configuration) =>
        configuration[SubjectKey] is { Length: > 0 } subject && IsSubject(subject) ? subject.Trim() : Evaluation.Evaluator.PublicUrl(configuration).GetLeftPart(UriPartial.Authority);

    internal static IReadOnlyList<string> ServiceHosts(IConfiguration configuration) => Platform.Settings.InstanceSettingDefinitions.Hosts(configuration[ServiceHostsKey]);

    private static bool IsSubject(string value) =>
        value.Trim().StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || value.Trim().StartsWith("https:", StringComparison.OrdinalIgnoreCase);

    internal static byte[]? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return WebEncoders.Base64UrlDecode(value.Trim().TrimEnd('='));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>A signature made with the private key verifies with the public one: the two are one pair.</summary>
    private static bool Matches(byte[] point, byte[] scalar)
    {
        try
        {
            using var signer = ECDsa.Create(P256.Parameters(point, scalar));
            using var verifier = ECDsa.Create(P256.Parameters(point, null));
            var data = "ardea"u8.ToArray();
            return verifier.VerifyData(data, signer.SignData(data, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

/// <summary>
/// The VAPID key pair, loaded once per process (master plan 8.5, 9.4). From the configuration when it is set,
/// else from <c>platform.setting</c>, encrypted, both halves in one value. The first process to start writes a
/// new pair if none exists; another process racing it inserts nothing and reads the same pair.
/// </summary>
internal sealed class VapidKeyStore(IServiceScopeFactory scopes, ISecretProtector secrets, IConfiguration configuration) : IDisposable
{
    public const string SettingKey = "notify.vapid_keys";
    public const string Purpose = "notify.vapid-keys";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private VapidKeys? _keys;

    public async Task<VapidKeys> GetAsync(CancellationToken cancellationToken)
    {
        if (_keys is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _keys ??= PushConfiguration.FromConfiguration(configuration) ?? await LoadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>Insert if absent, then read: whatever was there first wins (a short unit of work of its own).</summary>
    private async Task<VapidKeys> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        var fresh = JsonSerializer.Serialize(new StoredPair(
            WebEncoders.Base64UrlEncode(WebPushEncryption.PublicKey(key)), WebEncoders.Base64UrlEncode(parameters.D!)));
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO platform.setting (key, value, secret_value, updated_at, updated_by) VALUES ($1, NULL, $2, now(), NULL) ON CONFLICT (key) DO NOTHING", connection))
        {
            insert.Parameters.Add(new NpgsqlParameter { Value = SettingKey });
            insert.Parameters.Add(new NpgsqlParameter { Value = secrets.Protect(Purpose, fresh) });
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        string? stored;
        await using (var select = new NpgsqlCommand("SELECT secret_value FROM platform.setting WHERE key = $1", connection))
        {
            select.Parameters.Add(new NpgsqlParameter { Value = SettingKey });
            stored = await select.ExecuteScalarAsync(cancellationToken) as string;
        }

        await unitOfWork.CommitAsync(cancellationToken);
        var plain = stored is null ? null : secrets.Unprotect(Purpose, stored);
        var pair = plain is null ? null : JsonSerializer.Deserialize<StoredPair>(plain);
        if (pair is null || PushConfiguration.Decode(pair.PublicKey) is not { Length: 65 } point || PushConfiguration.Decode(pair.PrivateKey) is not { Length: 32 } scalar)
        {
            throw new InvalidOperationException("The stored VAPID key pair cannot be read; was the master key changed?");
        }

        return new VapidKeys(point, scalar, PushConfiguration.Subject(configuration));
    }

    private sealed record StoredPair(string PublicKey, string PrivateKey);
}
