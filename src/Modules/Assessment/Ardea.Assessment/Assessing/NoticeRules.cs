using Ardea.Assets.Contracts;
using Ardea.Catalog.Contracts;
using Ardea.Intel.Contracts;

namespace Ardea.Assessment.Assessing;

internal static class NoticeRules
{
    public const string OnlyImportant = "only_important";
    public const string Everything = "everything";

    public static bool IsCoarse(ProfileComponent component) => component.VersionPrecision is "none" or "line";

    public static IReadOnlyList<long> Line(ProfileComponent component) =>
        component.VersionPrecision == "line" ? VersionLines.LeadingNumbers(component.Version) : [];

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

    public static string? ReappearReason(EntryFacts entry, string? answeredExploitation, bool answeredInKev)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.InKev && !answeredInKev ? "kev"
            : entry.Exploitation == "active" && answeredExploitation != "active" ? "exploited"
            : null;
    }
}
