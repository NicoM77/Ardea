using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Assessing;

/// <summary>
/// The rules of the coarse path (master plan 17.1 to 17.3; ADR 044, ADR 046, ADR 047). Pure: no database, no clock.
/// </summary>
internal static class NoticeRules
{
    public const string OnlyImportant = "only_important";
    public const string Everything = "everything";

    /// <summary>A component without an exact version goes the coarse path: a notice asks instead of a finding (17.1).</summary>
    public static bool IsCoarse(ProfileComponent component) => component.VersionPrecision is "none" or "line";

    /// <summary>The leading numbers of a component's version line, e.g. <c>[17]</c> for «PostgreSQL 17»; empty without a line.</summary>
    public static IReadOnlyList<long> Line(ProfileComponent component) =>
        component.VersionPrecision == "line" ? VersionLines.LeadingNumbers(component.Version) : [];

    /// <summary>
    /// Whether an entry meets a coarse component's threshold (ADR 044 decisions 2 to 4). «Everything» always; «only important»
    /// when the entry's CVSS base score is 9.0 or more, it is actively exploited (KEV, SSVC «active»), an authority warns about
    /// it, or its EPSS class is high. For a product the catalogue marks as noisy, only exploitation and warnings count.
    /// </summary>
    public static bool Meets(string threshold, bool noisy, EntryFacts entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (threshold == Everything)
        {
            return true;
        }

        var exploitedOrWarned = entry.Exploitation == "active" || entry.AuthorityWarning;
        return noisy ? exploitedOrWarned : exploitedOrWarned || entry.CvssScore >= 9.0 || entry.EpssClass == "high";
    }

    /// <summary>
    /// Why an answered «doesn't affect us» comes back (E22, master plan 17.3 «aktiv ausgenutzt oder in KEV aufgenommen»):
    /// <c>kev</c> when CISA listed the entry after the answer, <c>exploited</c> when it became actively exploited after the
    /// answer otherwise; <c>null</c> when neither is new. The caller lets it come back once.
    /// </summary>
    /// <param name="entry">The entry now.</param>
    /// <param name="answeredExploitation">The entry's exploitation when it was answered.</param>
    /// <param name="answeredInKev">Whether it was in KEV when it was answered.</param>
    public static string? ReappearReason(EntryFacts entry, string? answeredExploitation, bool answeredInKev)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.InKev && !answeredInKev ? "kev"
            : entry.Exploitation == "active" && answeredExploitation != "active" ? "exploited"
            : null;
    }
}
