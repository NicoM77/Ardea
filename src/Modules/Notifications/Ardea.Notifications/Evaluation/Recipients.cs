using Ardea.Iam.Contracts;
using Ardea.Notifications.Persistence;
using Ardea.Notifications.Rules;
using Ardea.Platform.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Notifications.Evaluation;

/// <summary>A resolved recipient: a member, a confirmed external address or a webhook endpoint.</summary>
/// <param name="Key">The <c>recipient_key</c>.</param>
/// <param name="Kind"><c>user</c>, <c>address</c> or <c>endpoint</c>.</param>
/// <param name="Id">The user, address or endpoint.</param>
/// <param name="Email">Where e-mail goes, for members and addresses.</param>
/// <param name="Name">The member's name, the address or the endpoint's name.</param>
internal sealed record Recipient(string Key, string Kind, Guid Id, string? Email, string Name)
{
    public Guid? UserId => Kind == RecipientKinds.User ? Id : null;
}

/// <summary>
/// The organisation's recipients, read once per job inside its unit of work (master plan 8.3, 9.2): members
/// from the member directory of Iam, confirmed external addresses, enabled endpoints and the members' own
/// settings. Recipients are never resolved across organisations.
/// </summary>
internal sealed class RecipientDirectory
{
    private readonly Dictionary<Guid, OrganizationMember> _members;
    private readonly Dictionary<Guid, ExternalAddress> _addresses;
    private readonly Dictionary<Guid, WebhookEndpoint> _endpoints;
    private readonly Dictionary<Guid, UserPreference> _preferences;
    private readonly HashSet<Guid> _withPush;

    private RecipientDirectory(
        IReadOnlyList<OrganizationMember> members, IReadOnlyList<ExternalAddress> addresses, IReadOnlyList<WebhookEndpoint> endpoints, IReadOnlyList<UserPreference> preferences,
        IReadOnlyList<Guid> withPush)
    {
        _members = members.ToDictionary(m => m.UserId);
        _addresses = addresses.ToDictionary(a => a.Id);
        _endpoints = endpoints.ToDictionary(e => e.Id);
        _preferences = preferences.ToDictionary(p => p.UserId);
        _withPush = [.. withPush];
    }

    public IEnumerable<OrganizationMember> Members => _members.Values;

    public static async Task<RecipientDirectory> LoadAsync(NotifyDbContext db, IOrganizationMembers members, CancellationToken cancellationToken) => new(
        await members.ListAsync(cancellationToken),
        await db.Addresses.AsNoTracking().Where(a => a.ConfirmedAt != null && a.UnsubscribedAt == null).ToListAsync(cancellationToken),
        await db.Endpoints.AsNoTracking().Where(e => e.Enabled).ToListAsync(cancellationToken),
        await db.Preferences.AsNoTracking().ToListAsync(cancellationToken),
        // Through the worker's policy: only members of the job's organisation (ADR 041).
        await db.PushSubscriptions.AsNoTracking().Select(s => s.UserId).Distinct().ToListAsync(cancellationToken));

    /// <summary>
    /// Who a target names now. A role means that role and the ones above it; a member who left is nobody. On
    /// push, only members with at least one subscribed device: the others are not reached, not failed.
    /// </summary>
    public IEnumerable<Recipient> Resolve(RuleTarget target) =>
        target.Channel == Channels.Push ? Named(target).Where(r => HasPush(r.Id)) : Named(target);

    /// <summary>Whether the member subscribed at least one device to Web Push.</summary>
    public bool HasPush(Guid userId) => _withPush.Contains(userId);

    private IEnumerable<Recipient> Named(RuleTarget target)
    {
        switch (target.RecipientKind)
        {
            case RecipientKinds.Role when OrganizationRoles.TryParse(target.RecipientRef, out var minimum):
                return _members.Values.Where(m => OrganizationRoles.TryParse(m.Role, out var role) && role >= minimum).Select(Of);
            case RecipientKinds.User when Guid.TryParse(target.RecipientRef, out var user) && _members.TryGetValue(user, out var member):
                return [Of(member)];
            case RecipientKinds.Address when Guid.TryParse(target.RecipientRef, out var id) && _addresses.TryGetValue(id, out var address):
                return [new Recipient(RecipientKeys.Address(id), RecipientKinds.Address, id, address.Address, address.Address)];
            case RecipientKinds.Endpoint when Guid.TryParse(target.RecipientRef, out var id) && _endpoints.TryGetValue(id, out var endpoint):
                return [new Recipient(RecipientKeys.Endpoint(id), RecipientKinds.Endpoint, id, null, endpoint.Name)];
            default:
                return [];
        }
    }

    public Recipient? User(Guid userId) => _members.TryGetValue(userId, out var member) ? Of(member) : null;

    public IEnumerable<Recipient> WithRole(OrganizationRole minimum) =>
        _members.Values.Where(m => OrganizationRoles.TryParse(m.Role, out var role) && role >= minimum).Select(Of);

    /// <summary>
    /// A member muted the channel for themselves (master plan 8.3). Level 5 gets through unless the
    /// organisation allows muting it too.
    /// </summary>
    public bool IsMuted(Recipient recipient, string channel, int? level, bool allowMuteCritical) =>
        recipient.UserId is { } user && _preferences.TryGetValue(user, out var preference) && preference.MutedChannels.Contains(channel)
        && (level != 5 || allowMuteCritical);

    /// <summary>The language of messages to the recipient: the member's own, else the organisation's.</summary>
    public string Locale(Recipient recipient, string organizationLocale) =>
        recipient.UserId is { } user && _preferences.TryGetValue(user, out var preference) && preference.Locale is { Length: > 0 } locale ? locale : organizationLocale;

    private static Recipient Of(OrganizationMember member) =>
        new(RecipientKeys.User(member.UserId), RecipientKinds.User, member.UserId, member.Email, member.DisplayName);
}
