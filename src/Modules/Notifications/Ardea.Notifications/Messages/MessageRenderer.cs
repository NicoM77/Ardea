using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Ardea.Assessment.Contracts;
using Ardea.Notifications.Rules;
using Ardea.Platform.Email;

namespace Ardea.Notifications.Messages;

/// <summary>An in-app notification's text.</summary>
internal sealed record InAppText(string Title, string Body, string? Link);

/// <summary>
/// Words messages (master plan 8.5): every message answers what happened, whether we are affected and what to
/// do, in that order, like the interface. The subject starts with level and number. Without HTML from
/// sources: every value is encoded.
/// </summary>
internal static class MessageRenderer
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static InAppText InApp(MessageItem item, MessageContent content)
    {
        var texts = MessageTexts.For(content.Locale);
        return new InAppText(Truncate(texts.Title(item), 300), Truncate(texts.Headline(item, content.RecipientUserId), 2000), content.PathOf(item));
    }

    /// <summary>A digest in the app: its subject, and its first lines as the body.</summary>
    public static InAppText InAppDigest(MessageContent content)
    {
        var texts = MessageTexts.For(content.Locale);
        var (subject, _) = Digest(content, texts, minimal: false);
        var body = content.Items.Count == 0
            ? texts["digest.empty"]
            : string.Join("; ", content.Items.OrderByDescending(i => i.Level ?? 0).Take(5).Select(texts.Title));
        return new InAppText(Truncate(subject, 300), Truncate(body, 2000), $"/o/{content.Organization.Slug}/actions");
    }

    /// <summary>The e-mail; <paramref name="settingsOrUnsubscribe"/> is the member's settings or the signed unsubscribe page of an external address, <paramref name="oneClick"/> its RFC 8058 address.</summary>
    public static EmailMessage Email(MessageContent content, string to, Uri settingsOrUnsubscribe, Uri? oneClick)
    {
        var texts = MessageTexts.For(content.Locale);
        var minimal = content.Organization.EmailContent == "minimal";
        var (subject, blocks) = content.Kind == "digest" ? Digest(content, texts, minimal) : Immediate(content, texts, minimal);
        var footer = oneClick is null
            ? texts.Format("footer.member", content.Organization.Name, settingsOrUnsubscribe.AbsoluteUri)
            : texts.Format("footer.address", content.Organization.Name, settingsOrUnsubscribe.AbsoluteUri);

        var text = new StringBuilder();
        var html = new StringBuilder();
        foreach (var block in blocks)
        {
            text.Append(block.Text()).Append("\n\n");
            html.Append(block.Html());
        }

        text.Append("--\n").Append(footer).Append('\n');
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["List-Unsubscribe"] = $"<{(oneClick ?? settingsOrUnsubscribe).AbsoluteUri}>",
            ["Auto-Submitted"] = "auto-generated",
        };
        if (oneClick is not null)
        {
            headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
        }

        var body = $"""
            <!doctype html>
            <html lang="{texts.Locale}"><body style="margin:0;padding:24px;background:#f1f4f6;font-family:system-ui,-apple-system,'Segoe UI',sans-serif;color:#131416">
            <table role="presentation" style="max-width:600px;margin:0 auto;background:#ffffff;border:1px solid #e3e8eb;border-radius:8px;padding:32px">
            <tr><td>
            <p style="margin:0 0 16px;font-size:16px;font-weight:600">Ardea</p>
            {html}
            <p style="margin:24px 0 0;font-size:12px;color:#53595d">{Encode(footer)}</p>
            </td></tr></table></body></html>
            """;
        return new EmailMessage(to, subject, text.ToString(), body, headers);
    }

    /// <summary>The webhook's body, schema <c>ardea.notification.v1</c> (master plan 8.5).</summary>
    public static string Webhook(MessageContent content)
    {
        var texts = MessageTexts.For(content.Locale);
        var payload = new
        {
            schema = "ardea.notification.v1",
            id = content.DeliveryId,
            kind = content.Kind,
            organization = new { id = content.Organization.Id, slug = content.Organization.Slug, name = content.Organization.Name },
            periodStart = content.PeriodStart,
            periodEnd = content.PeriodEnd,
            events = content.Items.Select(i => new
            {
                id = i.EventId,
                @event = i.Event,
                occurredAt = i.At,
                level = i.Level,
                title = texts.Title(i),
                summary = texts.Headline(i, null),
                url = content.PathOf(i) is { } path ? content.Link(path).AbsoluteUri : null,
                alreadyReported = i.AlreadyReported ? true : (bool?)null,
                newFindings = i.NewFindings > 0 ? i.NewFindings : (int?)null,
                action = i.Action is { } a
                    ? new
                    {
                        number = a.Number,
                        level = a.Level,
                        status = a.Status,
                        project = new { id = a.ProjectId, name = a.ProjectName },
                        component = new { name = a.ComponentName, version = a.ComponentVersion },
                        openFindings = a.OpenFindings,
                        exploitation = a.Exploitation,
                        fixAvailable = a.FixAvailable,
                        targetVersion = a.TargetVersion,
                        entries = a.Entries.Select(e => new { key = e.Key, title = e.Title, level = e.Level }),
                    }
                    : null,
                warning = i.Warning is { } w ? new { key = w.Key, title = w.Title, publishedAt = w.PublishedAt } : null,
                backfill = i.Backfill is { } b ? new { project = new { id = b.ProjectId, name = b.ProjectName }, actions = b.Actions, highestLevel = b.HighestLevel, findings = b.Findings } : null,
                data = i.Data.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : i.Data,
            }),
        };
        return JsonSerializer.Serialize(payload, Json);
    }

    private static (string Subject, List<Block> Blocks) Immediate(MessageContent content, MessageTexts texts, bool minimal)
    {
        var subjects = content.Items.GroupBy(SubjectKey).ToList();
        var blocks = new List<Block>();
        string subject;
        if (subjects.Count == 1)
        {
            var items = subjects[0].OrderBy(i => i.At).ToList();
            var last = items[^1];
            subject = minimal && last.Action is { } a ? texts.Format("title.action", a.Level, texts.LevelName(a.Level), a.Number, string.Empty).TrimEnd() : texts.Title(last);
            blocks.AddRange(Describe(items, content, texts, minimal));
        }
        else
        {
            var level = content.Level ?? 0;
            subject = texts.Format("title.several", level, texts.LevelName(level), subjects.Count, content.Organization.Name);
            foreach (var group in subjects.OrderByDescending(g => g.Max(i => i.Level ?? 0)))
            {
                var items = group.OrderBy(i => i.At).ToList();
                blocks.Add(new Heading(minimal && items[^1].Action is { } a ? $"#{a.Number}" : texts.Title(items[^1])));
                blocks.AddRange(Describe(items, content, texts, minimal));
            }
        }

        return (subject, blocks);
    }

    /// <summary>The events of one subject, then the three answers and the link.</summary>
    private static IEnumerable<Block> Describe(List<MessageItem> items, MessageContent content, MessageTexts texts, bool minimal)
    {
        foreach (var item in items)
        {
            yield return new Paragraph(texts.Headline(item, content.RecipientUserId), Bold: true);
        }

        var last = items[^1];
        if (!minimal && last.Action is { } action && !NotifyEvents.IsAllClear(last.Event))
        {
            yield return new Facts(Answers(action, texts));
        }
        else if (!minimal && last.Warning is { Title: { } title } warning)
        {
            yield return new Paragraph($"{warning.Key}: {title}", Bold: false);
        }

        if (content.PathOf(last) is { } path)
        {
            yield return new Link(texts["label.open"], content.Link(path));
        }
    }

    private static List<(string Label, string Value)> Answers(ActionFacts action, MessageTexts texts)
    {
        var rows = new List<(string, string)>();
        if (action.Entries.Count > 0)
        {
            var top = action.Entries[0];
            var what = top.Exploitation is "active" or "poc"
                ? texts.Format("what.exploited", top.Key, top.Title ?? top.Key, texts.Exploitation(top.Exploitation))
                : texts.Format("what.plain", top.Key, top.Title ?? top.Key);
            rows.Add((texts["label.what_happened"], what));
        }

        if (action.ComponentName.Length > 0)
        {
            rows.Add((texts["label.affected"], action.ComponentVersion is { } version
                ? texts.Format("affected.runs", action.ProjectName, action.ComponentName, version)
                : texts.Format("affected.runs_unversioned", action.ProjectName, action.ComponentName)));
        }

        rows.Add((texts["label.to_do"], action.TargetVersion is { } target ? texts.Format("todo.update", target) : texts["todo.check"]));
        var covers = texts.Format("covers.count", action.OpenFindings);
        if (action.Entries.Count > 1)
        {
            covers += ": " + string.Join(", ", action.Entries.Select(e => e.Key));
        }

        rows.Add((texts["label.covers"], covers));
        return rows;
    }

    private static (string Subject, List<Block> Blocks) Digest(MessageContent content, MessageTexts texts, bool minimal)
    {
        var items = content.Items;
        var subject = content.Items.Count == 0
            ? texts.Format("title.digest_empty", content.Organization.Name)
            : texts.Format("title.digest", content.Organization.Name, content.Items.Count);
        var blocks = new List<Block>();
        if (content.PeriodStart is { } start)
        {
            blocks.Add(new Paragraph(texts.Format("digest.intro", TimeZoneInfo.ConvertTime(start, DigestSchedule.Zone(content.Organization.TimeZone)).ToString("f", texts.Culture)), Bold: false));
        }

        if (content.Items.Count == 0)
        {
            blocks.Add(new Paragraph(texts["digest.empty"], Bold: true));
            return (subject, blocks);
        }

        foreach (var item in items.OrderByDescending(i => i.Level ?? 0).ThenBy(i => i.At))
        {
            var line = minimal && item.Action is { } a ? $"#{a.Number}" : texts.Title(item);
            var detail = item.Event == "finding.joined" ? texts.Format("digest.new_findings", item.NewFindings) : texts.Headline(item, content.RecipientUserId);
            if (item.AlreadyReported)
            {
                detail += $" ({texts["digest.already"]})";
            }

            blocks.Add(new Item(line, detail, content.PathOf(item) is { } path ? content.Link(path) : null));
        }

        return (subject, blocks);
    }

    private static string SubjectKey(MessageItem item) =>
        item.Action is { } a ? $"action:{a.Id}" : item.Warning is { } w ? $"warning:{w.EntryId}" : item.Backfill is { } b ? $"project:{b.ProjectId}" : $"event:{item.EventId}";

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private abstract record Block
    {
        public abstract string Text();

        public abstract string Html();
    }

    private sealed record Heading(string Value) : Block
    {
        public override string Text() => Value;

        public override string Html() => $"<p style=\"margin:24px 0 8px;font-size:15px;font-weight:600\">{Encode(Value)}</p>";
    }

    private sealed record Paragraph(string Value, bool Bold) : Block
    {
        public override string Text() => Value;

        public override string Html() =>
            $"<p style=\"margin:0 0 12px;font-size:14px;line-height:1.5{(Bold ? ";font-weight:600" : string.Empty)}\">{Encode(Value)}</p>";
    }

    private sealed record Facts(List<(string Label, string Value)> Rows) : Block
    {
        public override string Text() => string.Join('\n', Rows.Select(r => $"{r.Label.PadRight(15)} {r.Value}"));

        public override string Html() =>
            "<table role=\"presentation\" style=\"margin:0 0 12px;font-size:14px;line-height:1.5\">"
            + string.Concat(Rows.Select(r => $"<tr><td style=\"padding:2px 16px 2px 0;color:#53595d;vertical-align:top;white-space:nowrap\">{Encode(r.Label)}</td><td style=\"padding:2px 0\">{Encode(r.Value)}</td></tr>"))
            + "</table>";
    }

    private sealed record Link(string Label, Uri Url) : Block
    {
        public override string Text() => $"{Label}: {Url.AbsoluteUri}";

        public override string Html() =>
            $"<p style=\"margin:16px 0\"><a href=\"{Encode(Url.AbsoluteUri)}\" style=\"display:inline-block;background:#131416;color:#ffffff;text-decoration:none;padding:10px 16px;border-radius:6px;font-size:14px;font-weight:600\">{Encode(Label)}</a></p>";
    }

    private sealed record Item(string Title, string Detail, Uri? Url) : Block
    {
        public override string Text() => Url is null ? $"- {Title}\n  {Detail}" : $"- {Title}\n  {Detail}\n  {Url.AbsoluteUri}";

        public override string Html() =>
            $"<p style=\"margin:0 0 12px;font-size:14px;line-height:1.5\">{(Url is null ? $"<strong>{Encode(Title)}</strong>" : $"<a href=\"{Encode(Url.AbsoluteUri)}\" style=\"color:#131416;font-weight:600\">{Encode(Title)}</a>")}<br>{Encode(Detail)}</p>";
    }

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
