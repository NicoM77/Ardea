namespace Ardea.Host.Tests;

/// <summary>
/// OpenAPI names a schema after its type's simple name. Two API types with the same name in different
/// modules would silently share one schema, and the generated web client would lose fields.
/// </summary>
public sealed class ContractNameTests
{
    private static readonly string[] Suffixes = ["View", "Request", "Response", "Preview", "Summary", "Ref"];

    [Fact]
    public void Api_types_have_unique_names_across_modules()
    {
        var types = Ardea.Host.Modules.All.Select(m => m.GetType().Assembly)
            .Append(typeof(Ardea.Platform.Http.ApiProblems).Assembly)
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsNested && Suffixes.Any(s => t.Name.EndsWith(s, StringComparison.Ordinal)));

        var duplicates = types.GroupBy(t => t.Name).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.FullName))}")
            .ToList();

        Assert.Empty(duplicates);
    }
}
