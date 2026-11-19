namespace Ardea.Iam.Contracts;

/// <summary>What other modules need to know about an organisation to work for it (master plan 7.8, 4.8).</summary>
/// <param name="Id">The organisation.</param>
/// <param name="Locale">The language of texts produced for it, e.g. <c>de</c>.</param>
/// <param name="AiPolicy">Its data policy for language models: <c>none</c>, <c>local_only</c> or <c>cloud_allowed</c>.</param>
public sealed record OrganizationProfile(Guid Id, string Locale, string AiPolicy)
{
    public string Name { get; init; } = string.Empty;

    /// <summary>The name in the web app's address, <c>/o/{slug}/…</c>.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>IANA time zone for digests and deadlines.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>How much e-mails say (master plan 8.5): <c>full</c>, or <c>minimal</c> for level, number and link only.</summary>
    public string EmailContent { get; init; } = "full";

    /// <summary>Whether members may mute a channel for level 5 too (master plan 8.3).</summary>
    public bool AllowMuteCritical { get; init; }
}

/// <summary>Read access to organisation profiles, inside the organisation's own unit of work.</summary>
public interface IOrganizationProfiles
{
    /// <summary>The organisation, or <c>null</c> when it does not exist or is not the current tenant.</summary>
    Task<OrganizationProfile?> FindAsync(Guid organizationId, CancellationToken cancellationToken);
}
