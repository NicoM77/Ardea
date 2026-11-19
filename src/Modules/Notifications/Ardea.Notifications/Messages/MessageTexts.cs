using System.Globalization;
using Ardea.Notifications.Rules;

namespace Ardea.Notifications.Messages;

/// <summary>
/// The wording of messages in the recipient's language (master plan 8.5). English and German; the other
/// languages of the interface fall back to English until their texts exist. Quoted titles stay as the
/// source wrote them.
/// </summary>
internal sealed class MessageTexts
{
    private readonly Dictionary<string, string> _texts;

    private MessageTexts(string locale, Dictionary<string, string> texts)
    {
        Locale = locale;
        _texts = texts;
    }

    public string Locale { get; }

    public CultureInfo Culture => CultureInfo.GetCultureInfo(Locale);

    public static MessageTexts For(string? locale) =>
        locale?.StartsWith("de", StringComparison.OrdinalIgnoreCase) == true ? new("de", German) : new("en", English);

    public string this[string key] => _texts.TryGetValue(key, out var text) ? text : English[key];

    public string Format(string key, params object?[] values) => string.Format(Culture, this[key], values);

    public string LevelName(int? level) => level switch
    {
        5 => this["level.5"],
        4 => this["level.4"],
        3 => this["level.3"],
        2 => this["level.2"],
        _ => this["level.1"],
    };

    /// <summary>One line that says what happened to the subject (master plan 8.5: «Level 5, Critical. Raised from level 4.»).</summary>
    public string Headline(MessageItem item, Guid? recipientUserId)
    {
        var level = item.Level;
        return item.Event switch
        {
            NotifyEvents.ActionRaised => Format("headline.raised", level, LevelName(level)),
            NotifyEvents.ActionLevelRaised => Format("headline.level_raised", level, LevelName(level), item.DataInt("from")),
            NotifyEvents.ActionExploitationEscalated => Format("headline.exploitation", level, LevelName(level), this["exploitation." + (item.Action?.Exploitation ?? "none")]),
            NotifyEvents.ActionFixAvailable => item.Action?.TargetVersion is { } target ? Format("headline.fix_version", target) : this["headline.fix"],
            NotifyEvents.ActionLevelLowered => Format("headline.lowered", item.DataInt("to"), item.DataInt("from")),
            NotifyEvents.ActionWithdrawn => Format("headline.withdrawn", Reason(item.DataString("reason"))),
            NotifyEvents.ActionAssigned => recipientUserId is { } user && item.DataString("to") == user.ToString() ? this["headline.assigned_you"] : this["headline.assigned"],
            NotifyEvents.ActionStatusChanged => Format("headline.status", Status(item.DataString("to"))),
            NotifyEvents.ActionNoteAdded => this["headline.note"],
            NotifyEvents.WarningPublished => this["headline.warning"],
            NotifyEvents.BackfillCompleted when item.Backfill is { } b => b.Actions == 0
                ? Format("headline.backfill_none", b.ProjectName)
                : Format("headline.backfill", b.ProjectName, b.Actions, b.HighestLevel, LevelName(b.HighestLevel)),
            NotifyEvents.DeliveryFailed => this["headline.delivery_failed"],
            _ => item.Event,
        };
    }

    /// <summary>The title of an item: «[5 Critical] #143 Puppet Enterprise in Platform Ops».</summary>
    public string Title(MessageItem item)
    {
        if (item.Action is { } action)
        {
            var subject = string.IsNullOrEmpty(action.ProjectName) ? action.ComponentName : Format("title.in", action.ComponentName, action.ProjectName);
            var level = item.Level ?? action.Level;
            return Format("title.action", level, LevelName(level), action.Number, subject);
        }

        if (item.Warning is { } warning)
        {
            return Format("title.warning", warning.Title ?? warning.Key);
        }

        if (item.Backfill is { } backfill)
        {
            return Format("title.backfill", backfill.ProjectName);
        }

        return item.Event;
    }

    public string Exploitation(string? value) => this["exploitation." + (value ?? "none")];

    public string Reason(string? reason) => reason is null ? this["reason.other"] : _texts.ContainsKey("reason." + reason) || English.ContainsKey("reason." + reason) ? this["reason." + reason] : reason;

    public string Status(string? status) => status is null ? string.Empty : _texts.ContainsKey("status." + status) || English.ContainsKey("status." + status) ? this["status." + status] : status;

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["level.5"] = "Critical",
        ["level.4"] = "High",
        ["level.3"] = "Medium",
        ["level.2"] = "Low",
        ["level.1"] = "Not relevant",
        ["exploitation.active"] = "actively exploited",
        ["exploitation.poc"] = "proof of concept published",
        ["exploitation.none"] = "no known exploitation",
        ["headline.raised"] = "Level {0}, {1}. New action.",
        ["headline.level_raised"] = "Level {0}, {1}. Raised from level {2}.",
        ["headline.exploitation"] = "Level {0}, {1}. Now {2}.",
        ["headline.fix"] = "A fix is available.",
        ["headline.fix_version"] = "A fix is available: {0}.",
        ["headline.lowered"] = "All clear: lowered to level {0} from {1}.",
        ["headline.withdrawn"] = "All clear: closed ({0}).",
        ["headline.assigned_you"] = "Assigned to you.",
        ["headline.assigned"] = "The assignment changed.",
        ["headline.status"] = "Status: {0}.",
        ["headline.note"] = "A note was added.",
        ["headline.warning"] = "New warning without a vulnerability identifier.",
        ["headline.backfill"] = "First assessment of {0} done: {1} action(s), highest level {2}, {3}.",
        ["headline.backfill_none"] = "First assessment of {0} done: nothing to do.",
        ["headline.delivery_failed"] = "A level 5 message reached a recipient on none of their channels.",
        ["title.action"] = "[{0} {1}] #{2} {3}",
        ["title.in"] = "{0} in {1}",
        ["title.warning"] = "[Warning] {0}",
        ["title.backfill"] = "First assessment: {0}",
        ["title.several"] = "[{0} {1}] {2} updates in {3}",
        ["title.digest"] = "Ardea digest for {0}: {1} item(s)",
        ["title.digest_empty"] = "Ardea digest for {0}: nothing to do",
        ["title.test"] = "Ardea test message",
        ["label.what_happened"] = "What happened",
        ["label.affected"] = "Affected",
        ["label.to_do"] = "What to do",
        ["label.covers"] = "Covers",
        ["label.open"] = "Open",
        ["what.exploited"] = "{0}: {1}. It is {2}.",
        ["what.plain"] = "{0}: {1}.",
        ["affected.runs"] = "{0} runs {1} {2}.",
        ["affected.runs_unversioned"] = "{0} runs {1}.",
        ["todo.update"] = "Update to {0}.",
        ["todo.check"] = "Read the advisory and decide; no fixed version is known yet.",
        ["covers.count"] = "{0} vulnerability(ies)",
        ["digest.intro"] = "Everything since {0}.",
        ["digest.empty"] = "Checked: nothing needed your attention in this period.",
        ["digest.already"] = "already reported",
        ["digest.new_findings"] = "{0} more finding(s), level unchanged.",
        ["footer.member"] = "You receive this because of the notification rules of {0}. Change your settings: {1}",
        ["footer.address"] = "You receive this because {0} added your address to its notification rules. Unsubscribe: {1}",
        ["test.body"] = "This is a test message from Ardea. The webhook works.",
        ["test.push"] = "Push works on this device.",
        ["reason.remediated"] = "remediated",
        ["reason.fixed_by_update"] = "fixed by an update",
        ["reason.withdrawn"] = "the advisory was withdrawn",
        ["reason.merged"] = "merged into another entry",
        ["reason.no_longer_matched"] = "no longer matched",
        ["reason.component_removed"] = "component removed",
        ["reason.project_archived"] = "project archived",
        ["reason.out_of_range"] = "version outside the affected range",
        ["reason.not_affected"] = "not affected",
        ["reason.risk_accepted"] = "risk accepted",
        ["reason.other"] = "closed",
        ["status.open"] = "open",
        ["status.in_review"] = "in review",
        ["status.remediated"] = "remediated",
        ["status.risk_accepted"] = "risk accepted",
        ["status.not_affected"] = "not affected",
        ["confirm.subject"] = "Confirm notifications from {0}",
        ["confirm.greeting"] = "Hello,",
        ["confirm.body"] = "{0} wants to send security notifications from Ardea to this address. Nothing is sent until you confirm.",
        ["confirm.ignore"] = "If you do not expect this, ignore this e-mail; the link expires in 7 days.",
        ["confirm.action"] = "Confirm address",
        ["confirm.footer"] = "Sent by Ardea on behalf of {0}.",
    };

    private static readonly Dictionary<string, string> German = new(StringComparer.Ordinal)
    {
        ["level.5"] = "Kritisch",
        ["level.4"] = "Hoch",
        ["level.3"] = "Mittel",
        ["level.2"] = "Niedrig",
        ["level.1"] = "Nicht relevant",
        ["exploitation.active"] = "wird aktiv ausgenutzt",
        ["exploitation.poc"] = "ein Proof of Concept ist veröffentlicht",
        ["exploitation.none"] = "keine bekannte Ausnutzung",
        ["headline.raised"] = "Stufe {0}, {1}. Neue Massnahme.",
        ["headline.level_raised"] = "Stufe {0}, {1}. Gestiegen von Stufe {2}.",
        ["headline.exploitation"] = "Stufe {0}, {1}. Neu: {2}.",
        ["headline.fix"] = "Eine Korrektur ist verfügbar.",
        ["headline.fix_version"] = "Eine Korrektur ist verfügbar: {0}.",
        ["headline.lowered"] = "Entwarnung: gesunken auf Stufe {0} von {1}.",
        ["headline.withdrawn"] = "Entwarnung: geschlossen ({0}).",
        ["headline.assigned_you"] = "Dir zugewiesen.",
        ["headline.assigned"] = "Die Zuweisung hat sich geändert.",
        ["headline.status"] = "Status: {0}.",
        ["headline.note"] = "Eine Notiz wurde hinzugefügt.",
        ["headline.warning"] = "Neue Warnung ohne Schwachstellenkennung.",
        ["headline.backfill"] = "Ersteinschätzung von {0} fertig: {1} Massnahme(n), höchste Stufe {2}, {3}.",
        ["headline.backfill_none"] = "Ersteinschätzung von {0} fertig: nichts zu tun.",
        ["headline.delivery_failed"] = "Eine Nachricht der Stufe 5 hat einen Empfänger auf keinem seiner Kanäle erreicht.",
        ["title.in"] = "{0} in {1}",
        ["title.warning"] = "[Warnung] {0}",
        ["title.backfill"] = "Ersteinschätzung: {0}",
        ["title.several"] = "[{0} {1}] {2} Änderungen in {3}",
        ["title.digest"] = "Ardea-Sammelnachricht für {0}: {1} Eintrag/Einträge",
        ["title.digest_empty"] = "Ardea-Sammelnachricht für {0}: nichts zu tun",
        ["title.test"] = "Ardea-Testnachricht",
        ["label.what_happened"] = "Was ist passiert",
        ["label.affected"] = "Betroffen",
        ["label.to_do"] = "Was tun",
        ["label.covers"] = "Umfasst",
        ["label.open"] = "Öffnen",
        ["what.exploited"] = "{0}: {1}. Sie {2}.",
        ["affected.runs"] = "{0} betreibt {1} {2}.",
        ["affected.runs_unversioned"] = "{0} betreibt {1}.",
        ["todo.update"] = "Auf {0} aktualisieren.",
        ["todo.check"] = "Advisory lesen und entscheiden; eine korrigierte Version ist noch nicht bekannt.",
        ["covers.count"] = "{0} Schwachstelle(n)",
        ["digest.intro"] = "Alles seit {0}.",
        ["digest.empty"] = "Geprüft: In diesem Zeitraum stand nichts an.",
        ["digest.already"] = "bereits gemeldet",
        ["digest.new_findings"] = "{0} weitere(r) Befund(e), Stufe unverändert.",
        ["footer.member"] = "Du erhältst diese Nachricht wegen der Benachrichtigungsregeln von {0}. Einstellungen ändern: {1}",
        ["footer.address"] = "Du erhältst diese Nachricht, weil {0} deine Adresse in seine Benachrichtigungsregeln aufgenommen hat. Abmelden: {1}",
        ["test.body"] = "Dies ist eine Testnachricht von Ardea. Der Webhook funktioniert.",
        ["test.push"] = "Push funktioniert auf diesem Gerät.",
        ["reason.remediated"] = "behoben",
        ["reason.fixed_by_update"] = "durch ein Update behoben",
        ["reason.withdrawn"] = "die Meldung wurde zurückgezogen",
        ["reason.merged"] = "mit einem anderen Eintrag zusammengeführt",
        ["reason.no_longer_matched"] = "trifft nicht mehr zu",
        ["reason.component_removed"] = "Komponente entfernt",
        ["reason.project_archived"] = "Projekt archiviert",
        ["reason.out_of_range"] = "Version ausserhalb des betroffenen Bereichs",
        ["reason.not_affected"] = "nicht betroffen",
        ["reason.risk_accepted"] = "Risiko akzeptiert",
        ["reason.other"] = "geschlossen",
        ["status.open"] = "offen",
        ["status.in_review"] = "in Prüfung",
        ["status.remediated"] = "behoben",
        ["status.risk_accepted"] = "Risiko akzeptiert",
        ["status.not_affected"] = "nicht betroffen",
        ["confirm.subject"] = "Benachrichtigungen von {0} bestätigen",
        ["confirm.greeting"] = "Hallo,",
        ["confirm.body"] = "{0} möchte Sicherheitsbenachrichtigungen von Ardea an diese Adresse senden. Bis zu deiner Bestätigung wird nichts gesendet.",
        ["confirm.ignore"] = "Wenn du das nicht erwartest, ignoriere diese E-Mail; der Link läuft in 7 Tagen ab.",
        ["confirm.action"] = "Adresse bestätigen",
        ["confirm.footer"] = "Gesendet von Ardea im Auftrag von {0}.",
    };
}
