using System.Security.Cryptography;
using System.Text;
using Ardea.Iam.Persistence;
using Ardea.Platform.Persistence;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ardea.Iam.Features;

/// <summary>The membership check in front of every organisation endpoint, inside the request's unit of work.</summary>
internal sealed class MembershipCheck(IamDbContext db) : IOrganizationMembership
{
    public async Task<OrganizationAccess?> FindAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        // The tenant filter limits both queries to the organisation of the route.
        var role = await db.Memberships.Where(m => m.UserId == userId).Select(m => m.Role).SingleOrDefaultAsync(cancellationToken);
        if (role is null || !OrganizationRoles.TryParse(role, out var parsed))
        {
            return null;
        }

        var settings = await db.Organizations.Select(o => o.Settings).SingleAsync(cancellationToken);
        var hasMfa = await db.Users.Where(u => u.Id == userId).Select(u => u.TwoFactorEnabled).SingleAsync(cancellationToken);
        return new OrganizationAccess(organizationId, userId, parsed, settings.RequireMfa, hasMfa);
    }
}

/// <summary>An organisation as its member sees it in lists and in the session.</summary>
internal sealed record OrganizationSummary(Guid Id, string Slug, string Name, string Role, bool MfaRequired);

/// <summary>
/// The two questions about organisations that come before an organisation context exists (master
/// plan 9.2, system operations): which organisations a user belongs to, and which organisation an
/// invitation token belongs to. Each is one narrow query.
/// </summary>
internal static class OrganizationDirectory
{
    /// <summary>The user's own memberships (the user rule on <c>membership</c>), for the session and the switcher.</summary>
    public static async Task<IReadOnlyList<OrganizationSummary>> ForUserAsync(IamDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Memberships.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).Where(m => m.UserId == userId)
            .Join(db.Organizations.IgnoreQueryFilters([ModuleDbContext.TenantFilter]), m => m.OrganizationId, o => o.OrganizationId, (m, o) => new { o.OrganizationId, o.Slug, o.Name, m.Role, o.Settings })
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Select(r => new OrganizationSummary(r.OrganizationId, r.Slug, r.Name, r.Role, r.Settings.RequireMfa))];
    }

    /// <summary>
    /// The organisation of an open invitation, on a connection of its own: it runs before the unit of
    /// work, whose organisation it determines. With <paramref name="normalizedEmail"/> only an
    /// invitation for that address counts.
    /// </summary>
    public static async Task<Guid?> OrganizationOfTokenAsync(NpgsqlDataSource dataSource, string token, string? normalizedEmail, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT organization_id FROM iam.invitation WHERE token_hash = $1 AND expires_at > now() AND ($2::text IS NULL OR normalized_email = $2)");
        command.Parameters.Add(new NpgsqlParameter { Value = HashToken(token) });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)normalizedEmail ?? DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text });
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    public static string NewToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim())));

    /// <summary>A slug from a name: lower-case letters, digits and hyphens, at most 50 characters.</summary>
    public static string Slugify(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        slug = slug.Length > 50 ? slug[..50].TrimEnd('-') : slug;
        return slug.Length == 0 ? "org" : slug;
    }

    public static bool IsValidSlug(string slug) =>
        System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]([a-z0-9-]{0,48}[a-z0-9])?$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
}
