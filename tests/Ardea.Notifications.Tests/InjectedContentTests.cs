using System.Text.Json;
using System.Text.RegularExpressions;
using Ardea.Iam.Contracts;
using Ardea.Notifications.Messages;
using Ardea.Notifications.Rules;

namespace Ardea.Notifications.Tests;

public sealed partial class InjectedContentTests
{
    private const string Title = "<script>alert(1)</script><a href=\"javascript:alert(2)\">Patch now</a> <img src=x onerror=alert(3)>";
    private const string Key = "//evil.example/<b>x</b>?a=1";
    private static readonly Uri PublicUrl = new("https://ardea.example/");

    [Fact]
    public void The_e_mail_encodes_a_hostile_title_and_links_only_to_the_instance()
    {
        var mail = MessageRenderer.Email(Content(), "member@example.org", new Uri(PublicUrl, "/account/notifications"), null);

        Assert.DoesNotContain("<script", mail.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", mail.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", mail.Html, StringComparison.Ordinal);
        var links = Href().Matches(mail.Html).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(links);
        Assert.All(links, link => Assert.StartsWith(PublicUrl.AbsoluteUri, link, StringComparison.Ordinal));
    }

    [Fact]
    public void Push_webhook_and_in_app_keep_the_title_as_text_and_point_to_the_instance()
    {
        var content = Content();

        var push = JsonDocument.Parse(MessageRenderer.Push(content)).RootElement.GetProperty("notification");
        Assert.StartsWith(PublicUrl.AbsoluteUri, push.GetProperty("navigate").GetString(), StringComparison.Ordinal);
        Assert.Contains(Title, push.GetProperty("title").GetString(), StringComparison.Ordinal);

        var webhook = JsonDocument.Parse(MessageRenderer.Webhook(content)).RootElement.GetProperty("events")[0];
        Assert.StartsWith(PublicUrl.AbsoluteUri, webhook.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.Equal(Title, webhook.GetProperty("warning").GetProperty("title").GetString());

        var inApp = MessageRenderer.InApp(content.Items[0], content);
        Assert.StartsWith("/entries/%2F%2Fevil.example", inApp.Link, StringComparison.Ordinal);
    }

    private static MessageContent Content() => new()
    {
        DeliveryId = Guid.CreateVersion7(),
        Kind = "immediate",
        Organization = new OrganizationProfile(Guid.CreateVersion7(), "en", "none") { Name = "Acme", Slug = "acme" },
        Locale = "en",
        PublicUrl = PublicUrl,
        Items =
        [
            new MessageItem
            {
                EventId = Guid.CreateVersion7(),
                Event = NotifyEvents.WarningPublished,
                At = DateTimeOffset.Parse("2026-11-19T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
                Warning = new WarningFacts(Guid.CreateVersion7(), Key, Title, null),
            },
        ],
    };

    [GeneratedRegex("href=\"([^\"]*)\"")]
    private static partial Regex Href();
}
