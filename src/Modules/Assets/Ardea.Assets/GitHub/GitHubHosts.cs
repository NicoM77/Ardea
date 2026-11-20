using System.Net;
using System.Text.Json;
using Ardea.Platform.Http;
using Ardea.Platform.Settings;

namespace Ardea.Assets.GitHub;

internal sealed record EnterpriseHost(string Id, string Name, string ApiUrl);

internal sealed class GitHubHosts(IInstanceSettings settings)
{
    public static readonly SettingDefinition Setting = new("assets.github_enterprise_hosts");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<EnterpriseHost>> ListAsync(CancellationToken cancellationToken) =>
        (await settings.GetAsync(Setting, cancellationToken)).Value is { Length: > 0 } json
            ? JsonSerializer.Deserialize<List<EnterpriseHost>>(json, Json) ?? []
            : [];

    public async Task SaveAsync(IReadOnlyList<EnterpriseHost> hosts, Guid? changedBy, CancellationToken cancellationToken) =>
        await settings.SetAsync(Setting, hosts.Count == 0 ? null : JsonSerializer.Serialize(hosts, Json), changedBy, cancellationToken);

    public async Task<GitHubHost?> ResolveAsync(string? hostId, CancellationToken cancellationToken)
    {
        if (hostId is null)
        {
            return GitHubHost.GitHubCom;
        }

        var host = (await ListAsync(cancellationToken)).FirstOrDefault(h => h.Id == hostId);
        return host is null ? null : new GitHubHost(host.Id, new Uri(host.ApiUrl));
    }

    public static async Task<(Uri? Url, string? Error)> CheckAddressAsync(string? text, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            return (null, "Enter the HTTPS address of the API, e.g. https://github.example.com/api/v3/.");
        }

        if (url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
        {
            return (null, "The address must not contain credentials, a query or a fragment.");
        }

        if (url.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) || url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "github.com needs no registration.");
        }

        try
        {
            var addresses = IPAddress.TryParse(url.Host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(url.Host, cancellationToken);
            if (addresses.Length == 0 || addresses.Any(OutboundAddresses.IsForbidden))
            {
                return (null, "The host resolves to an address Ardea must not call.");
            }
        }
        catch (System.Net.Sockets.SocketException)
        {
            return (null, "The host name cannot be resolved.");
        }

        var normalized = url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
        return (normalized, null);
    }
}
