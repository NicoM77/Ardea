using System.Net;

namespace Ardea.Platform.Email;

public static class EmailTemplate
{
    public static EmailMessage Render(string to, string subject, string greeting, IReadOnlyList<string> paragraphs, string actionLabel, Uri actionUrl, string footer)
    {
        var text = $"{greeting}\n\n{string.Join("\n\n", paragraphs)}\n\n{actionLabel}: {actionUrl}\n\n--\n{footer}\n";
        var html = $"""
            <!doctype html>
            <html lang="en"><body style="margin:0;padding:24px;background:#f1f4f6;font-family:system-ui,-apple-system,'Segoe UI',sans-serif;color:#131416">
            <table role="presentation" style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e3e8eb;border-radius:8px;padding:32px">
            <tr><td>
            <p style="margin:0 0 16px;font-size:16px;font-weight:600">Ardea</p>
            <p style="margin:0 0 16px;font-size:14px">{Encode(greeting)}</p>
            {string.Concat(paragraphs.Select(p => $"<p style=\"margin:0 0 16px;font-size:14px;line-height:1.5\">{Encode(p)}</p>"))}
            <p style="margin:24px 0"><a href="{Encode(actionUrl.AbsoluteUri)}" style="display:inline-block;background:#131416;color:#ffffff;text-decoration:none;padding:10px 16px;border-radius:6px;font-size:14px;font-weight:600">{Encode(actionLabel)}</a></p>
            <p style="margin:0;font-size:12px;color:#53595d">{Encode(footer)}</p>
            </td></tr></table></body></html>
            """;
        return new EmailMessage(to, subject, text, html);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
