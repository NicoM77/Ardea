namespace Ardea.Iam.Contracts;

public sealed record OrganizationProfile(Guid Id, string Locale, string AiPolicy)
{
    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string TimeZone { get; init; } = "UTC";

    public string EmailContent { get; init; } = "full";

    public bool AllowMuteCritical { get; init; }
}

public interface IOrganizationProfiles
{
    Task<OrganizationProfile?> FindAsync(Guid organizationId, CancellationToken cancellationToken);
}
