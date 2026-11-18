using System.IO.Compression;
using Ardea.Iam.Domain;
using Microsoft.AspNetCore.Identity;

namespace Ardea.Iam.Security;

/// <summary>
/// Password rules of master plan 9.3: at least 12 characters, no composition rules, and not on the
/// bundled list of common passwords, nor the account's own e-mail address.
/// </summary>
internal sealed class CommonPasswordValidator : IPasswordValidator<ArdeaUser>
{
    public const int MinimumLength = 12;

    private static readonly Lazy<HashSet<string>> Common = new(Load);

    public Task<IdentityResult> ValidateAsync(UserManager<ArdeaUser> manager, ArdeaUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var normalized = password.ToLowerInvariant();
        if (IsCommon(normalized) || (user.Email is { } email && normalized == email.ToLowerInvariant()))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "CommonPassword",
                Description = "This password is too common or too easy to guess. Choose a longer or less predictable one.",
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }

    internal static bool IsCommon(string password) => Common.Value.Contains(password.ToLowerInvariant());

    private static HashSet<string> Load()
    {
        using var stream = typeof(CommonPasswordValidator).Assembly.GetManifestResourceStream("Ardea.Iam.common-passwords.txt.gz")
            ?? throw new InvalidOperationException("The list of common passwords is missing from the assembly.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                set.Add(line);
            }
        }

        return set;
    }
}
